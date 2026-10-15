using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using UnityEngine;

namespace M1A1Abrams
{
    /// <summary>
    /// Submits a camera's instanced geometry when the renderer's own exact lookup cannot find it.
    ///
    /// The renderer keys each camera's prepared batches on that camera's whole render state - culling
    /// mask, field of view, aspect, both clip planes, position and rotation - and reads them back with
    /// an exact match, a thousandth of a metre of tolerance on position. The collection runs on worker
    /// threads between the renderer's LateUpdate and the camera's pre-cull, so by the time the table is
    /// read the camera has moved: a vehicle at ten metres a second covers seventeen hundred times that
    /// tolerance in one frame, and while the sight slews onto a freshly locked target it covers far more
    /// than that in the few milliseconds the collection itself takes. The read misses, the camera is
    /// handed nothing, and the map's instanced vegetation is missing through that sight for the frame -
    /// once per mesh group, which is the flood of "Unexpected failure to acquire" in the log.
    ///
    /// This replaces the renderer's own submission with one that answers that miss the same way, but at
    /// the layer above the out parameter. <c>GetMatsForCamParams</c> takes <c>out List&lt;SubmissionList&gt;</c>
    /// and the batches are a type from another assembly, so there is no way to hand a different answer
    /// back to it - a patch that tried set the result and left the caller's list null, and the caller's
    /// foreach threw, which cost the map its vegetation for the rest of the session.
    /// <c>SubmitCollectedInstances(Camera)</c> has no out parameter at all: skipping it and doing the
    /// work here means the near match is simply used, and nothing has to be written anywhere.
    ///
    /// The table is only read. A previous attempt re-keyed its entries from the main thread while the
    /// collection threads were using them and killed the worker thread twice, once by overwriting an
    /// entry and once by putting a null in its place. Nothing here writes, and every path that fails
    /// falls through to the renderer's own behaviour.
    /// </summary>
    [HarmonyPatch]
    public static class CrowsInstancingSubmit
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        /// <summary>The renderer's own wait on its collection threads, in milliseconds.</summary>
        private const int COLLECTION_WAIT_MS = 100;

        /// <summary>
        /// How far the camera may have moved since its batches were collected, in metres, and how far
        /// it may have turned, in degrees. A frame or two of travel at speed; far below the distance at
        /// which a different set of instances would be chosen.
        /// </summary>
        private const float POSITION_TOLERANCE = 12f;
        private const float ROTATION_TOLERANCE = 6f;

        private static bool resolved;
        private static bool usable;
        private static MethodBase target;

        private static FieldInfo groups_field;
        private static FieldInfo wait_field;
        private static FieldInfo table_field;
        private static FieldInfo config_field;
        private static PropertyInfo layers_property;
        private static MethodInfo renders_mask_method;
        private static ConstructorInfo key_ctor;
        private static MethodInfo submit_method;

        private static FieldInfo key_mask;
        private static FieldInfo key_near;
        private static FieldInfo key_far;
        private static FieldInfo key_fov;
        private static FieldInfo key_aspect;
        private static FieldInfo key_position;
        private static FieldInfo key_rotation;
        private static Type key_type;

        private static bool reported;

        // Compiled readers for the key's members. Every read of a value-typed member through
        // FieldInfo.GetValue boxes it, and SameCameraMoved reads seven of them for every entry of every
        // group's table on every frame the camera has moved - which is exactly the frame this whole
        // workaround exists for. Compiled once where the members resolve; null where they could not be
        // built, in which case the reflection path below still answers.
        private static Func<object, int> mask_of;
        private static Func<object, float> near_of;
        private static Func<object, float> far_of;
        private static Func<object, float> fov_of;
        private static Func<object, float> aspect_of;
        private static Func<object, Vector3> position_of;
        private static Func<object, Quaternion> rotation_of;

        /// <summary>Reflection argument arrays, reused: a frame that allocates four of them is a frame
        /// the garbage collector is asked to clean up after, sixty times a second.</summary>
        private static readonly object[] key_args = new object[1];
        private static readonly object[] renders_args = new object[2];

