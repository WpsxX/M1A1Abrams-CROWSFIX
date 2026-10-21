using System;
using System.Collections.Generic;
using System.IO;
using GHPC;
using GHPC.Camera;
using GHPC.Effects;
using GHPC.Equipment.Optics;
using GHPC.Player;
using GHPC.Thermals;
using GHPC.Utility;
using GHPC.Vehicle;
using GHPC.Weapons;
using GHPC.Weaponry;
using MelonLoader;
using MelonLoader.Utils;
using ModUtil;
using Reticle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VUtil = VanillaAssetUtil.ModUtil.Util;

namespace M1A1Abrams
{
    /// <summary>
    /// M153 CROWS II remote weapon station.
    ///
    /// Builds the station on the player's own Abrams: the .50 cal, its gunsight and its fire control
    /// computer are cloned from the cupola that is already on the vehicle, and the reticle tree and
    /// the range-box canvas are constructed in code. No other vehicle (M60A3, T-55A, M2 Bradley) has
    /// to be in the mission. Every non-public member is reached through ModUtil.Reflect: the running
    /// game loads the official (non-publicized) Assembly-CSharp.
    /// </summary>
    internal static partial class CROWS
    {
        private static bool MountInternal(Vehicle vic, Transform origin, Vector3 position, bool alt_placement, UsableOptic main_gun_thermal)
        {
            GameObject model = UnityEngine.Object.Instantiate(crows_prefab, origin);
            model.name = "CROWS";

            // Everything below is assembled while the hierarchy is inactive: the game's
            // Awake() methods (AimablePlatform, CameraSlot, AmmoRack, LateFollow, ...) all
            // assume their serialized fields are already filled in and throw otherwise.
            model.SetActive(false);
            model.transform.localPosition = position;
            model.transform.localEulerAngles = Vector3.zero;

            // Each of these lookups used to be dereferenced straight away, so a donor model missing any
            // one of them threw a NullReferenceException out of Mount() and left the prefab's native
            // objects behind. Checked up front, the failure is reported and the clone released instead -
            // the same shape LoadModel() uses for the finds it does there.
            Transform mount_tr = model.transform.Find("MOUNT");
            Transform gun_tr = model.transform.Find("GUN");
            Transform gun_vis_tr = gun_tr == null ? null : gun_tr.Find("GUN VIS");
            Transform mount_vis_tr = mount_tr == null ? null : mount_tr.Find("MOUNT VIS");
            Transform gun_col_tr = gun_tr == null ? null : gun_tr.Find("GUN COLLIDER");
            Transform mount_col_tr = mount_tr == null ? null : mount_tr.Find("MOUNT COLLIDER");
            Transform gun_scripts_tr = mount_tr == null ? null : mount_tr.Find("GUN SCRIPTS");

            string missing = mount_tr == null ? "MOUNT"
                : gun_tr == null ? "GUN"
                : gun_vis_tr == null ? "GUN/GUN VIS"
                : mount_vis_tr == null ? "MOUNT/MOUNT VIS"
                : gun_col_tr == null ? "GUN/GUN COLLIDER"
                : mount_col_tr == null ? "MOUNT/MOUNT COLLIDER"
                : gun_scripts_tr == null ? "MOUNT/GUN SCRIPTS"
                : null;

            if (missing != null)
            {
                Fail(missing + " missing from the donor model");
                UnityEngine.Object.Destroy(model);
                return false;
            }

            // The station's .50 cal, sight and computer are cloned off this vehicle's own cupola, so
            // the CROWS no longer needs an M60A3 in the mission. Done before anything is built: a
            // vehicle that cannot supply them should not leave half a station behind.
            if (!ResolveOwnCupola(vic == null ? null : vic.transform))
            {
                UnityEngine.Object.Destroy(model);
                return false;
            }

            GameObject mount = mount_tr.gameObject;
            GameObject gun = gun_tr.gameObject;
            GameObject gun_vis = gun_vis_tr.gameObject;
            GameObject mount_vis = mount_vis_tr.gameObject;
            GameObject gun_col = gun_col_tr.gameObject;
            GameObject mount_col = mount_col_tr.gameObject;
            GameObject gun_scripts = gun_scripts_tr.gameObject;

            mount.transform.localEulerAngles = Vector3.zero;
            gun.transform.localEulerAngles = Vector3.zero;

            // UniformArmor reports these colliders as penetrable targets.
            VUtil.CreateUniformArmour(gun_col, "M2HB machine gun", 5f, 5f);
            UniformArmor gun_armour = gun_col.GetComponent<UniformArmor>();
            gun_armour.Unit = vic;
            gun_col.layer = 7;

            VUtil.CreateUniformArmour(mount_col, "CROWS", 15f, 15f);
            UniformArmor mount_armour = mount_col.GetComponent<UniformArmor>();
            mount_armour.Unit = vic;
            mount_col.layer = 7;

            // The colliders are deliberately sent to the scene root and kept in place by the
            // LateFollow components below; AllowEmptyParent silences GHPC's "empty reference"
            // error, which does not apply to this (upstream) arrangement.
            Reparent gun_reparent = gun_col.AddComponent<Reparent>();
            gun_reparent.AllowEmptyParent = true;

            Reparent mount_reparent = mount_col.AddComponent<Reparent>();
            mount_reparent.AllowEmptyParent = true;

            // The colliders live at the scene root and are dragged along by LateFollow.
            gun_vis.AddComponent<LateFollowTarget>();
            mount_vis.AddComponent<LateFollowTarget>();

            LateFollow lf_gun = gun_col.AddComponent<LateFollow>();
            lf_gun.FollowTarget = gun_vis.transform;
            lf_gun.enabled = true;

            LateFollow lf_mount = mount_col.AddComponent<LateFollow>();
            lf_mount.FollowTarget = mount_vis.transform;
            lf_mount.enabled = true;

            // --- aimable platforms (the turret ring and the gun cradle) ---
            //
            // Both platforms chase the same world-space aim independently, so they must run at the same
            // speed or the sight and the model rotate at different rates and AimablePlatform's
            // per-second error correction snaps the difference away. One speed for both.
            // The rates themselves are the station's published ones - 90 degrees a second in traverse, 50
            // in elevation - and they live at the top of this class, because the tracking module and the
            // manual-offset walk are built on the same two numbers. They are separate now rather than one
            // number for both axes, which is what they were.

            AimablePlatform aimable_mount = AddAimablePlatform(mount, model.transform, GHPC.Utility.EnumHelpers.Axis3.Y, false);
            AimablePlatform aimable_gun = AddAimablePlatform(gun_scripts, gun.transform, GHPC.Utility.EnumHelpers.Axis3.X, true);
            aimable_gun.LocalEulerLimits = ELEVATION_LIMITS;
            aimable_mount.SpeedPowered = TRAVERSE_SPEED_DEGREES_PER_SECOND;
            aimable_gun.SpeedPowered = ELEVATION_SPEED_DEGREES_PER_SECOND;

            // The mount follows the fire control's aim with no servo lag, and the cradle carries no
            // azimuth of its own, so the model, the sight and the aim all share one reference.
            CrowsAimSlave aim_slave = model.AddComponent<CrowsAimSlave>();
            aim_slave.model = model.transform;
            aim_slave.cradle = gun.transform;
            aim_slave.mount_platform = aimable_mount;
            aim_slave.cradle_platform = aimable_gun;
            aim_slave.fcs = null; // set once the fire control exists

            // The servo's execution order is what puts it after the sights' own alignment (order 0) and
            // before the game renders the thermal picture (order 10); see CrowsAimSlave. Said out loud once
            // so a log proves the attribute survived to the running build.

            // --- the .50 cal, its sight and its computer, cloned off this vehicle's own cupola ---
            // IMPORTANT: every template is Instantiate'd as INACTIVE so the clone's Awake() methods run
            // after the wiring below rather than during Instantiate. That is not only the FCS's cached
            // reference position: the gun's and the sight's own Reparent scripts would otherwise drag
            // the clone back into the cupola it came from before SanitizeBorrowed can remove them.
            bool fcs_was_active = m2_fcs.activeSelf;
            GameObject fcs_go;
            try
            {
                m2_fcs.SetActive(false);
                fcs_go = UnityEngine.Object.Instantiate(m2_fcs, mount.transform);
            }
            finally
            {
                if (m2_fcs != null) m2_fcs.SetActive(fcs_was_active);
            }

            bool browning_was_active = m2_browning.activeSelf;
            GameObject browning;
            try
            {
                m2_browning.SetActive(false);
                browning = UnityEngine.Object.Instantiate(m2_browning, gun.transform);
            }
            finally
            {
                if (m2_browning != null) m2_browning.SetActive(browning_was_active);
            }
            // MK19 muzzle, measured on the crowmk19 prefab in GUN's own frame: the bore ring on
            // the muzzle face is centred on (0.0005, 0.0489) with a 0.027 m radius, so the
            // effects sit on the barrel axis instead of on the top edge of the crown.
            browning.transform.localPosition = new Vector3(0.0005f, 0.0489f, 0.5006f);

            // Mk 19 mode is decided up front: it picks the sight anchor here and the ammo /
            // cycle / sound setup further down. The coaxial M2 is a separate station.
            bool mk19 = M1A1.crows_mk19 != null && M1A1.crows_mk19.Value;
            Vector3 sight_pos = mk19 ? MK19_SIGHT_POS : M2_SIGHT_POS;

            // The crowmk19 mesh IS the visible launcher; the borrowed M85 only supplies the
            // weapon system, its muzzle transform and its effects. Left visible, a whole
            // .50 cal barrel hangs out of the Mk 19.
            if (mk19)
            {
                // Mesh renderers only: the muzzle flash and smoke live on ParticleSystemRenderer
                // components under this object, and killing those takes the effects with them.
                foreach (var r in browning.GetComponentsInChildren<MeshRenderer>(true)) r.enabled = false;
                foreach (var r in browning.GetComponentsInChildren<SkinnedMeshRenderer>(true)) r.enabled = false;
            }

            // The donor's own Reparent scripts must not survive the clone: theirs point at the donor
            // tank's cupola, and Reparent.Awake() does `transform.parent = NewParent`, which lifts the
            // borrowed gun into the donor prefab's hierarchy where nothing on it ever runs.
            SanitizeBorrowed(browning, model.transform);
            SanitizeBorrowed(fcs_go, model.transform);

            bool sight_was_active = m2_gun_sight.activeSelf;
            GameObject gun_sight;
            try
            {
                m2_gun_sight.SetActive(false);
                gun_sight = UnityEngine.Object.Instantiate(m2_gun_sight, gun.transform);
            }
            finally
            {
                if (m2_gun_sight != null) m2_gun_sight.SetActive(sight_was_active);
            }
            SanitizeBorrowed(gun_sight, model.transform);
            gun_sight.transform.localPosition = sight_pos;
            gun_sight.transform.localRotation = Quaternion.identity;
            gun_sight.transform.localScale = Vector3.one;

            CameraSlot sight_slot = gun_sight.GetComponent<CameraSlot>();
            UsableOptic day_optic = gun_sight.GetComponent<UsableOptic>();

            DetachFromDonor(day_optic, model.transform);
            DetachFromDonor(sight_slot, model.transform);

            if (sight_slot != null)
            {
                // The M85 gunsight's CameraSlot reparents itself to the M60A3's cupola
                // mantlet in Awake(). That reference is outside the cloned subtree, so it
                // survives Instantiate and would drag the CROWS sight onto the donor tank.
                sight_slot.transform.SetParent(gun.transform, false);
                Reflect.Set(sight_slot, "_reparentTarget", gun.transform);
            }

            GameObject lase = new GameObject("lase");
            lase.transform.SetParent(gun.transform, false);
            lase.transform.localPosition = Vector3.zero;
            lase.transform.localEulerAngles = Vector3.zero;

            // Create a stable FCS reference point that won't be affected by CameraSlot reparenting.
            // This point sits at the gun_sight's position but is NOT a CameraSlot, so it stays put.
            GameObject fcs_reference = new GameObject("fcs_reference");
            fcs_reference.transform.SetParent(gun.transform, false);
            fcs_reference.transform.localPosition = sight_pos;
            fcs_reference.transform.localRotation = Quaternion.identity;

            // Shared ammo selection; CROWS keeps its belt and no reserve. The Mk 19 option
            // swaps only this station's weapon - the Abrams' coaxial M2 keeps its own rounds.
            AmmoType.AmmoClip clip = mk19 ? Mk19.Select(M1A1.crows_mk19_ammo.Value) : null;
            if (clip == null)
            {
                mk19 = false;
                clip = CrowsAmmo.Select(M1A1.crows_ammo.Value);
            }
            if (clip == null) clip = m8_belt;

            GameObject rack_go = new GameObject("crows ammo rack");
            rack_go.transform.SetParent(gun.transform, false);
            rack_go.SetActive(false);

            GHPC.Weapons.AmmoRack ammo_rack = rack_go.AddComponent<GHPC.Weapons.AmmoRack>();
            ammo_rack.ClipTypes = new AmmoType.AmmoClip[] { clip };
            // The Mk 19 gets spare belts it can change into; the .50 cal keeps its single belt.
            int spare_belts = mk19 ? Mk19Rules.ReserveBelts : 0;
            Reflect.Set(ammo_rack, "_initialClipCounts", new int[] { spare_belts });
            // The rack's Awake() reads both of those, so they are set before the object is activated.
            rack_go.SetActive(true);

            // The HUD counts the rack's stored clips, so make the spare belts certain rather
            // than relying on Awake having seen the counts above.
            System.Collections.Generic.List<AmmoType.AmmoClip> stored = ammo_rack.StoredClips;
            while (stored.Count > spare_belts) stored.RemoveAt(stored.Count - 1);
            while (stored.Count < spare_belts) stored.Add(clip);

            WeaponSystem weapon = browning.GetComponent<WeaponSystem>();
            DetachFromDonor(weapon, model.transform);
            // The donor's AmmoFeed still points at the M60A3's "ROOF MG RACK", and the
            // weapon's BarrelHitZone at the M60A3's turret armour - both are dropped here.
            DetachFromDonor(weapon.Feed, model.transform);

            if (weapon.Feed == null || weapon.WeaponSound == null)
            {
                UnityEngine.Object.Destroy(model);
                return false;
            }

            weapon.Feed.ReadyRack = ammo_rack;
            if (mk19)
            {
                // Cycle rate, dispersion and the fire sound all belong to the launcher, not
                // to the .50 cal donor the weapon object came from.
                Mk19.Configure(weapon);
            }
            else
            {
                Reflect.Set(weapon, "_cycleTimeSeconds", 0.1091f);
                Reflect.Set(weapon.Feed, "_totalCycleTime", 0.1091f);
                weapon.WeaponSound.LoopEventPath = "event:/Weapons/MG_m2_550rmp";
                CrowsAmmo.AttachTracerLimit(weapon);
            }

            // The HUD names a weapon from `WeaponSystem.WeaponData` (= its CodexEntry), so the clone
            // would keep introducing itself as the donor's M85. The donor's codex is cloned to keep
            // every other parameter (audio, jiggle, blur) and renamed.
            WeaponSystemCodexScriptable donor_codex = weapon.WeaponData;

            if (donor_codex != null)
            {
                WeaponSystemCodexScriptable codex = ScriptableObject.CreateInstance<WeaponSystemCodexScriptable>();
                VUtil.ShallowCopy(codex, donor_codex);
                codex.name = mk19 ? "crows_mk19" : "crows_m2hb";
                codex.FriendlyName = mk19 ? CROWS_MK19_NAME : CROWS_NAME;
                codex.CaliberMm = mk19 ? Mk19Rules.CaliberMm : 12.7f;
                Reflect.Set(weapon, "CodexEntry", codex);
            }

            // --- fire control computer ---
            FireControlSystem fcs = fcs_go.GetComponent<FireControlSystem>();
            DetachFromDonor(fcs, model.transform);
            
            // FIX: Set ReferenceTransform BEFORE activating fcs_go, so FCS.Awake() caches the correct position.
            // The borrowed M60A3 FCS ReferenceTransform carries the cupola offset (0.585m right, 2.864m up).
            // We use fcs_reference (a stable GameObject that won't be reparented by CameraSlot) instead
            // of gun_sight.transform (which gets moved by CameraSlot._reparentTarget in UMC missions).
            fcs.ReferenceTransform = fcs_reference.transform;
            // The reference now sits at the sight, so muzzle parallax must be corrected
            // in manual aim as well as tracking. Attach() captures this as the restore value.
            Reflect.Set(fcs, "_fixParallaxForVectorMode", true);
            
            fcs.Mounts = new AimablePlatform[] { aimable_mount, aimable_gun };
            aim_slave.fcs = fcs;

            CameraSlot optic_slot = day_optic.slot;
            Reflect.Set(optic_slot, "_pairedOptic", day_optic);
            optic_slot.ExclusiveWeapons = new WeaponSystem[] { weapon };
            optic_slot.VibrationShakeMultiplier = 0f;
            optic_slot.VibrationBlurScale = 0f;
            optic_slot.DefaultFov = DAY_FOV_1X;
            optic_slot.OtherFovs = new float[] { DAY_FOV_5X, DAY_FOV_10X, DAY_FOV_15X };
            Reflect.Set(optic_slot, "_reparentTarget", gun.transform);

            day_optic.Alignment = OpticAlignment.BoresightStabilized;
            day_optic.RotateAzimuth = true;
            day_optic.RotateElevation = true;
            day_optic.LocalElevationLimits = ELEVATION_LIMITS;

            fcs.LaserOrigin = lase.transform;
            fcs.LaserAim = LaserAimMode.ImpactPoint;
            fcs.MaxLaserRange = 4000f;
            fcs.LinkedWeaponSystems = new WeaponSystem[] { weapon };
            // A 241 m/s grenade needs degrees of superelevation at range, and this station's sight
            // rides the gun - so the Mk 19 keeps the compensation out of the mount and puts it in
            // the muzzle instead (see Mk19BallisticMuzzle). The mount then holds the aim line,
            // the sight never climbs, and the rounds still arc. The M2 keeps the stock behaviour.
            fcs.SuperelevateWeapon = !mk19;
            fcs.SuperleadWeapon = true;
            fcs.CurrentStabMode = StabilizationMode.Vector;
            fcs.SetStabsActive(true);
            CrowsStabiliserLock.Lock(fcs);
            fcs.RecordTraverseRateBuffer = true;
            fcs.TraverseBufferSeconds = 0.1f;
            fcs.DynamicLead = true;

            // Manual lead includes horizontal and vertical target motion. Native
            // inertial compensation uses the host's full world-space velocity;
            // ATT replaces it with its own relative-motion solution while locked.
            Reflect.Set(fcs, "InertiaLeadIncludesVertical", true);
            Reflect.Set(fcs, "_horizontalLeadOnly", false);
            fcs.InertialCompensation = true;

            // FireControlSystem.Awake() cached these before we changed the fields above, so
            // leaving manual mode would otherwise switch superelevation/lead back off.
            Reflect.Set(fcs, "_autoDumpViaPalmSwitches", true);
            Reflect.Set(fcs, "_originalSuperelevationMode", !mk19);
            Reflect.Set(fcs, "_originalSuperleadMode", true);

            weapon.FCS = fcs;

            if (mk19)
            {
                // The barrel has to show the ballistic angle without dragging the sight off the
                // target. A pivot at the trunnion - GUN's own origin - carries the visible gun and
                // the borrowed weapon object that owns MuzzleIdentity, so the barrel, the muzzle
                // flash and the round's launch direction all turn together; GUN itself, which the
                // camera slot and the fire control's aim line hang from, is left alone.
                Transform ballistic = new GameObject("BALLISTIC").transform;
                ballistic.SetParent(gun.transform, false);
                ballistic.localPosition = Vector3.zero;
                ballistic.localRotation = Quaternion.identity;
                ballistic.localScale = Vector3.one;

                gun_vis.transform.SetParent(ballistic, true);

                // The muzzle anchor travels with the barrel too: it is what the round is launched
                // from and where the flash is spawned, so it has to sit at the tip of the barrel
                // that is actually shown. The borrowed weapon object itself is left where it is -
                // WeaponSystem re-places it at the muzzle on every shot, and moving it under this
                // pivot would put that assignment back into the rotation chain.
                Transform muzzle = weapon.MuzzleIdentity;
                if (muzzle != null && muzzle != gun.transform && muzzle != ballistic)
                {
                    muzzle.SetParent(ballistic, true);
                }

                Mk19BallisticMuzzle ballistics = weapon.gameObject.AddComponent<Mk19BallisticMuzzle>();
                ballistics.fcs = fcs;
                ballistics.ballistic = ballistic;
                ballistics.gun = gun.transform;
            }

            // UsableOptic.Start() wires itself into whatever FCS it points at, so both optics
            // are pointed at the CROWS computer; only the day sight claims the main optic slot.
            day_optic.FCS = fcs;
            day_optic.RegisterAtStart = true;

            // --- reticle, thermal sight and range boxes ---
            ReticleMesh reticle_mesh = day_optic.reticleMesh;
            if (reticle_mesh != null)
            {
                reticle_mesh.reticleSO = reticle_so;

                // Load() bails out early when the renderer already has a mesh, which is the
                // case for the donor sight; clearing it is what makes the CROWS reticle stick.
                SkinnedMeshRenderer smr = reticle_mesh.GetComponent<SkinnedMeshRenderer>();
                if (smr != null)
                {
                    smr.sharedMesh = null;
                }

                reticle_mesh.Load();
            }

            GameObject gun_sight_flir = UnityEngine.Object.Instantiate(gun_sight, gun.transform);
            SanitizeBorrowed(gun_sight_flir, model.transform);
            UsableOptic flir_optic = gun_sight_flir.GetComponent<UsableOptic>();
            DetachFromDonor(flir_optic, model.transform);
            DetachFromDonor(flir_optic.slot, model.transform);
            gun_sight_flir.transform.localPosition = sight_pos;
            gun_sight_flir.transform.localRotation = Quaternion.identity;
            gun_sight_flir.transform.localScale = Vector3.one;
            Reflect.Set(flir_optic.slot, "_reparentTarget", gun.transform);
            flir_optic.slot.DefaultFov = DAY_FOV_1X;
            flir_optic.slot.OtherFovs = new float[] { DAY_FOV_5X, DAY_FOV_10X, DAY_FOV_15X };
            flir_optic.slot.VisionType = NightVisionType.Thermal;
            flir_optic.slot.IsLinkedNightSight = true;
            flir_optic.slot.LinkedDaySight = optic_slot;
            flir_optic.slot.VibrationBlurScale = 0f;
            flir_optic.slot.VibrationShakeMultiplier = 0f;
            ConfigureMainGunThermal(flir_optic, main_gun_thermal);

            // Main-gun FLIR resolutions may have a different aspect ratio from the screen.
            // CrowsAutoTrack corrects marker projection using the thermal camera's actual aspect.
            //
            // The donor M85 gunsight this is cloned from is OpticAlignment.Fixed with UseRotationForShake
            // on, and its own LateUpdate therefore *resets* the slot's local rotation to the one it was
            // cloned with, every frame, and then shakes it. The slot hangs off the gun, so a Fixed sight
            // points wherever the *barrel* points: it inherits the gun's superelevation and the gun's servo
            // lag instead of following the fire control's aim the way the day sight does. So the moment a
            // lock is taken the computer is handed a range, the cradle elevates to compensate for it, and
            // the thermal picture - and the target in it - drops out from under the reticle, which reads as
            // the box sinking; and while the aim is being slewed the picture lags and hunts with the mount,
            // which reads as the shake. The day sight has none of that because it is BoresightStabilized.
            flir_optic.Alignment = OpticAlignment.BoresightStabilized;
            flir_optic.RotateAzimuth = true;
            flir_optic.RotateElevation = true;
            flir_optic.LocalElevationLimits = ELEVATION_LIMITS;
            flir_optic.UseRotationForShake = false;

            // One line, once a mission, so a log says whether the thermal sight really was configured this
            // way: if it ever reads Fixed/True again, the thermal lock report is on its way back.

            flir_optic.FCS = fcs;
            flir_optic.RegisterAtStart = false;

            ReticleMesh flir_reticle = flir_optic.reticleMesh;
            if (flir_reticle != null)
            {
                SkinnedMeshRenderer flir_smr = flir_reticle.GetComponent<SkinnedMeshRenderer>();
                if (flir_smr != null)
                {
                    flir_smr.sharedMesh = null;
                }
            }

            optic_slot.LinkedNightSight = flir_optic.slot;
            Reflect.SetProperty(fcs, "NightOptic", flir_optic);

            // The range the tracker writes has to survive the trip into the computer, because the
            // computer clamps it to RegisteredRangeLimits in the TargetRange setter and again on the way
            // into CurrentRange - and the range is not a readout, it is the superelevation and the whole
            // scale of the lead (the lead is a rate times the range). A donor whose own limits stop
            // short of this station's envelope would silently dial a short solution and a short lead,
            // and the rounds would then land short and off to the side the target is moving.
            //
            // The limits come from the donor's prefab, so they are pinned rather than assumed - on the
            // fire control and on both optics, because FireControlSystem.NotifyActiveOpticChanged
            // re-reads them from whichever sight the player switches to and would otherwise put the
            // donor's values back the first time the thermal sight is selected.
            PinRangeLimits(fcs, new UsableOptic[] { day_optic, flir_optic }, new Vector2(0f, 4000f));

            // The station's manual slew - the traverse and elevation the player gets while nothing is
            // locked - is handed to CrowsAimLock here, per magnification, once the fire control and both
            // sights exist. See that file for why the mod owns this number rather than letting
            // FireControlSystem.AdjustAimByRatio multiply it out of the platforms' rated speeds.
            CrowsAimLock.Register(fcs, new UsableOptic[] { day_optic, flir_optic });

            // Both sights get the mod's frame: the thermal optic is a separate object that the
            // game hides while the day sight is in use (and vice versa), so sharing one set of
            // canvases would make the frame disappear when the player switches to thermal.
            AddFrameBoxes(day_optic, model.transform);
            AddFrameBoxes(flir_optic, model.transform);

            // The font the user asked for: VCR OSD Mono, falling back to the game's Arial, then to
            // whatever the mod's own sight markers use, then to the TMP default.
            TMP_FontAsset ui_font = PickSightUiFont();

            // Every CROWS in the mission owns its own pair of reticle meshes, and they all read the same
            // tree, so all of them are kept on the list the rescaling walks. See RegisterReticleMesh.
            RegisterReticleMesh(day_optic.reticleMesh);
            RegisterReticleMesh(flir_optic.reticleMesh);

            // Both meshes are this mission's now, and the scale remembered from the last mission
            // belongs to meshes that no longer exist. See InvalidateReticleScale.
            InvalidateReticleScale();

            // The automatic tracker is created here, with the fire control and both sights in place,
            // but stays switched off until Activate() below: the fire control's Awake() has not run
            // yet, and it is what caches the defaults ArmLead() has to overwrite.
            CrowsAutoTrack tracker = CrowsAutoTrack.Attach(
                fcs, vic, new UsableOptic[] { day_optic, flir_optic }, weapon);

            // The Mk 19 station compensates at the muzzle (see Mk19BallisticMuzzle), so the tracker
            // must not let the fire control lift the mounts - and the sight with them - by the
            // ballistic angle. Every other station keeps the default and is unaffected.
            tracker.superelevate_mounts = !mk19;

            // The model's servo follows the compensated aim only while the tracker holds a lock.
            aim_slave.tracker = tracker;
            aim_slave.sights = new UsableOptic[] { day_optic, flir_optic };

            // GHPC hands a camera its instanced geometry through a cache this mod cannot see and must
            // not second-guess: the renderer's every-camera pass clears it once a frame, and the
            // per-camera read that follows is an exact match on a key built from the camera's whole
            // render state, which a moving vehicle never repeats. The only repair that does not race
            // the collection threads is to stay out of their data and submit for the camera ourselves:
            // schedule a collection of its own for the sight, and draw what it produced. See
            // CrowsInstancingAssist.
            CrowsInstancingAssist.Attach(fcs.gameObject);


            CrowsSightUi.Attach(day_optic, optic_slot, flir_optic.slot, fcs, weapon, model.transform, ui_font, tracker);
            CrowsSightUi.Attach(flir_optic, flir_optic.slot, optic_slot, fcs, weapon, model.transform, ui_font, tracker);

            CrowsScopeOverlay overlay = fcs.gameObject.AddComponent<CrowsScopeOverlay>();
            overlay.day_slot = optic_slot;
            overlay.night_slot = flir_optic.slot;

            // --- register the station with the vehicle ---
            WeaponSystemInfo crows_info = new WeaponSystemInfo();
            crows_info.Name = mk19 ? CROWS_MK19_NAME : CROWS_NAME;
            crows_info.FCS = fcs;
            crows_info.Weapon = weapon;
            crows_info.Role = WeaponSystemRole.RoofGun;
            crows_info.PreAimWeapon = WeaponSystemRole.MainGun;

            InstallWeaponInfo(vic, crows_info);

            Reflect.SetProperty(vic, "AimablePlatforms", VUtil.AppendToArray(vic.AimablePlatforms, aimable_mount));
            Reflect.SetProperty(vic, "AimablePlatforms", VUtil.AppendToArray(vic.AimablePlatforms, aimable_gun));
            Reflect.Set(vic, "_designatedCameraSlots", VUtil.AppendToArray(vic.DesignatedCameraSlots, optic_slot));

            Unit player_unit = PlayerInput.Instance == null ? null : PlayerInput.Instance.CurrentPlayerUnit;

            if (player_unit != null && vic.GetInstanceID() == player_unit.GetComponent<Vehicle>().GetInstanceID())
            {
                // Passed as object[] on purpose: CameraSlot[] is covariant with object[], so handing it
                // straight to a `params object[]` helper would spread it and make the lookup search for
                // an N-argument overload - which silently does nothing.
                Reflect.Call(CameraManager.Instance, "RescanCamSlots", new object[] { vic.DesignatedCameraSlots });
            }

            if (!alt_placement)
            {
                // The station sits where the commander's hatch was, so his view moves back.
                Transform commander_slot = vic.DesignatedCameraSlots.Length > 0
                    ? vic.DesignatedCameraSlots[0].transform
                    : null;

                if (commander_slot != null)
                {
                    commander_slot.localPosition = new Vector3(-0.1538f, 0.627f, -0.05f);
                }
            }

            StaticWhenNotInUse fix = fcs.gameObject.AddComponent<StaticWhenNotInUse>();
            fix.fcs = fcs;

            CrowsAmmoSetup setup = fcs.gameObject.AddComponent<CrowsAmmoSetup>();
            setup.weapon = weapon;
            setup.clip = clip;

            // Instantiate() copies the donor's runtime active state, and the parts were not
            // necessarily switched on in the donor. A clone that stays inactive never runs its
            // Awake/Start, which breaks the gun in subtle ways - no belt, no weapon audio, no cycling.
            EnsureActive(browning, model.transform);
            // fcs_go is activated LAST, after model.SetActive(), so FCS.Awake() runs with correct ReferenceTransform
            EnsureActive(gun_sight, model.transform);
            EnsureActive(gun_sight_flir, model.transform);
            EnsureActive(rack_go, model.transform);

            model.SetActive(true);
            
            // CRITICAL: Activate fcs_go AFTER model.SetActive(true) to ensure FCS.Awake() sees the correct
            // ReferenceTransform position. In UMC missions, CameraSlot reparenting happens during Start(),
            // so activating FCS before the model would cache the wrong reference position.
            EnsureActive(fcs_go, model.transform);

            // Last of all: the fire control has now had its Awake() pass (which caches the
            // superelevation and lead defaults), so the tracker may arm the game's own lead
            // computation and start running.
            if (tracker != null)
            {
                tracker.Activate();
            }

            return true;
        }

