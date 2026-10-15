using System;
using System.Collections.Generic;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace M1A1Abrams
{
    internal static partial class CROWS
    {
        /// <summary>
        /// Copies the configured gunner FLIR pipeline, keeping volume activation local to this optic.
        /// </summary>
        private static void ConfigureMainGunThermal(UsableOptic optic, UsableOptic main_gun)
        {
            Transform source_post = main_gun.transform.Find("FLIR Post Processing - Green");
            if (source_post == null)
                throw new InvalidOperationException("Abrams gunner FLIR post-processing is missing");

            CameraSlot source_slot = main_gun.slot;
            CameraSlot slot = optic.slot;
            slot.OverrideFLIRResolution = source_slot.OverrideFLIRResolution;
            slot.FLIRWidth = source_slot.FLIRWidth;
            slot.FLIRHeight = source_slot.FLIRHeight;
            slot.FLIRFilterMode = source_slot.FLIRFilterMode;
            slot.FLIRBlitMaterialOverride = source_slot.FLIRBlitMaterialOverride;
            slot.DoMechanicalFLIR = source_slot.DoMechanicalFLIR;
            slot.MechanicalScanWidth = source_slot.MechanicalScanWidth;
            slot.MechanicalScanHeight = source_slot.MechanicalScanHeight;
            slot.CanToggleFlirPolarity = source_slot.CanToggleFlirPolarity;
            slot.BaseBlur = source_slot.BaseBlur;
            slot.VibrationPreBlur = source_slot.VibrationPreBlur;

            // The M85 donor has a day-sight volume on the slot itself. It must not
            // be blended into the thermal camera or keep its old optic binding.
            foreach (PostProcessVolume donor in slot.GetComponentsInChildren<PostProcessVolume>(true))
                UnityEngine.Object.DestroyImmediate(donor);

            GameObject post = UnityEngine.Object.Instantiate(source_post.gameObject, slot.transform, false);
            post.SetActive(true);
            PostProcessVolume[] source_volumes = source_post.GetComponentsInChildren<PostProcessVolume>(true);
            PostProcessVolume[] volumes = post.GetComponentsInChildren<PostProcessVolume>(true);
            var volume_map = new Dictionary<PostProcessVolume, PostProcessVolume>();
            // Bounded by the shorter array: the clone is expected to mirror the source one for one, but
            // indexing past the end of a mismatched pair threw and took the whole mount with it.
            int n = Mathf.Min(source_volumes.Length, volumes.Length);
            for (int i = 0; i < n; i++)
            {
                // Include any runtime changes to the source profile. Each clone
                // owns its enabled state; profiles and materials are read-only here.
                volumes[i].sharedProfile = source_volumes[i].profile;
                volumes[i].enabled = false;
                // Assign rather than Add: a duplicate key in the source hierarchy threw ArgumentException.
                volume_map[source_volumes[i]] = volumes[i];
            }

            // UsableOptic.Start subscribes to ActiveChanged and controls both
            // MainCam and FLIR Only volumes, exactly as it does for the main gun.
            // TryGetValue rather than an indexer: a volume the clone does not carry left the old code
            // throwing KeyNotFoundException out of here, which Mount() reported as no CROWS at all.
            optic.post = main_gun.post != null && volume_map.TryGetValue(main_gun.post, out PostProcessVolume mapped)
                ? mapped : null;
            optic.AltNightPost = main_gun.AltNightPost != null && volume_map.TryGetValue(main_gun.AltNightPost, out PostProcessVolume alt_mapped)
                ? alt_mapped : null;
            // Filtered, not converted: Array.ConvertAll indexes the map for every element and a single
            // unmapped extra volume was enough to lose the optic entirely.
            List<PostProcessVolume> additional = new List<PostProcessVolume>(main_gun.postAdditional.Length);
            for (int i = 0; i < main_gun.postAdditional.Length; i++)
            {
                if (main_gun.postAdditional[i] != null && volume_map.TryGetValue(main_gun.postAdditional[i], out PostProcessVolume extra))
                {
                    additional.Add(extra);
                }
            }
            optic.postAdditional = additional.ToArray();
        }
    }
}
