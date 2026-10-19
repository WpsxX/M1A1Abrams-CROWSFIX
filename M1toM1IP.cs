using System;
using GHPC.AI;
using GHPC.Mission.Data;
using GHPC.Mission;
using GHPC;
using UnityEngine;
using HarmonyLib;
using ModUtil;
using Presets;
using M1A1Abrams.Presets;
using GHPC.Vehicle;

namespace M1A1Abrams
{
    public class PreviouslyM1 : MonoBehaviour {}

    public class TransmogState<T>
    {
        public bool TransmogNeeded { get; set; }
        public T Preset { get; set; }
    }

    // UnitSpawner.SpawnUnit is the most contested patch target in the mod scene: Fulda 1989 patches
    // it four times (LEO2A4 / LEO2K / T72BU / TrackingDimensions). Harmony runs prefixes in priority
    // order, so this one is deliberately Low to run after everyone else, and both halves are wrapped
    // so an exception here cannot abort the other mods' spawn handling (nor theirs ours).
    [HarmonyPatch(typeof(UnitSpawner), "SpawnUnit", new Type[] { typeof(string), typeof(UnitMetaData), typeof(WaypointHolder), typeof(Transform) })]
    [HarmonyPriority(Priority.Low)]
    public static class M1toM1IP
    {
        private static void Prefix(out TransmogState<M1Preset> __state, UnitSpawner __instance, ref string uniqueName)
        {
            __state = new TransmogState<M1Preset>();

            try
            {
                if (uniqueName != "M1")
                {
                    return;
                }

                bool use_preset = M1A1.preset_manager_m1 != null;
                bool transmog = M1A1.m1_to_m1ip.Value;
                M1Preset preset = null;

                if (use_preset)
                {
                    PresetManager<M1Preset> preset_manager = M1A1.preset_manager_m1;
                    preset = preset_manager.ChoosePreset();
                    if (preset != null)
                    {
                        transmog = preset.ConvertToM1IP;
                    }
                }

                if (transmog)
                {
                    __state.TransmogNeeded = true;
                    __state.Preset = preset;

                    // Permanent load, not TempLoad: the temp pool is released on PlayerReady, which
                    // fires before units spawn, so every mission used to leak one copy of the vanilla
                    // M1 prefab. LoadVanillaVehicle is cached and already warm from the menu scene.
                    AssetUtil.LoadVanillaVehicle("M1");
                    uniqueName = "M1IP";
                }
            }
            catch (Exception e)
            {
                __state.TransmogNeeded = false;
                __state.Preset = null;
                MelonLoader.MelonLogger.Warning("[M1A1] M1 to M1IP spawn hook skipped: " + e);
            }
        }

        private static void Postfix(TransmogState<M1Preset> __state, ref IUnit __result)
        {
            if (__state == null || !__state.TransmogNeeded || __result == null)
            {
                return;
            }

            try
            {
                Transform unit_root = __result.transform;
                Vehicle vehicle = unit_root.GetComponent<Vehicle>()
                    ?? unit_root.GetComponentInChildren<Vehicle>(true);
                GameObject marker_target = vehicle != null ? vehicle.gameObject : unit_root.gameObject;

                PreviouslyM1 comp = marker_target.GetComponent<PreviouslyM1>()
                    ?? marker_target.AddComponent<PreviouslyM1>();
                comp.enabled = false;

                if (__state.Preset != null)
                {
                    PresetMarker marker = marker_target.GetComponent<PresetMarker>()
                        ?? marker_target.AddComponent<PresetMarker>();
                    marker.Preset = __state.Preset;
                }
            }
            catch (Exception e)
            {
                MelonLoader.MelonLogger.Warning("[M1A1] M1 to M1IP spawn marker skipped: " + e);
            }
        }
    }
}
