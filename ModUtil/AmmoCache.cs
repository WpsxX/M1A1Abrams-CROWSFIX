using System.Collections.Generic;
using System.Linq;
using GHPC.Effects;
using GHPC.Weapons;
using UnityEngine;

namespace ModUtil
{
    // AmmoType.CachedIndex is a single field that two independent databases each treat
    // as an index into their own CachedData list, and both managers only append when it
    // is -1.  A registered round is therefore only addressable while the two lists agree,
    // so entries have to be added to both and the lists kept the same length.
    //
    // This is the one helper that cannot come from the shared VanillaAssetUtil assembly:
    // the copy there predates the drift repair below and would silently leave rounds
    // pointing at the other database's entry.
    internal static class AmmoCache
    {
        private static ImpactEffectsDatabaseScriptable cached_ammo_db;
        private static ImpactDecalsDatabaseScriptable cached_ammo_decals_db;

        private static readonly Dictionary<string, int> cached_ammo_indices =
            new Dictionary<string, int>();

        internal static void CacheAmmo(AmmoType ammo)
        {
            if (ammo == null) return;

            // Cache against the database actually used to render impacts this scene.
            // Resources may also contain donor/previous-scene databases.
            ImpactEffectsDatabaseScriptable active = Reflect.Get<ImpactEffectsDatabaseScriptable>(
                ParticleEffectsManager.Instance, "_impactEffectsScriptable");
            ImpactEffectsDatabaseScriptable impact_fx_db = active;
            if (impact_fx_db == null)
            {
                impact_fx_db = Resources.FindObjectsOfTypeAll<ImpactEffectsDatabaseScriptable>().FirstOrDefault();
            }

            ImpactDecalsDatabaseScriptable decals = Reflect.Get<ImpactDecalsDatabaseScriptable>(
                ImpactDecalsManager.Instance, "_ImpactDecalsScriptable");

            // AmmoType.CachedIndex is one field that both databases read as an index into
            // their own CachedData list, and each manager only appends when it is -1.  An
            // entry therefore has to be added to both lists or to neither: appending to a
            // single list leaves the round pointing at an entry belonging to the other one,
            // and the game never repairs it because CachedIndex is no longer -1.
            if (impact_fx_db == null || decals == null)
            {
                ammo.CachedIndex = -1;
                return;
            }

            // CacheNewData always appends.  The module is rebuilt on scene changes, so
            // use the round name as the stable key while these database instances live.
            // New databases get a fresh map because their indices belong to those objects.
            if (cached_ammo_db != impact_fx_db || cached_ammo_decals_db != decals)
            {
                cached_ammo_db = impact_fx_db;
                cached_ammo_decals_db = decals;
                cached_ammo_indices.Clear();
            }

            int cached_index;
            if (cached_ammo_indices.TryGetValue(ammo.Name, out cached_index))
            {
                ammo.CachedIndex = cached_index;
                return;
            }

            // Both appends record their position by writing ammo.CachedIndex, and the later
            // call wins, so the two lists have to agree on what index this round got.  A
            // drift means an earlier round was only ever added to one of them; growing only
            // the shorter list keeps this round addressable and reports the damage instead
            // of hiding it behind entries that belong to a different round.
            int effect_count = impact_fx_db.CachedData.Count;
            int decal_count = decals.CachedData.Count;
            if (effect_count != decal_count)
            {
                Reflect.Miss("impact cache drift: effect list has " + effect_count +
                    " entries, decal list has " + decal_count);
                for (int pad = decal_count; pad < effect_count; pad++)
                {
                    decals.CacheNewData(ammo, out int _);
                }
            }

            int id;
            decals.CacheNewData(ammo, out id);
            impact_fx_db.CacheNewData(ammo, out id);
            ammo.CachedIndex = id;
            cached_ammo_indices[ammo.Name] = id;
        }
    }
}
