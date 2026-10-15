using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC;
using GHPC.Equipment.Optics;
using GHPC.Weapons;
using GHPC.Thermals;
using UnityEngine;
using GHPC.Effects;

namespace ModUtil
{
    public sealed class AlreadyConverted : MonoBehaviour
    {
        void Awake()
        {
            enabled = false;
        }
    }

    public sealed class Util
    {
        public static ImpactEffectsDatabaseScriptable impact_fx_db;
        private static ImpactEffectsDatabaseScriptable cached_ammo_db;
        private static ImpactDecalsDatabaseScriptable cached_ammo_decals_db;

        // AmmoType.CachedIndex is a single field that two independent databases each treat
        // as an index into their own CachedData list, and both managers only append when it
        // is -1.  A registered round is therefore only addressable while the two lists
        // agree, so entries have to be added to both and the lists kept the same length.
        private static readonly Dictionary<string, int> cached_ammo_indices =
            new Dictionary<string, int>();

        public static readonly string[] MenuScreensWithLoader = new string[] {
            "MainMenu2_Scene",
            "MainMenu2-1_Scene",
            "LOADER_MENU",
            "LOADER_INITIAL",
            "t64_menu",
            "leo2_menu"
        };

        public static readonly string[] MenuScreens = new string[] {
            "MainMenu2_Scene",
            "MainMenu2-1_Scene",
            "t64_menu",
            "leo2_menu"
        };

        public static void CacheAmmo(AmmoType ammo)
        {
            if (ammo == null) return;

            // Cache against the database actually used to render impacts this scene.
            // Resources may also contain donor/previous-scene databases.
            ImpactEffectsDatabaseScriptable active = Reflect.Get<ImpactEffectsDatabaseScriptable>(
                ParticleEffectsManager.Instance, "_impactEffectsScriptable");
            if (active != null) impact_fx_db = active;
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

        public static void CreateUniformArmour(GameObject go, string name, float rha_sabot, float rha_heat, ArmorCodexScriptable codex = null)
        {
            UniformArmor component = go.AddComponent<UniformArmor>();
            go.tag = "Penetrable";
            go.layer = 8;
            component.SetName(name);
            component.PrimaryHeatRha = rha_heat;
            component.PrimarySabotRha = rha_sabot;

            if (codex != null)
            {
                Reflect.Set(component, "_armorType", codex);
            }
        }

        public static T[] AppendToArray<T>(T[] array, T new_item)
        {
            int old_length = array == null ? 0 : array.Length;
            T[] values = new T[old_length + 1];
            if (old_length > 0)
            {
                Array.Copy(array, values, old_length);
            }

            values[old_length] = new_item;
            return values;
        }

        public static void Coalesce<T>(ref T obj) where T : new()
        {
            if (obj != null) return;
            obj = new T();
        }

        public static void SetupFLIRShaders(GameObject parent)
        {
            foreach (MeshRenderer mrend in parent.GetComponentsInChildren<MeshRenderer>())
            {
                foreach (Material mat in mrend.materials)
                {
                    mat.shader = Shader.Find("Standard (FLIR)");
                }
            }

            HeatSource src = parent.AddComponent<HeatSource>();
            src.Heat = 0.4f;
        }

        public static void ShallowCopy(System.Object dest, System.Object src)
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo[] destFields = dest.GetType().GetFields(flags);
            FieldInfo[] srcFields = src.GetType().GetFields(flags);

            foreach (FieldInfo srcField in srcFields)
            {
                FieldInfo destField = destFields.FirstOrDefault(field => field.Name == srcField.Name);

                if (destField != null && !destField.IsLiteral)
                {
                    if (srcField.FieldType == destField.FieldType)
                        destField.SetValue(dest, srcField.GetValue(src));
                }
            }
        }

        public static UsableOptic GetDayOptic(FireControlSystem fcs)
        {
            if (fcs.MainOptic.slot.IsLinkedNightSight)
            {
                return fcs.MainOptic.slot.LinkedDaySight.PairedOptic;
            }
            else
            {
                return fcs.MainOptic;
            }
        }

        public static void EmptyRack(GHPC.Weapons.AmmoRack rack)
        {
            if (rack == null) return;
            rack.StoredClips.Clear();
            rack.SlotIndicesByAmmoType.Clear();

            foreach (Transform transform in rack.VisualSlots)
            {
                if (transform == null) continue;
                AmmoStoredVisual vis = transform.GetComponentInChildren<AmmoStoredVisual>();

                if (vis != null && vis.AmmoType != null)
                {
                    Reflect.Call(rack, "RemoveAmmoVisualFromSlot", transform);
                }
            }
        }
    }
}
