using System;
using System.Collections;
using System.Collections.Generic;
using GHPC;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using GHPC.Player;
using GHPC.Thermals;
using GHPC.Utility;
using GHPC.Vehicle;
using GHPC.Weapons;
using GHPC.World;
using ModUtil;
using UnityEngine;
using UnityEngine.UI;

namespace M1A1Abrams
{
    /// <summary>
    /// Automatic target tracking ("ATT") for the M153 CROWS II, driven through the game's own fire
    /// control system.
    ///
    /// The sight follows the locked target while a world-space intercept sets the weapon direction.
    /// The shot model matches GHPC's inherited launch velocity, drag and gravity; the fire control
    /// still drives the mount, with its table/rate solution retained as a fallback.
    ///
    /// One of these per CROWS station, on the fire control's object. The sight overlays
    /// (<see cref="CrowsSightUi"/>) exist twice, once per optic, and only read this class.
    /// </summary>
    public partial class CrowsAutoTrack : MonoBehaviour
    {
        // ------------------------------------------------------------------ lock / release

        private bool IsActiveWeapon()
        {
            try
            {
                PlayerInput input = PlayerInput.Instance;
                WeaponSystemInfo active = input == null ? null : input.CurrentPlayerWeapon;

                return active != null && active.FCS != null && active.FCS.GetInstanceID() == fcs.GetInstanceID();
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Says, once, where the bore actually points against where the fire control means it to.
        ///
        /// Everything about the impact point is arithmetic that cannot be seen from the source: the
        /// bore is the muzzle transform's own forward, the fire control aims the *platforms* and trusts
        /// the muzzle to be square to them, and the parallax correction only fixes the offset between
        /// the sight and the muzzle, never a rotation between them. A borrowed gun whose muzzle is not
        /// square to its cradle therefore shoots off the sight line by a fixed angle, and rounds that
        /// land consistently off to one side are exactly that - or exactly a range that has been
        /// clamped, which the same line reports.
        ///
        /// Measured once the aim has settled on a real target, so the servo is not sampled mid-slew,
        /// and said once per station: this is a property of the assembly, not of the frame.
        /// </summary>
        private void CheckBoreAlignment()
        {
            if (bore_reported || target == null || weapon == null || fcs == null || lead_suspended)
            {
                return;
            }

            bore_timer -= Time.deltaTime;

            if (bore_timer > 0f)
            {
                return;
            }

            bore_timer = BORE_CHECK_SECONDS;

            try
            {
                Transform reference = fcs.ReferenceTransform;
                Transform muzzle = weapon.MuzzleIdentity;

                if (reference == null || muzzle == null)
                {
                    return;
                }

                Vector3 intended = fcs.CompensatedWeaponVector(true);

                if (intended.sqrMagnitude < 1f)
                {
                    return;
                }

                bore_reported = true;

                float error = Vector3.Angle(muzzle.forward, intended);
                Vector3 parallax = reference.position - muzzle.position;

            }
            catch (Exception)
            {
                bore_reported = true;
            }
        }

        /// <summary>
        /// Keeps an eye on the one thing the log cannot otherwise show: whether the computer is solving
        /// for the round the gun is actually firing.
        ///
        /// The belt is chambered outside the game's own feed pipeline here (see CrowsAmmoSetup), and a
        /// round that never reaches the weapon leaves the computer solving for whatever placeholder its
        /// prefab carried - a nameless 1785 m/s round, in this station's case, which dials a fraction of
        /// the superelevation the belt needs and drops the rounds short by metres at range. The round is
        /// published when it is loaded; this says so if that ever stops working.
        /// </summary>
        private void CheckComputerAmmo()
        {
            ammo_timer -= Time.deltaTime;

            if (ammo_timer > 0f || ammo_reported)
            {
                return;
            }

            ammo_timer = AMMO_CHECK_SECONDS;

            try
            {
                AmmoType computer = fcs == null ? null : fcs.CurrentAmmoType;
                AmmoType breech = weapon == null || weapon.Feed == null ? null : weapon.Feed.AmmoTypeInBreech;

                if (computer == null || breech == null || computer == breech)
                {
                    return;
                }

                ammo_reported = true;

            }
            catch (Exception)
            {
                // nothing here is worth a second line in the log
            }
        }

        /// <summary>
        /// Says so if the round the computer is solving for carries <c>NoLeadCompensation</c>, which
        /// makes <c>GetLeadWorldOffset</c> return nothing at all - the lead is not small, it is absent,
        /// and no amount of measurement on this side puts it back. It is a flag on the ammunition, so it
        /// is the kind of thing that arrives silently with a borrowed or hand-made round, and the
        /// symptom - a crossing target that is never led, and a readout that never says LEAD - looks
        /// exactly like a fault in the tracker.
        /// </summary>
        private void CheckLeadCapableAmmo()
        {
            if (no_lead_ammo_reported || fcs == null)
            {
                return;
            }

            try
            {
                AmmoType ammo = fcs.CurrentAmmoType;

                if (ammo == null || !ammo.NoLeadCompensation)
                {
                    return;
                }

                no_lead_ammo_reported = true;

            }
            catch (Exception)
            {
                no_lead_ammo_reported = true;
            }
        }

        private bool no_lead_ammo_reported;


        /// <summary>
        /// Picks the unit closest to the centre of the sight, inside a three degree cone.
        ///
        /// The cone is built on the fire control's own aim vector rather than the camera's forward: the
        /// sight and the camera do not share an optical axis exactly, and it is the crosshair the player
        /// is pointing at the target, not the middle of the monitor. It runs on the one frame the key is
        /// pressed, so walking the scene's unit list costs nothing.
        /// </summary>
        private void TryLock()
        {
            Camera cam = CameraManager.MainCam;
            SceneUnitsManager manager = SceneUnitsManager.Instance;
            Transform reference = fcs.ReferenceTransform;

            if (cam == null || manager == null || reference == null)
            {
                return;
            }

            // The lock is what the player asks for while aiming at something. If the sight is still
            // swinging, they are not aiming at anything yet, and a lock taken then would grab whatever
            // happened to be crossing the crosshair.
            if (Sweeping())
            {
                return;
            }

            // No lock is taken at the widest magnification either; see UpdateDesignation.
            if (AtWidestZoom())
            {
                return;
            }

            Vector3 origin = reference.position;
            Vector3 forward = fcs.AimWorldVector;

            if (forward.sqrMagnitude < 0.25f)
            {
                forward = cam.transform.forward;
                origin = cam.transform.position;
            }

            Unit best = null;
            float best_angle = RECOGNISE_HALF_ANGLE_DEGREES;

            IReadOnlyList<Unit> units = manager.AllUnitsInScene;

            for (int i = 0; i < units.Count; i++)
            {
                Unit unit = units[i];

                if (!IsEngageable(unit))
                {
                    continue;
                }

                // Cheap reject first: measuring a box means walking every renderer under the unit. The
                // unit's own centre point is enough to tell whether it is anywhere near the reticle.
                Vector3 probe = unit.Center != null ? unit.Center.position : unit.transform.position;
                Vector3 to_probe = probe - origin;

                if (to_probe.sqrMagnitude > MAX_LOCK_RANGE * MAX_LOCK_RANGE)
                {
                    continue;
                }

                if (Vector3.Angle(forward, to_probe) > LOCK_CONE_DEGREES + 4f)
                {
                    continue;
                }

                Vector3 centre;
                Vector3 scratch_centre;
                Vector3 scratch_half;

                if (!Measure(unit, out centre, out scratch_centre, out scratch_half))
                {
                    continue;
                }

                // Nothing of it is switched on, so there is nothing there to lock onto; see Measure.
                if (!measure_found_parts)
                {
                    continue;
                }

                Vector3 to = centre - origin;

                if (to.sqrMagnitude > MAX_LOCK_RANGE * MAX_LOCK_RANGE)
                {
                    continue;
                }

                float angle = Vector3.Angle(forward, to);

                if (angle > best_angle)
                {
                    continue;
                }

                // It has to be in the sight picture: at 12X a three degree cone reaches well past the
                // edge of the screen, so a "lock" could be taken on a vehicle the player cannot see.
                if (!InSightPicture(centre, LOCK_SCREEN_MARGIN))
                {
                    continue;
                }

                // The same rays the lock is kept with, so that what can be locked is what can be
                // held: a target showing only its turret over a crest still locks, one behind the
                // crest does not.
                if (!CandidateVisible(unit, centre, scratch_half))
                {
                    continue;
                }

                best = unit;
                best_angle = angle;
            }

            if (best == null)
            {
                return;
            }

            // Measure() is a walk over the unit's renderers, and this is the one whose box is kept.
            Vector3 locked_centre;

            if (!Measure(best, out locked_centre, out box_centre_local, out box_half_local))
            {
                return;
            }

            target = best;
            state = TrackState.Locked;
            lost_timer = 0f;
            lead_shown = false;
            lead_suspended = true;   // nothing about the slew onto the target is a target rate
            lead_ready = false;      // and the world lead waits until the aim is nearly on it
            rate_reference_valid = false;
            aim_point_valid = false;
            smoothed_valid = false;
            velocity_valid = false;  // the last target's velocity is not this one's
            velocity_sample_valid = false;  // and neither is the transform the fallback last saw
            // MeasureLead reports the last intercept stored here, so a solution left from
            // the previous target would be reported as this one's, at this one's range.
            predictor_solution = new CrowsIntercept();
            ballistic_solution = new CrowsBallisticSolution();
            ballistic_solution_frame = -1;

            motion.Reset();
            imm.Reset();
            ukf.Reset();
            bias.Reset();
            latest_target_velocity = Vector3.zero;
            averaged_velocity = Vector3.zero;
            fused_relative_velocity = Vector3.zero;
            fused_relative_velocity_valid = false;
            los_rate_valid = false;
            // Start lock warmup: require fresh physical samples before activating firing solution
            lock_warmup_complete = false;

            // Whether this target's transform is already where the frame being drawn puts it; see
            // TrackedPoint. Read once here: it is a property of the rigidbody, not of the frame.
            target_interpolated = Interpolated(best);

            // Whatever angular rate the sight was carrying before the lock is not the target's.
            try
            {
                fcs.ZeroOutLead();
            }
            catch (Exception)
            {
                // a faulted computer simply carries no lead
            }

            ArmLead();
            ArmFiringSolution();
            HoldAimLock();
        }

        private void Release()
        {
            motion.Reset();
            imm.Reset();
            ukf.Reset();
            bias.Reset();
            latest_target_velocity = Vector3.zero;
            averaged_velocity = Vector3.zero;
            fused_relative_velocity = Vector3.zero;
            fused_relative_velocity_valid = false;
            los_rate_valid = false;
            platform_acceleration = Vector3.zero;
            platform_motion_sample_valid = false;
            target = null;
            state = TrackState.Idle;
            lost_timer = 0f;
            lead_shown = false;
            lead_suspended = false;
            lead_ready = false;
            rate_reference_valid = false;
            aim_point_valid = false;
            smoothed_valid = false;
            velocity_valid = false;
            velocity_sample_valid = false;
            sampled_velocity = Vector3.zero;
            target_interpolated = false;
            gate_state_valid = false;
            lock_warmup_complete = false;
            intended_lead_deg = 0f;
            applied_lead_deg = 0f;

            // Same reason as in the lock path above: nothing may read the previous target's solution
            // after the target it belonged to is gone.
            predictor_solution = new CrowsIntercept();
            ballistic_solution = new CrowsBallisticSolution();
            ballistic_solution_frame = -1;

            box_centre_local = Vector3.zero;
            box_half_local = Vector3.zero;
            manual_offset = Vector2.zero;

            try
            {
                if (fcs != null)
                {
                    DisarmLead();

                    // Mirrors FireControlSystem.SetPalmSwitches(false): refresh the mounts now that the
                    // lead is gone, so the borrowed gun's compensated aim is rebuilt at once.
                    fcs.AdjustAimByDegrees(0f, 0f, false);
                }
            }
            catch (Exception)
            {
                // releasing must never throw
            }

            RestoreAlignment();
            DisarmFiringSolution();
            ReleaseAimLock();
            HideMarkers();
        }

        /// <summary>
        /// Re-measures the boxes on a timer.
        ///
        /// The box is the model's own, and a model is not still: a turret swings, a dozer blade drops,
        /// a missile launcher erects, a vehicle that has been shot loses panels. Re-measuring is what
        /// follows all of that, and 0.33 s is the compromise between following it and walking every
        /// renderer of the vehicle often enough to matter.
        ///
        /// The framed - not yet locked - target is re-measured on the same timer, which it never used
        /// to be: it kept whatever box it was first picked with for as long as it stayed framed, so the
        /// passive box was the one that visibly failed to follow a rotating turret.
        /// </summary>
        private void RefreshTargetBox()
        {
            box_refresh_timer += Time.deltaTime;

            if (box_refresh_timer < BOX_REFRESH_INTERVAL)
            {
                return;
            }

            box_refresh_timer = 0f;

            if (target != null)
            {
                Vector3 refreshed_centre;
                Vector3 refreshed_local_centre;
                Vector3 refreshed_half;

                if (Measure(target, out refreshed_centre, out refreshed_local_centre, out refreshed_half))
                {
                    // Faster smoothing for dynamic changes (turret rotation, etc.)
                    // 0.25 provides responsive updates while still filtering noise
                    box_centre_local = Vector3.Lerp(box_centre_local, refreshed_local_centre, 0.25f);
                    box_half_local = Vector3.Lerp(box_half_local, refreshed_half, 0.25f);
                }
            }
            else if (designated != null && !designated.Destroyed)
            {
                Vector3 refreshed_centre;
                Vector3 refreshed_local_centre;
                Vector3 refreshed_half;

                if (Measure(designated, out refreshed_centre, out refreshed_local_centre, out refreshed_half))
                {
                    designated_centre_local = Vector3.Lerp(designated_centre_local, refreshed_local_centre, 0.25f);
                    designated_half_local = Vector3.Lerp(designated_half_local, refreshed_half, 0.25f);
                }
            }
        }

        // ------------------------------------------------------------------ geometry

        /// <summary>
        /// Whether a mesh is something the vehicle carries rather than the vehicle itself. Named rather
        /// than measured because there is nothing to measure: a decal sheet and a hull are both just
        /// triangles, and only the name says which is which.
        /// </summary>
        private static bool IsFurniture(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            string lower = name.ToLowerInvariant();

            // Original filters: interiors, decals, license plates, numbers
            if (lower.StartsWith("int_") || lower.StartsWith("numbers_") || 
                lower.StartsWith("lp_") || lower.StartsWith("decal") || 
                lower.Contains("decal"))
            {
                return true;
            }

            // Extended filters: antennas, flags, cables, stowage, camouflage
            // These are common decorative elements that extend beyond the vehicle silhouette
            if (lower.Contains("antenna") || lower.Contains("aerial") ||
                lower.Contains("flag") || lower.Contains("cable") ||
                lower.Contains("wire") || lower.Contains("rope") ||
                lower.Contains("stowage") || lower.Contains("tarp") ||
                lower.Contains("camouflage") || lower.Contains("camo") ||
                lower.Contains("netting") || lower.Contains("net_"))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// The target's box: a centre and a size in the vehicle's own frame, so the gate turns with the
        /// vehicle and the tracking point never jitters.
        ///
        /// There are three sources, taken in this order:
        ///
        /// 1. <b>The table</b> (<see cref="CrowsTrackingBoxes"/>). A known vehicle - and every vehicle
        ///    in the game is known, by name - is boxed with the dimensions somebody measured for it
        ///    once. This is the arrangement the Pact Increased Lethality mod uses for its own tracking
        ///    gates, and it is the right default: a number looked up by name cannot be inflated by a
        ///    stray mesh, a skinned mesh's bind pose, or a decal sheet, which is what a measurement
        ///    can be - and did, on a live T-72B3M that measured 65.9 x 329.1 x 29.9 m and lost its box
        ///    entirely. The model is still measured beside it and both numbers go in the log once per
        ///    family, because a table that has matched the wrong vehicle is otherwise invisible.
        /// 2. <b>The measurement</b>, for anything the table does not know: a vehicle from another mod,
        ///    a mission-specific variant with a name of its own. Each part is measured by its
        ///    <see cref="Renderer.localBounds"/> - its own box in its own space, before rotation -
        ///    rather than <c>renderer.bounds</c>, whose world AABB is already bigger than the part. The
        ///    eight corners of that box are carried into the unit's own frame and hulled per axis, and
        ///    then:
        ///    * A part is dropped when its bounds, measured in the unit's frame **in metres**, are
        ///      larger than a vehicle (<see cref="MAX_PART_SIZE"/>) or sit further from the unit than a
        ///      vehicle reaches (<see cref="MAX_PART_DISTANCE"/>). Both tests are on the *transformed*
        ///      bounds and not on <c>localBounds</c>: a skinned mesh carries its bind pose with it, and
        ///      a bind pose hundreds of metres from the rig's transform passes every test taken in the
        ///      renderer's own space.
        ///    * The parts are weighted by their own volume and each axis is swept for the narrowest
        ///      window holding <see cref="BOX_COVERAGE"/> of the total. The hull, the turret and the
        ///      running gear hold the box open; a barrel, an aerial, a stowage bundle or a decal sheet
        ///      is a fraction of a per cent of the volume and falls outside it. One rule fits a 3 m
        ///      scout car and a 12 m tank alike.
        /// 3. <b>The unit's own colliders</b>, when there is nothing measurable at all - a vehicle's
        ///    hull is a box collider in its own right.
        ///
        /// The box carries its own centre, and that is what the aim point follows. The game's
        /// representative centre (<see cref="Unit.Center"/>) sits at a fixed 1.5 m above the unit
        /// origin, which is the hull floor of most vehicles - a point the *vehicle* is not centred on.
        /// Aiming there puts the crosshair on the lower hull however tight the box is.
        /// </summary>
        private bool Measure(Unit unit, out Vector3 centre, out Vector3 local_centre, out Vector3 local_half)
        {
            Transform root = unit.transform;
            Vector3 aim = unit.Center != null ? unit.Center.position : root.position;

            local_centre = root.InverseTransformPoint(aim);

            // The fallback, set up front so the method has a box on every path: the game's own centre
            // point, at the minimum size. The measurement overwrites both below when it finds the hull.
            centre = aim;
            local_half = new Vector3(MIN_HALF_EXTENT, MIN_HALF_EXTENT, MIN_HALF_EXTENT);

            int count = GatherParts(unit, root);

            // "There is a vehicle here at all" is the live parts, not the measurable ones: a model whose
            // every part was rejected as not belonging to a vehicle is still a model that is switched on,
            // and whether it gets a box from the table or from its colliders it is a real thing to frame.
            measure_found_parts = parts_live > 0;

            // ---- the table's box, which wins when the vehicle is one the table knows
            //
            // This is the Pact Increased Lethality arrangement: the box for a known vehicle is a number
            // somebody measured once and wrote down, not something derived from its meshes every third of
            // a second. A table can be wrong, but it cannot be *blown out* - and the measurement that used
            // to be the only source is what produced a 329 m box on a live T-72B3M.
            //
            // It is asked *before* the sweep below, and that order is the point: the sweep is for the
            // vehicles the table does not know, and running it for one the table has just answered would
            // be a 384-point sample per part thrown away. It used to be run anyway, to feed a log line
            // comparing the two; that comparison was worth reading once per vehicle family, and is not
            // worth a sweep per re-measure.
            CrowsTrackingBoxes.Box table;

            if (CrowsTrackingBoxes.TryGet(unit, out table, out _))
            {
                CrowsTrackingBoxes.HalfExtents(table, root, aim, MIN_HALF_EXTENT, MAX_HALF_EXTENT,
                    MAX_CENTRE_SHIFT, out local_centre, out local_half);

                centre = root.TransformPoint(local_centre);

                return true;
            }

            // ---- the measured box, if the model can be measured at all
            bool measured = false;
            Vector3 measured_centre = Vector3.zero;
            Vector3 measured_half = Vector3.zero;
            float min_x = 0f;
            float max_x = 0f;
            float min_y = 0f;
            float max_y = 0f;
            float min_z = 0f;
            float max_z = 0f;

            if (count > 0
                && AxisWindow(0, count, out min_x, out max_x)
                & AxisWindow(1, count, out min_y, out max_y)
                & AxisWindow(2, count, out min_z, out max_z))
            {
                // Everything below is in metres: the box is built in the unit's local frame, which is
                // where it has to live, but a root that carries a scale would otherwise have its box
                // judged against thresholds in the wrong units.
                Vector3 scale = LocalScale(root);

                Vector3 local_span = new Vector3(max_x - min_x, max_y - min_y, max_z - min_z);
                Vector3 span = Vector3.Scale(local_span, scale) * (1f + BOX_MARGIN);

                if (span.x <= MAX_BOX_SIZE && span.y <= MAX_BOX_SIZE && span.z <= MAX_BOX_SIZE)
                {
                    measured_centre = ClampCentre(new Vector3((min_x + max_x) * 0.5f,
                        (min_y + max_y) * 0.5f, (min_z + max_z) * 0.5f), root, aim, scale);

                    measured_half = ClampHalf(local_span, scale);
                    measured = true;
                }
            }

            if (measured)
            {
                local_centre = measured_centre;
                local_half = measured_half;
                centre = root.TransformPoint(local_centre);

                return true;
            }

            // ---- nothing measurable: a vehicle's hull collider is still a box
            if (MeasureColliders(unit, root, ref local_centre, ref local_half))
            {
                centre = root.TransformPoint(local_centre);
                return true;
            }

            local_centre = root.InverseTransformPoint(aim);
            centre = aim;

            return true;
        }

        /// <summary>The unit's own scale, floored away from zero so nothing here can divide by it.</summary>
        private static Vector3 LocalScale(Transform root)
        {
            Vector3 scale = root.lossyScale;

            return new Vector3(Mathf.Max(Mathf.Abs(scale.x), 1e-4f),
                Mathf.Max(Mathf.Abs(scale.y), 1e-4f), Mathf.Max(Mathf.Abs(scale.z), 1e-4f));
        }

        /// <summary>
        /// A measured box centre, kept within <see cref="MAX_CENTRE_SHIFT"/> metres of the point the
        /// game calls the unit's centre; see that constant. Clamped in metres, then scaled back into
        /// the unit's own frame - scaling the local vector scales its metric length by the same factor.
        /// </summary>
        private static Vector3 ClampCentre(Vector3 box_centre, Transform root, Vector3 aim, Vector3 scale)
        {
            Vector3 reference = root.InverseTransformPoint(aim);
            Vector3 shift = box_centre - reference;
            float metres = Vector3.Scale(shift, scale).magnitude;

            return metres > MAX_CENTRE_SHIFT
                ? reference + shift * (MAX_CENTRE_SHIFT / metres)
                : box_centre;
        }

        /// <summary>
        /// A measured box's half extents with the padding on, clamped in metres - so a root that carries
        /// a scale gets the same limits as any other vehicle.
        /// </summary>
        private static Vector3 ClampHalf(Vector3 local_span, Vector3 scale)
        {
            return new Vector3(
                Mathf.Clamp(local_span.x * 0.5f * (1f + BOX_MARGIN),
                    MIN_HALF_EXTENT / scale.x, MAX_HALF_EXTENT / scale.x),
                Mathf.Clamp(local_span.y * 0.5f * (1f + BOX_MARGIN),
                    MIN_HALF_EXTENT / scale.y, MAX_HALF_EXTENT / scale.y),
                Mathf.Clamp(local_span.z * 0.5f * (1f + BOX_MARGIN),
                    MIN_HALF_EXTENT / scale.z, MAX_HALF_EXTENT / scale.z));
        }

        // ------------------------------------------------------------------ measurement buffers

        /// <summary>
        /// The parts of the unit's model, as boxes in the unit's own frame, one per renderer that
        /// counts: <c>part_bounds[0..part_count)</c>. Reused between measurements, so a re-measure
        /// every third of a second allocates nothing.
        /// </summary>
        private readonly List<Bounds> part_bounds = new List<Bounds>();

        /// <summary>Each part's volume in the unit's own frame; the sweep's weight.</summary>
        private readonly List<float> part_weights = new List<float>();

        /// <summary>The renderers the current unit was last gathered from, and the list that came out.</summary>
        private Unit parts_from;
        private Renderer[] parts_renderers;
        private Unit colliders_from;
        private Collider[] parts_colliders;

        /// <summary>Scratch for the volume sweep; see AxisWindow.</summary>
        private readonly float[] box_samples = new float[BOX_SAMPLES];

        /// <summary>
        /// Collects the unit's measurable parts into the reusable buffers and returns how many there
        /// are. The renderer list is cached per unit: <c>GetComponentsInChildren</c> allocates an array
        /// every call, and this runs for every candidate the sight is pointed at as well as on every
        /// refresh of a lock.
        /// </summary>
        private int GatherParts(Unit unit, Transform root)
        {
            part_bounds.Clear();
            part_weights.Clear();

            if (parts_from != unit || parts_renderers == null)
            {
                parts_from = unit;
                parts_renderers = root.GetComponentsInChildren<Renderer>(true);
            }

            Renderer[] renderers = parts_renderers;
            int rejected = 0;

            parts_live = 0;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];

                if (renderer == null || (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)))
                {
                    continue;
                }

                if (!renderer.enabled)
                {
                    continue;
                }

                // And the model has to be switched on. Renderer.enabled says nothing about that: a
                // renderer on a GameObject that is switched off, or under a parent that is, still
                // reports enabled, and the search above deliberately walks into switched-off children.
                //
                // This is what a vehicle the mission has not brought on yet looks like. The unit is
                // real, it is in the scene, and it has its renderers and its colliders - which is why
                // every other test for "is it there" passes and a box gets drawn on empty ground. Its
                // model is simply switched off until the mission spawns it.
                if (!renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                // Meshes that are not part of the vehicle's silhouette: a tactical-number decal is one
                // mesh carrying every number on the vehicle, an interior shell is the crew compartment,
                // and a lamp or an aerial is a thin stick. All three are either far bigger than the
                // vehicle or stick out of it, so any one of them ends up defining the box.
                if (IsFurniture(renderer.name))
                {
                    continue;
                }

                Bounds part = renderer.localBounds;

                if (part.size.sqrMagnitude < 0.05f * 0.05f)
                {
                    continue;
                }

                // Counted before the plausibility test below: this is the "the model is switched on and
                // it is a vehicle" count, which is what decides whether the unit can be framed at all.
                // See measure_found_parts.
                parts_live++;

                Transform owner = renderer.transform;

                // The part's own box in the unit's frame: the eight corners of its local bound carried
                // through the transform chain, hulled per axis. This is the part's real position and
                // real size, which is what the two plausibility tests below need - and what a test on
                // localBounds alone cannot see.
                Vector3 world_min = owner.TransformPoint(part.min) - root.position;
                Vector3 world_max = owner.TransformPoint(part.max) - root.position;

                Vector3 world_centre = (world_min + world_max) * 0.5f;
                Vector3 world_size = new Vector3(
                    Mathf.Abs(world_max.x - world_min.x),
                    Mathf.Abs(world_max.y - world_min.y),
                    Mathf.Abs(world_max.z - world_min.z));

                if (world_size.magnitude > MAX_PART_SIZE || world_centre.magnitude > MAX_PART_DISTANCE)
                {
                    rejected++;
                    continue;
                }

                Bounds local = LocalBounds(root, owner, part);

                part_bounds.Add(local);

                // The weight is the part's own volume as the sweep sees it - the box in the unit's own
                // frame, which is the same measurement the axis windows are built from - floored so a
                // flat decal or a thin rod still carries a vote: without a floor a sheet of zero
                // thickness would weigh nothing and could not hold the window open on the axis it spans.
                part_weights.Add(Mathf.Max(local.size.x * local.size.y * local.size.z, 1e-4f));
            }

            if (rejected > 0 && parts_rejected_reports < 3)
            {
                parts_rejected_reports++;

            }

            return part_bounds.Count;
        }

        /// <summary>How many of those lines have been written; enough to diagnose, not enough to spam.</summary>
        private int parts_rejected_reports;

        /// <summary>
        /// Renderers of the current unit that are switched on and carry a mesh, before the plausibility
        /// test: whether there is a vehicle here at all, as opposed to whether it can be measured.
        /// </summary>
        private int parts_live;

        /// <summary>A renderer's local bound, as a box in the unit's own frame.</summary>
        private static Bounds LocalBounds(Transform root, Transform owner, Bounds part)
        {
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            for (int c = 0; c < 8; c++)
            {
                Vector3 corner = new Vector3(
                    (c & 1) == 0 ? part.min.x : part.max.x,
                    (c & 2) == 0 ? part.min.y : part.max.y,
                    (c & 4) == 0 ? part.min.z : part.max.z);

                Vector3 local = root.InverseTransformPoint(owner.TransformPoint(corner));

                min = Vector3.Min(min, local);
                max = Vector3.Max(max, local);
            }

            Bounds bounds = new Bounds();
            bounds.SetMinMax(min, max);

            return bounds;
        }

        /// <summary>
        /// One axis of the box: the narrowest window that holds <see cref="BOX_COVERAGE"/> of the
        /// measured parts' volume.
        ///
        /// The axis is sampled, every part's own span is weighted into the samples it covers, and a
        /// moving window walks the samples looking for the smallest run that reaches the coverage.
        /// Volume is what makes this the right measure rather than corner count: a hull is a few large
        /// meshes and a vehicle carries dozens of bolts, mirrors, lamps and brackets, so counting parts
        /// would let the small change outvote the large - and it is the large that the box is for.
        ///
        /// The window is only ever *inside* the sampled span, so a single part cannot widen the box
        /// beyond the model, and the coverage guarantees that whatever is cut is a fraction of the
        /// vehicle's own bulk.
        /// </summary>
        private bool AxisWindow(int axis, int count, out float min, out float max)
        {
            min = 0f;
            max = 0f;

            if (count <= 0)
            {
                return false;
            }

            float lowest = float.MaxValue;
            float highest = float.MinValue;

            for (int i = 0; i < count; i++)
            {
                Bounds part = part_bounds[i];

                lowest = Mathf.Min(lowest, part.min[axis]);
                highest = Mathf.Max(highest, part.max[axis]);
            }

            float span = highest - lowest;

            if (span <= 1e-5f)
            {
                min = lowest;
                max = highest;

                return true;
            }

            // One part spanning the whole axis is not a distribution, and sampling it would only add
            // quantization to an answer that is already exact.
            float total = 0f;

            for (int i = 0; i < count; i++)
            {
                total += part_weights[i];
            }

            if (count == 1 || total <= 0f)
            {
                min = lowest;
                max = highest;

                return true;
            }

            Array.Clear(box_samples, 0, BOX_SAMPLES);

            float per_sample = span / (BOX_SAMPLES - 1);
            float inverse = (BOX_SAMPLES - 1) / span;

            for (int i = 0; i < count; i++)
            {
                Bounds part = part_bounds[i];
                float weight = part_weights[i];

                int first = Mathf.Clamp(Mathf.FloorToInt((part.min[axis] - lowest) * inverse), 0, BOX_SAMPLES - 1);
                int last = Mathf.Clamp(Mathf.CeilToInt((part.max[axis] - lowest) * inverse), 0, BOX_SAMPLES - 1);

                // The part's volume is spread *across* the samples it covers rather than added whole to
                // each of them. That distinction is the algorithm: with the whole volume in every
                // sample, a part's contribution to the sweep would grow with its length, and a long thin
                // barrel would weigh as much as the turret. Spread, the sum over a window is the volume
                // of the parts inside it, which is what the coverage is a fraction of.
                float density = weight / (last - first + 1);

                for (int s = first; s <= last; s++)
                {
                    box_samples[s] += density;
                }
            }

            float wanted = total * BOX_COVERAGE;
            float running = 0f;
            int head = 0;
            int best_head = 0;
            int best_tail = BOX_SAMPLES - 1;
            int best_width = BOX_SAMPLES;

            for (int tail = 0; tail < BOX_SAMPLES; tail++)
            {
                running += box_samples[tail];

                // Trim the head while the window still holds the coverage: this is what makes the
                // window the *narrowest* one rather than merely the first one that fits.
                while (head <= tail && running - box_samples[head] >= wanted)
                {
                    running -= box_samples[head];
                    head++;
                }

                if (running >= wanted && tail - head + 1 < best_width)
                {
                    best_width = tail - head + 1;
                    best_head = head;
                    best_tail = tail;
                }
            }

            if (best_width >= BOX_SAMPLES)
            {
                // No window narrower than the whole axis reaches the coverage - which is what a model
                // that really does fill its own extent looks like. Take the extent.
                min = lowest;
                max = highest;

                return true;
            }

            min = lowest + best_head * per_sample;
            max = lowest + best_tail * per_sample;

            // A window that came out inside a single sample's worth of the axis is a degenerate box;
            // the whole extent is the honest answer there, not a point.
            if (max - min < per_sample * 2f)
            {
                min = lowest;
                max = highest;
            }

            return true;
        }

        /// <summary>
        /// The unit's own colliders, as a box in its local frame: the fallback when the model cannot be
        /// measured. A vehicle's hull is a box collider and a wheeled one is a row of them, so this is
        /// still the vehicle's real size - where the old fallback was the game's centre point inside a
        /// 1.2 m cube, which is a bracket drawn around the middle of the hull and nothing else.
        /// </summary>
        private bool MeasureColliders(Unit unit, Transform root, ref Vector3 local_centre, ref Vector3 local_half)
        {
            if (colliders_from != unit || parts_colliders == null)
            {
                colliders_from = unit;
                parts_colliders = unit.GetComponentsInChildren<Collider>(true);
            }

            Collider[] colliders = parts_colliders;

            if (colliders == null || colliders.Length == 0)
            {
                return false;
            }

            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            int used = 0;

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];

                if (collider == null || !collider.enabled || collider.isTrigger)
                {
                    continue;
                }

                Bounds bounds = collider.bounds;

                if (bounds.size.sqrMagnitude <= 0f)
                {
                    continue;
                }

                // The same plausibility test the parts get: a collider hundreds of metres across is a
                // trigger volume or a misplaced ray proxy, not this vehicle's hull.
                if (bounds.size.magnitude > MAX_PART_SIZE
                    || (bounds.center - root.position).magnitude > MAX_PART_DISTANCE)
                {
                    continue;
                }

                // A box collider knows its own box in its own frame, so it is carried across exactly.
                // Anything else - a capsule, a wheel, a mesh - is taken as its world bounds, which is
                // generous by the collider's rotation and is the right way to be wrong on a fallback.
                BoxCollider box = collider as BoxCollider;
                Vector3 centre = box != null ? box.center : collider.transform.InverseTransformPoint(bounds.center);
                Vector3 extent = box != null
                    ? box.size * 0.5f
                    : collider.transform.InverseTransformVector(bounds.extents);

                Transform owner = collider.transform;

                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = centre + new Vector3(
                        (c & 1) == 0 ? -extent.x : extent.x,
                        (c & 2) == 0 ? -extent.y : extent.y,
                        (c & 4) == 0 ? -extent.z : extent.z);

                    Vector3 local = root.InverseTransformPoint(owner.TransformPoint(corner));

                    min = Vector3.Min(min, local);
                    max = Vector3.Max(max, local);
                }

