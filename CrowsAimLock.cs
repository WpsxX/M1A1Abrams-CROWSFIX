using System;
using System.Collections.Generic;
using GHPC.Equipment.Optics;
using GHPC.Weapons;
using HarmonyLib;
using UnityEngine;

namespace M1A1Abrams
{
    /// <summary>
    /// Owns the CROWS station's manual aim rate: the traverse and elevation the player gets while
    /// nothing is locked, per magnification.
    ///
    /// In GHPC the mouse reaches the fire control in Update as a *ratio*, and
    /// <c>FireControlSystem.AdjustAimByRatio</c> turns that ratio into degrees through
    /// <c>Mounts[0].CurrentMaxSpeed</c> and <c>Mounts[1].CurrentMaxSpeed</c> - the platforms
    /// <see cref="CROWS.MountInternal"/> builds, at 90 deg/s traverse and 50 deg/s elevation - so the
    /// game's own mapping already lands on the station's rated rates at every magnification. What was
    /// missing is that nothing made the four magnifications agree with each other: at 15X the sight shows
    /// a fifteenth of the field, so the same 90 deg/s crosses the picture fifteen times faster, and the
    /// station reads as four different instruments.
    ///
    /// So the mapping is taken over here. 1X keeps the published 90/50, and 5X/10X/15X scale by the
    /// sight's field of view, which puts the *screen* response back on one number: a full push of the
    /// stick moves the picture the same distance whatever the magnification.
    ///
    /// Fixed, not a setting: the requirement is a behaviour, and a knob for it would only be a way to get
    /// the old behaviour back by accident.
    ///
    /// The lock case is unrelated and unchanged: while a target is held the tracker drives the aim and the
    /// player's input is cut off entirely (see <see cref="Hold"/>), and the slow crosshair offset he still
    /// gets there is a different quantity in a different file, deliberately not routed through here.
    /// </summary>
    [HarmonyPatch(typeof(FireControlSystem), "AdjustAimByRatio")]
    public static class CrowsAimLock
    {
        private static FireControlSystem held;

        /// <summary>The fire control whose aim input is being held off, if any.</summary>
        internal static void Hold(FireControlSystem fcs)
        {
            held = fcs;
        }

        internal static void Drop()
        {
            held = null;
        }

        /// <summary>
        /// Whether this fire control's aim input is currently held off. A held fire control that has
        /// since been destroyed compares equal to null, so a missed Drop() cannot strand it.
        /// </summary>
        internal static bool Holds(FireControlSystem fcs)
        {
            return held != null && fcs != null && held.GetInstanceID() == fcs.GetInstanceID();
        }

        /// <summary>
        /// One station's manual slew. Index 0..3 of the two arrays is the magnification - 1X, 5X, 10X,
        /// 15X - and the figures are degrees a second *at 1X*, before the field-of-view scaling below.
        ///
        /// Kept as a table rather than one number because that is how the requirement was given - the four
        /// magnifications as four entries - and all four hold the station's published 90/50 today. If one
        /// of them ever has to differ, this is where it differs.
        /// </summary>
        internal class SlewRates
        {
            internal readonly float[] traverse = new float[4];
            internal readonly float[] elevation = new float[4];

            /// <summary>
            /// The station's own sights, so that a slot belonging to some other vehicle cannot answer for
            /// it when the magnification is read.
            /// </summary>
            internal UsableOptic[] sights;

            /// <summary>
            /// The fire control this row belongs to, kept so that a station torn down with its vehicle can
            /// be dropped when the next one registers - a destroyed fire control compares equal to null,
            /// which is a cheaper liveness test than tracking teardown order.
            /// </summary>
            internal FireControlSystem fcs;

            /// <summary>The field of view this station calls 1X; 30 degrees, as `CROWS.DAY_FOV_1X` sets.</summary>
            internal const float REFERENCE_FOV_DEGREES = 30f;

            /// <summary>The traverse for the magnification the given field of view belongs to.</summary>
            internal float Traverse(float fov)
            {
                return Scale(Rated(traverse, fov, CROWS.TRAVERSE_SPEED_DEGREES_PER_SECOND), fov);
            }

            /// <summary>The rated elevation for the magnification the given field of view belongs to.</summary>
            internal float Elevation(float fov)
            {
                return Scale(Rated(elevation, fov, CROWS.ELEVATION_SPEED_DEGREES_PER_SECOND), fov);
            }

            private float Rated(float[] rates, float fov, float fallback)
            {
                int index = MagnificationIndex(fov);

                // Nothing configured for this magnification: fall back to the station's rated rate, which
                // is the number the game would have used. Never zero - a zero here reads as a dead mount.
                return index < 0 || index >= rates.Length || rates[index] <= 0f ? fallback : rates[index];
            }

            /// <summary>
            /// The whole point of this file: what one push of the mouse covers *on screen* stays the same
            /// as the sight zooms in, so the figures above - the 1X rates - are scaled by
            /// `CurrentFov / 30`. At 15X (2 degrees) that is a fifteenth of the traverse and a fifteenth of
            /// the elevation, which is what stops the picture flying when the player zooms in and what makes
            /// 1X/5X/10X/15X feel like one instrument instead of four.
            ///
            /// Both axes take the same factor: the statement is about how much of the picture one push
            /// covers, and the horizontal and vertical fields differ by the screen's aspect at every zoom
            /// alike, so scaling both keeps the two axes in the ratio they already had.
            ///
            /// Only ever the unlocked case. The slow crosshair offset the player gets while a target is held
            /// is a different quantity in a different file (CrowsAutoTrack.WalkManualOffset, metres at the
            /// target per second per degree of field of view) and is deliberately not routed through here.
            /// </summary>
            private float Scale(float rate, float fov)
            {
                return fov > 0f ? rate * (fov / REFERENCE_FOV_DEGREES) : rate;
            }

