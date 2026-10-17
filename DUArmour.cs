using System;
using GHPC.Equipment;
using UnityEngine;
using System.IO;
using MelonLoader.Utils;
using ModUtil;

namespace M1A1Abrams
{
    public class DUArmour : Module
    {
        static Material gen1_du_aar_mat;
        static ArmorCodexScriptable gen1_du_armor_codex;

        static Material gen2_du_aar_mat;
        static ArmorCodexScriptable gen2_du_armor_codex;

        static Material gen3_du_aar_mat;
        static ArmorCodexScriptable gen3_du_armor_codex;

        public static Material[] du_aar_mats;
        public static ArmorCodexScriptable[] du_armor_codexes;

        public override void LoadStaticAssets()
        {
            string path = Path.Combine(MelonEnvironment.ModsDirectory, "m1a1crowsfix_assets", "du_mats");
            AssetBundle mats = AssetBundle.LoadFromFile(path);

            if (mats == null)
            {
                throw new InvalidOperationException("could not open asset bundle: " + path);
            }

            try
            {
                gen1_du_aar_mat = LoadMaterial(mats, "gen1.mat", path);
                gen2_du_aar_mat = LoadMaterial(mats, "gen2.mat", path);
                gen3_du_aar_mat = LoadMaterial(mats, "gen3.mat", path);
            }
            finally
            {
                mats.Unload(false);
            }

            gen1_du_armor_codex = ScriptableObject.CreateInstance<ArmorCodexScriptable>();
            gen1_du_armor_codex.name = "Gen 1 Abrams DU composite";

            gen2_du_armor_codex = ScriptableObject.CreateInstance<ArmorCodexScriptable>();
            gen2_du_armor_codex.name = "Gen 2 Abrams DU composite";

            gen3_du_armor_codex = ScriptableObject.CreateInstance<ArmorCodexScriptable>();
            gen3_du_armor_codex.name = "Gen 3 Abrams DU composite";

            ArmorType gen1 = new ArmorType();
            gen1.Name = "special armor";
            gen1.CanRicochet = true;
            gen1.CanShatterLongRods = true;
            gen1.NormalizesHits = true;
            gen1.ThicknessSource = ArmorType.RhaSource.Multipliers;
            gen1.SpallAngleMultiplier = 1;
            gen1.SpallPowerMultiplier = 0.80f;
            gen1.RhaeMultiplierCe = 1.4f;
            gen1.RhaeMultiplierKe = 0.75f;
            gen1_du_armor_codex.ArmorType = gen1;

            ArmorType gen2 = new ArmorType();
            gen2.Name = "special armor";
            gen2.CanRicochet = true;
            gen2.CanShatterLongRods = true;
            gen2.NormalizesHits = true;
            gen2.ThicknessSource = ArmorType.RhaSource.Multipliers;
            gen2.SpallAngleMultiplier = 1;
            gen2.SpallPowerMultiplier = 0.80f;
            gen2.RhaeMultiplierCe = 1.55f;
            gen2.RhaeMultiplierKe = 0.85f;
            gen2_du_armor_codex.ArmorType = gen2;

            ArmorType gen3 = new ArmorType();
            gen3.Name = "special armor";
            gen3.CanRicochet = true;
            gen3.CanShatterLongRods = true;
            gen3.NormalizesHits = true;
            gen3.ThicknessSource = ArmorType.RhaSource.Multipliers;
            gen3.SpallAngleMultiplier = 1;
            gen3.SpallPowerMultiplier = 0.80f;
            gen3.RhaeMultiplierCe = 1.75f;
            gen3.RhaeMultiplierKe = 0.96f;
            gen3_du_armor_codex.ArmorType = gen3;

            du_aar_mats = new Material[] { gen1_du_aar_mat, gen2_du_aar_mat, gen3_du_aar_mat };
            du_armor_codexes = new ArmorCodexScriptable[] { gen1_du_armor_codex, gen2_du_armor_codex, gen3_du_armor_codex };
        }

        private static Material LoadMaterial(AssetBundle bundle, string asset_name, string bundle_path)
        {
            Material material = bundle.LoadAsset<Material>(asset_name);

            if (material == null)
            {
                throw new InvalidOperationException("asset '" + asset_name + "' is missing from " + bundle_path);
            }

            material.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return material;
        }
    }
}