                used++;
            }

            if (used == 0)
            {
                return false;
            }

            local_centre = (min + max) * 0.5f;
            local_half = new Vector3(
                Mathf.Clamp((max.x - min.x) * 0.5f, MIN_HALF_EXTENT, MAX_HALF_EXTENT),
                Mathf.Clamp((max.y - min.y) * 0.5f, MIN_HALF_EXTENT, MAX_HALF_EXTENT),
                Mathf.Clamp((max.z - min.z) * 0.5f, MIN_HALF_EXTENT, MAX_HALF_EXTENT));

            return true;
        }

        /// <summary>
        /// Whether the last Measure() measured a box off the model itself. A unit whose renderers are
        /// all off, switched out of the hierarchy, or all rejected as not belonging to a vehicle is not
        /// something to frame, however real its transform and its colliders are.
        /// </summary>
        private bool measure_found_parts;

        /// <summary>Fills <see cref="corners"/> with a unit's box corners, in world space.</summary>
        private void BoxCorners(Unit unit, Vector3 local_centre, Vector3 local_half)
        {
            Transform root = unit.transform;

            for (int c = 0; c < 8; c++)
            {
                Vector3 local = new Vector3(
                    local_centre.x + ((c & 1) == 0 ? -local_half.x : local_half.x),
                    local_centre.y + ((c & 2) == 0 ? -local_half.y : local_half.y),
                    local_centre.z + ((c & 4) == 0 ? -local_half.z : local_half.z));

                corners[c] = root.TransformPoint(local);
            }
        }

        /// <summary>
        /// Whether the target can be seen from the fire control.
        ///
        /// Three rays are cast - the box centre, and the top and bottom of the box - and the target
        /// counts as visible if any of them gets through. A single ray to the centre is far too brittle:
        /// the centre of a hull sits low, so a hedge line, a fold in the ground or the target's own
        /// geometry clips it for a frame or two, and every one of those frames used to freeze the aim
        /// and make the whole picture stutter.
        /// </summary>
        private bool IsVisible(Vector3 aim_point)
        {
            Transform reference = fcs.ReferenceTransform;

            if (reference == null)
            {
                return true;
            }

            Vector3 up = target.transform.up;
            Vector3 top = aim_point + up * box_half_local.y * 0.8f;
            Vector3 bottom = aim_point - up * box_half_local.y * 0.8f;

            int clear = 0;

            if (!IsObstructedFrom(reference.position, aim_point, target))
            {
                clear++;
            }

            if (!IsObstructedFrom(reference.position, top, target))
            {
                clear++;
            }

            if (!IsObstructedFrom(reference.position, bottom, target))
            {
                clear++;
            }

            // Two of the three, not one. A single clear ray let a vehicle behind a crest keep its gate
            // for as long as a sliver of it showed, which is what the terrain-blocked boxes were.
            return clear >= 2;
        }

        /// <summary>
        /// Whether a unit the tracker is considering is actually in the open rather than behind cover.
        /// The box centre and the top and bottom of the box, accepted if any one of them gets through -
        /// the same three rays IsVisible uses once the lock is held, so a target that cannot be locked
        /// is a target that could not have been kept either.
        /// </summary>
        private bool CandidateVisible(Unit unit, Vector3 centre, Vector3 half)
        {
            Transform reference = fcs.ReferenceTransform;

            if (reference == null)
            {
                return true;
            }

            Vector3 up = unit.transform.up;
            float reach = half.y * 0.8f;

            int clear = 0;

            if (!IsObstructedFrom(reference.position, centre, unit))
            {
                clear++;
            }

            if (!IsObstructedFrom(reference.position, centre + up * reach, unit))
            {
                clear++;
            }

            if (!IsObstructedFrom(reference.position, centre - up * reach, unit))
            {
                clear++;
            }

            // The same two-of-three the held lock uses, so what can be acquired is what can be kept.
            return clear >= 2;
        }

        /// <summary>
        /// Whether anything between two points blocks the view of a unit, ignoring the player's own
        /// vehicle and the target. The CROWS's colliders live at the scene root (they are dragged along
        /// by LateFollow), so the game's own follow-aware unit lookup is what identifies them.
        /// </summary>
        private bool IsObstructedFrom(Vector3 from, Vector3 to, Unit ignore)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;

            if (distance < 2f)
            {
                return false;
            }

            // What hides a target, which is not the same question as what stops a laser: terrain is
            // exactly what a rangefinder is pointed at, and a sight that inherits the rangefinder's
            // mask can lock - and hold - a vehicle on the far side of a hill. The game's own
            // visibility-test mask is the starting point, and the terrain and main-body layers are
            // forced in on top of it because those are the two a lock must not see through; they are
            // the pair the Pact Increased Lethality mod casts against for the same purpose.
            int mask = ConstantsAndInfoManager.Instance == null
                ? (int)Physics.DefaultRaycastLayers
                : (int)ConstantsAndInfoManager.Instance.VisibilityTestLayerMask;

            int terrain_layer = CodeUtils.LAYER_INDEX_TERRAIN;
            int body_layer = CodeUtils.LAYER_INDEX_MAIN_BODY;

            if (terrain_layer >= 0)
            {
                mask |= 1 << terrain_layer;
            }

            if (body_layer >= 0)
            {
                mask |= 1 << body_layer;
            }

            int count;

            try
            {
                count = Physics.RaycastNonAlloc(from, delta / distance, hits, distance - 1f, mask, QueryTriggerInteraction.Ignore);
            }
            catch (Exception)
            {
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                Collider collider = hits[i].collider;

                if (collider == null)
                {
                    continue;
                }

                if (collider.CompareTag("Smoke"))
                {
                    continue;   // the fire control's own laser sees straight through smoke
                }

                Unit owner = CodeUtils.GetParentUnitFollowAware(collider.transform);

                if (owner != null)
                {
                    if (owner == ignore || owner == ours)
                    {
                        continue;
                    }

                    return true;
                }

                if (ours != null && collider.transform.IsChildOf(ours.transform))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

    }
}