        /// <summary>
        /// Remembered answers to the game's own mask test. It depends only on the camera's culling mask
        /// and the group's layers, and both change when the player switches optics rather than per
        /// frame, so a handful of entries covers everything the loop asks for - and the reflection call
        /// behind them costs an argument array and a boxed LayerMask each time it is made.
        /// </summary>
        private const int RENDERS_CACHE = 8;
        private static readonly long[] renders_ask = new long[RENDERS_CACHE];
        private static readonly bool[] renders_said = new bool[RENDERS_CACHE];
        private static int renders_next;
        private static int renders_used;

        /// <summary>And the same for each group's layers, which do not change at all.</summary>
        private const int LAYERS_CACHE = 16;
        private static readonly object[] layers_group = new object[LAYERS_CACHE];
        private static readonly int[] layers_value = new int[LAYERS_CACHE];
        private static int layers_next;
        private static int layers_used;

        /// <summary>
        /// A compiled reader for one member, or null if one cannot be built. Tolerant of the two shapes
        /// the game's vector and rotation members come in: the UnityEngine type directly, or
        /// Unity.Mathematics struct wrapping it in a field called "value".
        /// </summary>
        private static Func<object, T> Accessor<T>(FieldInfo member)
        {
            if (member == null) return null;

            try
            {
                ParameterExpression key = Expression.Parameter(typeof(object), "key");
                Expression read = Expression.Field(Expression.Convert(key, member.DeclaringType), member);
                Expression body = read.Type == typeof(T)
                    ? read
                    : Expression.Convert(read, typeof(T));
                return Expression.Lambda<Func<object, T>>(body, key).Compile();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// A compiled reader for a three or four component value held either directly in the member or
        /// in a "value" field inside it. Built as one expression so neither the wrapper nor the value
        /// is ever boxed; see Accessor.
        /// </summary>
        private static Func<object, T> Components<T>(FieldInfo member, string[] names, Func<MemberExpression[], Expression> build)
        {
            if (member == null || names == null) return null;

            try
            {
                ParameterExpression key = Expression.Parameter(typeof(object), "key");
                MemberExpression outer = Expression.Field(Expression.Convert(key, member.DeclaringType), member);
                Type holder = outer.Type;

                if (holder != typeof(T))
                {
                    FieldInfo inner = holder.GetField("value", Any);
                    if (inner == null) return null;
                    outer = Expression.Field(outer, inner);
                }

                MemberExpression[] parts = new MemberExpression[names.Length];
                for (int i = 0; i < names.Length; i++)
                {
                    FieldInfo part = outer.Type.GetField(names[i], Any);
                    if (part == null) return null;
                    parts[i] = Expression.Field(outer, part);
                }

                return Expression.Lambda<Func<object, T>>(build(parts), key).Compile();
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static Func<object, Vector3> PositionReader(FieldInfo member)
        {
            return Components<Vector3>(member, new string[] { "x", "y", "z" },
                parts => Expression.New(typeof(Vector3).GetConstructor(new Type[] { typeof(float), typeof(float), typeof(float) }),
                    parts[0], parts[1], parts[2]));
        }

        private static Func<object, Quaternion> RotationReader(FieldInfo member)
        {
            return Components<Quaternion>(member, new string[] { "x", "y", "z", "w" },
                parts => Expression.New(typeof(Quaternion).GetConstructor(new Type[] { typeof(float), typeof(float), typeof(float), typeof(float) }),
                    parts[0], parts[1], parts[2], parts[3]));
        }

        private static MethodBase TargetMethod()
        {
            if (resolved)
            {
                return target;
            }

            resolved = true;

            try
            {
                Type renderer = Find("GHPC.Rendering.Instancing.InstancedMeshRenderer");
                Type group = Find("GHPC.Rendering.Instancing.MeshGroup");
                Type utils = Find("GHPC.Rendering.Utils");

                if (renderer == null || group == null)
                {
                    usable = false;
                    return null;
                }

                groups_field = renderer.GetField("instancedMeshGroupDictionary", Any);
                wait_field = renderer.GetField("procThreadHandleEnd", Any);
                table_field = group.GetField("submissionListsPerCamSetup", Any);
                config_field = group.GetField("config", Any);

                if (groups_field == null || wait_field == null || table_field == null)
                {
                    usable = false;
                    return null;
                }

                // Found by name, arity and return type rather than by a signature built from the table's
                // generic arguments - that is easy to get wrong and fails silently when it is.
                MethodInfo[] methods = renderer.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance);

                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo method = methods[i];

                    if (method.Name == "SubmitCollectedInstances"
                        && method.ReturnType == typeof(void)
                        && method.GetParameters().Length == 1)
                    {
                        target = method;
                        break;
                    }
                }

                if (target == null)
                {
                    usable = false;
                    return null;
                }

                Type[] key_arguments = table_field.FieldType.GetGenericArguments();
                key_type = key_arguments[0];
                Type list_type = key_arguments[1];

                if (key_type != null)
                {
                    key_ctor = key_type.GetConstructor(Any, null, new Type[] { typeof(Camera) }, null);
                    key_mask = key_type.GetField("cullingMask", Any);
                    key_near = key_type.GetField("nearClipPlane", Any);
                    key_far = key_type.GetField("farClipPlane", Any);
                    key_fov = key_type.GetField("fieldOfView", Any);
                    key_aspect = key_type.GetField("aspect", Any);
                    key_position = key_type.GetField("position", Any);
                    key_rotation = key_type.GetField("rotation", Any);

                    mask_of = Accessor<int>(key_mask);
                    near_of = Accessor<float>(key_near);
                    far_of = Accessor<float>(key_far);
                    fov_of = Accessor<float>(key_fov);
                    aspect_of = Accessor<float>(key_aspect);
                    position_of = PositionReader(key_position);
                    rotation_of = RotationReader(key_rotation);
                }

                if (layers_property == null && config_field != null)
                {
                    layers_property = config_field.FieldType.GetProperty("RenderLayers", Any);
                }

                if (utils != null)
                {
                    MethodInfo[] helpers = utils.GetMethods(BindingFlags.Public | BindingFlags.Static);

                    for (int i = 0; i < helpers.Length; i++)
                    {
                        if (helpers[i].Name == "WillCameraRenderObjectOnMask" && helpers[i].GetParameters().Length == 2)
                        {
                            renders_mask_method = helpers[i];
                            break;
                        }
                    }
                }

                // The batch type is another assembly's; all that is needed from it is its Submit().
                Type element = list_type == null || !list_type.IsGenericType
                    ? null
                    : list_type.GetGenericArguments()[0];

                if (element != null)
                {
                    submit_method = element.GetMethod("Submit", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                }

                usable = key_ctor != null && key_mask != null && key_position != null
                    && layers_property != null && submit_method != null;

                return usable ? target : null;
            }
            catch (Exception)
            {
                usable = false;
                return null;
            }
        }

        private static Type Find(string name)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(name, false);

                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        /// <summary>
        /// Runs instead of the renderer's own submission. Returns false to skip it when the work has
        /// been done here, and true to hand the frame back to it whenever anything at all is not
        /// certain - a half-replaced submission is worse than an exact lookup that misses.
        /// </summary>
        private static bool Prefix(object __instance, object[] __args)
        {
            bool touched = false;

            try
            {
                if (!usable || __instance == null || __args == null || __args.Length < 1)
                {
                    return true;
                }

                Camera cam = __args[0] as Camera;

                if (cam == null)
                {
                    return true;
                }

                // The renderer's own wait, so a batch that is still being built is never submitted.
                EventWaitHandle collection = wait_field.GetValue(__instance) as EventWaitHandle;

                if (collection != null && !collection.WaitOne(COLLECTION_WAIT_MS))
                {
                    // It would log "Thread wait timed out" here and submit nothing. Nothing to submit
                    // is the same outcome, without the line.
                    return false;
                }

                key_args[0] = cam;
                object wanted = key_ctor.Invoke(key_args);

                if (wanted == null)
                {
                    return true;
                }

                IDictionary groups = groups_field.GetValue(__instance) as IDictionary;

                if (groups == null)
                {
                    return true;
                }

                int groups_drawn = 0;
                int near_matches = 0;

                foreach (DictionaryEntry entry in groups)
                {
                    object group = entry.Value;

                    if (group == null)
                    {
                        continue;
                    }

                    object config = config_field == null ? null : config_field.GetValue(group);
                    object layers = config == null || layers_property == null
                        ? null
                        : layers_property.GetValue(config, null);

                    if (layers == null || !Renders(cam, LayersOf(group, layers)))
                    {
                        continue;
                    }

                    IDictionary table = table_field.GetValue(group) as IDictionary;

                    if (table == null || table.Count == 0)
                    {
                        continue;
                    }

                    // The renderer's own lookup first. When the key is still there the behaviour is
                    // exactly as it was, which is the common case.
                    bool exact = table.Contains(wanted);
                    object batches = exact ? table[wanted] : Nearest(table, wanted);

                    if (batches == null)
                    {
                        // Nothing collected for this camera in this group. The renderer would log
                        // "Unexpected failure to acquire" and move on; moving on is what happens.
                        continue;
                    }

                    if (!exact)
                    {
                        near_matches++;
                    }

                    if (!SubmitAll(batches))
                    {
                        continue;
                    }

                    touched = true;
                    groups_drawn++;
                }

                if (groups_drawn > 0 && near_matches > 0 && !reported)
                {
                    reported = true;
                }

                // Nothing submitted means nothing was done here: when the exact key and nearest fallback
                // both miss, every group is skipped and the renderer must still get the frame, otherwise
                // its own SubmitCollectedInstances never runs and objects flicker out while turning fast.
                // Same shape as the catch below - hand the frame back only while nothing is drawn.
                return !touched;
            }
            catch (Exception)
            {
                // Nothing has been submitted yet, so the renderer can still have the frame. Once it has,
                // handing the frame back would draw the same batches twice.
                return !touched;
            }
        }

        /// <summary>Whether this camera draws this mesh group, asked of the game's own helper.</summary>
        private static bool Renders(Camera cam, int render_layers)
        {
            long ask = ((long)(uint)cam.cullingMask << 32) | (uint)render_layers;

            for (int i = 0; i < renders_used; i++)
            {
                if (renders_ask[i] == ask)
                {
                    return renders_said[i];
                }
            }

            bool said = RenderTest(cam, render_layers);

            int slot;
            if (renders_used < RENDERS_CACHE)
            {
                slot = renders_used++;
            }
            else
            {
                slot = renders_next;
                renders_next = (renders_next + 1) % RENDERS_CACHE;
            }

            renders_ask[slot] = ask;
            renders_said[slot] = said;

            return said;
        }

        /// <summary>The game's own test, asked once per distinct mask and layer set; see Renders.</summary>
        private static bool RenderTest(Camera cam, int render_layers)
        {
            if (renders_mask_method != null)
            {
                try
                {
                    renders_args[0] = (LayerMask)render_layers;
                    renders_args[0] = cam.cullingMask;
                    renders_args[1] = (LayerMask)render_layers;
                    return (bool)renders_mask_method.Invoke(null, renders_args);
                }
                catch (Exception)
                {
                    // fall through to the plain test
                }
            }

            return (cam.cullingMask & render_layers) != 0;
        }

        /// <summary>
        /// The group's render layers, remembered. They arrive as a boxed value from a reflected property
        /// call, which is an allocation per group per frame for an answer that never changes.
        /// </summary>
        private static int LayersOf(object group, object layers)
        {
            for (int i = 0; i < layers_used; i++)
            {
                if (ReferenceEquals(layers_group[i], group))
                {
                    return layers_value[i];
                }
            }

            int value;
            try
            {
                value = Convert.ToInt32(layers);
            }
            catch (Exception)
            {
                return 0;
            }

            int slot;
            if (layers_used < LAYERS_CACHE)
            {
                slot = layers_used++;
            }
            else
            {
                slot = layers_next;
                layers_next = (layers_next + 1) % LAYERS_CACHE;
            }

            layers_group[slot] = group;
            layers_value[slot] = value;

            return value;
        }

        /// <summary>
        /// The batches belonging to the same camera as the one being looked up, differing only by the
        /// camera having moved since they were collected. Everything set once per camera has to be
        /// identical; only position and rotation may drift, and only a little.
        /// </summary>
        private static object Nearest(IDictionary table, object wanted)
        {
            object best = null;
            float best_distance = float.MaxValue;

            foreach (DictionaryEntry entry in table)
            {
                if (entry.Value == null || entry.Key == null)
                {
                    continue;
                }

                float distance;

                if (!SameCameraMoved(entry.Key, wanted, out distance) || distance >= best_distance)
                {
                    continue;
                }

                best_distance = distance;
                best = entry.Value;
            }

            return best;
        }

        private static bool SameCameraMoved(object stored, object wanted, out float distance)
        {
            distance = float.MaxValue;

            if (stored != null && wanted != null && mask_of != null && near_of != null && far_of != null
                && fov_of != null && aspect_of != null)
            {
                // The compiled path; see Accessor. The same decisions as the reflection path below,
                // without a boxed read of every member of every candidate key on every frame.
                if (mask_of(stored) != mask_of(wanted)) return false;
                if (near_of(stored) != near_of(wanted)) return false;
                if (far_of(stored) != far_of(wanted)) return false;
                if (fov_of(stored) != fov_of(wanted)) return false;
                if (aspect_of(stored) != aspect_of(wanted)) return false;

                distance = (ReadPosition(position_of, stored) - ReadPosition(position_of, wanted)).magnitude;

                if (distance > POSITION_TOLERANCE) return false;
                if (key_rotation == null) return true;

                return ReadAngle(rotation_of, stored, wanted) <= ROTATION_TOLERANCE;
            }

            if (Convert.ToInt32(key_mask.GetValue(stored)) != Convert.ToInt32(key_mask.GetValue(wanted)))
            {
                return false;
            }

            if (key_near != null && Convert.ToSingle(key_near.GetValue(stored)) != Convert.ToSingle(key_near.GetValue(wanted)))
            {
                return false;
            }

            if (key_far != null && Convert.ToSingle(key_far.GetValue(stored)) != Convert.ToSingle(key_far.GetValue(wanted)))
            {
                return false;
            }

            if (key_fov != null && Convert.ToSingle(key_fov.GetValue(stored)) != Convert.ToSingle(key_fov.GetValue(wanted)))
            {
                return false;
            }

            if (key_aspect != null && Convert.ToSingle(key_aspect.GetValue(stored)) != Convert.ToSingle(key_aspect.GetValue(wanted)))
            {
                return false;
            }

            distance = (ReadVector3(key_position.GetValue(stored)) - ReadVector3(key_position.GetValue(wanted))).magnitude;

            if (distance > POSITION_TOLERANCE)
            {
                return false;
            }

            if (key_rotation == null)
            {
                return true;
            }

            return Quaternion.Angle(
                ReadRotation(key_rotation.GetValue(stored)),
                ReadRotation(key_rotation.GetValue(wanted))) <= ROTATION_TOLERANCE;
        }

        private static Vector3 ReadPosition(Func<object, Vector3> reader, object key)
        {
            return reader != null ? reader(key) : ReadVector3(key_position.GetValue(key));
        }

        private static float ReadAngle(Func<object, Quaternion> reader, object stored, object wanted)
        {
            Quaternion a = reader != null ? reader(stored) : ReadRotation(key_rotation.GetValue(stored));
            Quaternion b = reader != null ? reader(wanted) : ReadRotation(key_rotation.GetValue(wanted));
            return Quaternion.Angle(a, b);
        }

        private static bool SubmitAll(object batches)
        {
            IList lists = batches as IList;

            if (lists == null)
            {
                return false;
            }

            for (int i = 0; i < lists.Count; i++)
            {
                if (lists[i] != null)
                {
                    submit_method.Invoke(lists[i], null);
                }
            }

            return true;
        }

        private static Vector3 ReadVector3(object value)
        {
            if (value == null)
            {
                return Vector3.zero;
            }

            Type type = value.GetType();

            return new Vector3(ReadFloat(type, value, "x"), ReadFloat(type, value, "y"), ReadFloat(type, value, "z"));
        }

        /// <summary>Unity.Mathematics.quaternion wraps a float4 in a field called "value".</summary>
        private static Quaternion ReadRotation(object value)
        {
            if (value == null)
            {
                return Quaternion.identity;
            }

            FieldInfo inner = value.GetType().GetField("value", Any);
            object vector = inner == null ? value : inner.GetValue(value);

            if (vector == null)
            {
                return Quaternion.identity;
            }

            Type type = vector.GetType();

            return new Quaternion(
                ReadFloat(type, vector, "x"),
                ReadFloat(type, vector, "y"),
                ReadFloat(type, vector, "z"),
                ReadFloat(type, vector, "w"));
        }

        private static float ReadFloat(Type type, object instance, string name)
        {
            FieldInfo field = type.GetField(name, Any);

            return field == null ? 0f : Convert.ToSingle(field.GetValue(instance));
        }
    }
}
