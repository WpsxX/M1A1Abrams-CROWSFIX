using System;
using GHPC;
using GHPC.Weapons;
using HarmonyLib;
using UnityEngine;

namespace M1A1Abrams
{
    /// <summary>
    /// Multi-shot convergence: the fall of shot of the rounds this station fires, fed back into the
    /// solution the next one is fired with.
    ///
    /// Two hooks, and they are the whole of what the game offers for it. Where a round is fired
    /// (WeaponSystem.Fire) gives the moment, the ammunition, the range and the point that was being
    /// aimed at; where a round lands (OldLiveRound.logHit, which every impact path in the game funnels
    /// through) gives the point it actually went to. Between them is the error the rest of this module
    /// cannot see: boresight, superelevation the donated mount does not quite reproduce, wind, drift.
    ///
    /// Neither hook is required for anything else to work. Every entry point is wrapped, every lookup
    /// is by name, and a missing type or a throwing game method leaves the lead exactly as it is - this
    /// is a correction on top of a solution that is already complete without it.
    ///
    /// Attribution is by time and place rather than by round identity: there is no handle from a
    /// WeaponSystem's shot to the OldLiveRound it becomes, so a fall of shot is credited to the station
    /// that was aiming at that place, within a flight time and a fraction of the range of the point it
    /// was aiming at. A neighbouring vehicle shooting at the same target contributes a valid
    /// observation; one shooting at something else does not, because its round lands near a different
    /// point.
    /// </summary>
    internal static class CrowsShotFeedback
    {
        /// <summary>How long after a shot a fall of shot is still credited to it, in seconds.</summary>
        private const float FlightWindow = 12f;

        /// <summary>How far from the point that was aimed at a fall of shot may land and still be
        /// credited, as a fraction of the range fired at.</summary>
        private const float AttributionFraction = 0.25f;
        private const int PendingCapacity = 32;

        /// <summary>The station whose shots are being watched; see CrowsAutoTrack's arming.</summary>
        private static CrowsAutoTrack driver;

        private static readonly bool[] pending_used = new bool[PendingCapacity];
        private static readonly float[] pending_time = new float[PendingCapacity];
        private static readonly Vector3[] pending_aim = new Vector3[PendingCapacity];
        private static readonly float[] pending_range = new float[PendingCapacity];
        private static int pending_count;
        private static int pending_next;
        private static float last_decay;

        internal static void Hold(CrowsAutoTrack tracker)
        {
            driver = tracker;
            ClearPending();
            last_decay = Time.time;
        }

        internal static void Drop(CrowsAutoTrack tracker)
        {
            if (driver == tracker)
            {
                driver = null;
                ClearPending();
            }
        }

        private static void ClearPending()
        {
            Array.Clear(pending_used, 0, PendingCapacity);
            pending_count = 0;
            pending_next = 0;
        }

        private static void Expire(float now)
        {
            for (int i = 0; i < PendingCapacity; i++)
            {
                if (!pending_used[i]) continue;
                float age = now - pending_time[i];
                if (age >= 0f && age <= FlightWindow) continue;
                pending_used[i] = false;
                pending_count--;
            }
        }

        /// <summary>Whether this weapon is the one the watched station fires from.</summary>
        internal static bool IsOurs(WeaponSystem weapon)
        {
            try
            {
                CrowsAutoTrack tracker = driver;
                return tracker != null && weapon != null && tracker.Fires(weapon);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// A round has left. The point being aimed at is taken now, at the trigger, because that is the
        /// point the round was fired at; the target will have moved on by the time it lands.
        /// </summary>
        internal static void Fired(WeaponSystem weapon)
        {
            try
            {
                CrowsAutoTrack tracker = driver;

                if (tracker == null || weapon == null)
                {
                    return;
                }

                Vector3 point;

                if (!tracker.AimPointForFallOfShot(weapon, out point))
                {
                    return;
                }

                float now = Time.time;
                float range = tracker.FallOfShotRange(weapon);
                if (range <= 1f) return;
                Expire(now);

                int slot = -1;
                for (int step = 0; step < PendingCapacity; step++)
                {
                    int candidate = (pending_next + step) % PendingCapacity;
                    if (!pending_used[candidate]) { slot = candidate; break; }
                }
                if (slot < 0)
                {
                    // Capacity is a hard memory bound. In a burst longer than the whole flight window,
                    // replace the oldest observation; it is the least useful one for attribution.
                    slot = 0;
                    for (int i = 1; i < PendingCapacity; i++)
                        if (pending_time[i] < pending_time[slot]) slot = i;
                }
                else pending_count++;

                pending_used[slot] = true;
                pending_time[slot] = now;
                pending_aim[slot] = point;
                pending_range[slot] = range;
                pending_next = (slot + 1) % PendingCapacity;
            }
            catch (Exception)
            {
                // A shot that could not be recorded contributes no feedback.
            }
        }

        /// <summary>
        /// A round has landed. Credited to the shot waiting for it if it landed where that shot was
        /// aimed and soon enough to be it; anything else is somebody else's round and is ignored.
        /// </summary>
        internal static void Impact(Vector3 point)
        {
            try
            {
                if (pending_count == 0 || driver == null)
                {
                    return;
                }

                float now = Time.time;
                Expire(now);
                int best = -1;
                float best_error = float.PositiveInfinity;
                for (int i = 0; i < PendingCapacity; i++)
                {
                    if (!pending_used[i] || pending_range[i] <= 1f) continue;
                    float strayed = (point - pending_aim[i]).magnitude;
                    if (strayed > pending_range[i] * AttributionFraction) continue;
                    float normalised = strayed / pending_range[i];
                    if (normalised < best_error) { best = i; best_error = normalised; }
                }
                if (best < 0) return;
                float range = pending_range[best];
                pending_used[best] = false;
                pending_count--;
                driver.NoteFallOfShot(point, range);
            }
            catch (Exception)
            {
                // An impact that cannot be attributed contributes no feedback.
            }
        }

        /// <summary>
        /// Fades the correction while nothing is being fired, so a bias learned in one engagement does
        /// not follow the vehicle into the next one as though nothing had changed.
        /// </summary>
        internal static void Tick()
        {
            try
            {
                CrowsAutoTrack tracker = driver;

                if (tracker == null)
                {
                    return;
                }

                float now = Time.time;
                Expire(now);
                float elapsed = now - last_decay;
                last_decay = now;

                if (elapsed > 0f && elapsed < 1f && pending_count == 0)
                {
                    tracker.FadeFallOfShot(elapsed);
                }
            }
            catch (Exception)
            {
                // A correction that cannot be faded is still a correction; nothing to do here.
            }
        }

[HarmonyPatch(typeof(WeaponSystem), "Fire")]
        private static class FirePatch
        {
            private static void Postfix(WeaponSystem __instance)
            {
                try
                {
                    if (IsOurs(__instance))
                    {
                        Fired(__instance);
                    }
                }
                catch (Exception)
                {
                    // A patch that throws is worse than the error it measures.
                }
            }
        }

        [HarmonyPatch(typeof(OldLiveRound), "logHit")]
        private static class ImpactPatch
        {
            private static void Postfix(Vector3 hitPoint)
            {
                try
                {
                    Impact(hitPoint);
                }
                catch (Exception)
                {
                    // As above: the fall of shot is a bonus, never a requirement.
                }
            }
        }
    }
}