            /// <summary>
            /// Which magnification a field of view is, from the field of view itself rather than the
            /// slot's index: the index only means 1X/5X/10X/15X while nobody reorders `OtherFovs`, and the
            /// field of view is the thing that decides what the player sees. The thresholds sit between
            /// the four settings (30 / 6 / 3 / 2) rather than on them, so a slot whose field of view is a
            /// degree or two off - the thermal sight, a mod that changed one - still lands on the right row.
            /// </summary>
            internal int MagnificationIndex(float fov)
            {
                if (fov >= 20f) return 0;    // 1X, 30 degrees
                if (fov >= 4.5f) return 1;   // 5X, 6
                if (fov >= 2.4f) return 2;   // 10X, 3
                return 3;                    // 15X, 2
            }
        }

        private static readonly Dictionary<int, SlewRates> stations = new Dictionary<int, SlewRates>();
        private static readonly List<int> dead_station_ids = new List<int>();

        /// <summary>
        /// Puts a station under this file's control, once per CROWS, with the fire control and the two
        /// platforms it was assembled around and the sights it aims through.
        /// </summary>
        internal static void Register(FireControlSystem fcs, UsableOptic[] sights)
        {
            if (fcs == null)
            {
                return;
            }

            SlewRates rates = new SlewRates();
            rates.fcs = fcs;
            rates.sights = sights;

            for (int i = 0; i < rates.traverse.Length; i++)
            {
                rates.traverse[i] = CROWS.TRAVERSE_SPEED_DEGREES_PER_SECOND;
                rates.elevation[i] = CROWS.ELEVATION_SPEED_DEGREES_PER_SECOND;
            }

            DropDeadStations();

            stations[fcs.GetInstanceID()] = rates;
        }

        /// <summary>
        /// Forgets stations whose fire control has been destroyed, so a mission's worth of assemblies does
        /// not accumulate across missions. Unity's `== null` on a destroyed object is what answers it.
        /// </summary>
        private static void DropDeadStations()
        {
            dead_station_ids.Clear();

            foreach (KeyValuePair<int, SlewRates> entry in stations)
            {
                if (entry.Value.fcs == null)
                {
                    dead_station_ids.Add(entry.Key);
                }
            }

            for (int i = 0; i < dead_station_ids.Count; i++)
            {
                stations.Remove(dead_station_ids[i]);
            }
        }

        internal static void Unregister(FireControlSystem fcs)
        {
            // During Unity teardown the native object may already compare equal to null while the
            // managed wrapper is still available. Its instance ID is still enough to remove our row.
            if (!ReferenceEquals(fcs, null))
            {
                int id = fcs.GetInstanceID();
                stations.Remove(id);

                if (!ReferenceEquals(held, null) && held.GetInstanceID() == id)
                {
                    held = null;
                }
            }
        }

        /// <summary>
        /// The field of view the player is looking through at this station, or 1X when nobody is on it.
        /// </summary>
        private static float SightFov(SlewRates rates)
        {
            if (rates.sights != null)
            {
                for (int i = 0; i < rates.sights.Length; i++)
                {
                    UsableOptic optic = rates.sights[i];

                    if (optic != null && optic.slot != null && optic.slot.IsActive)
                    {
                        return optic.slot.CurrentFov;
                    }
                }

                for (int i = 0; i < rates.sights.Length; i++)
                {
                    if (rates.sights[i] != null && rates.sights[i].slot != null)
                    {
                        return rates.sights[i].slot.CurrentFov;
                    }
                }
            }

            return SlewRates.REFERENCE_FOV_DEGREES;
        }

        private static bool Prefix(FireControlSystem __instance, float horizontalRatio, float verticalRatio)
        {
            try
            {
                if (__instance == null)
                {
                    return true;
                }

                // A lock is held: the tracker is driving the aim and the player's input is cut off, which
                // is what this patch was originally written for. Nothing here applies.
                if (Holds(__instance))
                {
                    return false;
                }

                SlewRates rates;

                if (!stations.TryGetValue(__instance.GetInstanceID(), out rates))
                {
                    // Not one of this mod's stations - the AI, the main gun, every other vehicle. Untouched.
                    return true;
                }

                // Ours, unlocked: this is the manual aim, so rotate it here, by the rated rate for the
                // magnification the player is looking through, and keep the game's own mapping out of the
                // way. The input arrives as a ratio exactly as the game computed it - same accumulator,
                // same dead band, same sensitivity setting - so the feel is unchanged; the only number
                // that differs is the one the ratio is multiplied by, and that number is this station's.
                float fov = SightFov(rates);
                float delta = Time.deltaTime;

                float yaw = Mathf.Clamp(horizontalRatio, -1f, 1f) * rates.Traverse(fov) * delta;
                float pitch = Mathf.Clamp(verticalRatio, -1f, 1f) * rates.Elevation(fov) * delta;

                // noLeadUpdate stays false: this is the player traversing, and the fire control's lead
                // inference is built on being told about it. The game's own ratio method records it the
                // same way, from the same two numbers.
                __instance.AdjustAimByDegrees(yaw, pitch, false);

                return false;
            }
            catch (Exception)
            {
                // A patch that throws would be far worse than the aim rate it is here to pin: hand the
                // frame back to the game's own mapping.
                return true;
            }
        }
    }
}
