using System;
using GHPC;
using GHPC.Weapons;
using ModUtil;
using UnityEngine;

namespace M1A1Abrams
{
    internal static partial class CROWS
    {
        /// <summary>
        /// Makes sure the borrowed .50 cal has a belt in its breech before the first shot.
        /// </summary>
        public class CrowsAmmoSetup : MonoBehaviour
        {
            public WeaponSystem weapon;
            public AmmoType.AmmoClip clip;

            void Start()
            {
                StartCoroutine(Setup());
            }

            private System.Collections.IEnumerator Setup()
            {
                yield return null;

                AmmoFeed feed = weapon == null ? null : weapon.Feed;
                if (feed == null)
                {
                    yield break;
                }

                if (feed.AmmoTypeInBreech == null)
                {
                    LoadBelt(feed);
                }

                yield return null;
            }

            private void LoadBelt(AmmoFeed feed)
            {
                // Invariant first, before anything is touched: the loop below indexes MinimalPattern
                // modulo its own length and reads Capacity, so an empty pattern divided by zero and a
                // null one threw outright - from a coroutine, after the belt had already been cleared.
                if (clip.MinimalPattern == null || clip.MinimalPattern.Length == 0 || clip.Capacity <= 0)
                {
                    return;
                }

                if (feed.QueuedClipType == null)
                {
                    Reflect.SetProperty(feed, "QueuedClipType", clip);
                }

                Reflect.Set(feed, "_queuedClipTypeLockedIn", clip);

                if (feed.ReadyRack != null)
                {
                    feed.ReadyRack.RetrieveClipWithPattern(clip, -1);
                }

                System.Collections.Generic.Queue<AmmoType> belt =
                    Reflect.Get<System.Collections.Generic.Queue<AmmoType>>(feed, "_feedClipMain");

                if (belt == null)
                {
                    Reflect.SetProperty(feed, "LoadedClipType", clip);
                    Reflect.SetProperty(feed, "AmmoTypeInBreech", clip.MinimalPattern[0].AmmoType);
                    PublishRoundInBreech(feed);
                    return;
                }

                belt.Clear();
                for (int i = 0; i < clip.Capacity; i++)
                {
                    belt.Enqueue(clip.MinimalPattern[i % clip.MinimalPattern.Length].AmmoType);
                }

                Reflect.SetProperty(feed, "LoadedClipType", clip);
                Reflect.SetProperty(feed, "AmmoTypeInBreech", belt.Dequeue());
                PublishRoundInBreech(feed);
            }

            private void PublishRoundInBreech(AmmoFeed feed)
            {
                AmmoType round = feed.AmmoTypeInBreech;
                if (weapon == null || round == null || weapon.CurrentAmmoType == round)
                {
                    return;
                }

                try
                {
                    Reflect.Call(weapon, "newRoundInBreech", round);
                }
                catch (Exception)
                {
                    // Best effort: the weapon keeps its previous state if the private callback changes.
                }
            }
        }
    }
}
