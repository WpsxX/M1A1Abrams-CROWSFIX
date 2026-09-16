using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using GHPC.Mission;
using GHPC.Vehicle;
using UnityEngine.AddressableAssets;
using System.Collections;
using GHPC.State;

namespace ModUtil
{
    internal class AssetUtil
    {
        private static UnitPrefabLookupScriptable.UnitPrefabMetadata[] lookup_all_units;
        // Handles acquired by this mod. Keep each addressable at most once.
        private static readonly List<AssetReference> loaded_asset_references = new List<AssetReference>();
        private static readonly List<AssetReference> temp_asset_references = new List<AssetReference>();
        private static readonly List<AssetReference> no_loaded_units = new List<AssetReference>();

        internal static void TempLoadVanillaVehicle(string name)
        {
            AssetReference prefab_ref = FindPrefabReference(name);

            if (prefab_ref == null || prefab_ref.Asset != null)
            {
                return;
            }

            TrackReference(temp_asset_references, prefab_ref);
            prefab_ref.LoadAssetAsync<GameObject>().WaitForCompletion();
        }

        internal static Vehicle LoadVanillaVehicle(string name)
        {
            AssetReference prefab_ref = FindPrefabReference(name);

            if (prefab_ref == null)
            {
                return null;
            }

            if (prefab_ref.Asset == null)
            {
                temp_asset_references.Remove(prefab_ref);
                TrackReference(loaded_asset_references, prefab_ref);
                GameObject asset = prefab_ref.LoadAssetAsync<GameObject>().WaitForCompletion();
                return asset == null ? null : asset.GetComponent<Vehicle>();
            }

            // Promote a temporary handle before returning it. The deferred temp cleanup must not
            // release a prefab that a dynamic module has retained for this scene.
            if (temp_asset_references.Remove(prefab_ref))
            {
                TrackReference(loaded_asset_references, prefab_ref);
            }

            GameObject loaded = prefab_ref.Asset as GameObject;
            return loaded == null ? null : loaded.GetComponent<Vehicle>();
        }


        internal static void ReleaseTempVanillaAssets()
        {
            ReleaseAssets(temp_asset_references, true);
        }

        internal static void ReleaseVanillaAssets()
        {
            ReleaseAssets(loaded_asset_references);
        }

        internal static IEnumerator ReleaseTempVanillaAssetsDeferred(GameState _)
        {
            ReleaseTempVanillaAssets();
            yield break;
        }

        internal static bool VehicleInMission(string name)
        {
            foreach (AssetReference unit in LoadedUnits())
            {
                if (unit.Asset != null && unit.Asset.name == name)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// `UnitSpawner._loadedUnits` is private, so the list is read through reflection.
        /// Returns an empty list when the spawner is not up yet (menu scenes).
        /// </summary>
        private static List<AssetReference> LoadedUnits()
        {
            List<AssetReference> units = Reflect.Get<List<AssetReference>>(UnitSpawner.Instance, "_loadedUnits");
            return units ?? no_loaded_units;
        }

        private static AssetReference FindPrefabReference(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            if (lookup_all_units == null)
            {
                UnitPrefabLookupScriptable lookup = Resources.FindObjectsOfTypeAll<UnitPrefabLookupScriptable>()
                    .FirstOrDefault();
                lookup_all_units = lookup == null ? null : lookup.AllUnits;
            }

            if (lookup_all_units == null)
            {
                return null;
            }

            UnitPrefabLookupScriptable.UnitPrefabMetadata metadata =
                lookup_all_units.FirstOrDefault(o => o != null && o.Name == name);
            return metadata == null ? null : metadata.PrefabReference;
        }

        private static void ReleaseAssets(List<AssetReference> asset_references, bool hard_destroy = false)
        {
            HashSet<string> released_guids = new HashSet<string>();
            HashSet<AssetReference> released_references = new HashSet<AssetReference>();

            foreach (AssetReference prefab in asset_references)
            {
                if (prefab == null)
                {
                    continue;
                }

                if (!released_references.Add(prefab))
                {
                    continue;
                }

                string guid = prefab.AssetGUID;
                if (!string.IsNullOrEmpty(guid) && !released_guids.Add(guid))
                {
                    continue;
                }

                try
                {
                    if (hard_destroy && !SpawnerOwns(prefab))
                    {
                        UnityEngine.Object asset = prefab.Asset;
                        if (asset != null)
                        {
                            GameObject.DestroyImmediate(asset);
                        }
                    }

                    prefab.ReleaseAsset();
                }
                catch (Exception)
                {
                    // Scene teardown can invalidate one handle; continue releasing other handles.
                }
            }

            asset_references.Clear();
        }

        private static bool SpawnerOwns(AssetReference reference)
        {
            if (reference == null)
            {
                return false;
            }

            string guid = reference.AssetGUID;
            foreach (AssetReference loaded in LoadedUnits())
            {
                if (loaded == reference)
                {
                    return true;
                }

                if (!string.IsNullOrEmpty(guid) && loaded != null && loaded.AssetGUID == guid)
                {
                    return true;
                }
            }

            return false;
        }

        private static void TrackReference(List<AssetReference> references, AssetReference reference)
        {
            if (reference == null || references.Contains(reference))
            {
                return;
            }

            string guid = reference.AssetGUID;
            if (!string.IsNullOrEmpty(guid))
            {
                for (int i = 0; i < references.Count; i++)
                {
                    if (references[i] != null && references[i].AssetGUID == guid)
                    {
                        return;
                    }
                }
            }

            references.Add(reference);
        }
    }
}
