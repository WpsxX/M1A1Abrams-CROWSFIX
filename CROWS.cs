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
        private const string ASSET_FOLDER = "m1a1crowsfix_assets";
        // The donor station is authored at 0.75 scale; the Mk 19 rebuild is 1:1. LoadModel picks
        // whichever belongs to the weapon the player configured.
        private const float MODEL_SCALE = 0.75f;
        private const string CROWS_NAME = "M153 CROWS II - M2HB";
        private const string CROWS_MK19_NAME = "M153 CROWS II - Mk 19";

        // Eye point of the Mk 19's own optics, in GUN's local frame: the crowmk19 prefab's
        // optics/lens meshes were measured against the trunnion (GUN origin) and land here.
        // The donor M2's sight sat at z = 0.85, which is 0.35 m past the Mk 19 muzzle (0.50).
        private static readonly Vector3 MK19_SIGHT_POS = new Vector3(0.028f, -0.186f, 0.282f);

        // Where the donor station puts its sight: the value the original prefab was built around.
        private static readonly Vector3 M2_SIGHT_POS = new Vector3(0f, -0.085f, 0.85f);

        // The station's elevation stops: the mount and both sights share them so the sight
        // cannot travel past where the launcher itself stops. The Mk19 needs a lot of depression
        // for targets on a slope below the vehicle, and the mount is the only thing that can give
        // it: the sight rides on the gun, so the sight's own limits must allow the same travel.
        private static readonly Vector2 ELEVATION_LIMITS = new Vector2(-30f, 60f);

        // The game's sight FoVs map to magnification as roughly 30 degrees = 1x, which is what the
        // borrowed Bradley optics use (8 / 2.5 = its 4x / 12x). So: 1x = 30, 5x = 6, 12x = 2.5.
        private const float DAY_FOV_1X = 30f;
        private const float DAY_FOV_5X = 6f;
        private const float DAY_FOV_10X = 3f;
        private const float DAY_FOV_15X = 2f;

        /// <summary>
        /// The station's rated slew, in degrees a second, and the single source of truth for it: the two
        /// aimable platforms are built with these numbers and both are re-asserted onto them every frame
        /// (see <see cref="CrowsAimSlave.PinRatedSpeeds"/>).
        ///
        /// Traverse and elevation are separate numbers because the station publishes two - 90 and 50 -
        /// rather than the one rate for both axes it used to be given. They are what the aiming chain is
        /// built on: the fire control reads them for its own per-frame aim limit, the tracker reads them
        /// for the slew onto a target, and the player's own manual offset walks at them. None of that has
        /// anything to do with magnification, and saying so every frame is cheaper than discovering from
        /// the far end of a slow traverse that something wrote over them.
        /// </summary>
        internal const float TRAVERSE_SPEED_DEGREES_PER_SECOND = 90f;
        internal const float ELEVATION_SPEED_DEGREES_PER_SECOND = 50f;

        /// <summary>
        /// The sight colour, and the single source of truth for it: the reticle is drawn with it and it
        /// tints the sight HUD, which uses <see cref="SightUiColor"/> so the two cannot drift.
        /// </summary>
        internal static readonly RGB SIGHT_COLOR = new RGB(3f, 0f, 0f, true);

        /// <summary>The sight colour as a plain UI colour (the HDR value clamped for screen UI).</summary>
        internal static Color SightUiColor
        {
            get
            {
                RGB colour = SIGHT_COLOR;
                return new Color(Mathf.Clamp01(colour.r), Mathf.Clamp01(colour.g), Mathf.Clamp01(colour.b), 1f);
            }
        }

        private static bool initialised;
        private static bool ready;
        private static readonly HashSet<string> setup_failures = new HashSet<string>();

        private static GameObject crows_prefab;

        private static GameObject m2_browning;
        private static GameObject m2_gun_sight;
        private static GameObject m2_fcs;
        private static GameObject box_canvas;

        private static AmmoCodexScriptable ammo_codex_m8;


        private static AmmoType.AmmoClip m8_belt;


        private static ReticleSO reticle_so;

        /// <summary>
        /// Reticle pieces that change with magnification, with their original geometry, so the reticle
        /// is rescaled from base values rather than accumulating rounding errors.
        /// </summary>
        private class ReticlePart
        {
            public ReticleTree.Line line;
            public Vector2 position;
            public float length;
            public float thickness;
        }

        private static readonly List<ReticlePart> reticle_parts = new List<ReticlePart>();

        /// <summary>
        /// Every reticle mesh in the mission, paired with the vehicle it belongs to.
        ///
        /// Each CROWS carries its own pair of meshes - one for the day sight, one for the thermal -
        /// and every one of them is built from the same tree, so every one of them has to be rebuilt
        /// when the tree is rescaled. Remembering the pair from the last conversion only leaves the
        /// other sights, the player's included, drawing the geometry that was baked when they were
        /// converted; and the tree holds no scale at all yet in the first mission of a session, so
        /// those sights fall back to a reticle that grows and shrinks with the magnification.
        /// </summary>
        private static readonly List<ReticleMesh> reticle_meshes = new List<ReticleMesh>();

        private static float reticle_scale = 1f;


        private static readonly List<List<Vector3>> borders = new List<List<Vector3>>() {
            new List<Vector3> {new Vector3(0f, -410f, 0f), new Vector3(0f, 0f, 180f)},
            new List<Vector3> {new Vector3(0f, 410f, 0f), new Vector3(0f, 0f, 0f)},
            new List<Vector3> {new Vector3(480f, 0f, 0f), new Vector3(0f, 0f, 270f)},
            new List<Vector3> {new Vector3(-480f, 0f, 0f), new Vector3(0f, 0f, 90f)}
        };

        /// <summary>
        /// Keeps the CROWS computer from thinking it is "the" active weapon and dropping its stabilizer
        /// whenever the main gun is selected.
        /// </summary>
        public class StaticWhenNotInUse : MonoBehaviour
        {
            public FireControlSystem fcs;

            void Update()
            {
                // The in_use test below null-checks both active and active.FCS; this field was the one
                // dereference left unguarded. A destroyed host would make SetStabsActive walk Mounts[]
                // and throw, so bail out rather than drive a station that is not there any more. Unity's
                // overloaded == reports a destroyed object as null, so that case is covered here too.
                if (fcs == null) return;

                // Do not call SetStabsActive here. That used to force stabilization on every
                // frame while CROWS was selected, so the player's stabilization toggle could
                // never remain off. SetStabsActive is owned by the game's input/crew code; this
                // component only mirrors its current state onto the cloned platforms.
                AimablePlatform[] mounts = fcs.Mounts;
                if (mounts == null)
                {
                    return;
                }

                bool active = fcs.StabsActive;
                for (int i = 0; i < mounts.Length; i++)
                {
                    if (mounts[i] != null)
                    {
                        mounts[i].StabilizerActive = active;
                    }
                }
            }
        }

        /// <summary>
        /// Keeps the CROWS model on the aiming reference, with the stabiliser left on.
        ///
        /// AimablePlatform chases the world-space aim through a rate limit twice over - the virtual
        /// rotation turns towards the aim, then the transform turns towards that - so the model trails
        /// the aim by two servo steps and never quite agrees with the sight. This mount only traverses,
        /// so it is driven straight onto the aim's azimuth instead, which costs nothing because the aim
        /// does not move when the vehicle does. The cradle keeps its elevation, so the superelevation
        /// survives, and loses the yaw and roll AimablePlatform's own error correction would clear.
        ///
        /// Runs *between* the sights and the thermal camera, which is what the execution order is for: the
        /// optics align their slots in their own LateUpdate (order 0) and the game renders the thermal
        /// picture in the FLIR camera's LateUpdate (order 10), so a servo that runs at order 0 may have
        /// its work undone by the optic, or may simply run after the picture has already been drawn. At 5
        /// this is the last word on where the sight points before the thermal frame is rendered, and the
        /// main camera's pre-cull pass repeats it for the frame the player actually sees.
        /// </summary>
        [DefaultExecutionOrder(5)]
        public class CrowsAimSlave : MonoBehaviour
        {
            public Transform model;
            public Transform cradle;
            public AimablePlatform mount_platform;
            public AimablePlatform cradle_platform;
            public FireControlSystem fcs;

            /// <summary>
            /// The station's sights. They hang off the gun, which hangs off the model, so the sight and
            /// the camera inherit whatever this servo does to the model's rotation.
            /// </summary>
            public UsableOptic[] sights;

            /// <summary>
            /// Set once the tracker exists. Without it, or while no target is locked, the mount follows
            /// the bare aim exactly as it did before the tracker was added.
            /// </summary>
            public CrowsAutoTrack tracker;

            /// <summary>
            /// Backs the fix below out without a rebuild: with the stabiliser off, feed the mount the
            /// old world-space aim every frame again. Exposed so it can be flipped from the debug bridge.
            /// </summary>
            public static bool keep_world_aim_when_unstabilized = false;

            /// <summary>
            /// Whether the station was stabilized on the previous frame. Only the transition matters: it is
            /// where the stale world-space goal has to be dropped.
            /// </summary>
            private bool stabilizer_was_active;

            /// <summary>
            /// Drives the model onto the aim and the cradle back onto its own plane, with the sight's
            /// world orientation captured first and put back afterwards.
            ///
            /// That capture is load-bearing: the model is driven onto the *compensated* azimuth - the
            /// aim plus the lead - because the gun's azimuth has nowhere else to come from here, while
            /// the sight must point at the target itself. Without it the model's lead rotation lands on
            /// top of the sight's alignment every frame and the whole picture ends up off the target by
            /// the lead angle.
            /// </summary>
            void LateUpdate()
            {
                Drive(null);
            }

            /// <summary>
            /// The last word on where the sight points: the optic's own alignment runs in a LateUpdate
            /// and would otherwise have the final say on some frames and this servo on others. The
            /// camera is read after this callback, so this is the value that gets drawn.
            /// </summary>
            void OnEnable()
            {
                Camera.onPreCull -= HandlePreCull;
                Camera.onPreCull += HandlePreCull;
            }

            void OnDisable()
            {
                Camera.onPreCull -= HandlePreCull;
            }

            /// <summary>
            /// The whole servo, run again here - after every LateUpdate in the scene has had its turn and
            /// immediately before the frame is drawn.
            ///
            /// AimablePlatform moves the model through *two* rate limits: the virtual rotation chases the
            /// goal (`doRotation`), then the transform chases the virtual rotation
            /// (`MoveTransformToMatchVirtualRotation`, `Quaternion.RotateTowards` by `CurrentMaxSpeed *
            /// dt`). Forcing the rotation in LateUpdate therefore does not settle it: the platform's own
            /// LateUpdate is free to run afterwards and put the model back on that chase, which trails the
            /// aim by about a frame's worth of traverse - 1.5 degrees at the mount's 90 deg/s and sixty
            /// frames. That trailing is exactly what the player sees as the muzzle leaning to one side
            /// while the sight is already on the target, with the side swapping when the traverse does.
            ///
            /// Re-running the servo here also means the rounds leave along the direction that was on
            /// screen when the trigger was pulled, rather than a frame behind it.
            /// </summary>
            private void HandlePreCull(Camera cam)
            {
                // Two cameras can be drawing the picture this servo has to answer for: the main camera,
                // which draws the day sight, and the FLIR camera, whose own render *is* the thermal sight -
                // and that one is rendered from the game's LateUpdate, before the main camera culls
                // anything. Whichever of them is about to draw gets the aim re-applied immediately before
                // it does, so the thermal frame cannot be a servo step out of date.
                if (cam == null || (cam != CameraManager.MainCam && cam != PictureCamera()))
                {
                    return;
                }

                Drive(cam);
            }

            /// <summary>
            /// Keeps the station stabilised. SetStabsActive is otherwise owned by the game's crew and
            /// input code, and the mount is only servoed correctly while it is on, so it is pinned here.
            /// </summary>
            private void PinStabiliser()
            {
                if (fcs == null || fcs.StabsActive)
                {
                    return;
                }

                fcs.SetStabsActive(true);
            }

            /// <summary>
            /// Drives the model onto the aim and the cradle onto its own plane, with the sight's world
            /// orientation captured first and put back afterwards. Run from both LateUpdate and pre-cull;
            /// see HandlePreCull for why one place is not enough.
            ///
            /// <paramref name="drawing"/> is the camera that is about to draw, when the servo is being run
            /// from a pre-cull callback, and null when it is being run from LateUpdate - in which case the
            /// picture is whatever the current sight draws.
            /// </summary>
            private void Drive(Camera drawing)
            {
                // Before anything reads the platforms, make sure they still carry the station's rated
                // slew. This is the one write that has to happen no matter which sight is in use.
                PinRatedSpeeds();

                // Stabilisation is locked on for this station. The servo was written for a mount that
                // holds the aim against the hull, so an attempt to switch stabilisation off - from the
                // crew order, the keybind or another mod - is undone here instead of being allowed to
                // leave the station following the hull with the sight still tracking.
                PinStabiliser();

                // The sight is only turned while it is the sight the player is looking through. A lock
                // outlives the station being selected - it is kept so the station picks the track back up
                // when the player comes back to it - and a sight left aimed at a target nobody is looking
                // at, or fighting a free-look camera, is a fault with no upside. The mount itself is still
                // servoed either way, which is what keeps the gun where the fire control put it.
                bool in_use = InUse();
                Transform sight = in_use ? SightTransform() : null;
                Camera picture = in_use ? (drawing != null ? drawing : PictureCamera()) : null;

                // What has to be turned for the picture to turn: the main camera when the picture camera
                // hangs off it (the thermal camera does), the picture camera itself otherwise. Aiming this
                // rather than the sight - and aiming the sight as well - is what keeps the projection the
                // markers are placed with and the projection the picture is drawn with from coming apart.
                Transform pivot = PicturePivot(picture);

                // Whether turning the sight is enough. In the day sight it is, the main camera being
                // parented to the slot; when it is not, the camera itself is aimed below as well, which is
                // the same thing said to the transform that actually decides what the player sees.
                bool carried = sight != null && pivot != null && pivot.IsChildOf(sight);

                Quaternion held = sight == null ? Quaternion.identity : sight.rotation;

                ServoMount();
                ClampCradle();

                if (sight != null)
                {
                    sight.rotation = held;
                }

                if (!in_use)
                {
                    return;
                }

                AimSight(sight);
                AimCamera(pivot, carried);
            }

            /// <summary>
            /// Puts the station's rated slew back on the two platforms this station owns, every frame.
            ///
            /// The rates are written once while the station is assembled, and that is enough for every
            /// path the game has today. They are re-asserted anyway because they are load-bearing far away
            /// from here: the fire control turns the player's aim input into a rate through
            /// <c>Mounts[i].CurrentMaxSpeed</c>, the tracker limits its slew onto a target by the same
            /// number, and the player's manual offset walks at it (see CrowsAutoTrack.WalkManualOffset).
            /// A rate that has been quietly lowered somewhere else therefore does not read as one fault -
            /// it reads as the mount being slow in every one of those places at once, which is a symptom
            /// with no obvious cause. Two field writes a frame is the cheap end of that trade.
            ///
            /// Only this station's platforms are touched, and only their rated speed: whether the mount is
            /// powered at all is the vehicle's business, not this servo's.
            /// </summary>
            private void PinRatedSpeeds()
            {
                if (mount_platform != null)
                {
                    mount_platform.SpeedPowered = TRAVERSE_SPEED_DEGREES_PER_SECOND;
                }

                if (cradle_platform != null)
                {
                    cradle_platform.SpeedPowered = ELEVATION_SPEED_DEGREES_PER_SECOND;
                }
            }

            /// <summary>
            /// Whether the player is looking through one of this station's sights - which is the only state
            /// in which there is a picture to put on the target, and the only one in which the servo may
            /// touch a transform the player can see.
            /// </summary>
            private bool InUse()
            {
                CameraSlot active = CameraSlot.ActiveInstance;

                if (active == null || sights == null)
                {
                    return false;
                }

                for (int i = 0; i < sights.Length; i++)
                {
                    if (sights[i] != null && sights[i].slot == active)
                    {
                        return true;
                    }
                }

                return false;
            }

            /// <summary>
            /// The camera the player's picture is actually drawn with.
            ///
            /// Normally that is the main camera. In the thermal sight it is not: the game renders the whole
            /// thermal view through the FLIR camera's own render texture and blits it over the screen, and
            /// that camera's transform is what decides what the player sees. It is a child of the main
            /// camera, so it follows whatever happens to the main camera - which is exactly why the servo
            /// has to have finished before the game renders it, in its own LateUpdate, one execution step
            /// after this one.
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
            /// The transform that has to be rotated for the given picture camera to look somewhere else:
            /// the main camera when the picture camera is the main camera or hangs off it, so that the
            /// main camera's own projection - the one every marker is placed with - is turned with the
            /// picture instead of lagging a transform behind it.
            /// </summary>
            private static Transform PicturePivot(Camera picture)
            {
                Camera main = CameraManager.MainCam;

                if (picture == null)
                {
                    return main == null ? null : main.transform;
                }

                if (main != null && picture.transform.IsChildOf(main.transform))
                {
                    return main.transform;
                }

                return picture.transform;
            }

            /// <summary>
            /// Points the picture camera itself at the tracked target, for the case where it does not
            /// inherit the sight's turn.
            ///
            /// In the day sight the camera is a child of the optic's slot, so AimSight rotating the slot
            /// rotates the camera with it and there is nothing to do here. The thermal sight is the same
            /// arrangement when the game has attached its slot - the FLIR camera hangs off the main camera
            /// and so does the main camera off the slot - but that is the game's arrangement to make, not
            /// this mod's, and when it does not hold, turning the slot moves nothing that is on screen. The
            /// camera is the thing that decides what the player sees, so it is the thing to aim when that
            /// happens - and it is aimed at exactly the point AimSight would have used, so the two paths
            /// cannot disagree about where the crosshair is.
            ///
            /// Run from the pre-cull callback, after every LateUpdate in the scene has had its turn,
            /// including the optic's own alignment. Writing either transform earlier just gets overwritten.
            /// </summary>
            private void AimCamera(Transform pivot, bool carried)
            {
                if (pivot == null || tracker == null || carried)
                {
                    return;
                }

                Vector3 point;

                if (!tracker.SightPoint(out point))
                {
                    return;
                }

                Vector3 to = point - pivot.position;

                if (to.sqrMagnitude < 1f)
                {
                    return;
                }

                // Free look is the player's own camera, lock or no lock; the station must not fight it.
                if (CameraSlot.ActiveInstance != null && CameraSlot.ActiveInstance.AllowFreeLook)
                {
                    return;
                }

                // Levelled the way the game levels its own sight rather than against the mount; see
                // AimSight. The mount carries the gun's roll, and handing that roll to the camera tilts the
                // whole picture.
                Vector3 up = Vector3.up;

                if (Vector3.Cross(to.normalized, up).sqrMagnitude < 1e-6f)
                {
                    up = Vector3.forward;
                }

                pivot.rotation = Quaternion.LookRotation(to, up);
            }

            /// <summary>
            /// Turns the sight - and with it the camera and the crosshair drawn in it - onto the tracked
            /// target.
            ///
            /// Nothing else here does it. The optic's own alignments follow either the fire control's aim
            /// *direction*, which is parallel to the line to the target rather than on it, or its
            /// *impact point*, which is the gun's aim - leaving the crosshair on the barrel and the
            /// camera bolted to the gun servo.
            ///
            /// The rotation is written directly rather than through the optic's platform, from a pre-cull
            /// callback so that it survives the optic's own alignment later in the same frame.
            /// </summary>
            private void AimSight(Transform sight)
            {
                if (sight == null || tracker == null)
                {
                    return;
                }

                Vector3 point;

                if (!tracker.SightPoint(out point))
                {
                    return;
                }

                Vector3 to = point - sight.position;

                if (to.sqrMagnitude < 1f)
                {
                    return;
                }

                // Levelled the way the game levels its own sight, not against the mount. The mount
                // carries the gun's roll, and handing the sight that roll tilts the whole picture - the
                // thermal view shows it plainly and the day view shows it as the overlay sitting
                // crooked. UsableOptic.LookAt levels against the world, so this does too.
                Vector3 up = Vector3.up;

                if (Vector3.Cross(to.normalized, up).sqrMagnitude < 1e-6f)
                {
                    up = Vector3.forward;
                }

                sight.rotation = Quaternion.LookRotation(to, up);
            }

            private Transform SightTransform()
            {
                if (sights == null)
                {
                    return null;
                }

                for (int i = 0; i < sights.Length; i++)
                {
                    UsableOptic optic = sights[i];

                    if (optic != null && optic.slot != null && optic.slot.IsActive)
                    {
                        return optic.slot.transform;
                    }
                }

                for (int i = 0; i < sights.Length; i++)
                {
                    if (sights[i] != null && sights[i].slot != null)
                    {
                        return sights[i].slot.transform;
                    }
                }

                return null;
            }

            private void ServoMount()
            {
                if (model == null || mount_platform == null || fcs == null)
                {
                    return;
                }

                Vector3 up = model.parent != null ? model.parent.up : Vector3.up;

                // Where the mount is pointed: the platform's own current goal, flattened onto the turret
                // plane so an azimuth-only mount is not tilted by its superelevation.
                //
                // Read back off the platform rather than rebuilt from the fire control's public API, and
                // that matters: the platform's own servo is driving towards that same vector, so a servo
                // working from even a slightly different one - the muzzle parallax term is enough - pulls
                // the model the other way every frame and everything that follows it shivers. One source
                // of truth, or a fight.
                Vector3 aim;

                // Both manual aim and tracking follow the same compensated FCS goal.
                // The reference has been moved to the sight; the donor-offset workaround
                // must no longer bypass muzzle parallax before the first lock.
                if (WeaponGoal(out aim))
                {
                    aim = Vector3.ProjectOnPlane(aim, up);
                }
                else if (fcs.CurrentRange > 0f)
                {
                    // Before the platform has a world-space goal, use the same compensated
                    // vector that FinalSetAimVector will send to it.
                    aim = Vector3.ProjectOnPlane(fcs.CompensatedWeaponVector(true), up);
                }
                else
                {
                    aim = Vector3.ProjectOnPlane(fcs.AimWorldVector, up);
                }

                // Aiming close to straight up gives nothing sensible to flatten onto the turret plane.
                if (aim.sqrMagnitude < 0.0625f)
                {
                    return;
                }

                // When stabilization is enabled, the station goal is world-space and the direct write
                // keeps the sight and muzzle together at render time.
                //
                // When the player turns stabilization off the mount is left alone. Feeding it a
                // world-space aim every frame *is* a stabilizer: SetAimVector(stabilized:false,
                // worldSpace:true) folds that world aim into the current rotation on every call
                // (AimablePlatform.SetAimVector), so the mount keeps chasing one fixed world direction
                // and the station never follows the hull - which in game reads as the stabilizer toggle
                // doing nothing. The fire control already hands the mount its own unstabilized goal, so
                // all that is left to do here is drop the stale world goal on the transition and stay out
                // of the way.
                if (fcs.StabsActive)
                {
                    mount_platform.SetAimVector(aim.normalized, true, true, false, false);
                    mount_platform.ForceAimVectorNow(aim);
                    stabilizer_was_active = true;
                }
                else
                {
                    if (stabilizer_was_active)
                    {
                        stabilizer_was_active = false;
                        if (!keep_world_aim_when_unstabilized)
                        {
                            try
                            {
                                // StabilizationMode.None: the platform stops chasing the world goal it was
                                // left with and rides the hull until the fire control aims it again.
                                Reflect.Call(mount_platform, "UnsetAimVector");
                            }
                            catch (Exception)
                            {
                            }
                        }
                    }

                    if (keep_world_aim_when_unstabilized)
                    {
                        mount_platform.SetAimVector(aim.normalized, true, false, true, false);
                    }
                }

                model.rotation = mount_platform.Rotation;

                // The cradle is deliberately NOT touched: `aim` is flattened onto the turret plane, so
                // handing it to the elevation platform would strip the superelevation out of the gun and
                // the rounds would fall short by the whole ballistic drop. The fire control already hands
                // both mounts its compensated weapon vector (see FinalSetAimVector).
            }

            /// <summary>
            /// The weapon vector the platform is being driven towards, in world space, or false if it has
            /// none - the fire control's own output, lead, superelevation and muzzle parallax and all.
            /// </summary>
            private bool WeaponGoal(out Vector3 goal)
            {
                goal = Vector3.zero;

                if (mount_platform == null)
                {
                    return false;
                }

                try
                {
                    if (!Reflect.Get<bool>(mount_platform, "_useWorldSpace"))
                    {
                        return false;
                    }

                    goal = Reflect.Get<Vector3>(mount_platform, "_goalAimVector");
                }
                catch (Exception)
                {
                    return false;
                }

                return goal.sqrMagnitude > 0.25f;
            }

            private void ClampCradle()
            {
                if (cradle == null)
                {
                    return;
                }

                Vector3 local = cradle.localEulerAngles;

                if (Mathf.Abs(Mathf.DeltaAngle(local.y, 0f)) < 0.001f
                    && Mathf.Abs(Mathf.DeltaAngle(local.z, 0f)) < 0.001f)
                {
                    return;
                }

                cradle.localRotation = Quaternion.Euler(local.x, 0f, 0f);

                if (cradle_platform != null)
                {
                    cradle_platform.Rotation = cradle.rotation;
                }
            }
        }

        public static void Init()
        {
            setup_failures.Clear();
            // Keep the asset-bundle model; rebuild ammo references for the current mission.
            if (!initialised)
            {
                try
                {
                    LoadModel();
                    initialised = crows_prefab != null;
                }
                catch (Exception e)
                {
                    Fail("asset setup threw: " + e.Message);
                }
            }

            LoadAmmo();

            // Nothing is harvested from other vehicles any more: the reticle and the sight frame are
            // built in code here, and the .50 cal, its sight and its computer are cloned off the
            // vehicle's own cupola when the station is mounted (see ResolveOwnCupola). The sight frame
            // is the only piece that is a real clone, so it dies with the mission while this class's
            // statics survive; rebuilding it when it has gone is both the fix for that and the test
            // for "the mission changed".
            if (box_canvas == null)
            {
                try
                {
                    BuildRangeBoxCanvas();
                }
                catch (Exception e)
                {
                    Fail("sight frame threw: " + e.Message);
                }
            }

            try
            {
                BuildReticle();
            }
            catch (Exception e)
            {
                Fail("reticle threw: " + e.Message);
            }

            ready = crows_prefab != null
                && box_canvas != null
                && ammo_codex_m8 != null
                && m8_belt != null
                && reticle_so != null;

            if (!ready)
            {
            }

            ReportFailures();
        }

        public static bool Mount(Vehicle vic, Transform origin, Vector3 position, bool alt_placement, UsableOptic main_gun_thermal)
        {
            if (!ready || main_gun_thermal == null || main_gun_thermal.slot == null)
            {
                return false;
            }

            try
            {
                return MountInternal(vic, origin, position, alt_placement, main_gun_thermal);
            }
            catch (Exception e)
            {
                // Silent here means "no CROWS and no explanation"; the station is optional, so a
                // failure must stay non-fatal, but it must not stay anonymous.
                Fail("mount threw: " + e);
                return false;
            }
        }

        // ------------------------------------------------------------------ assets

        private static void LoadAmmo()
        {
            // Independent of the model/donor setup and refreshed on every mission.
            m8_belt = CrowsAmmo.Select(CrowsAmmoRules.M8);
            ammo_codex_m8 = m8_belt == null ? null : m8_belt.MinimalPattern[0];

            // A null belt here is the one way "ready" can go false without a setup failure being
            // recorded: every other term has its own Fail() on the way in.
            if (m8_belt == null)
            {
            }
        }

        private static void Fail(string reason)
        {
            if (string.IsNullOrEmpty(reason))
            {
                return;
            }

            // Printed the moment it happens. The list is only reported at the end of Init(), and the
            // mount runs after that, so a failure collected there used to be swallowed whole: no
            // station, no error, nothing to look at.
            setup_failures.Add(reason);
            MelonLogger.Error("[M1A1] CROWS setup failed: " + reason);
        }

        private static void ReportFailures()
        {
            if (setup_failures.Count > 0)
            {
            }
        }
    }
}




