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
    /// GHPC already does the hard part: the sight and the weapon axis are decoupled, the ballistic
    /// computer holds the flight-time table, and the lead is derived from the angular rate the aim is
    /// rotated at. So this module only pins the aim onto a locked target and feeds that rotation in
    /// through <c>AdjustAimByDegrees</c> - the only entry point that writes <c>_lastAngleAdjust</c>,
    /// and therefore the only one that produces any lead at all (<c>SetAimWorldPosition</c> would
    /// leave the lead at zero). Flight time, superelevation, the lead split and the traverse-rate
    /// buffer are all the game's.
    ///
    /// One of these per CROWS station, on the fire control's object. The sight overlays
    /// (<see cref="CrowsSightUi"/>) exist twice, once per optic, and only read this class.
    /// </summary>
    public partial class CrowsAutoTrack : MonoBehaviour
    {
        // ------------------------------------------------------------------ tuning

        /// <summary>Half-angle of the capture cone around the centre of the screen.</summary>
        private const float LOCK_CONE_DEGREES = 3f;

        /// <summary>
        /// Half of the recognition window, in degrees: the cone around the crosshair that a target box
        /// can be taken from. A fixed angle rather than a fraction of the field of view, which is what
        /// it used to be - the same cone of the world at 5X, 10X and 15X alike, so it covers a wide
        /// part of the picture when the sight is wound up and a narrow one when it is not.
        /// </summary>
        private const float RECOGNISE_HALF_ANGLE_DEGREES = 0.4f;

        private const float MAX_LOCK_RANGE = 4000f;

        /// <summary>How long the sight may stay obstructed before the lock is dropped.</summary>
        private const float LOST_GRACE_SECONDS = 1.5f;

        /// <summary>
        /// How long the line of sight has to stay blocked before the aim stops being driven. A single
        /// blocked frame is almost always noise - the ray clipping the target's own hull, a hedge, the
        /// ground under a box centre that sits low - and freezing the aim for it makes the picture
        /// stutter, because the target keeps moving while the sight does not.
        /// </summary>
        private const float BLOCK_DEBOUNCE_SECONDS = 0.2f;

        /// <summary>
        /// Angular error below which the aim counts as on target. While it is larger the aim is still
        /// slewed, but the rotation is kept out of the lead buffer: a 40 deg/s slew onto a target three
        /// degrees off would otherwise be read as a 40 deg/s target rate and throw the gun tens of
        /// metres ahead of the target.
        /// </summary>
        private const float SETTLE_CONE_DEGREES = 0.75f;

        /// <summary>
        /// The error that has to come back before the lead is suspended again. Without the gap the
        /// settle test flickers around its threshold and the lead - which moves the gun by the target's
        /// whole flight-time displacement - switches on and off with it.
        /// </summary>
        private const float LEAD_RESET_CONE_DEGREES = 6f;

        /// <summary>
        /// The cone the aim has to be inside before the *world* lead is fed, wider than the one the
        /// traverse rate waits for. The rate the game's own lead is built from is the rate the aim was
        /// rotated at, so a slew must not be allowed into it - but the world lead is solved from the
        /// filter's state and never reads that rate, and holding it back until the slew finishes costs
        /// the whole slew before the gun is laid on the point it is meant to be laid on. Feeding it from
        /// three degrees out lets the mount go straight to the led point rather than to the target and
        /// then out again.
        /// </summary>
        private const float LEAD_ENTRY_CONE_DEGREES = 3f;

        /// <summary>Fallback traverse rate if the mount cannot be asked (deg/s).</summary>
        private const float FALLBACK_RATE = 40f;

        private const float MIN_HALF_EXTENT = 0.6f;

        /// <summary>
        /// The largest half-extent a target box may have. Sized for a vehicle, not for "anything that
        /// renders": an oversized box puts corners behind the camera, which makes the gate flicker.
        /// </summary>
        private const float MAX_HALF_EXTENT = 12f;

        /// <summary>
        /// The largest a single part may be, in metres, and still count as part of the vehicle.
        ///
        /// This is a plausibility test and not a size ranking: every vehicle in the game fits inside a
        /// 15 m cube, so a *part* larger than this is not a part at all. It is the safety net under the
        /// measurement, and it is why the test is done on the part's bounds *in the unit's own frame*
        /// rather than on its <c>localBounds</c>: a mesh one metre across under a transform scaled by
        /// three hundred passes a localBounds test and then spans three hundred metres of the box.
        /// </summary>
        private const float MAX_PART_SIZE = 25f;

        /// <summary>
        /// How far from the unit's origin, in metres, a part's own bounds may sit and still belong to
        /// the vehicle. The same safety net as <see cref="MAX_PART_SIZE"/> for the other failure mode:
        /// a skinned mesh carries its bind pose wherever the rig was built, and a bind pose a few
        /// hundred metres up is exactly what produced a 329 m box on a live target.
        /// </summary>
        private const float MAX_PART_DISTANCE = 20f;

        /// <summary>
        /// The fraction of the vehicle's measured volume the box has to contain.
        ///
        /// This is what sizes the box now, and it is what makes the box follow the model rather than a
        /// constant: every part is weighted by its own volume, the axis is walked from end to end, and
        /// the narrowest window holding this much of the total is the extent taken. The hull, the
        /// turret and the running gear carry nearly all of it and hold the box open; a gun barrel, an
        /// aerial, a stowage bundle, a camouflage net or a decal sheet carries a fraction of a per cent
        /// and is simply outside the window. Nothing is tuned per vehicle size - a 3 m BRDM and a 12 m
        /// tank are sized by the same rule, from their own geometry.
        ///
        /// Half a per cent is the whole margin there is: it is twenty times the volume of the largest
        /// thing that has any business hanging off a vehicle, and what it costs is a few centimetres at
        /// the very ends of the axes, where the outermost part is thin - less than the padding below
        /// puts back. What it buys is that a mesh reaching past the hull cannot set the box, which is
        /// what the old fixed percentage trim was for and did not reliably do.
        /// </summary>
        private const float BOX_COVERAGE = 0.995f;

        /// <summary>
        /// Resolution of the volume sweep, in samples across the vehicle's own extent on one axis. At
        /// 384 the quantization is a couple of centimetres on a tank, well under the box margin.
        /// </summary>
        private const int BOX_SAMPLES = 384;

        /// <summary>A box bigger than this on any axis, in metres, is not a vehicle: fall back to the colliders.</summary>
        private const float MAX_BOX_SIZE = 30f;

        /// <summary>Padding added to the box so the gate hugs the hull instead of clipping into it.</summary>
        private const float BOX_MARGIN = 0.02f;

        /// <summary>
        /// How far the measured box centre may sit from the point the game calls the unit's centre, in
        /// metres. The measurement has the last word - it is the only thing that knows where the hull
        /// actually is - but a bad measurement must not be able to throw the aim off the vehicle
        /// entirely, and the game's own centre point is inside the hull for every vehicle in the game.
        /// </summary>
        private const float MAX_CENTRE_SHIFT = 3f;

        /// <summary>
        /// Marker smoothing rate (1/s). The gate is a readout, not the picture, and a readout that
        /// twitches by a pixel or two a frame is unbearable at 12X. 45/s is a 22 ms time constant:
        /// enough to take the twitch out, far too little to be felt as lag.
        /// </summary>
        private const float MARKER_SMOOTHING = 45f;

        /// <summary>
        /// The rate the gate's *position* is filtered at when the target's transform is already where the
        /// frame being drawn puts it. The box is drawn on the target, so every millisecond of filter is a
        /// lag the player watches the box trail the target by - at 12X, 22 ms of it is half a vehicle on a
        /// crossing target. The ordinary rate above is kept for the case it was written for: a transform
        /// that steps with the physics, where the filter is what hides the stepping. See TrackedPoint.
        /// </summary>
        private const float GATE_POSITION_SMOOTHING_INTERPOLATED = 240f;

        private const int SORTING_ORDER = 20;   // above CrowsSightUi's 10
        private const int RAY_HITS = 24;

        private const float GATE_MIN_PIXELS = 12f;
        private const float GATE_LINE_PIXELS = 2f;

        /// <summary>Bracket arm length, as a fraction of the short side of the gate.</summary>
        private const float GATE_BRACKET_FRACTION = 0.3f;
        private const float GATE_BRACKET_MIN = 7f;
        private const float GATE_BRACKET_MAX = 34f;

        /// <summary>The gate is never allowed to swallow the whole sight picture.</summary>
        private const float GATE_MAX_SCREEN_FRACTION = 0.62f;


        /// <summary>The shortest range the computer is given a solution for.</summary>
        private const float MIN_FIRING_RANGE = 60f;

        /// <summary>
        /// How fast a full sweep of the player's aim input walks the manual offset, as metres at the
        /// target per second per degree of field of view. The scaling with the zoom is deliberate: the
        /// offset is a distance at the target, so a narrow field of view walks it more slowly, and the
        /// adjustment is fine where the sight is magnified. See WalkManualOffset.
        /// </summary>
        private const float MANUAL_SLEW_METRES_PER_DEGREE_SECOND = 2f;

        /// <summary>
        /// How far the crosshair may be walked off the tracked point, in degrees at the target. Enough
        /// to pick a spot on a vehicle or to hold off for a mover, not enough to lose the target.
        /// </summary>
        private const float MANUAL_OFFSET_LIMIT_DEGREES = 1f;

        /// <summary>
        /// The lead angles at which the readout says a lead is dialled in, in degrees at the target.
        /// Two values rather than one, because a slow creep sits right on any single threshold and a
        /// line that switches between "TRACK" and "TRACK  LEAD" there is a line that flickers.
        ///
        /// A hundredth of a degree is the scale to think in: at 1,500 m it is a quarter of a metre of
        /// lead, and a vehicle creeping at the speed the station counts as stopped - 0.3 m/s across the
        /// line of sight - works out at about 0.018 degrees over a flight and a half. So the high
        /// threshold shows that, and the low one lets go only when the lead has really gone.
        /// </summary>
        private const float LEAD_SHOW_DEGREES = 0.015f;
        private const float LEAD_HIDE_DEGREES = 0.006f;

        /// <summary>
        /// The lead angle at which the one-shot "is the computer applying it" check has something worth
        /// saying; see MeasureLead. Below this the lead is a few centimetres and not worth a line.
        /// </summary>
        private const float LEAD_CHECK_DEGREES = 0.05f;

        /// <summary>The lock / release key. The middle button was chosen because GHPC leaves it free.</summary>
        private const KeyCode ATT_KEY = KeyCode.Mouse2;

        /// <summary>
        /// Smoothing for the followed aim point (1/s). Light on purpose: it only has to cover the gap
        /// between the physics step the target moves on and the frame being drawn.
        /// </summary>
        private const float AIM_SMOOTHING = 25f;

        /// <summary>How often the passive box is re-picked, and how near the crosshair it has to be.</summary>
        private const float DESIGNATE_INTERVAL = 0.25f;

        /// <summary>
        /// How close to the crosshair the sight has to be before a target is framed at all, as a
        /// fraction of the screen's shorter side. Distance on screen rather than an angle, so it means
        /// the same thing at 1X and at 12X: the box is there to say "this is what you are pointing at",
        /// and a target out at the edge of the picture is not what the player is pointing at.
        /// </summary>
        private const float DESIGNATE_NEAR_FRACTION = 0.20f;

        /// <summary>The same, once the box has been picked: wider, so a box on the fringe cannot flicker.</summary>
        private const float DESIGNATE_KEEP_FRACTION = 0.32f;

        /// <summary>
        /// How far outside the sight picture a lock may be taken. Tighter than the passive box's
        /// margin: the player is aiming at what they are locking, so it should be in the picture.
        /// </summary>
        private const float LOCK_SCREEN_MARGIN = 24f;

        /// <summary>
        /// The fastest the sight can be swinging and still take a lock, in degrees per second. A
        /// player traversing across a treeline sweeps at tens of degrees a second and would otherwise
        /// leave a trail of locks and boxes behind the crosshair.
        /// </summary>
        private const float LOCK_MAX_SWEEP = 6f;

        /// <summary>
        /// Smoothing for the measured traverse rate (1/s). Shorter than the fire control's own one
        /// second buffer, so the lead is steadier than a raw per-frame figure and still quick to follow
        /// a target that changes its crossing rate.
        /// </summary>
        private const float LEAD_RATE_SMOOTHING = 8f;

        /// <summary>
        /// How fast the lead-carrying rate may itself change, in degrees per second per second. A
        /// ceiling, not a filter: whatever the measurement does, the rate reaches the aim as a push and
        /// never as a jump. Four is still an order of magnitude more than a tank can manage - two
        /// metres per second squared across the line of sight at 500 m is a quarter of one - so it
        /// never touches real motion, and it bounds a single bad frame.
        /// </summary>
        private const float LEAD_RATE_SLEW = 4f;

        /// <summary>
        /// The lag between the solution being asked for and the round leaving, in seconds, split into the
        /// two parts that pull in different directions. The aim latency is the station still settling onto
        /// the lead it was given; the fire latency is the trigger and the breech. Both are flown as extra
        /// time of flight - during them the target and the platform keep moving - and keeping them apart
        /// is what lets one be judged against the other when a hit pattern says which one is out.
        /// </summary>
        private const double AIM_LATENCY_SECONDS = 0.05;
        private const double FIRE_LATENCY_SECONDS = 0.03;
        private const double LAUNCH_DELAY_SECONDS = AIM_LATENCY_SECONDS + FIRE_LATENCY_SECONDS;

        /// <summary>
        /// Smoothing for the target's own velocity (1/s) before it becomes a crossing rate; see
        /// ReportTargetRate. Half a second averages out the speed a tracked vehicle only appears to
        /// have as it works over rough ground, and keeps the acceleration that matters.
        /// </summary>
        private const float LEAD_VELOCITY_SMOOTHING = 2f;

        /// <summary>
        /// The smallest measured rate worth calling motion, in degrees per second. Only used when the
        /// target reports no velocity of its own, where all that is left to measure is the aim's noise
        /// on a parked vehicle.
        /// </summary>
        private const float STATIONARY_RATE_DEGREES = 1.5f;

        /// <summary>
        /// The smallest speed worth calling motion, in metres per second, for the fallback that measures
        /// a target whose solver does not publish a velocity. A tenth of a metre a second over a second
        /// and a half of flight is fifteen centimetres of lead, which is inside the error of the box
        /// measurement the rate is derived from.
        /// </summary>
        private const float TARGET_VELOCITY_FLOOR = 0.1f;

        /// <summary>
        /// The largest speed that fallback will believe, in metres per second. It exists for the units
        /// the solver gives no velocity for - infantry on foot, a towed gun - and a sample larger than
        /// this is a teleport or a re-parented transform rather than motion.
        /// </summary>
        private const float TARGET_VELOCITY_CEILING = 60f;

        /// <summary>
        /// Lock warmup duration in seconds. After acquiring a new target, the tracker waits this long
        /// before activating the firing solution, allowing platform and target velocity measurements
        /// to stabilize. During warmup, the sight tracks the target normally but impact prediction is delayed.
        /// </summary>
        private const float LOCK_WARMUP_SECONDS = 0.02f;

        // ------------------------------------------------------------------ wiring

        public FireControlSystem fcs;

        private Vehicle ours;
        private WeaponSystem weapon;
        private readonly List<UsableOptic> optics = new List<UsableOptic>();

        /// <summary>How often, in seconds, the computer's ammunition is compared with the breech's.</summary>
        private const float AMMO_CHECK_SECONDS = 2f;

        /// <summary>How often the bore is measured against the fire control's weapon vector before it is reported.</summary>
        private const float BORE_CHECK_SECONDS = 1f;

        private float ammo_timer;
        private bool ammo_reported;

        private float bore_timer;
        private bool bore_reported;

        // ------------------------------------------------------------------ state

        private enum TrackState
        {
            Idle,
            Locked,
            Lost
        }

        private TrackState state = TrackState.Idle;

        private Unit target;
        private Vector3 box_centre_local;
        private Vector3 box_half_local;
        private float box_refresh_timer;
        private const float BOX_REFRESH_INTERVAL = 0.33f; // Faster refresh for dynamic targets (was 1.0f)

        /// <summary>
        /// Whether the physics engine is interpolating the target's rigidbody - which decides whether the
        /// target's transform has to be carried forward to the frame being drawn, or is already there.
        /// Measured when the lock is taken; see TrackedPoint.
        /// </summary>
        private bool target_interpolated;

        /// <summary>
        /// The unit the sight is merely pointed at - the box that is on screen whenever the station is
        /// being used, locked or not. Locking adds the impact marker and the leader line to it; it does
        /// not create it.
        /// </summary>
        private Unit designated;
        private Vector3 designated_centre_local;
        private Vector3 designated_half_local;
        private float designate_timer;
        private float lost_timer;
        private bool lead_suspended;

        /// <summary>The wider gate the world lead uses; see LEAD_ENTRY_CONE_DEGREES.</summary>
        private bool lead_ready;

        /// <summary>
        /// Whether the world lead is the one supplying this fire control's offset. While it is, the
        /// traverse rate this module writes is discarded on the other side anyway - GetLeadWorldOffset is
        /// replaced wholesale - so writing it only leaves a second and staler author of one quantity
        /// inside the fire control for anything else that reads it.
        /// </summary>
        private bool world_lead_active;
        private bool armed;

        /// <summary>The aim as this module last set it - the reference the target's rate is measured against.</summary>
        private Vector3 rate_reference_aim;
        private bool rate_reference_valid;

        /// <summary>
        /// The target's own velocity in the ground frame, averaged; see LEAD_VELOCITY_SMOOTHING.
        ///
        /// The ground frame and not the player's frame, and that distinction is the whole of the lead's
        /// accuracy: the round is spawned with the platform's velocity added to the muzzle velocity
        /// (WeaponSystem adds its own parent rigidbody's), so the lead has to be the target's motion
        /// *relative to the platform* or the rounds fall short by the platform's whole displacement over
        /// the flight - a metre and a half at walking pace and fifteen at speed. Both terms are carried
        /// in one rate by this module, with the fire control's own inertial term switched off at arming;
        /// see ReportTargetRate and ArmLead.
        /// </summary>
        private readonly CrowsMotionSampler motion = new CrowsMotionSampler();

        /// <summary>
        /// The five hypotheses the carried displacement is blended from, fed once per physics tick by
        /// SampleMotionMixture; see CrowsImm. It answers with the single-model solve until it has two
        /// lags of motion to weigh, so a lock that is still acquiring is unchanged by its existence.
        /// </summary>
        private readonly CrowsImm imm = new CrowsImm();

        /// <summary>
        /// The unscented filter that estimates the state the hypotheses are extrapolated from; see
        /// CrowsUkf. It is fed the same tick, and where it is healthy its velocity, acceleration and
        /// confidence are what the lead uses - the sampler below stays as the fallback for the first
        /// ticks of a lock, for a station with no chassis to measure from, and for any tick the filter
        /// refuses. Nothing else about the lead changes when it is not.
        /// </summary>
        private readonly CrowsUkf ukf = new CrowsUkf();

        /// <summary>
        /// What the last few rounds landed away from the target, carried as a hold-off; see
        /// CrowsBallisticBias and CrowsShotFeedback. Zero until this station has fired and watched
        /// where a round went.
        /// </summary>
        private readonly CrowsBallisticBias bias = new CrowsBallisticBias();

        /// <summary>The ammunition the correction was learned with; it is dropped when that changes.</summary>
        private AmmoType bias_ammo;

        /// <summary>The round the ballistic replica is configured for; see ConfigureBallistics.</summary>
        private AmmoType ballistics_ammo;

        private CrowsIntercept predictor_solution;

        /// <summary>The borrowed weapon's rigidbody, read once in Attach; see the comment there.</summary>
        private Rigidbody weapon_rb;

        private Vector3 averaged_velocity;
        private Vector3 latest_target_velocity;
        private Vector3 fused_relative_velocity;
        private bool fused_relative_velocity_valid;
        private Vector3 last_los_unit;
        private Vector3 los_angular_velocity;
        private bool los_rate_valid;
        private float los_sample_time;

        private static LeadVector MathVector(Vector3 v) { return new LeadVector(v.x, v.y, v.z); }
        private static Vector3 UnityVector(LeadVector v) { return new Vector3((float)v.X, (float)v.Y, (float)v.Z); }

        private void FixedUpdate()
        {
            UpdatePlatformMotion();
            // Render-time LOS fusion only describes the previous physics sample.
            fused_relative_velocity_valid = false;
            // Destroyed is its own field on Unit and is not implied by the transform: a wreck keeps a
            // valid transform for the rest of the mission, and its body keeps drifting and settling.
            // Without this test a wreck's velocity is sampled as a live target, and the filter is fed a
            // track that is physical but not the target. Every other place in this class that consumes
            // a target checks it (CheckBoreAlignment, UpdateMarkers); this one did not.
            if (!armed || target == null || target.transform == null || target.Destroyed) return;
            // No derivatives or target filtering run on the render clock. FixedUpdate's
            // state can be one physics step old, but has a consistent physical timestamp.
            //
            // Read once and handed to both estimators: TargetWorldVelocity falls back to differencing
            // the transform for a target whose solver reports nothing, and reading it twice in one tick
            // would difference that transform against itself.
            Vector3 target_velocity = TargetWorldVelocity();
            // A solver's stopped reading is authoritative. Do not let filter lag or
            // suspension/transform jitter invent target motion after it stops.
            if (target_velocity.sqrMagnitude < TARGET_VELOCITY_FLOOR * TARGET_VELOCITY_FLOOR)
                target_velocity = Vector3.zero;
            latest_target_velocity = target_velocity;
            bool accepted = motion.Sample(target.GetInstanceID(), Time.fixedTime,
                MathVector(target_velocity), FlightTime(fcs == null ? 1f : fcs.CurrentRange));
            if (accepted)
            {
                averaged_velocity = UnityVector(motion.Velocity);
                velocity_valid = motion.Samples >= 2;
                lock_warmup_complete = velocity_valid && motion.Age >= LOCK_WARMUP_SECONDS;
                // The filter first: the mixture is extrapolated from the state it produces, and would
                // be a tick behind if it were fed after its own input.
                if (target_velocity_from_solver && target_velocity == Vector3.zero)
                {
                    averaged_velocity = Vector3.zero;
                    bias.Reset();
                    ukf.Reset();
                    imm.Reset();
                }
                else
                {
                    SampleFilter(target_velocity);
                    SampleMotionMixture();
                }
            }
            else if (motion.Samples == 0)
            {
                velocity_valid = false;
                lock_warmup_complete = false;
                imm.Reset();
                ukf.Reset();
            }
        }

        /// <summary>
        /// Feeds the unscented filter this tick's measurement: the target's position and velocity
        /// relative to the hull, in the frame the lead is taken in, plus whether that velocity is an
        /// independent measurement or the same transform differenced (see TargetWorldVelocity). A
        /// transform's own difference is not evidence a filter may hold against its own arithmetic, so
        /// the update is told which it is and drops the velocity measurement when it is not.
        /// </summary>
        private void SampleFilter(Vector3 target_velocity)
        {
            Vector3 relative;
            if (!TargetRelativePosition(out relative) || !platform_velocity_raw_valid)
            {
                ukf.Reset();
                return;
            }

            ukf.Update(Time.fixedTime, MathVector(relative),
                MathVector(target_velocity - PlatformLeadVelocity()), target_velocity_from_solver);
        }

        /// <summary>
        /// The target's velocity for the lead: the filter's estimate where the filter is healthy, the
        /// sampler's averaged measurement otherwise. Every consumer of the lead goes through here, so
        /// there is one answer to which estimator is driving it.
        /// </summary>
        private Vector3 LeadVelocity()
        {
            if (target_velocity_from_solver && latest_target_velocity == Vector3.zero)
                return Vector3.zero;
            // UKF is hull-relative; the sampler is world-relative. Publish world
            // velocity here so callers subtract inherited velocity exactly once.
            return ukf.Healthy ? UnityVector(ukf.Velocity) + PlatformLeadVelocity() : averaged_velocity;
        }

        /// <summary>The target's acceleration for the lead, on the same terms as LeadVelocity.</summary>
        private Vector3 LeadAcceleration()
        {
            if (target_velocity_from_solver && latest_target_velocity == Vector3.zero)
                return Vector3.zero;
            return ukf.Healthy ? UnityVector(ukf.Acceleration) + platform_acceleration : UnityVector(motion.Acceleration);
        }

        private Vector3 RelativeLeadVelocity(Vector3 inherited)
        {
            // A solver reading wins even before the render loop refreshes LOS fusion.
            // In particular, a freshly stopped target must not revive last frame's lead.
            return !target_velocity_from_solver && fused_relative_velocity_valid
                ? fused_relative_velocity + PlatformLeadVelocity() - inherited
                : LeadVelocity() - inherited;
        }

        private CrowsImm LeadMotionMixture()
        {
            // Mixture weights are learned from hull-relative motion. While the hull
            // moves, use the single world-acceleration model: a round inherits launch
            // velocity, not the hull's later braking or turning manoeuvre.
            float floor = TARGET_VELOCITY_FLOOR * TARGET_VELOCITY_FLOOR;
            return PlatformLeadVelocity().sqrMagnitude < floor
                && platform_acceleration.sqrMagnitude < floor ? imm : null;
        }

        /// <summary>How much of that acceleration is worth carrying, on the models' 0..0.65 scale.</summary>
        private double LeadConfidence()
        {
            return ukf.Healthy ? ukf.Confidence : motion.Confidence;
        }

        /// <summary>
        /// Feeds the mixture this tick's measurement: where the target is relative to the hull, what
        /// the filter has made of its motion relative to the hull, and what that estimate is worth.
        ///
        /// Relative on both sides, and in the frame the projectile's inherited velocity is measured in,
        /// because that is the quantity the lead is: a target standing still and a station rolling past
        /// it are the same problem, and a mixture fed the target's own velocity while the measurement
        /// carried the station's motion would spend every tick convincing itself that all five
        /// hypotheses are wrong.
        ///
        /// The position is read from the chassis rather than from the weapon on purpose. The weapon's
        /// transform swings with the turret, and a slew would arrive here as the target moving.
        ///
        /// The mixture owns the interval and the displacement - it differences consecutive positions
        /// itself and resets on anything no hypothesis could have predicted - so a tick that cannot be
        /// measured here leaves the single-model solve in charge rather than feeding the weights noise.
        /// </summary>
        private void SampleMotionMixture()
        {
            Vector3 relative;
            if (motion.Samples < 2 || !platform_velocity_raw_valid || LeadMotionMixture() == null
                || !TargetRelativePosition(out relative))
            {
                imm.Reset();
                return;
            }

            imm.Sample(Time.fixedTime, MathVector(relative),
                MathVector(LeadVelocity() - PlatformLeadVelocity()),
                MathVector(LeadAcceleration() - platform_acceleration),
                LeadConfidence());
        }

        /// <summary>Whether this station fires from that weapon; see CrowsShotFeedback.</summary>
        internal bool Fires(WeaponSystem other)
        {
            return other != null && weapon != null && other.GetInstanceID() == weapon.GetInstanceID();
        }

        private void OnRoundFired(AmmoType ammo, LiveRound round)
        {
            CrowsShotFeedback.Fired(weapon, round);
        }

        /// <summary>The range a round from this weapon is being fired at, for the fall of shot.</summary>
        internal float FallOfShotRange(WeaponSystem other)
        {
            return Fires(other) && fcs != null ? fcs.CurrentRange : 0f;
        }

        /// <summary>
        /// Where the round is being aimed, for attributing a fall of shot to this station: the point
        /// the sight is following, which is the target rather than the gun. Only used to tell this
        /// station's rounds from a neighbour's; the correction itself is measured against where the
        /// target is when the round lands.
        /// </summary>
        internal bool AimPointForFallOfShot(WeaponSystem other, out Vector3 point)
        {
            point = Vector3.zero;
            if (!Fires(other) || target == null || target.transform == null) return false;
            return SightPoint(out point);
        }

        /// <summary>
        /// One round's fall of shot, measured where it landed against where the target is at that
        /// moment: a round that lands on the target is a round that was led correctly, whatever the
        /// lead was. Feeds CrowsBallisticBias in the reference frame's own right and up, which is the
        /// frame the lead is written in.
        /// </summary>
        internal void NoteFallOfShot(Vector3 impact, float range)
        {
            if (fcs == null || range < 50f || float.IsNaN(impact.x) || float.IsInfinity(impact.x)) return;

            Vector3 aimed;

            if (!SightPoint(out aimed))
            {
                // The target left the sight between the shot and the fall of shot; a correction cannot
                // be measured against a point that is no longer being followed.
                return;
            }

            Transform reference = fcs.ReferenceTransform;

            if (reference == null) return;

            Vector3 error = impact - aimed;
            bias.Observe(Vector3.Dot(error, reference.right), Vector3.Dot(error, reference.up), range);
        }

        /// <summary>
        /// Hands the ballistic replica the round in the breech. Called every frame and free when nothing
        /// has changed; see CrowsBallistics.Configure.
        /// </summary>
        /// <summary>
        /// Where the round leaves from. The fire control measures its range from its own reference point,
        /// which is where the sight is rather than where the barrel is; an intercept solved from the wrong
        /// one of the two is out by the parallax between them.
        /// </summary>
        private Vector3 MuzzlePosition()
        {
            try
            {
                Transform muzzle = weapon == null ? null : weapon.MuzzleIdentity;

                if (muzzle != null)
                {
                    return muzzle.position;
                }
            }
            catch (Exception)
            {
                // A station whose weapon will not answer is measured from the fire control instead.
            }

            return fcs == null || fcs.ReferenceTransform == null ? Vector3.zero : fcs.ReferenceTransform.position;
        }

        private void ConfigureBallistics()
        {
            AmmoType ammo = fcs == null ? null : fcs.CurrentAmmoType;

            if (ammo == null || ammo == ballistics_ammo)
            {
                return;
            }

            ballistics_ammo = ammo;
            CrowsBallistics.Configure(ammo.MuzzleVelocity, ammo.Coeff, ammo.SectionalArea, ammo.Mass);
        }

        /// <summary>Fades the correction between engagements, and drops it when the round changes.</summary>
        internal void FadeFallOfShot(float seconds)
        {
            AmmoType now = fcs == null ? null : fcs.CurrentAmmoType;

            if (now != null && now != bias_ammo)
            {
                // A different round has a different muzzle velocity and a different drop; a correction
                // learned with the last one is not evidence about this one.
                bias_ammo = now;
                bias.Reset();
                return;
            }

            bias.Decay(seconds);
        }

        /// <summary>
        /// The target's position relative to the hull, in the same frame the relative velocity above is
        /// taken in. False when the station has no chassis to measure from - a towed mount or an
        /// emplacement - in which case the mixture stays out of the way.
        /// </summary>
        private bool TargetRelativePosition(out Vector3 relative)
        {
            relative = Vector3.zero;
            if (target == null || target.transform == null || fcs == null) return false;

            try
            {
                var chassis = fcs.Chassis;
                if (chassis == null || chassis.Rigidbody == null) return false;
                relative = target.transform.position - chassis.Rigidbody.position;
                return true;
            }
            catch (Exception)
            {
                // A station whose chassis will not answer is one the mixture cannot be fed from.
                return false;
            }
        }
        private bool velocity_valid;

        /// <summary>
        /// The transform the fallback velocity measurement last saw, and the physics clock it saw it on.
        /// The clock is the point: a transform is advanced by the physics step, so differencing it across
        /// a frame that had no physics tick reports nothing, and the next frame reports twice as much.
        /// See TargetWorldVelocity.
        /// </summary>
        private Vector3 velocity_sample_position;
        private float velocity_sample_time;
        private bool velocity_sample_valid;
        private Vector3 sampled_velocity;

        /// <summary>
        /// Whether the last TargetWorldVelocity reading came from the target's own solver rather than
        /// from differencing its transform. Only the filter acts on it; see SampleFilter.
        /// </summary>
        private bool target_velocity_from_solver;

        /// <summary>The followed aim point, low-passed; see AIM_SMOOTHING.</summary>
        private Vector3 smoothed_aim_point;
        private bool aim_point_valid;

        /// <summary>
        /// Whether <see cref="smoothed_aim_point"/> is a point this lock produced. AimPointValid only
        /// says the filter has been seeded; this says the point belongs to the target being held now,
        /// which is what the sight and the gate are allowed to be drawn from.
        /// </summary>
        private bool smoothed_valid;

        /// <summary>
        /// The player's aim-point offset while a target is held, in metres in the frame facing the
        /// target: x to its right, y above it. Reset with the lock. See WalkManualOffset.
        /// </summary>
        private Vector2 manual_offset;

        /// <summary>
        /// Physics-sample warmup: require at least two fresh samples before enabling lead.
        /// Acceleration confidence has its own longer warmup in CrowsMotionSampler.
        /// </summary>
        private bool lock_warmup_complete;

        /// <summary>
        /// The aim's own angular rate, in degrees per second, and the direction it had last frame. See
        /// UpdateAimRate: this is what tells a deliberate traverse from the player pointing at
        /// something.
        /// </summary>
        private Vector3 aim_was;
        private float aim_rate;
        private bool aim_rate_valid;

        /// <summary>
        /// Where the *crosshair* should be pointed, or false when nothing is being tracked: the point the
        /// aim was last driven to. That point already carries the player's manual offset - DriveAim is
        /// handed the offset point and filters it - so the crosshair follows a hold-off with the gun, and
        /// the gate, which is drawn on the target itself, does not. See GatePoint.
        ///
        /// This is not a fresh evaluation of the target's transform. Fresh looked more accurate and was
        /// worse: the fire control's aim is what the sight servo, the crosshair and the camera all follow,
        /// so a point that has moved on by one more frame's worth of the target's travel since the fire
        /// control was last written to is a sight that trails the aim. See DriveAim, which settles the
        /// point and keeps it.
        ///
        /// It answers while the lock is merely *lost*, too, and that matters more than it looks. Lost is a
        /// temporary state - the line of sight is blocked for a fraction of a second - and it is entered and
        /// left several times a second while a target moves behind cover. Refusing to point the sight in
        /// that state hands it back to the game's own alignment for those frames, which aims it somewhere
        /// else entirely; the sight then snaps back and forth between the two, which at this magnification
        /// is a shake of the whole picture. The point it holds during the loss is the last one the aim was
        /// driven to, which is where the station was already pointing.
        /// </summary>
        internal bool SightPoint(out Vector3 point)
        {
            point = Vector3.zero;

            if (target == null || target.transform == null || !smoothed_valid)
            {
                return false;
            }

            point = smoothed_aim_point;

            return true;
        }

        /// <summary>
        /// The point the gate is drawn on: the target's own centre, straight off its transform - the same
        /// point the passive box is drawn on, so the box does not move when the lock is taken.
        ///
        /// The box marks the target, and only the target. It used to be drawn on the aim point instead,
        /// which is where the *crosshair* is, on the argument that the two could then never disagree - and
        /// what that produced was the opposite of what a tracking gate should look like: the fire control's
        /// aim lags the target by the aim filter's time constant, and the gate, riding the aim, wandered
        /// across the target as the lag grew and shrank, while the target itself sat still. Locking now
        /// moves the *sight* onto the target (see SightPoint) and leaves the box where the target is, so
        /// the crosshair is the thing that travels and the box is the thing that stays put.
        /// </summary>
        private bool GatePoint(out Vector3 point)
        {
            point = Vector3.zero;

            if (target == null || target.transform == null)
            {
                return false;
            }

            point = target.transform.TransformPoint(box_centre_local);

            return true;
        }

        /// <summary>
        /// Lets the player move the crosshair while the tracker holds the target.
        ///
        /// The aim input is cut off at the fire control while the tracker is driving it - that is what
        /// stops the player and the tracker fighting over the aim - but the input itself is still read
        /// every frame, because it is stored before the call that gets suppressed. This takes that
        /// input and walks an offset with it, and the offset is added to the tracked point for the gun,
        /// the sight and the range alike.
        ///
        /// The lock is not disturbed: the gate stays on the target and the offset is what moves. The
        /// fire control's own lead still lands on top of it, so this adjusts the point being tracked
        /// rather than replacing the solution.
        /// </summary>
        private void WalkManualOffset(Vector3 tracked)
        {
            if (fcs == null || fcs.ReferenceTransform == null)
            {
                return;
            }

            PlayerInput input = PlayerInput.Instance;

            if (input == null)
            {
                return;
            }

            // Metres at the target per second at full deflection, scaled by the sight's field of view.
            float fov = 6f;
            UsableOptic optic = fcs.MainOptic;

            if (optic != null && optic.slot != null && optic.slot.CurrentFov > 0f)
            {
                fov = optic.slot.CurrentFov;
            }

            manual_offset += input.VirtualJoystick
                * (MANUAL_SLEW_METRES_PER_DEGREE_SECOND * fov * Time.deltaTime);

            // Bounded as an angle at the target rather than as a distance, so the same limit means the
            // same thing near and far.
            float range = Vector3.Distance(fcs.ReferenceTransform.position, tracked);
            float limit = Mathf.Tan(MANUAL_OFFSET_LIMIT_DEGREES * Mathf.Deg2Rad) * range;

            if (manual_offset.sqrMagnitude > limit * limit)
            {
                manual_offset = limit <= 0f ? Vector2.zero : manual_offset.normalized * limit;
            }
        }

        /// <summary>
        /// The tracked point with the player's manual offset applied, in the frame facing the target:
        /// x to its right, y above it - the frame the offset was measured in, so the same input always
        /// moves the crosshair the same way on screen.
        /// </summary>
        private Vector3 OffsetAimPoint(Vector3 point)
        {
            if ((manual_offset.x == 0f && manual_offset.y == 0f) || fcs == null || fcs.ReferenceTransform == null)
            {
                return point;
            }

            Vector3 forward = point - fcs.ReferenceTransform.position;

            if (forward.sqrMagnitude < 1f)
            {
                return point;
            }

            return point + Quaternion.LookRotation(forward, Vector3.up)
                * new Vector3(manual_offset.x, manual_offset.y, 0f);
        }
        /// <summary>Whether this tracker is the one holding the player's aim off the fire control.</summary>
        private bool aim_held;

        /// <summary>
        /// The target's crossing rate, in degrees per second, as this module measures it; see
        /// ReportTargetRate. Smoothed here because it is written straight past the fire control's own
        /// averaging (CrowsLeadLock).
        /// </summary>
        private Vector2 lead_rate;
        private bool lead_rate_valid;

        /// <summary>Whether the status line is currently showing a lead; see HasLead.</summary>
        private bool lead_shown;

        // smoothed marker state (see MARKER_SMOOTHING)
        private bool gate_state_valid;
        private int smoothed_frame = -1;
        private int last_precull_frame = -1;
        private Vector2 smoothed_gate_position;
        private Vector2 smoothed_gate_size;


        /// <summary>Original optic alignment, so a change can be undone when the lock drops.</summary>
        private readonly List<OpticAlignment> original_alignment = new List<OpticAlignment>();

        private readonly RaycastHit[] hits = new RaycastHit[RAY_HITS];
        private readonly Vector3[] corners = new Vector3[8];
        private readonly Vector3[] projected = new Vector3[8];

        /// <summary>The fire control's own parallax option, as it was before this module touched it.</summary>
        private bool original_parallax;
        private bool parallax_armed;

        /// <summary>The computer's range granularity, and whether it has been taken over.</summary>
        private int original_granularity;
        private bool granularity_armed;

        /// <summary>The computer's own inertial-compensation setting, for handing back with the lock.</summary>
        private bool original_inertial;
        private bool lead_armed;

        // ------------------------------------------------------------------ ui

        private class SightMarker
        {
            public UsableOptic optic;
            public RectTransform canvas;
            public RectTransform gate;

            /// <summary>The four bracket blocks (TL, TR, BL, BR), each holding two bars.</summary>
            public RectTransform[] gate_corners;



        }

        private readonly List<SightMarker> markers = new List<SightMarker>();


        private Sprite bar_sprite;

        // ------------------------------------------------------------------ attach

        /// <summary>
        /// Builds the tracker and its sight overlays. Called from CROWS.MountInternal() once the fire
        /// control, both optics and the borrowed weapon all exist.
        ///
        /// The component starts disabled and is switched on by <see cref="Activate"/>: GHPC's
        /// FireControlSystem.Awake() caches the original superelevation and superlead modes, and those
        /// are overwritten afterwards, so nothing may run before the CROWS model has been activated.
        /// </summary>
        public static CrowsAutoTrack Attach(FireControlSystem fcs, Vehicle ours, UsableOptic[] sights, WeaponSystem weapon)
        {
            if (fcs == null || ours == null || sights == null || sights.Length == 0)
            {
                return null;
            }

            GameObject host = new GameObject("CROWS auto track");
            host.transform.SetParent(fcs.transform, false);

            CrowsAutoTrack tracker = host.AddComponent<CrowsAutoTrack>();
            tracker.enabled = false; // until Activate()
            tracker.fcs = fcs;
            tracker.ours = ours;
            tracker.weapon = weapon;
            if (weapon != null) weapon.Fired += tracker.OnRoundFired;
            // WeaponSystem._rb is assigned exactly once, in Awake, and never again: the field is not
            // readonly, but nothing in the game writes it after activation. Reading it through reflection
            // twice per frame in the lead path is therefore pure cost; take it here, where the weapon
            // handed to Attach is already the activated one.
            tracker.weapon_rb = weapon == null ? null : Reflect.Get<Rigidbody>(weapon, "_rb");

            foreach (UsableOptic optic in sights)
            {
                if (optic == null || tracker.optics.Contains(optic))
                {
                    continue;
                }

                tracker.optics.Add(optic);
                tracker.original_alignment.Add(optic.Alignment);
            }

            tracker.original_parallax = Reflect.Get<bool>(fcs, "_fixParallaxForVectorMode");
            tracker.BuildTextures();
            tracker.BuildMarkers();

            return tracker;
        }

        /// <summary>
        /// Switches the tracker on and prepares the game's lead computation. Must be called after the
        /// CROWS model has been activated (CROWS.MountInternal's model.SetActive(true)).
        ///
        /// The lead is configured here - which is also what proves the reflected flags work, and the
        /// only chance to log it before the player locks anything - and then handed straight back.
        /// Until a target is locked the station behaves exactly as it did before this module existed.
        /// </summary>
        public void Activate()
        {
            // Avoid duplicate camera subscriptions and resetting an existing lock.
            if (armed || fcs == null) return;
            armed = true;

            // Reset all tracking state to ensure clean initialization
            // This is critical in multi-faction scenarios where CROWS may be reused
            lead_rate = Vector2.zero;
            lead_rate_valid = false;
            rate_reference_valid = false;
            platform_motion_sample_valid = false;
            platform_acceleration = Vector3.zero;
            velocity_valid = false;
            velocity_sample_valid = false;
            los_rate_valid = false;
            fused_relative_velocity = Vector3.zero;
            fused_relative_velocity_valid = false;
            
            // Reset aim tracking state
            aim_rate = 0f;
            aim_rate_valid = false;
            aim_was = Vector3.zero;

            ArmLead();
            DisarmLead();
            
            // Force clear the FCS's internal aim rate buffer (_lastAngleAdjust)
            // This must be done AFTER DisarmLead() to ensure the buffer is truly zeroed
            // Use a coroutine to ensure it happens after all initialization is complete
            StartCoroutine(ClearAimRateBuffer());

            // The markers are world points run through the camera, so they have to be projected with
            // the pose the frame is actually rendered with. GHPC moves the camera in its own
            // LateUpdate and ours may run before it, which would make every marker slip against the
            // world it is drawn over. onPreCull runs after every LateUpdate and immediately before the
            // render, so it always sees the camera the picture is about to be drawn with.
            Camera.onPreCull += HandlePreCull;

            enabled = true;

            // LateUpdate samples platform motion continuously, including before a lock.
            UpdatePlatformMotion();
        }
        
        /// <summary>
        /// Coroutine to clear the aim rate buffer after initialization is complete.
        /// This ensures Time.deltaTime is valid and the FCS is fully initialized.
        /// </summary>
        private IEnumerator ClearAimRateBuffer()
        {
            // Wait for 3 frames to ensure FCS is fully initialized
            for (int i = 0; i < 3; i++)
            {
                yield return null;
                
                // Startup cleanup must not erase input or a newly acquired lock.
                if (target != null || IsActiveWeapon()) yield break;
                if (fcs != null)
                {
                    try
                    {
                        Reflect.Set(fcs, "_lastAngleAdjust", Vector2.zero);
                    }
                    catch (Exception)
                    {
                        // Silent failure - will retry next frame
                    }
                }
            }
        }

        private void HandlePreCull(Camera cam)
        {
            try
            {
                if (cam == null || cam != CameraManager.MainCam)
                {
                    return;
                }

                last_precull_frame = Time.frameCount;

                // Only the markers are placed here. Nothing in the scene is touched from this callback:
                // the optic and the camera hang off the CROWS model and the gun, so rotating either of
                // them after every LateUpdate has run moves the transforms the frame is about to be
                // rendered with - a shake in the whole sight picture, not a marker in the wrong place.
                // The model is driven from CrowsAimSlave's own LateUpdate instead, and the mouse never
                // reaches the fire control to begin with (see CrowsAimLock).
                UpdateMarkers();
            }
            catch (Exception)
            {
                Camera.onPreCull -= HandlePreCull;
            }
        }

        /// <summary>
        /// Updates the markers from LateUpdate, but only if the camera callback has not already done it
        /// recently. GHPC has its own onPreCull handler (InstancedMeshRenderer) that throws on some
        /// frames, and an exception in one delegate can skip the rest - so this hook cannot be the only
        /// path.
        /// </summary>
        private void UpdateMarkersTimed()
        {
            if (Time.frameCount - last_precull_frame <= 2)
            {
                return;
            }

            UpdateMarkers();
        }

        /// <summary>
        /// Takes the lead back out of the fire control. Its traverse-rate buffer is fed by whatever
        /// rotates the aim - including the player's own hand - so a station that is not tracking must
        /// not be running it.
        /// </summary>
        private void DisarmLead()
        {
            if (fcs == null || !lead_armed) return;
            try
            {
                fcs.ZeroOutLead();
                fcs.SetLeadHold(false);

                // The platform's travel goes back to the fire control with the lock, so a player aiming
                // the station by hand again gets the game's own compensation for it rather than none;
                // see ArmLead for why this module takes it over while a target is held.
                fcs.InertialCompensation = original_inertial;
            }
            catch (Exception)
            {
                // releasing must never throw
            }
            finally
            {
                lead_armed = false;
            }
        }

        // ------------------------------------------------------------------ the game's own lead

        /// <summary>
        /// Turns the fire control's native lead computation on and keeps it on.
        ///
        /// Every field here is public except three, which are reached through ModUtil.Reflect in
        /// keeping with the rest of the mod (the running game loads the un-publicized
        /// Assembly-CSharp). Each reflection step is wrapped: if one fails the feature degrades to
        /// "tracking without lead" instead of throwing, and the log says so.
        /// </summary>
        private void ArmLead()
        {
            if (fcs == null || lead_armed)
            {
                return;
            }

            try
            {
                fcs.SuperelevateWeapon = true;
                fcs.SuperleadWeapon = true;
                fcs.RecordTraverseRateBuffer = true;
                fcs.DynamicLead = true;

                // The platform's own travel is NOT left to the fire control.
                //
                // It can be: GetLeadWorldOffset adds Chassis.Velocity * flightTime when
                // InertialCompensation is on and it found a chassis, and that is the same term. But then
                // the lead has two authors - a traverse rate this module measures and a velocity the
                // fire control reads - and the readout can only see one of them, which is exactly why a
                // station rolling across open ground with a parked target in the sight read "TRACK"
                // while the computer had a metre and a half of lead dialled in.
                //
                // With it off, everything the computer leads by arrives through _averageTraverseRate,
                // which this module writes (see ReportTargetRate and CrowsLeadLock): one formula,
                // (target velocity - platform velocity) across the line of sight over the range, and one
                // place to look when it is wrong. The two arrangements are arithmetically identical -
                // the same term, in the same frame, with the same sign - so nothing about the ballistics
                // changes.
                original_inertial = fcs.InertialCompensation;
                lead_armed = true;
                fcs.InertialCompensation = false;
                fcs.ClampDegreeAdjust = false;   // this module clamps its own rate

                // The rate the lead is derived from is the rate the aim is rotated at, and with a
                // machine driving the aim that rate is polluted by whatever else touched the aim that
                // frame - above all the player's hand, which can be a full 40 deg/s. CROWS.cs sets this
                // buffer to 0.1 s, which averages barely six frames and lets a single such frame
                // through. The game's own one second window turns the same spike into 0.012 deg/s.
                fcs.TraverseBufferSeconds = 1f;
            }
            catch (Exception)
            {
                return;
            }

            bool ok = true;

            try
            {
                // EngageLead() bails out unless the palm switches are held.
                Reflect.Set(fcs, "_autoDumpViaPalmSwitches", false);

                // SetLeadHold() bails out unless separate lead is enabled. Only a getter exists.
                Reflect.Set(fcs, "_useSeparateLead", true);

                fcs.SetLeadHold(true);
                Reflect.Set(fcs, "_leadUpdatesLocked", false);

                if (!Reflect.Get<bool>(fcs, "_trackingEnabled"))
                {
                    Reflect.Set(fcs, "_trackingEnabled", true);
                }

                if (!Reflect.Get<bool>(fcs, "_trackingEnabled"))
                {
                    ok = false;
                }
            }
            catch (Exception)
            {
                ok = false;
            }

            if (!ok)
            {
            }
        }

        /// <summary>
        /// Lines the gun up with the sight by fixing the muzzle parallax offset.
        ///
        /// The weapon vector ignores the distance between the fire control and the muzzle unless
        /// <c>_fixParallaxForVectorMode</c> is set, so without this the barrel is aimed as if it fired
        /// from the fire control - about two metres on this station - and the rounds then land off the
        /// sight line by exactly that.
        /// </summary>
        private void ArmFiringSolution()
        {
            if (fcs == null || parallax_armed)
            {
                return;
            }

            bool found = Reflect.FindField(typeof(FireControlSystem), "_fixParallaxForVectorMode") != null;

            if (found)
            {
                try
                {
                    Reflect.Set(fcs, "_fixParallaxForVectorMode", true);
                    parallax_armed = true;
                }
                catch (Exception)
                {
                // The call is best-effort: a failure here leaves the previous state alone.
                }
            }

            if (!parallax_armed)
            {
            }

            // The range is written continuously from a target the tracker can see to the centimetre,
            // and a granularity would round every one of those writes back off again - the solution
            // would step instead of track.
            try
            {
                original_granularity = fcs.RangeGranularity;

                if (fcs.RangeGranularity != 0)
                {
                    fcs.RangeGranularity = 0;
                }
            }
            catch (Exception)
            {
                // a computer without a granularity field has nothing to round with anyway
            }

            granularity_armed = true;
        }

        private void DisarmFiringSolution()
        {
            if (fcs == null)
            {
                return;
            }

            if (granularity_armed)
            {
                granularity_armed = false;

                try
                {
                    fcs.RangeGranularity = original_granularity;
                }
                catch (Exception)
                {
                    // handing back must never throw
                }
            }

            if (!parallax_armed)
            {
                return;
            }

            try
            {
                Reflect.Set(fcs, "_fixParallaxForVectorMode", original_parallax);
            }
            catch (Exception)
            {
                // handing back must never throw
            }

            parallax_armed = false;
        }

        // ------------------------------------------------------------------ input

        private void Update()
        {
            if (fcs == null)
            {
                return;
            }

            // The CROWS key only answers while the CROWS is the weapon the player is holding, so it
            // can never steal the middle mouse button from the main gun.
            if (!IsActiveWeapon())
            {
                return;
            }

            if (UnityEngine.Input.GetKeyDown(ATT_KEY))
            {
                if (target != null)
                {
                    Release();
                }
                else
                {
                    TryLock();
                }
            }
        }

        // ------------------------------------------------------------------ tracking loop

        private void LateUpdate()
        {
            if (fcs == null)
            {
                return;
            }

            // How fast the aim is swinging, which decides whether the player is pointing at anything in
            // particular; see Sweeping.
            UpdateAimRate();

            // How fast the player's own vehicle is going, and whether that counts as moving. Read every
            // frame whichever state the tracker is in, because it belongs to the vehicle and not to the
            // lock; see UpdatePlatformMotion.
            UpdatePlatformMotion();

            // Keeps an eye on whether the computer is solving for the round the gun is firing, and on
            // whether that round is one the computer will lead with at all.
            if (IsActiveWeapon())
            {
                CheckComputerAmmo();
                CheckLeadCapableAmmo();
            }

            // Nothing to do while the station is not the one being used and nothing is locked: no box
            // to place, and the sight is not even on screen.
            if (target == null && !IsActiveWeapon())
            {
                HideMarkers();
                return;
            }

            // The passive box runs whether or not anything is locked, because it is the box the player
            // sees at all times; see UpdateDesignation, and UpdateMarkers for what a lock adds.
            UpdateDesignation();

            if (target == null)
            {
                // The passive box is re-measured on the same timer as a locked one, so a framed vehicle
                // that changes shape - a turret swinging, a launcher erecting - is followed before it is
                // ever locked; see RefreshTargetBox.
                RefreshTargetBox();
                UpdateMarkersTimed();
                return;
            }

            // Only drive while the CROWS is the selected weapon. The lock is kept, so the station
            // picks the track straight back up when the player switches back to it.
            if (!IsActiveWeapon())
            {
                lost_timer = 0f;
                state = TrackState.Locked;

                // Hand the player's aim input back: a station the player has switched away from must
                // not be holding anything.
                ReleaseAimLock();
                rate_reference_valid = false;

                // The aim is nobody's this frame, so the lead buffer is told exactly that rather than
                // being left to age with whatever it last held.
                WriteAimRate(Vector2.zero);

                HideMarkers();
                return;
            }

            HoldAimLock();

            RestoreAlignment();

            Vector3 tracked = TrackedPoint();

            WalkManualOffset(tracked);

            Vector3 aim_point = OffsetAimPoint(tracked);
            bool blocked = !IsVisible(aim_point);

            if (blocked)
            {
                lost_timer += Time.deltaTime;

                if (lost_timer >= LOST_GRACE_SECONDS)
                {
                    state = TrackState.Lost;
                    Release();
                    return;
                }

                // A blocked frame on its own is noise, so the aim keeps being driven through the
                // debounce window; past it the sight holds the last aim. The gate stays - the target is
                // still locked - but the impact marker does not, there being no fresh solution.
                if (lost_timer > BLOCK_DEBOUNCE_SECONDS)
                {
                    state = TrackState.Lost;
                    UpdateMarkersTimed();
                    return;
                }
            }
            else
            {
                lost_timer = 0f;
                state = TrackState.Locked;
                DriveAim(aim_point);
            }

            if (!blocked)
            {
                ManageTrackedRange(aim_point);
                RefreshTargetBox();
                CheckBoreAlignment();
            }

            UpdateMarkersTimed();
        }

        /// <summary>
        /// The tracked point for this frame: the target's box centre, carried forward to the frame being
        /// drawn *when the transform has to be carried forward at all*.
        ///
        /// The box centre comes off the target's transform, and the transform is only advanced by the
        /// physics step - fifty times a second, against the hundred and ten or so frames the station
        /// draws. Read straight, it therefore stands still for a frame or two and then jumps by a whole
        /// tick's worth of the target's travel, and the tracker faithfully drives the aim onto those
        /// steps: the aim error trace of a target crossing at thirty metres a second at 1,600 m reads
        /// 0.000 0.000 0.020 0.020 0.000, a five-hundredth of a degree of stagger repeated forever. At
        /// 15X that is about twenty pixels, and it is what the eye reports as the picture shaking.
        ///
        /// The target's own velocity closes the gap: the rigidbody reports it smoothly, and the fraction
        /// of a physics tick that has passed since the last one is exactly how far the transform is
        /// behind. Extrapolating by that much makes the followed point advance every frame, so the aim
        /// does too. The error is arithmetic, not a guess about the future.
        ///
        /// That is only true of a transform the physics engine leaves where the physics step put it. A
        /// *rigidbody being interpolated* (RigidbodyInterpolation.Interpolate) has its transform moved to
        /// the frame being drawn by the engine itself, so it is already "now", and carrying it forward
        /// again puts the point a whole tick's worth of the target's travel *ahead* of the target the
        /// player can see - a sawtooth at the physics rate, of the same size as the stepping it was there
        /// to hide, and this time between the crosshair and the target rather than inside the aim. The
        /// target's own setting says which case it is, and it is read once per lock.
        ///
        /// The *platform* has the same setting and it counts just as much, and that half used to be
        /// assumed rather than read: the correction is the target's travel less the platform's, because
        /// the reference the aim is measured from is on the platform and steps with it. Assuming the
        /// platform stepped when it was really being interpolated subtracted a whole tick of the
        /// player's own travel from the point every frame - four or five pixels of sideways drift at 15X
        /// on a station driving at thirty kilometres an hour, changing sign with the beat between the
        /// frame rate and the physics rate. Read both, and the correction is zero whenever neither has
        /// to be carried forward.
        /// </summary>
        private Vector3 TrackedPoint()
        {
            Vector3 point = target.transform.TransformPoint(box_centre_local);

            bool target_steps = !target_interpolated;
            bool platform_steps = !PlatformInterpolated();

            if (!target_steps && !platform_steps)
            {
                return point;
            }

            // Get target velocity using Chassis.Velocity (most reliable for vehicles)
            Vector3 target_velocity = Vector3.zero;
            try
            {
                if (target?.Chassis != null)
                {
                    target_velocity = target.Chassis.Velocity;
                }
            }
            catch (Exception)
            {
                target_velocity = Vector3.zero;
            }

            // Get player velocity using Chassis.Velocity
            Vector3 player_velocity = Vector3.zero;
            try
            {
                if (ours?.Chassis != null)
                {
                    player_velocity = ours.Chassis.Velocity;
                }
            }
            catch (Exception)
            {
                player_velocity = Vector3.zero;
            }

            // Only the half that really is behind by a fraction of a tick is carried forward.
            Vector3 velocity = (target_steps ? target_velocity : Vector3.zero)
                - (platform_steps ? player_velocity : Vector3.zero);

            if (velocity.sqrMagnitude < 0.01f)
            {
                return point;
            }

            float behind = Time.time - Time.fixedTime;

            if (behind <= 0f)
            {
                return point;
            }

            return point + velocity * Mathf.Min(behind, Time.fixedDeltaTime * 2f);
        }

        /// <summary>
        /// Whether the physics engine is interpolating this unit's rigidbody - the switch that decides
        /// whether its transform is already at the frame being drawn (see TrackedPoint).
        ///
        /// A unit with no rigidbody of its own - an emplacement, a towed gun - reports false, which leaves
        /// the extrapolation in place; that is harmless, because such a target's transform does not move
        /// between physics steps in the first place.
        /// </summary>
        private static bool IsInterpolated(Unit unit)
        {
            try
            {
                Rigidbody body = unit == null ? null : unit.GetComponentInChildren<Rigidbody>();

                return body != null && body.interpolation != RigidbodyInterpolation.None;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// The same answer, remembered for the unit it was asked about: the marker update asks once a
        /// frame and the answer is a property of the vehicle, not of the frame.
        /// </summary>
        private bool Interpolated(Unit unit)
        {
            if (unit == null)
            {
                return false;
            }

            if (interpolation_asked != null && interpolation_asked == unit)
            {
                return interpolation_known;
            }

            interpolation_asked = unit;
            interpolation_known = IsInterpolated(unit);

            return interpolation_known;
        }

        /// <summary>
        /// The same question asked of the station's own vehicle; see TrackedPoint, which needs both
        /// answers because the correction is the difference between them. Asked once: it is a property
        /// of the chassis, not of the frame.
        /// </summary>
        private bool PlatformInterpolated()
        {
            if (!platform_interpolation_known)
            {
                platform_interpolation_known = true;
                platform_interpolated = IsInterpolated(ours);
            }

            return platform_interpolated;
        }

        private Unit interpolation_asked;
        private bool interpolation_known;
        private bool platform_interpolation_known;
        private bool platform_interpolated;

        /// <summary>
        /// Tells the computer how far away the target is, every frame.
        ///
        /// This used to be a laser shot whose return stood until the target drifted past a tolerance,
        /// which left the superelevation dialled for a distance the target was no longer at and nothing
        /// but dropping the lock and taking it again would correct it. A laser is also just a ray: it
        /// can return the berm in front of the target, and that wrong range then stood for as long as
        /// the lock did.
        ///
        /// Writing <c>TargetRange</c> is the game's own path - the setter raises <c>_rangeMoving</c>,
        /// and the fire control walks <c>CurrentRange</c> onto it at its own interpolation rate and
        /// re-superelevates as it goes - so the readout still moves like a computer working, and the
        /// solution is right the whole time rather than right once.
        /// </summary>
        private void ManageTrackedRange(Vector3 aim_point)
        {
            if (fcs == null || fcs.ReferenceTransform == null)
            {
                return;
            }

            // No laser is fired. The tracker knows the range exactly - it is written below every frame -
            // and the fire control's lead machinery is armed directly by ArmLead, which is the only
            // thing DoLase was ever needed for here.
            //
            // Firing it was also doing harm. DoLase drops the fire control out of manual mode, resets
            // its lead state and overwrites the range with whatever the beam happened to hit, and it
            // brings the game's laser effect in with it. That last part is what made the scenery
            // disappear on locking: GHPC draws its trees and vegetation through an InstancedMeshRenderer
            // whose own onPreCull handler enumerates its collected instances, and something appearing in
            // that collection mid-enumeration makes it throw "Collection was modified; enumeration
            // operation may not execute" - after which the rest of that draw is abandoned and the
            // scenery is simply gone until the next frame that does not throw.
            float actual = Vector3.Distance(fcs.ReferenceTransform.position, aim_point);

            if (actual < MIN_FIRING_RANGE)
            {
                return;
            }

            try
            {
                fcs.TargetRange = actual;
            }
            catch (Exception)
            {
                // a computer that will not take a range simply keeps the one it has
            }
        }

        /// <summary>
        /// The one line the whole feature hangs on: rotate the fire control's aim towards the target by
        /// at most the mount's own traverse rate.
        /// </summary>
        private void DriveAim(Vector3 world_point)
        {
            Transform reference = fcs.ReferenceTransform;

            if (reference == null)
            {
                return;
            }

            // Ease the aim onto the target instead of snapping it there. The point being followed is
            // the target's transform, which the game advances on its physics step, and handing that
            // staircase straight to the fire control puts it into the aim - which is what the sight and
            // the camera are aimed along. The low pass costs about forty milliseconds of following lag
            // and everything downstream glides.
            //
            // The filtered point is then *kept*: it is the point the fire control's aim is driven to
            // *and* the point the sight is served onto (SightPoint = the crosshair), so the crosshair sits
            // where the gun is aimed rather than a frame ahead of it. The lag it carries is real and it is
            // visible - the gate is drawn on the target itself (GatePoint), so the two part company by
            // exactly this lag whenever the target is crossing - and that is the deliberate trade: the
            // crosshair is the thing that has to arrive, and the box is the thing that must not move.
            if (aim_point_valid)
            {
                float k = 1f - Mathf.Exp(-AIM_SMOOTHING * Time.deltaTime);
                smoothed_aim_point = Vector3.Lerp(smoothed_aim_point, world_point, k);
            }
            else
            {
                smoothed_aim_point = world_point;
                aim_point_valid = true;
            }

            smoothed_valid = true;

            world_point = smoothed_aim_point;

            // Put the aim back where this module left it before anything else looks at it. The player's
            // mouse rotates the fire control's aim in Update, and correcting for that afterwards is not
            // enough: by then the fire control has handed the mounts a goal built from the disturbed
            // aim and has recorded the disturbance as the target's traverse rate.
            RestoreAim();

            Vector3 up = reference.up;
            Vector3 right = reference.right;
            Vector3 current = fcs.AimWorldVector;
            Vector3 to = world_point - reference.position;

            if (current.sqrMagnitude < 0.25f || to.sqrMagnitude < 0.25f)
            {
                return;
            }

            // Yaw lives on the turret plane, so both sides are flattened onto it first.
            Vector3 from_plane = Vector3.ProjectOnPlane(current, up);
            Vector3 to_plane = Vector3.ProjectOnPlane(to, up);

            float yaw = 0f;

            if (from_plane.sqrMagnitude >= 0.0625f && to_plane.sqrMagnitude >= 0.0625f)
            {
                yaw = Vector3.SignedAngle(from_plane, to_plane, up);
            }

            // AdjustAimByDegrees applies pitch around ReferenceTransform.right first, then yaw
            // around ReferenceTransform.up. Solve the inverse order: remove the yaw from the
            // target before measuring pitch, otherwise yaw error leaks into vertical command
            // whenever the target is off-axis (the vertical short/long oscillation).
            Vector3 pitch_target = to;
            if (Mathf.Abs(yaw) > 1e-5f)
            {
                pitch_target = Quaternion.AngleAxis(-yaw, up) * to;
            }
            Vector3 pitch_from = Vector3.ProjectOnPlane(current, right);
            Vector3 pitch_to = Vector3.ProjectOnPlane(pitch_target, right);
            float pitch = (pitch_from.sqrMagnitude >= 0.0625f && pitch_to.sqrMagnitude >= 0.0625f)
                ? Vector3.SignedAngle(pitch_from, pitch_to, right) : 0f;

            float limit_h = Mathf.Max(1f, RateOf(0)) * Time.deltaTime;
            float limit_v = Mathf.Max(1f, RateOf(1)) * Time.deltaTime;

            float h = Mathf.Clamp(yaw, -limit_h, limit_h);
            float v = Mathf.Clamp(pitch, -limit_v, limit_v);

            float error = Mathf.Max(Mathf.Abs(yaw), Mathf.Abs(pitch));

            // Off target: slew, but keep the slew out of the lead buffer (see SETTLE_CONE_DEGREES).
            // The two thresholds give it hysteresis, so the lead is fed every frame once the aim is on
            // the target and only suspended again if the aim really does come off.
            if (lead_suspended)
            {
                if (error <= SETTLE_CONE_DEGREES)
                {
                    lead_suspended = false;
                }
            }
            else if (error > LEAD_RESET_CONE_DEGREES)
            {
                lead_suspended = true;
            }

            // The world lead's own, wider gate: the same hysteresis, opened three degrees out rather
            // than three quarters of one; see LEAD_ENTRY_CONE_DEGREES.
            if (lead_ready)
            {
                if (error > LEAD_RESET_CONE_DEGREES)
                {
                    lead_ready = false;
                }
            }
            else if (error <= LEAD_ENTRY_CONE_DEGREES)
            {
                lead_ready = true;
            }

            try
            {
                // noLeadUpdate: the fire control must not record a rate of its own here, because what
                // it would record is the rotation applied, which contains the player's own mouse input;
                // the rate is written explicitly below instead.
                fcs.AdjustAimByDegrees(h, v, true);
            }
            catch (Exception)
            {
                Release();
                return;
            }

            ReportTargetRate(to, up, right);
            // Publish the current velocity before rebuilding mount goals. The earlier aim
            // adjustment used the previous sample; avoid carrying it into this frame's servo.
            fcs.AdjustAimByDegrees(0f, 0f, true);
        }

        /// <summary>
        /// Puts the fire control's aim vector back to what this module last set it to.
        ///
        /// The property's setter is private, so the backing field is written directly - with a value
        /// that came out of the property in the first place, so it is already normalised.
        ///
        /// With the player's aim input held off (see CrowsAimLock) this mostly has nothing to do. It is
        /// still worth the line: the aiming-cursor path drives the fire control through SetAimVector
        /// rather than through the ratio method, and that one is not patched.
        /// </summary>
        private void RestoreAim()
        {
            if (!rate_reference_valid)
            {
                return;
            }

            try
            {
                Reflect.Set(fcs, "_aimWorldVector", rate_reference_aim);
            }
            catch (Exception)
            {
                // without it the aim keeps whatever the player's input left, as before
            }
        }

        /// <summary>
        /// Takes the player's aim input off this fire control, and takes over the traverse rate the
        /// fire control works its lead from, for as long as the lock holds.
        /// </summary>
        private void HoldAimLock()
        {
            if (aim_held)
            {
                return;
            }

            aim_held = true;
            CrowsAimLock.Hold(fcs);
            CrowsLeadLock.Hold(this);
            CrowsShotFeedback.Hold(this);
        }

        private void ReleaseAimLock()
        {
            if (!aim_held)
            {
                return;
            }

            aim_held = false;

            if (CrowsAimLock.Holds(fcs))
            {
                CrowsAimLock.Drop();
            }

            CrowsLeadLock.Drop(this);
            CrowsShotFeedback.Drop(this);
            lead_rate_valid = false;
        }

        /// <summary>
        /// Whether the fire control's aim is this module's to drive - a target is held and the station
        /// is the one being used. CrowsAimSlave asks this before it takes the model over.
        /// </summary>
        /// <summary>Whether the world lead owns the fire control this module is driving; see CrowsLeadLock.</summary>
        internal bool WorldLeadActive
        {
            get { return world_lead_active; }
        }

        public bool Tracking
        {
            get { return armed && target != null; }
        }

        /// <summary>Whether this tracker is the one driving that particular fire control.</summary>
        internal bool Drives(FireControlSystem other)
        {
            return other != null && fcs != null && other.GetInstanceID() == fcs.GetInstanceID();
        }

        /// <summary>
        /// The traverse rate the fire control should work its lead from. Zero while the lead is
        /// suspended, so a slew onto a target is never mistaken for the target's own motion.
        /// </summary>
        internal Vector2 LeadRate
        {
            get { return lead_suspended || !lead_rate_valid ? Vector2.zero : lead_rate; }
        }

        /// <summary>
        /// Returns the negative transverse relative displacement consumed by the FCS subtraction.
        /// The existing table refines time only; drop stays at CurrentRange. Radial inherited velocity
        /// changes the queried distance, not the ballistic model's launch speed or drag integration.
        /// </summary>
        // Compute in world space when the FCS consumes the solution. Cached local rates
        // cannot represent a stable correction while the hull/reference frame rotates.
        internal bool TryGetWorldLead(FireControlSystem other, out Vector3 offset)
        {
            offset = Vector3.zero;
            // The cached solution describes this frame's answer or nothing at all. MeasureLead reads it
            // to report what the fire control was handed, and a solution left over from before a
            // release, a fault or a switch of ammunition would be reported as if it were this frame's.
            predictor_solution = new CrowsIntercept();
            world_lead_active = false;
            if (!Drives(other) || !isActiveAndEnabled || !Tracking || !IsActiveWeapon()
                || fcs.FcsFault || fcs.CurrentAmmoType == null
                || fcs.CurrentAmmoType.NoLeadCompensation) return false;
            // lead_ready and not lead_suspended: the world lead does not read the traverse rate the
            // suspension exists to protect, so it goes out as soon as the aim is anywhere near the
            // target; see LEAD_ENTRY_CONE_DEGREES.
            if (!rate_reference_valid || !lead_ready || !lock_warmup_complete
                || !velocity_valid || !motion.Fresh(Time.fixedTime) || fcs.CurrentRange <= 1f)
            {
                // A zero offset still means the world lead owns this fire control: the alternative is the
                // game's own lead, which this module has just decided not to use.
                world_lead_active = true;
                return true;
            }

            Vector3 direction = fcs.AimWorldVector;
            if (direction.sqrMagnitude < 0.25f) return false;
            float flight = FlightTime(fcs.CurrentRange);
            if (flight <= 0f) return false;
            // WeaponSystem inherits its cached rigidbody's linear velocity, including Y.
            // Do not add GetPointVelocity: the game does not add rotational muzzle velocity.
            Rigidbody body = weapon_rb;
            Vector3 inherited = body == null ? Vector3.zero : body.velocity;
            // Solve the single relative state. The projectile inherits the station's current
            // rigidbody velocity. Only target acceleration carries into flight; adding an
            // angular muzzle term here would double-count GHPC's launch code.
            // The line the intercept is solved along starts at the muzzle, not at the fire control's
            // reference point. The two are a metre or two apart: nothing against two kilometres, and a
            // tenth of the lead at three hundred metres, and it is the muzzle the round leaves from.
            Vector3 muzzle = MuzzlePosition();
            Vector3 wanted = fcs.ReferenceTransform == null
                ? muzzle + direction.normalized * fcs.CurrentRange
                : fcs.ReferenceTransform.position + direction.normalized * fcs.CurrentRange;
            Vector3 line = wanted - muzzle;

            // A degenerate line is a valid state the module can solve, not a failure to solve it:
            // the early returns above hand the fire control back to the game's own lead, and this
            // solve is perfectly defined when the target sits on the muzzle. Returning true here
            // keeps the world lead owning the fire control with a zero offset it is entitled to.
            if (line.sqrMagnitude < 1f)
            {
                world_lead_active = true;
                return true;
            }

            Vector3 relativeVelocity = RelativeLeadVelocity(inherited);
            predictor_solution = CrowsPredictor.SolveRelative(
                MathVector(line),
                MathVector(relativeVelocity),
                // The round inherits velocity at launch, not the hull's future acceleration.
                MathVector(LeadAcceleration()),
                LeadConfidence(), TableFlightTime, LeadMotionMixture(), LAUNCH_DELAY_SECONDS);
            if (!predictor_solution.Valid)
            {
                // Invalid/unavailable table: retain the existing flat-time fallback, without acceleration.
                offset = -Vector3.ProjectOnPlane(relativeVelocity, direction.normalized) * flight;
            }
            else offset = -UnityVector(predictor_solution.TransverseDisplacement);

            // The multi-shot correction, added after the lead because it is not the target's motion:
            // what the last few rounds landed away from where they were aimed, as an angular hold-off
            // the whole solution is displaced by. Written in the same right/up basis the game writes
            // its own lead in, so the two cannot disagree about which way is right; a round that landed
            // right of the target puts a negative right term here, which moves the aim left.
            if (latest_target_velocity.sqrMagnitude >= TARGET_VELOCITY_FLOOR * TARGET_VELOCITY_FLOOR
                && fcs.ReferenceTransform != null && fcs.CurrentRange > 1f
                && (Math.Abs(bias.Right(fcs.CurrentRange)) > 1e-4 || Math.Abs(bias.Up(fcs.CurrentRange)) > 1e-4))
            {
                float radians = Mathf.Deg2Rad * fcs.CurrentRange;
                offset += fcs.ReferenceTransform.right * (float)(bias.Right(fcs.CurrentRange) * radians);
                offset += fcs.ReferenceTransform.up * (float)(bias.Up(fcs.CurrentRange) * radians);
            }
            world_lead_active = true;
            return !(float.IsNaN(offset.sqrMagnitude) || float.IsInfinity(offset.sqrMagnitude));
        }

        private void UpdateAimRate()
        {
            CrowsShotFeedback.Tick();
            ConfigureBallistics();
            Vector3 aim = fcs.AimWorldVector;
            float dt = Time.deltaTime;

            if (aim_rate_valid && dt > 1e-5f && aim.sqrMagnitude > 0.25f && aim_was.sqrMagnitude > 0.25f)
            {
                float swept = Vector3.Angle(aim_was, aim) / dt;

                // Smoothed, so a single frame of mouse input cannot open the gate by itself.
                aim_rate = Mathf.Lerp(aim_rate, swept, 1f - Mathf.Exp(-AIM_SMOOTHING * dt));
            }
            else
            {
                aim_rate = 0f;
                aim_rate_valid = true;
            }

            aim_was = aim;
        }

        /// <summary>Whether the sight is being swung too fast for the crosshair to mean any one target.</summary>
        private bool Sweeping()
        {
            return aim_rate_valid && aim_rate > LOCK_MAX_SWEEP;
        }

        /// <summary>
        /// Whether a unit is something the player could be shooting at, as opposed to something that
        /// merely exists in the scene. The unit list is everything active in the mission, which is not
        /// the same as everything in the fight: it includes the vehicles this mod itself pulls in to
        /// borrow parts from, which are real, active Vehicle instances parked wherever the loader left
        /// them.
        ///
        /// A wreck counts. A knocked-out vehicle is worth locking - it marks a vehicle that is out of
        /// the fight, and the player may be watching it for the crew that got out - so being
        /// neutralised, abandoned or destroyed is not a reason to refuse it, and neither is the game
        /// having stopped calling it targetable.
        /// </summary>
        internal bool IsEngageable(Unit unit)
        {
            if (unit == null || unit == ours)
            {
                return false;
            }

            if (CROWS.IsDonor(unit))
            {
                return false;
            }

            // Friend or foe is the game's own line, and it is drawn on allegiance: a unit flying the
            // same flag is not a target. That is the rule the game's own TargetScanner uses for the AI
            // (`allegiance != mine && allegiance != Neutral`), and the box, the lock and the key all
            // come through here, so this is the one place it has to be said.
            //
            // Neutrals stay lockable: they are not on our side, and the player is allowed to shoot at
            // them. Only the player's own faction is refused.
            //
            // FIX: In missions where faction can be selected, ours.Allegiance might be stale.
            // Use the current player unit's allegiance instead for reliable faction detection.
            Faction ourAllegiance = Faction.Neutral;
            if (ours != null)
            {
                // Try to get current player unit allegiance first (handles faction-select missions)
                Unit playerUnit = PlayerInput.Instance?.CurrentPlayerUnit;
                if (playerUnit != null)
                {
                    ourAllegiance = playerUnit.Allegiance;
                }
                else
                {
                    // Fallback to vehicle's allegiance
                    ourAllegiance = ours.Allegiance;
                }
                
                // Don't lock friendly units (same allegiance, excluding Neutral)
                if (ourAllegiance != Faction.Neutral && unit.Allegiance == ourAllegiance)
                {
                    return false;
                }
            }

            // A live target has to be something the game will let us shoot at; a wreck does not.
            if (!unit.Neutralized && !unit.Targetable)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Keeps the passive box on whatever the sight is pointed at - the box the player sees at all
        /// times, with no tracking, no lead and no impact marker. Only a lock starts driving the vehicle
        /// and adds the marker and the leader line.
        ///
        /// It is sampled a few times a second rather than every frame, and the expensive part - the walk
        /// over the unit's renderers - only runs when the sight settles on a *different* vehicle.
        /// </summary>

        /// <summary>
        /// Whether a point is inside the recognition window around the crosshair. Nothing outside it is
        /// taken as a target, whatever else is visible in the picture - the sight recognises what the
        /// crosshair is on, not what happens to be somewhere in the field.
        /// </summary>
        private bool InRecognitionWindow(Vector3 world)
        {
            if (fcs == null || fcs.ReferenceTransform == null)
            {
                return false;
            }

            Vector3 forward = fcs.AimWorldVector;

            if (forward.sqrMagnitude < 0.25f)
            {
                return false;
            }

            // The widest magnification recognises nothing, so the window does not exist there.
            if (AtWidestZoom())
            {
                return false;
            }

            return Vector3.Angle(forward, world - fcs.ReferenceTransform.position)
                <= RECOGNISE_HALF_ANGLE_DEGREES;
        }

        private void UpdateDesignation()
        {
            designate_timer -= Time.deltaTime;

            if (designate_timer > 0f)
            {
                return;
            }

            designate_timer = DESIGNATE_INTERVAL;

            // The widest magnification is the whole field with the box not even drawn on it, so nothing
            // is recognised there at all.
            if (AtWidestZoom())
            {
                designated = null;
                return;
            }

            Transform reference = fcs.ReferenceTransform;

            if (reference == null)
            {
                designated = null;
                return;
            }

            Vector3 origin = reference.position;
            Vector3 forward = fcs.AimWorldVector;

            if (forward.sqrMagnitude < 0.25f)
            {
                designated = null;
                return;
            }

            // While the sight is being swung across the field there is no one target the crosshair is
            // on, so no box: otherwise every vehicle the sweep passes over gets framed.
            if (Sweeping())
            {
                designated = null;
                return;
            }

            // Keep what is already framed: re-picking every sample would make the box flicker between
            // two vehicles whenever the crosshair sits between them.
            if (StillFramed(designated, origin))
            {
                return;
            }

            SceneUnitsManager manager = SceneUnitsManager.Instance;

            if (manager == null)
            {
                designated = null;
                return;
            }

            Unit best = null;
            float best_near = DESIGNATE_NEAR_FRACTION;
            Vector3 best_centre = Vector3.zero;
            Vector3 best_half = Vector3.zero;

            IReadOnlyList<Unit> units = manager.AllUnitsInScene;

            for (int i = 0; i < units.Count; i++)
            {
                Unit unit = units[i];

                if (!IsEngageable(unit))
                {
                    continue;
                }

                Vector3 probe = unit.Center != null ? unit.Center.position : unit.transform.position;
                Vector3 to_probe = probe - origin;

                if (to_probe.sqrMagnitude > MAX_LOCK_RANGE * MAX_LOCK_RANGE)
                {
                    continue;
                }

                if (!NearCrosshair(probe, DESIGNATE_NEAR_FRACTION))
                {
                    continue;
                }

                Vector3 centre;
                Vector3 local_centre;
                Vector3 local_half;

                if (!Measure(unit, out centre, out local_centre, out local_half))
                {
                    continue;
                }

                // Nothing of it is switched on, so it is not on screen whatever its transform says;
                // boxing it draws a bracket around thin air.
                if (!measure_found_parts)
                {
                    continue;
                }

                // Judged on the box centre, which is what actually gets drawn, and by how near it is
                // to the crosshair on screen: the nearest target wins, so pointing at one vehicle in a
                // crowd does not frame the one behind it.
                // Only what the crosshair is on.
                if (!InRecognitionWindow(centre))
                {
                    continue;
                }

                float near = CrosshairDistance(centre);

                if (near > best_near)
                {
                    continue;
                }

                if (!CandidateVisible(unit, centre, local_half))
                {
                    continue;
                }

                best = unit;
                best_near = near;
                best_centre = local_centre;
                best_half = local_half;
            }

            designated = best;
            designated_centre_local = best_centre;
            designated_half_local = best_half;
        }

        /// <summary>
        /// Whether a unit is still near enough to the crosshair to keep its box, and within range.
        /// Nothing else needs re-checking: the box is stored in the vehicle's own space, so it stays
        /// correct for as long as it is the same vehicle. The keep radius is wider than the acquire
        /// one, so a box on the fringe cannot flicker on and off.
        /// </summary>
        private bool StillFramed(Unit unit, Vector3 origin)
        {
            if (unit == null || unit.Destroyed || unit == ours)
            {
                return false;
            }

            Vector3 probe = unit.Center != null ? unit.Center.position : unit.transform.position;
            Vector3 to = probe - origin;

            if (to.sqrMagnitude > MAX_LOCK_RANGE * MAX_LOCK_RANGE)
            {
                return false;
            }

            return NearCrosshair(probe, DESIGNATE_KEEP_FRACTION);
        }

        /// <summary>
        /// The camera the player's picture is actually drawn with.
        ///
        /// Normally that is the main camera, and every marker placed with it lands where it should. In the
        /// thermal sight it is not: the game renders the whole thermal view through the FLIR camera's own
        /// render texture and stretches it over the screen, and that camera has a field of view of its own -
        /// matched to the main camera's *horizontal* one, so its vertical one differs whenever the render
        /// texture's aspect differs from the screen's. Projecting a world point with the main camera in that
        /// mode is therefore off by the difference, in the vertical direction only, and by an amount that
        /// grows with the distance from the crosshair - a box that creeps down the picture as the target
        /// moves away from the centre, which is exactly what a locked box did in thermal.
        /// </summary>
        private static Camera PictureCamera()
        {
            Camera main = CameraManager.MainCam;

            if (main == null)
            {
                return null;
            }

            CameraSlot active = CameraSlot.ActiveInstance;

            if (active == null || (active.VisionType != NightVisionType.Thermal && !active.DoMechanicalFLIR))
            {
                return main;
            }

            FLIRCamera flir = FLIRCamera.Instance;
            Camera thermal = flir == null ? null : flir.GetComponent<Camera>();

            return thermal == null ? main : thermal;
        }

        /// <summary>
        /// A world point in screen pixels, in the frame the picture is drawn in.
        ///
        /// The main camera's projection is the one used - it is the camera the screen actually is - and the
        /// picture camera's own field of view is then accounted for by scaling the point about the centre of
        /// the screen, axis by axis. The two cameras share a transform, so there is nothing else to correct:
        /// only the number of degrees each pixel spans.
        /// </summary>
        private static bool PicturePoint(Vector3 world, out Vector3 screen)
        {
            Camera main = CameraManager.MainCam;

            screen = Vector3.zero;

            if (main == null)
            {
                return false;
            }

            Vector3 point = main.WorldToScreenPoint(world);

            if (point.z <= 0f)
            {
                screen = point;
                return false;
            }

            Camera picture = PictureCamera();

            if (picture != null && picture != main)
            {
                float scale_x = HalfTangent(main, true) / HalfTangent(picture, true);
                float scale_y = HalfTangent(main, false) / HalfTangent(picture, false);

                point.x = Screen.width * 0.5f + (point.x - Screen.width * 0.5f) * scale_x;
                point.y = Screen.height * 0.5f + (point.y - Screen.height * 0.5f) * scale_y;
            }

            screen = point;

            return true;
        }

        /// <summary>
        /// The tangent of half of a camera's field of view, on the given axis: the scale that turns an
        /// angle off the optical axis into a fraction of that axis on screen. Vertical comes straight off
        /// the camera; horizontal is the vertical widened by the aspect, which is what Unity's projection
        /// does and what the game itself does when it lines the thermal camera up with the main one.
        /// </summary>
        private static float HalfTangent(Camera cam, bool horizontal)
        {
            if (cam == null)
            {
                return 1f;
            }

            float tangent = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);

            if (tangent <= 1e-6f)
            {
                return 1e-6f;
            }

            return horizontal ? tangent * Mathf.Max(cam.aspect, 1e-4f) : tangent;
        }

        /// <summary>
        /// How far a world point is from the middle of the sight picture, as a fraction of the screen's
        /// shorter side. Distance on screen rather than an angle, so "near the crosshair" means the same
        /// thing at every magnification.
        /// </summary>
        private static float CrosshairDistance(Vector3 world)
        {
            Vector3 point;

            if (!PicturePoint(world, out point))
            {
                return float.MaxValue;
            }

            float side = Mathf.Min(Screen.width, Screen.height);
            float dx = (point.x - Screen.width * 0.5f) / side;
            float dy = (point.y - Screen.height * 0.5f) / side;

            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Whether a world point is within the given fraction of the screen's shorter side.</summary>
        private static bool NearCrosshair(Vector3 world, float fraction)
        {
            return CrosshairDistance(world) <= fraction;
        }

        /// <summary>Whether a world point projects inside the sight picture, with a little slack.</summary>
        private static bool InSightPicture(Vector3 world, float margin)
        {
            if (CameraManager.MainCam == null)
            {
                return true;
            }

            Vector3 point;

            if (!PicturePoint(world, out point))
            {
                return false;
            }

            return point.x >= -margin && point.x <= Screen.width + margin
                && point.y >= -margin && point.y <= Screen.height + margin;
        }

        /// <summary>
        /// Fuses the measured line-of-sight angular rate with the chassis velocity solution. The
        /// visual rate is only a transverse correction, so it remains useful when a unit reports a
        /// stale or quantised velocity while never replacing a coherent physics sample outright.
        /// </summary>
        private Vector3 FuseLineOfSight(Vector3 to, Vector3 relative)
        {
            // Solver velocity already measures actual vehicle motion. The sight point
            // can move as the tracker/aim offset settles even on a parked target; that
            // is not additional evidence of target velocity.
            if (target_velocity_from_solver || relative.sqrMagnitude < TARGET_VELOCITY_FLOOR * TARGET_VELOCITY_FLOOR)
            {
                fused_relative_velocity = relative;
                fused_relative_velocity_valid = true;
                los_rate_valid = false;
                return relative;
            }
            float length = to.magnitude;
            float now = Time.time;
            if (length < 1f || float.IsNaN(length) || float.IsInfinity(length))
            {
                fused_relative_velocity_valid = false;
                los_rate_valid = false;
                return relative;
            }

            Vector3 unit = to / length;
            if (!los_rate_valid)
            {
                last_los_unit = unit;
                los_sample_time = now;
                los_angular_velocity = Vector3.zero;
                los_rate_valid = true;
                fused_relative_velocity_valid = false;
                return relative;
            }

            float dt = now - los_sample_time;
            los_sample_time = now;
            Vector3 delta = unit - last_los_unit;
            last_los_unit = unit;
            if (dt <= 1e-4f || dt > 0.25f || delta.sqrMagnitude > 0.25f)
            {
                fused_relative_velocity_valid = false;
                return relative;
            }

            Vector3 measured = Vector3.Cross(last_los_unit - delta, unit) / dt;
            if (measured.sqrMagnitude > 9f || float.IsNaN(measured.sqrMagnitude)
                || float.IsInfinity(measured.sqrMagnitude))
            {
                fused_relative_velocity_valid = false;
                return relative;
            }

            float blend = 1f - Mathf.Exp(-10f * dt);
            los_angular_velocity = Vector3.Lerp(los_angular_velocity, measured, blend);
            Vector3 visual = Vector3.Cross(los_angular_velocity, to);
            visual = Vector3.ProjectOnPlane(visual, unit);
            Vector3 physics = Vector3.ProjectOnPlane(relative, unit);
            float discrepancy = (visual - physics).magnitude;
            float weight = physics.sqrMagnitude < 0.25f
                ? 0.35f
                : Mathf.Clamp01(0.22f - discrepancy * 0.0025f);
            fused_relative_velocity = relative + (visual - physics) * weight;
            fused_relative_velocity_valid = true;
            return fused_relative_velocity;
        }

        /// <summary>
        /// Tells the fire control how fast the target is really crossing, instead of letting it infer
        /// that from how fast the aim is being rotated.
        ///
        /// The inference cannot be trusted here: the fire control works its lead out from
        /// <c>_lastAngleAdjust</c>, "how far was the aim rotated this frame", and with a machine doing
        /// the rotating that number is whatever the tracker applied. So the rate is measured directly
        /// from the target's own motion and written past the buffer (see CrowsLeadLock).
        ///
        /// Target velocity is measured in world space, then platform velocity is subtracted
        /// because the projectile inherits it. ArmLead disables the game's inertial term to
        /// avoid compensating twice. Convert the relative transverse velocity into rates in
        /// the reference right/up basis using the current FCS range. Adaptive filters retain
        /// steady tracking while responding faster to acceleration and braking.
        /// </summary>
        private void ReportTargetRate(Vector3 to, Vector3 up, Vector3 right)
        {
            Vector2 rate = Vector2.zero;

            float range = to.magnitude;

            // Target velocity, acceleration and warmup are published only by FixedUpdate.

            // ------------------------------------------------------------------ the lead itself
            //
            // One formula, both velocities: what the gun has to lead by is the target's motion
            // *relative to the station*, because the round leaves with the station's own velocity
            // already added to it by the weapon (WeaponSystem adds its parent rigidbody's). A target
            // standing still and a station rolling past it need exactly as much lead as the other way
            // round, and this is where that comes from - target velocity less platform velocity, taken
            // across the line of sight, over the range, as an angular rate.
            //
            // The fire control's own inertial term is switched off at arming (see ArmLead) so this rate
            // is the only author of the lead; the two would otherwise be the same term applied twice.
            Vector3 unit_to = range > 1f ? to / range : Vector3.forward;
            Vector3 relative = LeadVelocity() - PlatformLeadVelocity();
            relative = FuseLineOfSight(to, relative);
            Vector3 across = relative - unit_to * Vector3.Dot(relative, unit_to);

            // Invert GHPC's actual lead formula, which applies rates along the
            // reference's right/up axes (not the instantaneous sight's tangent axes).
            // Use CurrentRange because that is the scale used by GetLeadWorldOffset.
            float solutionRange = fcs.CurrentRange;
            if (rate_reference_valid && !lead_suspended && lock_warmup_complete
                && velocity_valid && range > 1f && solutionRange > 1f
                && across.sqrMagnitude > TARGET_VELOCITY_FLOOR * TARGET_VELOCITY_FLOOR)
            {
                rate = new Vector2(Vector3.Dot(across, right), -Vector3.Dot(across, up))
                    / solutionRange * Mathf.Rad2Deg;
            }
            else
            {
                // Do not let an old target velocity or platform motion leak through
                // during warmup or slewing; a zero input alone would retain filter history.
                lead_rate = Vector2.zero;
                lead_rate_valid = false;
            }

            // Smoothed here, not by the fire control's buffer: CrowsLeadLock writes this rate into the
            // fire control after it has finished its own averaging, so the smoothing the buffer would
            // have provided has to happen on this side.
            if (!lead_rate_valid)
            {
                lead_rate = rate;
                lead_rate_valid = true;
            }

            // The second filter used to add another 125 ms even after velocity
            // responded. Open it when its lag represents a significant miss at impact.
            float rateMiss = (rate - lead_rate).magnitude * Mathf.Deg2Rad
                * Mathf.Max(0f, solutionRange) * FlightTime(Mathf.Max(1f, solutionRange));
            float rateResponse = Mathf.Lerp(LEAD_RATE_SMOOTHING, 40f,
                Mathf.Clamp01((rateMiss - 0.15f) / 0.85f));
            float k = 1f - Mathf.Exp(-rateResponse * Time.deltaTime);
            Vector2 wanted = Vector2.Lerp(lead_rate, rate, k);

            // And a ceiling on how fast the rate itself may change. The filter above still passes a step
            // through in proportion to its size; this bounds whatever is left, so a dropped frame or a
            // re-measured box arrives at the aim as a push rather than a jump.
            lead_rate = Vector2.MoveTowards(lead_rate, wanted, LEAD_RATE_SLEW * Time.deltaTime);

            // Write the smoothed rate, not the raw calculated rate
            WriteAimRate(lead_rate);

            // What the round will miss by if nothing leads it, and what the computer is leading by
            // now. See MeasureLead - these two are the whole of the readout and of its self-check.
            MeasureLead(range, across);

            rate_reference_aim = fcs.AimWorldVector;
            rate_reference_valid = true;
        }

        /// <summary>
        /// The lead, as two angles at the target: the one the solution needs and the one it has.
        ///
        /// The first is arithmetic on numbers this module already reads - the relative velocity across
        /// the line of sight, and how long the round is in the air - so it cannot fail to answer, and it
        /// is what the status line goes by. The second is the fire control's own lead offset, read
        /// straight off its solution by asking it for the weapon vector with the lead and without it;
        /// that is the number that says whether the first one is reaching the gun at all.
        ///
        /// A gap between them is reported once. It is the difference between "the lead is dialled in and
        /// the line does not say so" and "the lead is not being applied", and there is no way to tell
        /// those apart from outside - which is where this feature has been stuck.
        /// </summary>
        private void MeasureLead(float range, Vector3 across)
        {
            intended_lead_deg = 0f;
            applied_lead_deg = 0f;

            if (range <= 1f || fcs == null)
            {
                return;
            }

            // --- what the solution needs
            float flight = FlightTime(range);

            if (predictor_solution.Valid)
            {
                // Prefer the answer the fire control was actually handed - solved this frame or the
                // last, but with the same model, the same horizon and the same launch delay as the
                // offset the gun is carrying. The constant-velocity estimate below is the fallback for
                // when no solution was produced at all, and differs from the applied lead exactly
                // where the mixture does.
                intended_lead_deg = Mathf.Atan2(
                    UnityVector(predictor_solution.TransverseDisplacement).magnitude, range) * Mathf.Rad2Deg;
            }
            else if (flight > 0f)
            {
                Vector3 displacement = across * flight;

                intended_lead_deg = Mathf.Atan2(displacement.magnitude, range) * Mathf.Rad2Deg;
            }

            // --- what it has
            try
            {
                if (fcs.CurrentRange > 1f)
                {
                    Vector3 with_lead = fcs.DistanceVectorToImpactPoint(true);
                    Vector3 without_lead = fcs.DistanceVectorToImpactPoint(false);

                    if (without_lead.sqrMagnitude > 1f)
                    {
                        applied_lead_deg = Mathf.Atan2((with_lead - without_lead).magnitude, range)
                            * Mathf.Rad2Deg;
                    }
                }
            }
            catch (Exception)
            {
                // a computer that will not answer reads as applying nothing, and the check below says so
            }

            if (lead_check_reported || intended_lead_deg < LEAD_CHECK_DEGREES)
            {
                return;
            }

            if (applied_lead_deg >= intended_lead_deg * 0.34f)
            {
                lead_check_reported = true;
                return;
            }

            lead_check_reported = true;

        }

        /// <summary>Whether the one-shot lead check has had its say.</summary>
        private bool lead_check_reported;

        /// <summary>The lead the solution needs and the lead it has, in degrees at the target; see MeasureLead.</summary>
        private float intended_lead_deg;
        private float applied_lead_deg;

        /// <summary>
        /// The round's time of flight to a range, from the game's own ballistic computer - the same
        /// table the fire control superelevates and leads from, so the readout is measuring the
        /// solution the gun is actually firing rather than a second opinion about it. The flat fallback
        /// is degraded constant-velocity lead only; it omits drag and has no accuracy guarantee.
        /// The bounded predictor iteration requires the real table instead (TableFlightTime).
        /// </summary>
        // Iteration must use a real table, never silently mix table and flat estimates.
        private double TableFlightTime(double range)
        {
            try
            {
                // The fire control's own computer and its own round, not the repository's. The table it
                // leads by is that instance's; another mod can rebuild it through
                // BallisticComputer.SetAmmoType(ammo, muzzleVelocityOverride, elevationOverride)
                // (CustomBallisticComputerData does exactly that) or leave it on the last round when the
                // computer is inoperative, and neither of those is visible through the repository or
                // through CurrentAmmoType. Asking the fire control what flying time it believes in is
                // the only way this module and the lead it is writing cannot disagree.
                var computer = Reflect.Get<BallisticComputerRepository>(fcs, "_bc");
                AmmoType ammo = Reflect.Get<AmmoType>(fcs, "_bcAmmo");

                if (computer == null || ammo == null)
                {
                    return double.NaN;
                }

                double game = computer.GetFlightTime(ammo, (float)range);
                double fine;

                // The replica is offered only where it agrees with the game's own answer. The game's table
                // quantises every sample's flight time to its 20 ms integration step and interpolates
                // between samples hundreds of metres apart at these ranges; the replica integrates the same
                // model at the crossing itself. Where they disagree by more than that sawtooth and its
                // margin - a round whose mass or drag this does not know, a table rebuilt at another
                // temperature, a custom ballistic computer - the game's table is what is used.
                if (CrowsBallistics.FlightTime(range, out fine) && CrowsPredictor.Finite(game) && game > 0
                    && Math.Abs(fine - game) < 0.05)
                {
                    return fine;
                }

                return game;
            }
            catch (Exception) { return double.NaN; }
        }

        private float FlightTime(float range)
        {
            try
            {
                var computer = fcs == null ? null : Reflect.Get<BallisticComputerRepository>(fcs, "_bc");
                AmmoType ammo = fcs == null ? null : (Reflect.Get<AmmoType>(fcs, "_bcAmmo") ?? fcs.CurrentAmmoType);

                if (computer != null && ammo != null)
                {
                    float flight = computer.GetFlightTime(ammo, range);

                    if (flight > 0f && !float.IsNaN(flight) && !float.IsInfinity(flight))
                    {
                        return flight;
                    }
                }

                if (ammo != null && ammo.MuzzleVelocity > 1f)
                {
                    return range / ammo.MuzzleVelocity;
                }
            }
            catch (Exception)
            {
                // fall through to the flat approximation
            }

            return 0f;
        }

        /// <summary>
        /// The platform's velocity for the lead: the chassis's own reading, unfiltered, which is what
        /// the fire control would have used when it carried this term itself. The smoothed copy is for
        /// the readout and the moving flag - a lead has no business lagging half a second behind a
        /// vehicle that is accelerating.
        /// </summary>
        private Vector3 PlatformLeadVelocity()
        {
            return platform_velocity_raw_valid ? platform_velocity_raw : Vector3.zero;
        }

        /// <summary>
        /// The target's own velocity, in the ground frame, in metres per second; zero when nothing about
        /// it is moving. See ReportTargetRate for why the ground frame is the one that belongs in the
        /// lead.
        ///
        /// The solver's velocity is the good one - smooth, exact, and taken from the physics rather than
        /// inferred from a transform - and every vehicle and aircraft has it through <c>Unit.Velocity</c>.
        /// A unit that does not (infantry on foot, a towed gun, an emplacement) reports zero there, and
        /// for those the target's own transform is differenced instead, on the physics clock: the
        /// transform only advances when the physics stepped, so the elapsed <c>fixedTime</c> is the only
        /// honest divisor. A measurement taken on the frame clock would report nothing on the frames that
        /// had no tick and double on the ones that had two.
        /// </summary>
        private Vector3 TargetWorldVelocity()
        {
            if (target == null || target.transform == null) return Vector3.zero;
            try
            {
                if (target.Chassis != null)
                {
                    // A valid solver zero means STOPPED, not 'try transform jitter'.
                    velocity_sample_valid = false;
                    target_velocity_from_solver = true;
                    return target.Chassis.Velocity;
                }
            }
            catch (Exception)
            {
                // Unsupported solver: sample the transform on the physical clock.
            }
            target_velocity_from_solver = false;
            return SampledVelocity();
        }

        /// <summary>
        /// The target's velocity from its own transform, differenced across the physics clock; see
        /// TargetWorldVelocity. The last good sample stands between physics ticks, because the transform
        /// has not moved either.
        /// </summary>
        private Vector3 SampledVelocity()
        {
            if (target == null || target.transform == null)
            {
                return Vector3.zero;
            }

            Vector3 position = target.transform.position;
            float now = Time.fixedTime;

            if (!velocity_sample_valid)
            {
                velocity_sample_position = position;
                velocity_sample_time = now;
                velocity_sample_valid = true;
                sampled_velocity = Vector3.zero;

                return sampled_velocity;
            }

            float dt = now - velocity_sample_time;

            if (dt > 1e-4f)
            {
                Vector3 sampled = (position - velocity_sample_position) / dt;

                velocity_sample_position = position;
                velocity_sample_time = now;

                float speed = sampled.magnitude;

                if (speed > TARGET_VELOCITY_CEILING)
                {
                    // Reject a teleport rather than turning it into a plausible 60 m/s target. This is a
                    // declaration that the tick produced no measurement at all, not a measurement of
                    // zero: return the last good velocity and make the next tick re-baseline, so the
                    // displacement across the teleport is never divided by its interval. A teleport is a
                    // very large velocity, and writing one here - as NaN, as the game's own value, or as
                    // zero - would be believed either way.
                    velocity_sample_position = position;
                    velocity_sample_time = now;
                    velocity_sample_valid = false;

                    return sampled_velocity;
                }

                sampled_velocity = sampled;
            }

            return sampled_velocity.magnitude < TARGET_VELOCITY_FLOOR
                ? Vector3.zero
                : sampled_velocity;
        }

        /// <summary>
        /// Writes the traverse rate the fire control should believe it is seeing, in degrees per second.
        /// It divides what it finds here by the frame time and averages it over its traverse buffer, so
        /// this is the value that goes into that buffer.
        /// </summary>
        private void WriteAimRate(Vector2 rate)
        {
            if (fcs == null)
            {
                return;
            }

            try
            {
                Reflect.Set<UnityEngine.Vector2>(fcs, "_lastAngleAdjust", rate * Time.deltaTime);
            }
            catch (Exception)
            {
                // without it the lead is only as good as the fire control's own inference
            }
        }

        /// <summary>The mount's current traverse / elevation speed limit, in degrees per second.</summary>
        private float RateOf(int index)
        {
            try
            {
                if (fcs != null && fcs.Mounts != null && fcs.Mounts.Length > index && fcs.Mounts[index] != null)
                {
                    float speed = fcs.Mounts[index].CurrentMaxSpeed;

                    if (speed > 0.1f)
                    {
                        return speed;
                    }
                }
            }
            catch (Exception)
            {
                // fall through to the constant
            }

            return FALLBACK_RATE;
        }

    }
}
