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
        // Every bundle we have loaded stays loaded for the session: a live station's materials point
        // into them and a released texture renders as a bare white surface (see LoadModel's finally).
        private static readonly List<AssetBundle> held_bundles = new List<AssetBundle>();

        // Which weapon the currently loaded prefab belongs to. Init() compares this against the
        // preference so switching .50 cal <-> Mk 19 reloads the model instead of mounting the old one
        // with the new weapon's sight and ammo.
        private static bool loaded_mk19;

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

                if (gun_renderer == null || mount_renderer == null
                    || gun_renderer.sharedMaterial == null || mount_renderer.sharedMaterial == null)
                {
                    crows_prefab = null;
                    Fail("CROWS.prefab is missing a renderer or material");
                    return;
                }

                // Only the Mk 19 bundle needs this. The donor .50 cal station (bundle "crows") ships
                // materials that already render correctly against this player, and rebuilding those
                // throws away their _MetallicGlossMap/_OcclusionMap - which is what turned the M2
                // station dark and plasticky. The two models are handled separately on purpose.
                if (mk19)
                {
                    RebuildMaterials(crows_prefab);
                }

                // The bundle ships its own Standard-shader materials with baked albedo/normal maps.
                // Overwriting them with "Standard (FLIR)" made gun and belt render as flat white in
                // the normal view, so only the thermal marker component is added here.
                crows_prefab.AddComponent<HeatSource>().Heat = 5f;
            }
            finally
            {
                // Do NOT unload. The prefab is instantiated much later, in MountInternal, and a
                // material only pulls its textures in when it is first drawn. Unloading here left the
                // station with live materials but dead texture objects, so it rendered as bare white
                // _Color under lighting - which is exactly the "the whole CROWS is white" report.
                // Both bundles are a few hundred kB, so they are simply kept for the session.
                if (crows_prefab != null)
                {
                    loaded_mk19 = mk19;
                    if (!held_bundles.Contains(bundle))
                    {
                        held_bundles.Add(bundle);
                    }
                }
                else
                {
                    bundle.Unload(true);
                }
            }
        }

        /// <summary>
        /// Rebuilds the station's solid materials on the game's own Standard shader and re-binds the
        /// bundle's albedo, normal and packed metal/gloss maps by hand.
        ///
        /// The Mk 19 bundle carries its own copy of Standard, compiled when the bundle was built.
        /// Against this player it does not sample the maps: gun and mount both came out as flat pale
        /// grey (pixel deviation inside a single face is ~0, and changing _Color changes them, so the
        /// shader runs - it just never reads the texture). Swapping in the shader the game itself uses
        /// and binding the maps explicitly fixes the surface without touching the artwork.
        ///
        /// Materials with no albedo (the muzzle-flash particle materials) are left untouched.
        /// </summary>
        private static void RebuildMaterials(GameObject root)
        {
            Shader standard = Shader.Find("Standard");
            if (standard == null)
            {
                Fail("Standard shader is not available, station keeps its bundled materials");
                return;
            }

            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] materials = renderers[i].sharedMaterials;
                bool replaced = false;

                for (int m = 0; m < materials.Length; m++)
                {
                    Material source = materials[m];
                    if (source == null || !source.HasProperty("_MainTex"))
                    {
                        continue;
                    }

                    Texture albedo = source.GetTexture("_MainTex");
                    if (albedo == null)
                    {
                        continue;
                    }

                    Texture normal = source.HasProperty("_BumpMap") ? source.GetTexture("_BumpMap") : null;
                    Texture metallic_gloss = source.HasProperty("_MetallicGlossMap") ? source.GetTexture("_MetallicGlossMap") : null;
                    Texture occlusion = source.HasProperty("_OcclusionMap") ? source.GetTexture("_OcclusionMap") : null;

                    Material rebuilt = new Material(standard);
                    rebuilt.name = source.name;
                    rebuilt.SetTexture("_MainTex", albedo);
                    if (normal != null)
                    {
                        rebuilt.SetTexture("_BumpMap", normal);
                        rebuilt.EnableKeyword("_NORMALMAP");
                    }
                    // A source with a packed metallic-gloss map must keep it: copying only the scalar
                    // _Metallic/_Glossiness never reproduces a mapped surface.
                    if (metallic_gloss != null)
                    {
                        rebuilt.SetTexture("_MetallicGlossMap", metallic_gloss);
                        rebuilt.EnableKeyword("_METALLICGLOSSMAP");
                    }
                    if (occlusion != null)
                    {
                        rebuilt.SetTexture("_OcclusionMap", occlusion);
                        rebuilt.EnableKeyword("_OCCLUSIONMAP");
                    }
                    rebuilt.SetColor("_Color", source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white);
                    rebuilt.SetFloat("_Glossiness", source.HasProperty("_Glossiness") ? source.GetFloat("_Glossiness") : 0.35f);
                    rebuilt.SetFloat("_Metallic", source.HasProperty("_Metallic") ? source.GetFloat("_Metallic") : 0f);

                    materials[m] = rebuilt;
                    replaced = true;
                }

                if (replaced)
                {
                    renderers[i].sharedMaterials = materials;
                }
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




