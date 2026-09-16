using GHPC.Weaponry;
using GHPC.Weapons;
using ModUtil;

namespace M1A1Abrams
{
    internal static class MainGunLoadout
    {
        internal static void Apply(LoadoutManager manager, AmmoClipCodexScriptable[] clips,
            int[] requested, bool vanillaCapacity)
        {
            int capacity = 0;
            for (int i = 0; i < manager.RackLoadouts.Length; i++)
            {
                GHPC.Weapons.AmmoRack rack = manager.RackLoadouts[i].Rack;
                if (!vanillaCapacity && i < 3) rack.ClipCapacity = i == 2 ? 4 : 18;
                if (rack.ClipCapacity < 0) rack.SetUpVisualSlots();
                capacity += System.Math.Max(0, rack.ClipCapacity);
                Util.EmptyRack(rack);
            }
            int[] counts = AmmoLoadoutRules.Fit(requested, capacity);
            int total = 0;
            foreach (int count in counts) total += count;
            manager.TotalAmmoCounts = counts;
            manager.LoadedAmmoList.AmmoClips = clips;
            Reflect.Set(manager, "_totalAmmoTypes", clips.Length);
            Reflect.Set(manager, "_totalAmmoCount", total);

            // All converted main-gun racks accept the same 120 mm rounds. Replace the old
            // 105 mm distribution/overrides with explicit counts, including zero-filled slots.
            // This also avoids the game's automatic fill loop on intentionally partial loads.
            int[] remaining = (int[])counts.Clone();
            foreach (LoadoutManager.RackLoadout entry in manager.RackLoadouts)
            {
                entry.AmmoCounts = AmmoLoadoutRules.Fit(remaining, entry.Rack.ClipCapacity);
                for (int i = 0; i < remaining.Length; i++) remaining[i] -= entry.AmmoCounts[i];
                entry.FixedChoices = new LoadoutManager.RackLoadoutFixedChoice[0];
                entry.ForbiddenAmmoIndices = new int[0];
                entry.OverrideInitialClips = null;
            }
            manager.SpawnCurrentLoadout();
        }

        internal static void Refill(AmmoFeed feed)
        {
            // Re-run only the ammo setup, not Start(), which subscribes crew-health events.
            AmmoType.AmmoClip clip = feed.ReadyRack.ClipTypes[0];
            Reflect.SetProperty(feed, "AmmoTypeInBreech", null);
            Reflect.SetProperty(feed, "QueuedClipType", clip);
            Reflect.Set(feed, "_queuedClipTypeLockedIn", clip);
            Reflect.Call(feed, "TryFinalFeedNewClip", -1);
            Reflect.Call(feed, "ToggleExclusiveItems", clip);
        }
    }
}
