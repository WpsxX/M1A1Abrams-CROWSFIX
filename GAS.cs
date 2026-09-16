using GHPC.Camera;
using GHPC.Weapons;
using Reticle;
using UnityEngine;
using GHPC.Weaponry;
using ModUtil;
using GHPC.Vehicle;
using GHPC.Equipment.Optics;
using System.Collections.Generic;

namespace M1A1Abrams
{
    /// <summary>
    /// Gunner's Auxiliary Sight reticles for the 120mm conversion: the vanilla 105mm
    /// reticle trees are cloned and re-pointed at the new projectiles.
    /// </summary>
    public class GAS : Module
    {
        public static ReticleSO reticleSO_heat;
        public static object reticle_cached_heat; // boxed ReticleMesh.CachedReticle

        public static ReticleSO reticleSO_ap;
        public static object reticle_cached_ap;

        private sealed class ReticlePair
        {
            internal AmmoCodexScriptable sabot, heat;
            internal ReticleSO apTree, heatTree;
            internal object apCache, heatCache;
        }

        private static readonly List<ReticlePair> pairs = new List<ReticlePair>();

        public override void UnloadDynamicAssets()
        {
            foreach (ReticlePair pair in pairs)
            {
                if (pair.apTree != null) ScriptableObject.DestroyImmediate(pair.apTree);
                if (pair.heatTree != null) ScriptableObject.DestroyImmediate(pair.heatTree);
            }
            pairs.Clear();
            reticleSO_heat = null;
            reticleSO_ap = null;
            reticle_cached_heat = null;
            reticle_cached_ap = null;
        }

        public static void Add(Transform gas, WeaponSystem[] exclusives)
        {
            if (gas == null) return;
            CameraSlot slot = gas.GetComponent<CameraSlot>();
            if (slot != null)
            {
                slot.ExclusiveWeapons = exclusives;
            }

            ApplyReticle(gas, "Reticle Mesh", reticleSO_ap, reticle_cached_ap);
            ApplyReticle(gas, "Reticle Mesh HEAT", reticleSO_heat, reticle_cached_heat);
        }

        private static void ApplyReticle(Transform gas, string child_name, ReticleSO tree, object cached)
        {
            Transform child = gas.Find(child_name);

            if (child == null || tree == null)
            {
                return;
            }

            ReticleMesh mesh = child.GetComponent<ReticleMesh>();

            if (mesh == null)
            {
                return;
            }

            mesh.reticleSO = tree;
            Reflect.Set(mesh, "reticle", cached);

            // ReticleMesh.Load() returns early when the renderer already holds a mesh (the
            // donor sight has one), so the mesh is cleared to force a rebuild. Assigning
            // `SMR = null` instead - what the mod used to do - makes Load() throw inside its
            // own try/catch and the custom reticle silently never appears.
            SkinnedMeshRenderer smr = mesh.GetComponent<SkinnedMeshRenderer>();
            if (smr != null)
            {
                smr.sharedMesh = null;
            }

            mesh.Load();
        }

        public static void Create(AmmoCodexScriptable sabot, AmmoCodexScriptable heat)
        {
            reticleSO_ap = reticleSO_heat = null;
            reticle_cached_ap = reticle_cached_heat = null;
            if (sabot == null || heat == null) return;
            foreach (ReticlePair pair in pairs)
            {
                if (pair.sabot != sabot || pair.heat != heat) continue;
                reticleSO_ap = pair.apTree; reticleSO_heat = pair.heatTree;
                reticle_cached_ap = pair.apCache; reticle_cached_heat = pair.heatCache;
                return;
            }

            EnsureSourceReticles();

            object ap_source = Reflect.DictGet(CachedReticles(), "M1_105_GAS_APFSDS");
            object heat_source = Reflect.DictGet(CachedReticles(), "M1_105_GAS_HEAT");

            if (ap_source == null || heat_source == null)
            {
                return;
            }

            reticleSO_ap = ScriptableObject.Instantiate(Reflect.Get<ReticleSO>(ap_source, "tree"));
            reticleSO_ap.name = "120mm_gas_ap";

            reticle_cached_ap = System.Runtime.CompilerServices.RuntimeHelpers.GetObjectValue(ap_source);
            Reflect.Set(reticle_cached_ap, "tree", reticleSO_ap);
            Reflect.Set(reticle_cached_ap, "mesh", null);

            ReticleTree.Angular boresight = ((reticleSO_ap.planes[0]
                as ReticleTree.FocalPlane).elements[0]
                as ReticleTree.Angular);

            ReticleTree.VerticalBallistic reticle_range_ap = boresight.elements[4] as ReticleTree.VerticalBallistic;
            reticle_range_ap.projectile = sabot;
            reticle_range_ap.UpdateBC();

            ReticleTree.Text reticle_text_ap = boresight.elements[0]
                as ReticleTree.Text;

            reticle_text_ap.text = sabot.AmmoType.Name + "\nMETERS";

            reticleSO_heat = ScriptableObject.Instantiate(Reflect.Get<ReticleSO>(heat_source, "tree"));
            reticleSO_heat.name = "120mm_gas_heat";

            reticle_cached_heat = System.Runtime.CompilerServices.RuntimeHelpers.GetObjectValue(heat_source);
            Reflect.Set(reticle_cached_heat, "tree", reticleSO_heat);
            Reflect.Set(reticle_cached_heat, "mesh", null);

            ReticleTree.Angular boresight_heat = ((reticleSO_heat.planes[0]
                as ReticleTree.FocalPlane).elements[0]
                as ReticleTree.Angular);

            ReticleTree.VerticalBallistic reticle_range_heat = boresight_heat.elements[4]
                as ReticleTree.VerticalBallistic;
            reticle_range_heat.projectile = heat;
            reticle_range_heat.UpdateBC();

            ReticleTree.Text reticle_text_heat = boresight_heat.elements[0]
                as ReticleTree.Text;

            string heat_name = heat.AmmoType.Name;
            reticle_text_heat.text = heat_name + "\nMETERS";
            pairs.Add(new ReticlePair { sabot = sabot, heat = heat, apTree = reticleSO_ap,
                heatTree = reticleSO_heat, apCache = reticle_cached_ap, heatCache = reticle_cached_heat });
        }

        /// <summary>
        /// ReticleMesh.cachedReticles is private and typed with a private nested struct, so
        /// it is read as object through the non-generic IDictionary view.
        /// </summary>
        private static object CachedReticles()
        {
            return Reflect.GetStatic<object>(typeof(ReticleMesh), "cachedReticles");
        }

        /// <summary>
        /// The vanilla reticles only enter ReticleMesh's cache when their optic is loaded.
        /// </summary>
        private static void EnsureSourceReticles()
        {
            if (!Reflect.DictHas(CachedReticles(), "M1_105_GAS_APFSDS")
                || !Reflect.DictHas(CachedReticles(), "M1_105_GAS_HEAT"))
            {
                Vehicle m1ip = AssetUtil.LoadVanillaVehicle("M1IP");

                if (m1ip == null)
                {
                    return;
                }

                Transform gas = m1ip.transform.Find("Gun Scripts/Aux sight (GAS)");

                if (gas == null)
                {
                    return;
                }

                UsableOptic optic = gas.GetComponent<UsableOptic>();

                if (optic != null && optic.reticleMesh != null)
                {
                    optic.reticleMesh.Load();
                }
            }
        }
    }
}


