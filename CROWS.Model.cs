using System;
using System.Collections.Generic;
using System.IO;
using GHPC;
using GHPC.Camera;
using GHPC.Effects;
using GHPC.Equipment.Optics;
using GHPC.Player;
using GHPC.Thermals;
using GHPC.Utility;
using GHPC.Vehicle;
using GHPC.Weapons;
using GHPC.Weaponry;
using MelonLoader;
using MelonLoader.Utils;
using ModUtil;
using Reticle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VUtil = VanillaAssetUtil.ModUtil.Util;

namespace M1A1Abrams
{
    /// <summary>
    /// M153 CROWS II remote weapon station.
    ///
    /// Builds the station on the player's own Abrams: the .50 cal, its gunsight and its fire control
    /// computer are cloned from the cupola that is already on the vehicle, and the reticle tree and
    /// the range-box canvas are constructed in code. No other vehicle (M60A3, T-55A, M2 Bradley) has
    /// to be in the mission. Every non-public member is reached through ModUtil.Reflect: the running
    /// game loads the official (non-publicized) Assembly-CSharp.
    /// </summary>
    internal static partial class CROWS
    {
        private static void LoadModel()
        {
            // Two models live in this mod: the donor .50 cal station (0.75 scale, its own base
            // plate height) and the Mk 19 rebuild (1:1). The weapon option decides which one is
            // loaded, so switching back to the M2 puts the original station back instead of an
            // Mk 19 firing .50 cal rounds.
            bool mk19 = M1A1.crows_mk19 != null && M1A1.crows_mk19.Value;
            string bundle_name = mk19 ? "crowmk19" : "crows";
            string asset_name = mk19 ? "crowmk19.prefab" : "CROWS.prefab";
            float model_scale = mk19 ? 1f : MODEL_SCALE;

            string path = Path.Combine(MelonEnvironment.ModsDirectory, ASSET_FOLDER, bundle_name);

            if (!File.Exists(path))
            {
                Fail("asset bundle not found: " + path);
                return;
            }

            AssetBundle bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null)
            {
                Fail("could not open asset bundle: " + path);
                return;
            }

            try
            {
                crows_prefab = bundle.LoadAsset<GameObject>(asset_name);
                if (crows_prefab == null)
                {
                    Fail(asset_name + " missing from " + path);
                    return;
                }

                crows_prefab.hideFlags = HideFlags.DontUnloadUnusedAsset;
                crows_prefab.transform.localScale = new Vector3(model_scale, model_scale, model_scale);

                Transform gun_col = crows_prefab.transform.Find("GUN/GUN COLLIDER");
                Transform mount_col = crows_prefab.transform.Find("MOUNT/MOUNT COLLIDER");
                Transform gun_vis_transform = crows_prefab.transform.Find("GUN/GUN VIS");
                Transform mount_vis_transform = crows_prefab.transform.Find("MOUNT/MOUNT VIS");

                if (gun_col == null || mount_col == null || gun_vis_transform == null || mount_vis_transform == null)
                {
                    crows_prefab = null;
                    Fail("CROWS.prefab is missing its collider or visual hierarchy");
                    return;
                }

                GameObject gun_vis = gun_vis_transform.gameObject;
                GameObject mount_vis = mount_vis_transform.gameObject;

                gun_col.tag = "Penetrable";
                mount_col.tag = "Penetrable";
                gun_col.gameObject.layer = 7;
                mount_col.gameObject.layer = 7;

                MeshRenderer gun_renderer = gun_vis.GetComponent<MeshRenderer>();
                MeshRenderer mount_renderer = mount_vis.GetComponent<MeshRenderer>();
                Shader flir_shader = Shader.Find("Standard (FLIR)");

                if (gun_renderer == null || mount_renderer == null
                    || gun_renderer.sharedMaterial == null || mount_renderer.sharedMaterial == null
                    || flir_shader == null)
                {
                    crows_prefab = null;
                    Fail("CROWS.prefab is missing a renderer or the Standard (FLIR) shader");
                    return;
                }

                gun_renderer.sharedMaterial.shader = flir_shader;
                mount_renderer.sharedMaterial.shader = flir_shader;
                crows_prefab.AddComponent<HeatSource>().Heat = 5f;
            }
            finally
            {
                // The prefab and its dependencies stay loaded through their references; the bundle
                // itself no longer needs to keep its file mapping and native decompression state.
                bundle.Unload(false);
            }
        }

