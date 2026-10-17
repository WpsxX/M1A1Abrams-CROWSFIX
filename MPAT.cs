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
                || weapon == null) return;

            // Middle mouse toggles the proximity fuze, but only while the main gun is the weapon the
            // player is holding. CrowsAutoTrack binds the same button to CROWS lock/release and gates
            // itself on the CROWS FCS being the active one, so comparing FCS instance ids keeps the two
            // mutually exclusive instead of letting a single press do both. The round in the breech is
            // deliberately not checked: the flag only decides whether the next M830A1 gets a fuze, so it
            // can be set while reloading or with AP loaded.
            if (Input.GetKeyDown(KeyCode.Mouse2) && IsMainGunActive())
            {
                ProximityActive = !ProximityActive;
                return;
            }

            if (weapon.CurrentAmmoType == null
                || weapon.CurrentAmmoType.CachedIndex != AmmoCachedIdx || loadout == null
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

        /// <summary>
        /// True while the player is holding the main gun: the FCS of the weapon the player has selected
        /// is this weapon's. Mirrors CrowsAutoTrack.IsActiveWeapon, which owns the same middle mouse
        /// button for CROWS lock/release, so the two can never both answer one press.
        /// </summary>
        private bool IsMainGunActive()
        {
            PlayerInput input = PlayerInput.Instance;
            WeaponSystemInfo active = input == null ? null : input.CurrentPlayerWeapon;
            return active != null && active.FCS != null && weapon != null && weapon.FCS != null
                && active.FCS.GetInstanceID() == weapon.FCS.GetInstanceID();
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

        // The round leaves the muzzle at about 1400 m/s, i.e. roughly 23 m per frame at 60 fps, so a
        // fixed 10 m forward probe steps clean over a target and the fuze only ever fired when the
        // 30 m downward probe happened to catch a vehicle. The forward check is now a sphere sweep
        // over the distance the round actually travelled during the frame; the downward probe is
        // kept as a fallback so a round passing just above a vehicle still functions over it.
        private const float FuzeRadius = 3f;
        private const float VerticalReach = 30f;
        private const float MinimumClearance = 3f;

        private static readonly RaycastHit[] sweep_hits = new RaycastHit[64];
        // static so the early-out below actually works: the round is created lazily from the mission's
        // own objects, which are the same for every MPAT. AmmoType is a plain class, so the cached
        // reference keeps its DetonateEffect alive for as long as this field points at it.
        private static AmmoType dummy_he;

        private LiveRound live_round;
        private Vector3 launch_position;
        private Vector3 last_position;
        private bool started;

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

        void Detonate(Vector3 burst_point)
        {
            if (detonated) return;
            detonated = true;

            // Commit the predicted burst point: without this the explosion is placed wherever the
            // round happened to be sampled and can sit a frame's worth of travel away from the target.
            live_round.transform.position = burst_point;

            // LiveRound only builds a fuzed effect while _armed is set (SetEffectsData bails out
            // immediately on !_armed) and its effect bookkeeping is one-shot, so an airburst on a
            // pooled round used to produce spall but no blast, no sound and no impact effect at all.
            ModUtil.Reflect.Set(live_round, "_terrainHit", false);
            ModUtil.Reflect.Set(live_round, "_didTerrainHit", false);
            ModUtil.Reflect.Set(live_round, "_materialHit", ParticleEffectsManager.SurfaceMaterial.None);
            ModUtil.Reflect.Set(live_round, "_impactNormal", Vector3.zero);
            ModUtil.Reflect.Set(live_round, "_didFuzedEffect", false);
            ModUtil.Reflect.Set(live_round, "_didTerrainImpactVfx", false);
            ModUtil.Reflect.Set(live_round, "_didNonTerrainImpactVfx", false);
            ModUtil.Reflect.Set(live_round, "_fuzeCompleted", true);
            ModUtil.Reflect.Set(live_round, "_failedToDetonate", false);
            ModUtil.Reflect.Set(live_round, "_armed", true);
            ModUtil.Reflect.Set(live_round, "_exploded", false);
            if (live_round.Info != null)
                ModUtil.Reflect.Set(live_round, "_heHitNormalRha", live_round.Info.RhaToFuse);

            // createExplosion has a 5th parameter with a default value, which reflection
            // cannot omit, so it is passed explicitly.
            ModUtil.Reflect.Call(live_round, "createExplosion", false, 0f, Vector3.zero, 0.03f,
                live_round.Info != null ? live_round.Info.DetonateSpallCount : 50);
            live_round.Detonate();

            if (ParticleEffectsManager.Instance != null && dummy_he != null)
            {
                ParticleEffectsManager.Instance.CreateImpactEffectOfType(dummy_he,
                    ParticleEffectsManager.FusedStatus.Fuzed, ParticleEffectsManager.SurfaceMaterial.None, false, burst_point);
            }

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
                frag.transform.position = burst_point;
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
            if (detonated || live_round == null || live_round.IsDestroyed || live_round.Info == null) return;

            Vector3 position = transform.position;
            if (!started)
            {
                started = true;
                launch_position = position;
                last_position = position;
                return;
            }

            Vector3 previous = last_position;
            last_position = position;

            // Own-hull clearance: a proximity fuze must not function over the vehicle that fired it.
            float clearance = Mathf.Max(MinimumClearance, live_round.Info.ArmingDistance);
            if ((position - launch_position).sqrMagnitude < clearance * clearance) return;

            Vector3 burst_point;
            if (TryGetBurstPoint(previous, position, out burst_point)) Detonate(burst_point);
        }

        private bool TryGetBurstPoint(Vector3 previous, Vector3 position, out Vector3 burst_point)
        {
            burst_point = position;

            ConstantsAndInfoManager constants = ConstantsAndInfoManager.Instance;
            if (constants == null) return false;

            int mask = constants.LaserRangefinderLayerMask;
            Vector3 travel = position - previous;
            float distance = travel.magnitude;
            Vector3 direction = distance > 0.0001f ? travel / distance : transform.forward;

            // Sweep the whole frame's travel, not a fixed 10 m probe.
            float reach = Mathf.Max(distance, 0.01f);
            Ray sweep = new Ray(previous, direction);
            // The game's collider trees are enabled lazily, so let the helper switch them on before
            // the plain physics query below; its boolean result is not needed here.
            RaycastColliderUtils.SphereCast(sweep, FuzeRadius, reach, mask | RaycastColliderUtils.RAYCAST_LAYER_MASK);
            int count = Physics.SphereCastNonAlloc(sweep, FuzeRadius, sweep_hits, reach,
                mask & ~RaycastColliderUtils.GROUP_LAYER_MASK);

            float contact = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = sweep_hits[i];
                if (hit.collider == null || hit.distance >= contact || !IsTarget(hit.collider)) continue;
                contact = Mathf.Max(hit.distance, 0f);
            }
            if (contact != float.MaxValue)
            {
                burst_point = previous + direction * contact;
                return true;
            }

            // Fallback kept from the original fuze: function over a vehicle that the round passes above.
            RaycastHit below;
            if (RaycastColliderUtils.Raycast(new Ray(position, Vector3.down), out below, VerticalReach,
                    mask | RaycastColliderUtils.RAYCAST_LAYER_MASK)
                && IsTarget(below.collider))
            {
                return true;
            }

            return false;
        }

        private bool IsTarget(Collider collider)
        {
            if (collider == null || !collider.CompareTag("Penetrable")) return false;

            IArmor armor = collider.GetComponent<IArmor>();
            if (armor != null && ReferenceEquals(armor.Unit, live_round.Shooter)) return false;
            Unit unit = collider.GetComponentInParent<Unit>();
            return unit == null || !ReferenceEquals(unit, live_round.Shooter);
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