        /// <summary>
        /// Replaces the vehicle's existing roof gun entry in place when there is one, so the arsenal
        /// does not end up with two roof guns. The array keeps its length: the crew weapon lists are
        /// built from the same instances and index into it.
        /// </summary>
        private static void InstallWeaponInfo(Vehicle vic, WeaponSystemInfo crows_info)
        {
            WeaponsManager manager = vic.WeaponsManager;
            WeaponSystemInfo[] weapons = manager.Weapons;

            for (int i = 0; i < weapons.Length; i++)
            {
                if (weapons[i] == null || weapons[i].Role != WeaponSystemRole.RoofGun)
                {
                    continue;
                }

                weapons[i].Name = crows_info.Name;
                weapons[i].FCS = crows_info.FCS;
                weapons[i].Weapon = crows_info.Weapon;
                weapons[i].Role = crows_info.Role;
                weapons[i].PreAimWeapon = crows_info.PreAimWeapon;
                return;
            }

            manager.Weapons = VUtil.AppendToArray(manager.Weapons, crows_info);
        }

        /// <summary>
        /// Pins the range envelope the fire control will accept, on the computer and on every sight the
        /// player can switch to.
        ///
        /// The computer clamps everything written to <c>TargetRange</c> to
        /// <c>RegisteredRangeLimits</c>, and it re-derives those limits every time the active optic
        /// changes - from the optic's own override when it has one, and from the value the donor's
        /// prefab carried when it does not. Both of those belong to the borrowed sight, not to this
        /// station, so both are written here: the field for immediate use, and the optic's override (and
        /// the flag that says it has one, which is only ever set in Awake) so that switching between the
        /// day and thermal sights cannot quietly put the donor's limits back.
        ///
        /// Nothing changes for a donor whose limits already cover the station; when they do not, the
        /// log says so, because the symptom - rounds falling short and off to the side of a mover - does
        /// not point at the range on its own.
        /// </summary>
        private static void PinRangeLimits(FireControlSystem fcs, UsableOptic[] optics, Vector2 limits)
        {
            if (fcs == null)
            {
                return;
            }

            try
            {
                Vector2 donor = fcs.RegisteredRangeLimits;

                if (donor.y < limits.y || donor.x > limits.x)
                {
                }

                fcs.RegisteredRangeLimits = limits;

                for (int i = 0; i < optics.Length; i++)
                {
                    UsableOptic optic = optics[i];

                    if (optic == null)
                    {
                        continue;
                    }

                    optic.OverrideRangeLimits = limits;
                    Reflect.SetProperty(optic, "HasOverrideRangeLimits", true);
                }
            }
            catch (Exception)
            {
            // The call is best-effort: a failure here leaves the previous state alone.
            }
        }

