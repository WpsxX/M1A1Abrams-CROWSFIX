using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using UnityEngine;

namespace M1A1Abrams
{
    /// <summary>
    /// GHPC hands a camera its instanced vegetation through a cache keyed on that cameras entire
    /// render state - culling mask, field of view, aspect, near and far clip planes, position and
    /// rotation. The collection runs on a worker thread behind two schedules: one for every camera in
    /// the renderers LateUpdate, which clears the whole table before refilling it, and one for each
    /// camera in its own pre-cull just before the table is read back. Whichever the worker finishes
    /// last decides which cameras key survives, so a camera that loses that race is handed no batches
    /// at all and GHPC says so once per mesh group per frame:
    ///
    ///   Unexpected failure to acquire grass_lush instances for camera camera (UnityEngine.Camera)
    ///
    /// Sixteen of those a frame is the sight camera starving: the grass, the ferns, the leaf clutter
    /// and every tree species are missing through the sight, or flicker, while the main view keeps
    /// rendering them. The camera that loses is decided by frame timing, which is why it comes and
    /// goes, and why nothing on the drawing side of this mod ever changed it - the batches are dropped
    /// before any of this mods code is consulted.
    ///
    /// The renderer calls its submit only from its own pre-cull handler, so a starved camera can only
    /// be put back on screen by asking again and submitting, in that order, from a handler that runs
    /// after the renderers. That is what the assist does.
    ///
    /// Every entry point is a no-op if the type or a member it needs is not there.
    /// </summary>
    internal static class CrowsInstancing
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;


        private static bool resolved;
        private static bool usable;

        /// <summary>Reflection argument arrays, reused rather than rebuilt on every submit; see
        /// CrowsInstancingSubmit.key_args.</summary>
        private static readonly object[] prepare_args = new object[1];
        private static readonly object[] submit_args = new object[1];

        private static Type renderer_type;
        private static FieldInfo singleton_field;
        private static MethodInfo prepare_method;
        private static MethodInfo submit_method;
        private static FieldInfo groups_field;
        private static FieldInfo deferred_handle_field;

        private static FieldInfo submissions_field;
        private static Type submissions_owner;


        private static PropertyInfo pair_value_property;

        internal static bool Usable
        {
            get { return Resolve() && usable; }
        }

        private static bool Resolve()
        {
            if (resolved)
            {
                return usable;
            }

            resolved = true;

            try
            {
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

                for (int i = 0; i < assemblies.Length; i++)
                {
                    Type candidate = assemblies[i].GetType(
                        "GHPC.Rendering.Instancing.InstancedMeshRenderer", false);

                    if (candidate != null)
                    {
                        renderer_type = candidate;
                        break;
                    }
                }

                if (renderer_type == null)
                {
                    usable = false;
                    return false;
                }

                const BindingFlags hiddenInstance = BindingFlags.NonPublic | BindingFlags.Instance;
                Type[] camera = new Type[] { typeof(Camera) };

                singleton_field = renderer_type.GetField("singletonCurrent", Any);
                groups_field = renderer_type.GetField("instancedMeshGroupDictionary", Any);
                deferred_handle_field = renderer_type.GetField("procThreadHandleDeferredEnd", Any);
                prepare_method = renderer_type.GetMethod("PrepareAndScheduleCollection", hiddenInstance, null, camera, null);
                submit_method = renderer_type.GetMethod("SubmitCollectedInstances", hiddenInstance, null, camera, null);

                // The singleton is read rather than created on purpose: SingletonGetOrCreate() would
                // build a renderer at whatever moment this happens to run.
                usable = singleton_field != null && groups_field != null
                    && prepare_method != null && submit_method != null;

                return usable;
            }
            catch (Exception)
            {
                usable = false;
                return false;
            }
        }

        private static object Singleton()
        {
            if (!Resolve())
            {
                return null;
            }

