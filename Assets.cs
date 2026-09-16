using System;
using System.IO;
using System.Linq;
using GHPC.Camera;
using GHPC.Vehicle;
using MelonLoader.Utils;
using UnityEngine;
using GHPC.Weaponry;
using ModUtil;
using GHPC.Thermals;
using GHPC;
using GHPC.Weapons;

namespace M1A1Abrams
{
    internal class Assets : Module
    {
        public static AmmoType ammo_m833;
        public static AmmoType ammo_m456;

        public static GameObject flir_post;
        public static Material flir_blit_mat_green;
        public static Material flir_blit_mat_green_no_scan;

        public static GameObject citv_monitor;

        public static GameObject citv_obj;
        public static GameObject m256_obj;

        public override void UnloadDynamicAssets()
        {
            if (flir_blit_mat_green_no_scan != null)
            {
                Material.DestroyImmediate(flir_blit_mat_green_no_scan);
            }

            flir_blit_mat_green_no_scan = null;
            flir_blit_mat_green = null;
            flir_post = null;
        }

        public override void LoadDynamicAssets()
        {
            UnloadDynamicAssets();

            if (!AssetUtil.VehicleInMission("_M1IP (variant)") && !AssetUtil.VehicleInMission("_M1 (variant)"))
            {
                DynamicAssetLoadSucceeded = false;
                return;
            }

            Vehicle vic;
            if (AssetUtil.VehicleInMission("_M1IP (variant)"))
            {
                vic = AssetUtil.LoadVanillaVehicle("M1IP");
            } 
            else
            {
                vic = AssetUtil.LoadVanillaVehicle("M1");
            }

            FireControlSystem fcs = vic == null ? null : vic.GetComponentInChildren<FireControlSystem>(true);
            Transform flir = fcs == null ? null : fcs.transform.Find("FLIR");
            CameraSlot slot = flir == null ? null : flir.GetComponent<CameraSlot>();
            Transform post = flir == null ? null : flir.Find("FLIR Post Processing - Green");

            if (flir == null || slot == null || post == null || slot.FLIRBlitMaterialOverride == null)
            {
                DynamicAssetLoadSucceeded = false;
                return;
            }

            try
            {
                Shader no_scan_shader = Shader.Find("Blit (FLIR)/Blit Simple");

                if (no_scan_shader == null)
                {
                    DynamicAssetLoadSucceeded = false;
                    return;
                }

                flir_post = post.gameObject;
                flir_blit_mat_green = slot.FLIRBlitMaterialOverride;
                flir_blit_mat_green_no_scan = new Material(no_scan_shader);
                flir_blit_mat_green_no_scan.CopyPropertiesFromMaterial(flir_blit_mat_green);
                flir_blit_mat_green_no_scan.SetTexture("_PixelCookie", null);
            }
            catch
            {
                UnloadDynamicAssets();
                throw;
            }
        }

        public override void LoadStaticAssets()
        {
            ammo_m833 = FindAmmo("ammo_M833");
            ammo_m456 = FindAmmo("ammo_M456");

            if (ammo_m833 == null || ammo_m456 == null)
            {
                throw new InvalidOperationException("required 120 mm ammunition was not found");
            }

            citv_monitor = LoadBundleAsset<GameObject>("citv_monitor", "CITV MONITOR");
            citv_obj = LoadBundleAsset<GameObject>("citv", "citv.prefab");
            citv_obj.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);

            GameObject assem = citv_obj.transform.Find("assembly").gameObject;
            GameObject glass = citv_obj.transform.Find("glass").gameObject;

            MeshRenderer assem_renderer = assem.GetComponent<MeshRenderer>();
            MeshRenderer glass_renderer = glass.GetComponent<MeshRenderer>();
            Shader flir_shader = Shader.Find("Standard (FLIR)");

            if (assem_renderer == null || glass_renderer == null
                || assem_renderer.sharedMaterial == null || glass_renderer.sharedMaterial == null
                || flir_shader == null)
            {
                throw new InvalidOperationException("CITV prefab is missing its FLIR materials or shader");
            }

            assem_renderer.sharedMaterial.shader = flir_shader;
            glass_renderer.sharedMaterial.shader = flir_shader;
            citv_obj.AddComponent<HeatSource>().Heat = 0.5f;

            GameObject assem_armour = assem.transform.Find("ARMOUR").gameObject;
            GameObject glass_armour = glass.transform.Find("ARMOUR").gameObject;

            assem_armour.tag = "Penetrable";
            glass_armour.tag = "Penetrable";
            assem_armour.layer = 8;
            glass_armour.layer = 8;

            UniformArmor assem_u_armour = assem.AddComponent<UniformArmor>();
            UniformArmor glass_u_armour = glass.AddComponent<UniformArmor>();
            assem_u_armour.PrimarySabotRha = 40f;
            assem_u_armour.PrimaryHeatRha = 40f;

            glass_u_armour.PrimarySabotRha = 5f;
            glass_u_armour.PrimaryHeatRha = 5f;

            assem_u_armour.SetName("CITV");
            glass_u_armour.SetName("CITV glass");

            m256_obj = LoadBundleAsset<GameObject>("m256", "m256.prefab");
            m256_obj.transform.localScale = new Vector3(0.75f, 0.75f, 0.8f);
            MeshRenderer m256_renderer = m256_obj.GetComponent<MeshRenderer>();

            if (m256_renderer == null || m256_renderer.sharedMaterial == null || flir_shader == null)
            {
                throw new InvalidOperationException("m256 prefab is missing its FLIR material or shader");
            }

            m256_renderer.sharedMaterial.shader = flir_shader;
            m256_obj.AddComponent<HeatSource>().Heat = 0.5f;
        }

        private static AmmoType FindAmmo(string name)
        {
            AmmoCodexScriptable codex = Resources.FindObjectsOfTypeAll<AmmoCodexScriptable>()
                .FirstOrDefault(o => o != null && o.name == name);
            return codex == null ? null : codex.AmmoType;
        }

        private static T LoadBundleAsset<T>(string bundle_name, string asset_name) where T : UnityEngine.Object
        {
            string path = Path.Combine(MelonEnvironment.ModsDirectory, "m1a1assets", bundle_name);
            AssetBundle bundle = AssetBundle.LoadFromFile(path);

            if (bundle == null)
            {
                throw new InvalidOperationException("could not open asset bundle: " + path);
            }

            try
            {
                T asset = bundle.LoadAsset<T>(asset_name);

                if (asset == null)
                {
                    throw new InvalidOperationException("asset '" + asset_name + "' is missing from " + path);
                }

                asset.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return asset;
            }
            finally
            {
                // Loaded objects remain valid; releasing the bundle drops its file and decompression
                // state, which is otherwise retained for the whole process by the local variable's
                // native handle.
                bundle.Unload(false);
            }
        }
    }
}
