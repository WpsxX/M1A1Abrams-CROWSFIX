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

        public static string[] menu_screens = new string[] {
            "MainMenu2_Scene",
            "MainMenu2-1_Scene",
            "LOADER_MENU",
            "LOADER_INITIAL",
            "t64_menu"
        };

        public static void CacheAmmo(AmmoType ammo)
        {
            // Cache against the database actually used to render impacts this scene.
            // Resources may also contain donor/previous-scene databases.
            ImpactEffectsDatabaseScriptable active = Reflect.Get<ImpactEffectsDatabaseScriptable>(
                ParticleEffectsManager.Instance, "_impactEffectsScriptable");
            if (active != null) impact_fx_db = active;
            if (impact_fx_db == null)
            {
                impact_fx_db = Resources.FindObjectsOfTypeAll<ImpactEffectsDatabaseScriptable>().FirstOrDefault();
            }
            if (impact_fx_db == null) { ammo.CachedIndex = -1; return; }

            int id;
            ImpactDecalsDatabaseScriptable decals = Reflect.Get<ImpactDecalsDatabaseScriptable>(
                ImpactDecalsManager.Instance, "_ImpactDecalsScriptable");

            if (decals != null)
            {
                decals.CacheNewData(ammo, out id);
            }

            impact_fx_db.CacheNewData(ammo, out id);
            ammo.CachedIndex = id;
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