            try
            {
                return singleton_field.GetValue(null);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Whether the renderer is up and has served any camera at all. False while a mission is still
        /// coming up, when an empty cache means nothing.
        /// </summary>
        internal static bool PipelineServing()
        {
            object singleton = Singleton();

            if (singleton == null)
            {
                return false;
            }

            try
            {
                object groups = groups_field.GetValue(singleton);

                if (groups == null)
                {
                    return false;
                }

                foreach (object pair in (IEnumerable)groups)
                {
                    IDictionary table = SubmissionsOf(GroupOf(pair));

                    if (table != null && table.Count > 0)
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
            // The call is best-effort: a failure here leaves the previous state alone.
            }

            return false;
        }
        private static object GroupOf(object pair)
        {
            if (pair == null)
            {
                return null;
            }

            if (pair_value_property == null)
            {
                pair_value_property = pair.GetType().GetProperty("Value");
            }

            return pair_value_property == null ? null : pair_value_property.GetValue(pair, null);
        }

        private static IDictionary SubmissionsOf(object group)
        {
            if (group == null)
            {
                return null;
            }

            if (submissions_field == null || submissions_owner != group.GetType())
            {
                submissions_owner = group.GetType();
                submissions_field = submissions_owner.GetField("submissionListsPerCamSetup", Any);
            }

            return submissions_field == null ? null : submissions_field.GetValue(group) as IDictionary;
        }

        /// <summary>
        /// Captures this cameras render state and wakes the collection threads. A null camera means
        /// every camera in the scene.
        /// </summary>
        internal static bool Recollect(Camera cam)
        {
            object singleton = Singleton();

            if (singleton == null)
            {
                return false;
            }

            // The all-camera path waits on the deferred-collection handle with no timeout, which is
            // not something to risk from here; the handle is manual-reset, so a zero-time wait reads
            // it without consuming it.
            if (cam == null && !DeferredThreadFinished(singleton))
            {
                return false;
            }

            try
            {
                prepare_args[0] = cam;
                prepare_method.Invoke(singleton, prepare_args);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Walks the cache for this camera and issues the draw calls. Nothing else reaches this: the
        /// renderer calls it only from its own pre-cull handler, so this is the only way to put a
        /// camera the renderer dropped back on screen for the frame it dropped it.
        /// </summary>
        internal static bool Submit(Camera cam)
        {
            object singleton = Singleton();

            if (cam == null || singleton == null)
            {
                return false;
            }

            try
            {
                submit_args[0] = cam;
                submit_method.Invoke(singleton, submit_args);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool DeferredThreadFinished(object singleton)
        {
            try
            {
                if (deferred_handle_field == null)
                {
                    return false;
                }

                System.Threading.WaitHandle handle =
                    deferred_handle_field.GetValue(singleton) as System.Threading.WaitHandle;

                return handle != null && handle.WaitOne(0);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Compensates for GHPC dropping a cameras instanced batches.
    ///
    /// The renderers read is an exact match on a key built from the cameras whole render state -
    /// mask, field of view, aspect, both clip planes, position and rotation - and its collection runs
    /// on worker threads, so a camera that has moved since the collection can never be found again.
    /// Nothing here touches that table: the collection threads own it, and a mod writing to it races
    /// them. That race killed the worker thread twice - once leaving a null in the table, once
    /// disposing a list twice - and took the whole maps vegetation with it both times.
    ///
    /// So this works around the renderer instead of inside it. It watches the games own complaint -
    /// "Unexpected failure to acquire {type} instances for camera {name}" - which says both that there
    /// is a problem and, more usefully, which camera has it; then it submits for that camera from
    /// inside its own pre-cull, after the renderers handler has had its turn.
    ///
    /// There is no "sight camera" to look up. GHPC renders an optic through the main camera and a
    /// CameraSlot owns no camera of its own (CameraManager.MainCam, and its fov and culling mask are
    /// what the slot changes), so the camera worth compensating is whatever the game names, and the
    /// name is resolved against the scenes cameras at the moment it complains.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class CrowsInstancingAssist : MonoBehaviour
    {
        private const string FAILURE_MARKER = "Unexpected failure to acquire";
        private const string CAMERA_MARKER = "for camera ";
        private const float CHECK_SECONDS = 1f;
        private const int HITS_TO_ARM = 8;

        private static string reported_camera;
        private static int reported_hits;
        private static bool hooked;
        private static int instances;
        private static Camera[] camera_buffer = new Camera[8];
        private static int camera_buffer_used;

        private readonly HashSet<int> assist_ids = new HashSet<int>();

        private float timer;
        private float warm_timer;
        private bool armed;
        private bool finished;
        // OnEnable/OnDisable may run many times while a vehicle is reused between scenes.  Keep one
        // registration per component so the static event is released when the last active component
        // goes away instead of waiting for a final OnDestroy that may never arrive during a reload.
        private bool counted;

        public static void Attach(GameObject host)
        {
            if (host != null)
            {
                host.AddComponent<CrowsInstancingAssist>();
            }
        }

        private void OnEnable()
        {
            if (!counted)
            {
                instances++;
                counted = true;
            }

            // One hook for the whole session, not one per station: the messages being watched are the
            // games, not this components. This is a *reader* of the games log, not a writer - the name
            // in the renderers own complaint is the only way it knows which camera was dropped.
            if (!hooked)
            {
                hooked = true;
                Application.logMessageReceived += NoteLogMessage;
            }
        }

        private void OnDestroy()
        {
            Camera.onPreCull -= HandlePreCull;
            ReleaseRegistration();
        }

        private void OnDisable()
        {
            Camera.onPreCull -= HandlePreCull;
            assist_ids.Clear();
            armed = false;
            warm_timer = 0f;
            ReleaseRegistration();
        }

        private void ReleaseRegistration()
        {
            if (!counted)
            {
                return;
            }

            counted = false;

            if (instances > 0)
            {
                instances--;
            }

            if (instances == 0 && hooked)
            {
                Application.logMessageReceived -= NoteLogMessage;
                hooked = false;
                reported_camera = null;
                reported_hits = 0;
            }
        }


        /// <summary>
        /// Reads the renderers own complaint - "Unexpected failure to acquire {type} instances for camera
        /// {name}" - for the one thing it carries that cannot be had any other way: which camera was
        /// dropped. Nothing is written back to the log; the name and a count are all this keeps.
        /// </summary>
        private static void NoteLogMessage(string message, string stack, LogType type)
        {
            if (message == null)
            {
                return;
            }

            int at = message.IndexOf(FAILURE_MARKER, StringComparison.Ordinal);

            if (at < 0)
            {
                return;
            }

            int named = message.IndexOf(CAMERA_MARKER, at, StringComparison.Ordinal);

            if (named < 0)
            {
                return;
            }

            int start = named + CAMERA_MARKER.Length;
            int end = message.IndexOf(" (", start, StringComparison.Ordinal);

            if (end <= start)
            {
                return;
            }

            reported_camera = message.Substring(start, end - start);
            reported_hits++;
        }

        /// <summary>
        /// Last in the frame, after the renderers own LateUpdate - which is what re-subscribes this
        /// handler behind the renderers, so that it runs after the renderers pre-cull and can submit
        /// for a camera the renderer has just dropped.
        /// </summary>
        private void LateUpdate()
        {
            if (armed)
            {
                Camera.onPreCull -= HandlePreCull;
                Camera.onPreCull += HandlePreCull;
            }

            if (finished)
            {
                return;
            }

            if (!CrowsInstancing.Usable)
            {
                finished = true;
                return;
            }

            timer += Time.deltaTime;

            // Keep-warm: a collection for the dropped camera every second, so the renderer has an entry
            // of its own to find when it looks. Harmless when it turns out not to need one.
            if (armed && reported_camera != null)
            {
                warm_timer += Time.deltaTime;

                if (warm_timer >= CHECK_SECONDS)
                {
                    warm_timer = 0f;
                    Camera warm = FindByName(reported_camera);

                    if (warm != null)
                    {
                        CrowsInstancing.Recollect(warm);
                    }
                }
            }

            if (timer < CHECK_SECONDS)
            {
                return;
            }

            timer = 0f;

            int hits = reported_hits;
            reported_hits = 0;

            if (armed || reported_camera == null || hits < HITS_TO_ARM)
            {
                return;
            }

            Arm();
        }

        private void Arm()
        {
            int camera_count = GetCameras();
            int found = 0;

            // Camera instance IDs from an unloaded scene must not remain in this per-component
            // cache.  Clearing before each arm keeps the set bounded by the current scene cameras.
            assist_ids.Clear();

            for (int i = 0; i < camera_count; i++)
            {
                Camera camera = camera_buffer[i];

                if (camera == null || camera.name != reported_camera)
                {
                    continue;
                }

                assist_ids.Add(camera.GetInstanceID());
                found++;
            }

            if (found == 0)
            {
                return;
            }

            armed = true;
        }

        private void HandlePreCull(Camera cam)
        {
            if (!armed || cam == null || !assist_ids.Contains(cam.GetInstanceID()))
            {
                return;
            }

            if (!CrowsInstancing.Recollect(cam))
            {
                return;
            }

            CrowsInstancing.Submit(cam);
        }

        private static Camera FindByName(string name)
        {
            int camera_count = GetCameras();

            for (int i = 0; i < camera_count; i++)
            {
                Camera camera = camera_buffer[i];

                if (camera != null && camera.name == name)
                {
                    return camera;
                }
            }

            return null;
        }

        private static int GetCameras()
        {
            int wanted = Camera.allCamerasCount;

            if (camera_buffer.Length < wanted)
            {
                int capacity = camera_buffer.Length;
                while (capacity < wanted)
                {
                    capacity *= 2;
                }

                Array.Resize(ref camera_buffer, capacity);
            }

            int count = Camera.GetAllCameras(camera_buffer);

            for (int i = count; i < camera_buffer_used; i++)
            {
                camera_buffer[i] = null;
            }

            camera_buffer_used = count;
            return count;
        }

    }
}





