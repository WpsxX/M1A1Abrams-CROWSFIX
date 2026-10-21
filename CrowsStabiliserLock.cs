using System.Collections.Generic;
using GHPC.Weapons;
using HarmonyLib;

namespace M1A1Abrams
{
    /// <summary>
    /// Pins a fire control's stabilisation on by refusing the switch-off where it is decided, instead of
    /// turning it back on afterwards.
    ///
    /// FireControlSystem.SetStabsActive is the single gate every caller goes through: the player's
    /// stabiliser key through CrewBrainWeaponsModule, the crew order that clears it, the loadout manager
    /// when a mission starts, and any other mod that decides to switch it. Refusing the "off" value here
    /// means the state never leaves "on" at all - no frame where the mount has already dropped the aim,
    /// no repeated off/on churn fighting the input, and nothing for the HUD or the crew to disagree with.
    ///
    /// Only fire controls registered through <see cref="Lock"/> are affected, so the player's main gun
    /// keeps the stock behaviour.
    /// </summary>
    [HarmonyPatch(typeof(FireControlSystem), "SetStabsActive")]
    internal static class CrowsStabiliserLock
    {
        private static readonly HashSet<int> locked = new HashSet<int>();

        /// <summary>
        /// Marks a fire control as permanently stabilised. Called when the station is built.
        /// </summary>
        internal static void Lock(FireControlSystem fcs)
        {
            if (fcs == null)
            {
                return;
            }

            locked.Add(fcs.GetInstanceID());
        }

        /// <summary>
        /// Drops every registration. The fire controls belong to the mission's vehicles, so the set has
        /// to be emptied when the mission is torn down or the stale ids would outlive what they name.
        /// </summary>
        internal static void Reset()
        {
            locked.Clear();
        }

        /// <summary>
        /// A request to switch stabilisation off on a locked fire control is rewritten to "on". The
        /// original then runs with that value, which is already the current state, so it does nothing.
        /// </summary>
        private static bool Prefix(FireControlSystem __instance, ref bool value)
        {
            if (value || __instance == null)
            {
                return true;
            }

            if (!locked.Contains(__instance.GetInstanceID()))
            {
                return true;
            }

            value = true;
            return true;
        }
    }
}