        /// <summary>
        /// Adds an AimablePlatform that survives its own Awake(): it starts out disabled so Awake is
        /// deferred, gets its HitZones list filled, and is only then switched on.
        /// </summary>
        private static AimablePlatform AddAimablePlatform(GameObject host, Transform aim_transform, GHPC.Utility.EnumHelpers.Axis3 axis, bool reverse_euler)
        {
            bool was_active = host.activeSelf;
            host.SetActive(false);

            AimablePlatform platform = host.AddComponent<AimablePlatform>();
            platform.Axis = axis;
            platform.Transform = aim_transform;
            platform.HitZones = new GHPC.Equipment.DestructibleComponent[0];
            // The stabiliser stays on (see CrowsAimSlave for how the model is kept on the aim).
            platform.StabilizerActive = true;
            platform.ProtectFromBadProjection = true;
            platform.ReverseLocalEuler = reverse_euler;
            Reflect.Set(platform, "_stabMode", StabilizationMode.Vector);

            if (!was_active)
            {
            }
            else
            {
                host.SetActive(true);
            }

            return platform;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Unity remaps the references that stay inside a cloned subtree but keeps the ones pointing
        /// outside it, so a sight cloned out of a donor tank stays wired to that tank. This drops every
        /// component and GameObject reference landing outside the CROWS model; asset references
        /// (materials, meshes, scriptables, codexes) are left alone, and the components re-derive their
        /// environment from their new parent when activated.
        /// </summary>
    }
}