        /// <summary>
        /// Takes the vanilla commander's cupola station off the vehicle the CROWS is going onto: the
        /// .50 cal, its sight and its fire control are cloned from there, so no other vehicle has to
        /// be in the mission. Resolved per mount rather than once per mission, because these are live
        /// scene objects and die with the vehicle.
        /// </summary>
        private static void AddFrameBoxes(UsableOptic optic, Transform model_root)
        {
            if (box_canvas == null)
            {
                return;
            }

            if (optic == null)
            {
                return;
            }

            for (int i = 0; i < borders.Count; i++)
            {
                GameObject box = UnityEngine.Object.Instantiate(box_canvas, optic.transform);
                SanitizeBorrowed(box, model_root);

                // The template is built in code with exactly one child, but the check costs nothing and
                // keeps a broken template from throwing here and taking the remaining strips with it.
                Transform child = box.transform.childCount > 0 ? box.transform.GetChild(0) : null;

                if (child != null)
                {
                    child.localPosition = borders[i][0];
                    child.localEulerAngles = borders[i][1];
                }

                if (i == 2 || i == 3)
                {
                    CanvasScaler scaler = box.GetComponent<CanvasScaler>();
                    if (scaler != null)
                    {
                        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Shrink;
                    }
                }

                box.SetActive(true);
            }
        }

        /// <summary>
        /// Removes the donor scripts that would move a cloned object out of the CROWS model.
        /// Reparent.Awake() does `transform.parent = NewParent` and the donor's NewParent points at its
        /// own tank, which is how the borrowed gun ended up inside an inactive prefab hierarchy where
        /// nothing on it ran; LateFollow would drag a part after a donor object the same way.
        /// </summary>
        private static void SanitizeBorrowed(GameObject borrowed, Transform model_root)
        {
            if (borrowed == null)
            {
                return;
            }

            foreach (Reparent reparent in borrowed.GetComponentsInChildren<Reparent>(true))
            {
                // DestroyImmediate on purpose: Destroy() would let Awake() run first.
                UnityEngine.Object.DestroyImmediate(reparent);
            }

            foreach (LateFollow follow in borrowed.GetComponentsInChildren<LateFollow>(true))
            {
                Transform target = follow.FollowTarget;

                if (target != null && target.IsChildOf(model_root) && !follow.ForceToRoot)
                {
                    continue;
                }

                UnityEngine.Object.DestroyImmediate(follow);
            }
        }

        /// <summary>
        /// Activates a cloned object and every ancestor up to (but not including) the CROWS model, so
        /// the components on it get their Awake/Start pass together with the model.
        /// </summary>
        private static void EnsureActive(GameObject target, Transform model_root)
        {
            if (target == null)
            {
                return;
            }

            for (Transform t = target.transform; t != null && t != model_root; t = t.parent)
            {
                if (!t.gameObject.activeSelf)
                {
                    t.gameObject.SetActive(true);
                }
            }
        }

        private static bool IsExternal(UnityEngine.Object value, Transform model_root)
        {
            if (value == null)
            {
                return false;
            }

            Component component = value as Component;
            GameObject game_object = value as GameObject;
            Transform transform = component != null
                ? component.transform
                : (game_object != null ? game_object.transform : null);

            // Assets (materials, meshes, scriptable objects) have no transform.
            return transform != null && !transform.IsChildOf(model_root);
        }

        /// <summary>
        /// No longer any such thing: the station takes what it needs off the vehicle it is mounted on,
        /// so nothing is ever pulled into the scene to be borrowed from. Kept because the tracker asks,
        /// and the answer still matters there - a donor would not be a target, and drawing a box on
        /// one would bracket a vehicle that is not in the level.
        /// </summary>
    }
}




