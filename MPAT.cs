using System.Text;
using UnityEngine;
using UnityEngine.UI;
using GHPC.Weapons;
using HarmonyLib;
using GHPC.Player;
using GHPC.UI.Hud;
using GHPC.Utility;
using GHPC.PhysicsHelpers;
using GHPC;
using GHPC.Effects;
using System.Linq;

namespace M1A1Abrams
{
    public class MPATManager : MonoBehaviour
    {
        public bool ProximityActive { get; set; }
        public int AmmoCachedIdx { get; set; }


        private WeaponSystem weapon = null;
        private int self_id;
        private string[] ammoKeys;
        private LoadoutManager loadout;


        void Awake()
        {
            weapon = GetComponent<WeaponsManager>().Weapons[0].Weapon;
            weapon.Fired += OnFired;
            self_id = gameObject.GetInstanceID();
            ammoKeys = ModUtil.Reflect.GetStatic<string[]>(typeof(PlayerInput), "ammoKeys");
            loadout = GetComponent<LoadoutManager>();
        }

        void Update()
        {
            var playerUnit = PlayerInput.Instance?.CurrentPlayerUnit;
            if (playerUnit == null || playerUnit.gameObject.GetInstanceID() != self_id
                || weapon == null || weapon.CurrentAmmoType == null) return;

            if (weapon.CurrentAmmoType.CachedIndex != AmmoCachedIdx || loadout == null
                || ammoKeys == null || InputUtil.MainPlayer == null) return;
            var clips = loadout.LoadedAmmoList.AmmoClips;
            for (int i = 0; i < clips.Length && i < ammoKeys.Length; i++)
            {
                var pattern = clips[i]?.ClipType?.MinimalPattern;
                if (pattern == null || pattern.Length == 0 || pattern[0] == null
                    || pattern[0].AmmoType.CachedIndex != AmmoCachedIdx) continue;
                if (!InputUtil.MainPlayer.GetButtonDoublePressDown(ammoKeys[i])) continue;
                ProximityActive = !ProximityActive;
                break;
            }
        }

        private void OnDestroy()
        {
            if (weapon != null) weapon.Fired -= OnFired;
        }

        private void OnFired(AmmoType ammo, LiveRound live_round)
        {
            if (ammo.CachedIndex != AmmoCachedIdx || !ProximityActive) return;

            live_round.gameObject.AddComponent<MPAT>();
        }
    }

    public class MPAT : MonoBehaviour
    {
        public bool detonated = false;

        private LiveRound live_round;
        private static float[] ranges = new float[] { 10f, 30f };
        // static so the early-out below actually works: the round is created lazily from the mission's
        // own objects, which are the same for every MPAT. AmmoType is a plain class, so the cached
        // reference keeps its DetonateEffect alive for as long as this field points at it.
        private static AmmoType dummy_he;

        void Awake()
        {
            live_round = this.GetComponent<LiveRound>();

            if (dummy_he != null) return;

            dummy_he = new AmmoType();
            // FirstOrDefault, not First: with no match the exception this used to throw aborted
            // AddComponent<MPAT> and the proximity round silently lost its effect. No match now just
            // leaves the cache empty so a later round can try again.
            dummy_he.DetonateEffect = Resources.FindObjectsOfTypeAll<GameObject>().Where(o => o.name == "HE_explosion").FirstOrDefault();
            if (dummy_he.DetonateEffect == null)
            {
                dummy_he = null;
                return;
            }
            dummy_he.ImpactEffectDescriptor = new ParticleEffectsManager.ImpactEffectDescriptor()
            {
                HasImpactEffect = true,
                ImpactCategory = ParticleEffectsManager.Category.HighExplosive,
                EffectSize = ParticleEffectsManager.EffectSize.MainGun,
                RicochetType = ParticleEffectsManager.RicochetType.None,
                Flags = ParticleEffectsManager.ImpactModifierFlags.Large,
                MinFilterStrictness = ParticleEffectsManager.FilterStrictness.Low
            };
        }

        void OnDisable()
        {
            Component.Destroy(this);
        }

        void Detonate()
        {
            if (detonated) return;
            detonated = true;
            ModUtil.Reflect.Set(live_round, "_terrainHit", false);
            ModUtil.Reflect.Set(live_round, "_didTerrainHit", false);
            ModUtil.Reflect.Set(live_round, "_fuzeCompleted", true);
            ModUtil.Reflect.Set(live_round, "_materialHit", GHPC.Effects.ParticleEffectsManager.SurfaceMaterial.None);
            // createExplosion has a 5th parameter with a default value, which reflection
            // cannot omit, so the default (50 spall) is passed explicitly.
            ModUtil.Reflect.Call(live_round, "createExplosion", false, 0f, Vector3.zero, 0.03f, 50);
            live_round.Detonate();

            ParticleEffectsManager.Instance.CreateImpactEffectOfType(dummy_he,
                ParticleEffectsManager.FusedStatus.Fuzed, ParticleEffectsManager.SurfaceMaterial.None, false, transform.position);

            for (int i = 0; i < 25; i++)
            {
                GHPC.Weapons.LiveRound frag;
                frag = LiveRoundMarshaller.Instance.GetRoundOfVisualType(LiveRoundMarshaller.LiveRoundVisualType.Spall)
                    .GetComponent<GHPC.Weapons.LiveRound>();
                frag.Info = Ammo_120mm.m830a1_forward_frag;
                frag.CurrentSpeed = 600f;
                frag.MaxSpeed = 600f;
                frag.IsSpall = false;
                frag.Shooter = live_round.Shooter;
                frag.transform.position = live_round.transform.position;
                frag.transform.forward = Quaternion.Euler(
                    UnityEngine.Random.Range(-5f, 5f),
                    UnityEngine.Random.Range(-5f, 5f),
                    UnityEngine.Random.Range(-5f, 5f)
                ) * live_round.transform.forward;
                frag.Init(live_round, null);
                frag.name = "mpat forward frag " + i;
            }
        }

        void Update()
        {
            Vector3 pos = this.transform.position;
            for (int i = 0; i < ranges.Length; i++)
            {
                RaycastHit hit;
                Ray ray = new Ray(pos, i == 0 ? transform.forward : Vector3.down);
                if (
                    RaycastColliderUtils.Raycast(ray, out hit, ranges[i], ConstantsAndInfoManager.Instance.LaserRangefinderLayerMask)
                    && hit.collider.CompareTag("Penetrable")
                )
                {
                    Detonate();
                    return;
                }
            }

            RaycastHit hit_sphere;
            if (
                Physics.SphereCast(pos, 3f, this.transform.forward, out hit_sphere, 0.1f, ConstantsAndInfoManager.Instance.LaserRangefinderLayerMask) 
                && hit_sphere.collider.CompareTag("Penetrable")
            )
            {
                Detonate();
            }
        }
    }

    [HarmonyPatch(typeof(WeaponHud), "Update")]
    public static class MPATProximityHud
    {
        private static void Postfix(WeaponHud __instance)
        {
            PlayerInput player_input = ModUtil.Reflect.Get<PlayerInput>(__instance, "_playerInput");
            MPATManager mpat_manager = player_input?.CurrentPlayerUnit?.GetComponent<MPATManager>();

            if (mpat_manager == null || !mpat_manager.ProximityActive) return;

            Text hud_text = ModUtil.Reflect.Get<Text>(__instance, "_hudText");
            StringBuilder hud_builder = ModUtil.Reflect.Get<StringBuilder>(__instance, "_sb");

            if (hud_text == null || hud_builder == null) return;

            hud_text.text = hud_builder.ToString() + "\nMPAT Proximity";
        }
    }
}
