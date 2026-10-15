using System;
using GHPC.Weapons;
using HarmonyLib;
using ModUtil;

namespace M1A1Abrams
{
    /// <summary>
    /// Guarantees the lead the fire control works from, whatever else wrote to it this frame.
    ///
    /// The lead is derived from <c>_averageTraverseRate</c>, which <c>doTrackingUpdate</c> builds out
    /// of the traverse-rate buffer - and that buffer is fed by whatever rotated the aim during the
    /// frame, which with a machine driving the aim is whatever the tracker applied. CrowsAimLock keeps
    /// the player's hand off that field entirely; this is the guarantee underneath it, writing the
    /// tracker's own measurement of the target's motion after the fire control has finished working,
    /// so the value the lead ends up using is the tracker's whatever else happened.
    /// </summary>
    [HarmonyPatch(typeof(FireControlSystem), "doTrackingUpdate")]
    public static class CrowsLeadLock
    {
        private static CrowsAutoTrack driver;

        internal static void Hold(CrowsAutoTrack tracker)
        {
            driver = tracker;
        }

        internal static void Drop(CrowsAutoTrack tracker)
        {
            if (driver == tracker)
            {
                driver = null;
            }
        }

        [HarmonyPatch(typeof(FireControlSystem), "GetLeadWorldOffset")]
        private static class WorldLeadPatch
        {
            private static void Postfix(FireControlSystem __instance, ref UnityEngine.Vector3 __result)
            {
                try
                {
                    // The game's GetLeadWorldOffset opens by returning Vector3.zero when the loaded round
                    // asks for no lead compensation at all - gun-launched missiles, currently the 9M112
                    // family. A Harmony postfix still runs after that early return, so without this guard
                    // the tracker would overrule a decision the game deliberately made. Returning without
                    // touching __result keeps the game's own zero.
                    if (__instance.CurrentAmmoType == null || __instance.CurrentAmmoType.NoLeadCompensation)
                    {
                        return;
                    }

                    UnityEngine.Vector3 offset;
                    if (driver != null && driver.TryGetWorldLead(__instance, out offset))
                        __result = offset;
                }
                catch (Exception)
                {
                    // A patch that throws must leave the game's original lead result intact.
                }
            }
        }

        private static void Postfix(FireControlSystem __instance)
        {
            try
            {
                CrowsAutoTrack tracker = driver;

                if (tracker == null || __instance == null)
                {
                    return;
                }

                if (!tracker.Drives(__instance))
                {
                    return;
                }

                if (tracker.WorldLeadActive)
                {
                    // The world lead answers this fire control's GetLeadWorldOffset itself, so a traverse
                    // rate written here would be a second author of the same quantity - and the stale one,
                    // since the world offset overrules whatever the fire control works out from it.
                    return;
                }

                Reflect.Set<UnityEngine.Vector2>(__instance, "_averageTraverseRate", tracker.LeadRate);
            }
            catch (Exception)
            {
                // A patch that throws is worse than the drift it is here to prevent.
            }
        }
    }
}
