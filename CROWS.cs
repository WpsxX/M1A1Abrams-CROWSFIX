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

namespace M1A1Abrams
{
    /// <summary>
    /// M153 CROWS II remote weapon station.
    ///
    /// Loads the .50 cal, gunsight, fire control computer, reticle tree and range-box canvas from
    /// the game's Addressables prefabs. The templates are loaded as assets, so the mission does not
    /// need to deploy M60A3, T-55A or M2 Bradley donor vehicles. A scene donor is retained only as a
    /// compatibility fallback for older game builds whose catalogue does not expose the prefab. Every
    /// non-public member is reached through ModUtil.Reflect: the running game loads the official
    /// (non-publicized) Assembly-CSharp.
    /// </summary>
    internal static partial class CROWS
    {
        private const string ASSET_FOLDER = "m1a1assets";
        private const float MODEL_SCALE = 0.75f;
        private const string CROWS_NAME = "M153 CROWS II - M2HB";

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
        private static object reticle_cached; // boxed ReticleMesh.CachedReticle (private nested struct)

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
                // keeps the sight and muzzle together at render time. When the player turns stabilization
                // off, feed the same aim as a local/unstabilized goal and leave the platform's own
                // transform update in charge; forcing a world rotation here would silently re-enable
                // point stabilization every frame.
                if (fcs.StabsActive)
                {
                    mount_platform.SetAimVector(aim.normalized, true, true, false, false);
                    mount_platform.ForceAimVectorNow(aim);
                }
                else
                {
                    mount_platform.SetAimVector(aim.normalized, true, false, true, false);
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

            // The donors are a different matter: the sight frame's template is a real clone, so it dies
            // with the mission while this class's statics survive. Re-harvesting whenever a donor has
            // gone is both the fix for that and the test for "the mission changed".
            if (!DonorsAlive())
            {
                try
                {
                    LoadDonors();
                    BuildReticle();
                }
                catch (Exception e)
                {
                    Fail("donor harvest threw: " + e.Message);
                }
            }

            ready = crows_prefab != null
                && m2_browning != null
                && m2_gun_sight != null
                && m2_fcs != null
                && box_canvas != null
                && ammo_codex_m8 != null
                && m8_belt != null
                && reticle_cached != null;

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
                Fail("mount threw: " + e.Message);
                return false;
            }
        }

        // ------------------------------------------------------------------ assets

        private static void LoadModel()
        {
            string path = Path.Combine(MelonEnvironment.ModsDirectory, ASSET_FOLDER, "crows");

            if (!File.Exists(path))
            {
                Fail("asset bundle not found: " + path);
                return;
            }

            AssetBundle bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null)
            {
                Fail("could not open asset bundle: " + path);
                return;
            }

            try
            {
                crows_prefab = bundle.LoadAsset<GameObject>("CROWS.prefab");
                if (crows_prefab == null)
                {
                    Fail("CROWS.prefab missing from " + path);
                    return;
                }

                crows_prefab.hideFlags = HideFlags.DontUnloadUnusedAsset;
                crows_prefab.transform.localScale = new Vector3(MODEL_SCALE, MODEL_SCALE, MODEL_SCALE);

                Transform gun_col = crows_prefab.transform.Find("GUN/GUN COLLIDER");
                Transform mount_col = crows_prefab.transform.Find("MOUNT/MOUNT COLLIDER");
                Transform gun_vis_transform = crows_prefab.transform.Find("GUN/GUN VIS");
                Transform mount_vis_transform = crows_prefab.transform.Find("MOUNT/MOUNT VIS");

                if (gun_col == null || mount_col == null || gun_vis_transform == null || mount_vis_transform == null)
                {
                    crows_prefab = null;
                    Fail("CROWS.prefab is missing its collider or visual hierarchy");
                    return;
                }

                GameObject gun_vis = gun_vis_transform.gameObject;
                GameObject mount_vis = mount_vis_transform.gameObject;

                gun_col.tag = "Penetrable";
                mount_col.tag = "Penetrable";
                gun_col.gameObject.layer = 7;
                mount_col.gameObject.layer = 7;

                MeshRenderer gun_renderer = gun_vis.GetComponent<MeshRenderer>();
                MeshRenderer mount_renderer = mount_vis.GetComponent<MeshRenderer>();
                Shader flir_shader = Shader.Find("Standard (FLIR)");

                if (gun_renderer == null || mount_renderer == null
                    || gun_renderer.sharedMaterial == null || mount_renderer.sharedMaterial == null
                    || flir_shader == null)
                {
                    crows_prefab = null;
                    Fail("CROWS.prefab is missing a renderer or the Standard (FLIR) shader");
                    return;
                }

                gun_renderer.sharedMaterial.shader = flir_shader;
                mount_renderer.sharedMaterial.shader = flir_shader;
                crows_prefab.AddComponent<HeatSource>().Heat = 5f;
            }
            finally
            {
                // The prefab and its dependencies stay loaded through their references; the bundle
                // itself no longer needs to keep its file mapping and native decompression state.
                bundle.Unload(false);
            }
        }

        /// <summary>
        /// Whether everything harvested from the mission is still usable. A UnityEngine.Object whose
        /// native side has gone compares equal to null, so this doubles as the "mission changed" test.
        /// </summary>
        private static bool DonorsAlive()
        {
            PruneLoadedDonors();
            return m2_browning != null
                && m2_gun_sight != null
                && m2_fcs != null
                && box_canvas != null;
        }

        private static void PruneLoadedDonors()
        {
            for (int i = loaded_donors.Count - 1; i >= 0; i--)
            {
                if (loaded_donors[i] == null)
                {
                    loaded_donors.RemoveAt(i);
                }
            }
        }

        private static void LoadDonors()
        {
            // A leftover template from an earlier mission is no longer any use - it is about to be
            // cloned again - and dropping it keeps the scene from collecting one copy per mission.
            if (box_canvas != null)
            {
                UnityEngine.Object.Destroy(box_canvas);
                box_canvas = null;
            }

            Vehicle m60a3 = FindDonor(new string[] { "M60A3 TTS", "M60A3", "M60A3TTS" }, "M60A3TTS");

            if (m60a3 != null)
            {
                m2_browning = FindChild(m60a3.transform, "Cupola Scripts/12.7mm Machine Gun M85", "Cupola Scripts/12.7mm Machine Gun M85(Clone)");
                m2_gun_sight = FindChild(m60a3.transform, "Cupola Scripts/M85 gunsight");
                m2_fcs = FindChild(m60a3.transform, "Cupola Scripts/M85 FCS");
            }

            if (m2_browning == null || m2_gun_sight == null || m2_fcs == null)
            {
                Fail("no M60A3 with a cupola (needs 'Cupola Scripts/12.7mm Machine Gun M85', 'M85 gunsight' and 'M85 FCS')");
            }

            Vehicle t55 = FindDonor(new string[] { "T55A", "T-55A" }, "T55A");
            if (t55 != null)
            {
                GameObject nvs_object = FindChild(t55.transform, "Gun Scripts/Sights (and FCS)/NVS");
                UsableOptic nvs = nvs_object == null ? null : nvs_object.GetComponent<UsableOptic>();

                if (nvs != null && nvs.reticleMesh != null)
                {
                    // Makes ReticleMesh register the T-55A night reticle under "T55-NVS".
                    nvs.reticleMesh.Load();
                }
            }

            if (!Reflect.DictHas(CachedReticles(), "T55-NVS"))
            {
                Fail("T-55A night sight reticle (\"T55-NVS\") is not in the reticle cache");
            }

            Vehicle bradley = FindDonor(new string[] { "M2 Bradley", "M2BRADLEY", "M2 Bradley(alt)" }, "M2BRADLEY");
            if (bradley != null)
            {
                GameObject canvas_source = FindChild(bradley.transform, "FCS and sights/GPS Optic/M2 Bradley GPS canvas");

                if (canvas_source != null)
                {
                    box_canvas = UnityEngine.Object.Instantiate(canvas_source);

                // The donor is the Bradley's sight canvas, and it carries an autocannon ammunition
                // indicator - "HE selected" and "AP selected" under "HUD elements/autocannon ammo
                // types" - which the CROWS has no use for. It is live, though: it follows the gun's own
                // ammunition selection, so switching belts on the CROWS lights two of them up and their
                // first lines, H and A, show at the bottom of the sight. Hidden on the template, which
                // every clone of it inherits. Found by name rather than by path so a hierarchy change
                // in the donor cannot quietly leave it showing again.
                HideDonorAmmoIndicator(box_canvas);

                    // The third child of the GPS canvas is the Bradley's own range readout.
                    if (box_canvas.transform.childCount > 2)
                    {
                        UnityEngine.Object.Destroy(box_canvas.transform.GetChild(2).gameObject);
                    }

                    box_canvas.SetActive(false);
                    box_canvas.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    box_canvas.name = "crows range box";
                }
            }

            if (box_canvas == null)
            {
                Fail("no M2 Bradley GPS canvas to build the range boxes from");
            }
        }

        private static void LoadAmmo()
        {
            // Independent of the model/donor setup and refreshed on every mission.
            m8_belt = CrowsAmmo.Select(CrowsAmmoRules.M8);
            ammo_codex_m8 = m8_belt == null ? null : m8_belt.MinimalPattern[0];
        }

        /// <summary>Copies the T-55A night reticle tree and turns it into a CROWS box reticle.</summary>
        private static void BuildReticle()
        {
            if (reticle_so != null)
            {
                return;
            }

            object source = Reflect.DictGet(CachedReticles(), "T55-NVS");
            if (source == null)
            {
                return;
            }

            ReticleSO donor = Reflect.Get<ReticleSO>(source, "tree");
            if (donor == null)
            {
                Fail("the T-55A reticle cache entry has no tree");
                return;
            }

            reticle_so = ScriptableObject.Instantiate(donor);
            reticle_so.name = "CROWS-RETICLE";

            reticle_cached = source; // boxed copy: mutating it does not touch the cache
            Reflect.Set(reticle_cached, "tree", reticle_so);
            Reflect.Set(reticle_cached, "mesh", null);

            reticle_so.lights = new List<ReticleTree.Light>() { new ReticleTree.Light() };
            reticle_so.lights[0].type = ReticleTree.Light.Type.Powered;
            reticle_so.lights[0].color = SIGHT_COLOR;

            if (reticle_so.planes.Count == 0 || reticle_so.planes[0].elements.Count == 0)
            {
                Fail("the T-55A reticle tree has an unexpected shape");
                // reticle_so was already assigned above, so leaving it set would make the next Init()
                // return at the guard at the top of this method and keep the half-built reticle for
                // good. Cleared like the two shape checks below do.
                reticle_cached = null;
                reticle_so = null;
                return;
            }

            ReticleTree.Angular outer = reticle_so.planes[0].elements[0] as ReticleTree.Angular;
            ReticleTree.Angular reticle_hq = outer == null ? null : outer.elements[0] as ReticleTree.Angular;

            if (reticle_hq == null || reticle_hq.elements.Count < 5)
            {
                Fail("the T-55A reticle tree does not have the expected shape");
                reticle_cached = null;
                reticle_so = null;
                return;
            }

            outer.align = ReticleTree.GroupBase.Alignment.Impact;
            reticle_hq.align = ReticleTree.GroupBase.Alignment.Impact;

            // The donor tree's ladder/rangefinder elements are not wanted in a CROWS sight.
            reticle_hq.elements.RemoveAt(4);
            reticle_hq.elements.RemoveAt(1);
            reticle_hq.elements.RemoveAt(0);

            ReticleTree.Line line1 = reticle_hq.elements[0] as ReticleTree.Line;
            ReticleTree.Line line2 = reticle_hq.elements[1] as ReticleTree.Line;

            if (line1 == null || line2 == null)
            {
                Fail("the T-55A reticle tree does not have the expected crosshair");
                reticle_cached = null;
                reticle_so = null;
                return;
            }

            line1.rotation.MRADS = 0f;
            line1.position.x = 0f;
            line1.position.y = 0f;
            line1.length.MRADS = 13.0944f;
            line1.thickness.MRADS /= 1.7f;
            line1.illumination = ReticleTree.Light.Type.Powered;
            line1.visualType = ReticleTree.VisualElement.Type.Painted;

            line2.position.y = 0f;
            line2.length.MRADS = 13.0944f;
            line2.thickness.MRADS /= 1.7f;
            line2.illumination = ReticleTree.Light.Type.Powered;
            line2.visualType = ReticleTree.VisualElement.Type.Painted;

            RecordReticlePart(line1);
            RecordReticlePart(line2);

            List<Vector3> box_pos = new List<Vector3>() {
                new Vector3(0f, -7.344f),
                new Vector3(0f, 7.344f),
                new Vector3(7.344f, 0f),
                new Vector3(-7.344f, 0f),
            };

            foreach (Vector3 pos in box_pos)
            {
                ReticleTree.Line box = new ReticleTree.Line();
                box.roundness = 0f;
                box.thickness.MRADS = line2.thickness.MRADS * 2.8f;
                box.length.MRADS = 6f;
                box.thickness.unit = AngularLength.AngularUnit.MIL_USSR;
                box.length.unit = AngularLength.AngularUnit.MIL_USSR;
                box.rotation.MRADS = pos.x == 0f ? line2.rotation.MRADS : line1.rotation.MRADS;

                box.position = new ReticleTree.Position(pos.x, pos.y, AngularLength.AngularUnit.MIL_NATO, LinearLength.LinearUnit.M);
                box.visualType = ReticleTree.VisualElement.Type.Painted;
                box.illumination = ReticleTree.Light.Type.Powered;

                reticle_hq.elements.Add(box);
                RecordReticlePart(box);
            }
        }

        private static void RecordReticlePart(ReticleTree.Line line)
        {
            ReticlePart part = new ReticlePart();
            part.line = line;
            part.position = new Vector2(line.position.x, line.position.y);
            part.length = line.length.MRADS;
            part.thickness = line.thickness.MRADS;
            reticle_parts.Add(part);
        }

        /// <summary>
        /// Rescales the reticle so it keeps the same apparent size and proportions at every
        /// magnification, using the 12X picture as the reference. It is drawn through PostMeshComp,
        /// which ignores the renderer's transform, so the geometry itself has to change.
        /// </summary>
        internal static void ApplyReticleScale(float factor)
        {
            if (reticle_parts.Count == 0)
            {
                return;
            }

            factor = Mathf.Clamp(factor, 0.05f, 40f);

            if (Mathf.Abs(factor - reticle_scale) < 0.001f)
            {
                return;
            }

            reticle_scale = factor;

            foreach (ReticlePart part in reticle_parts)
            {
                if (part.line == null)
                {
                    continue;
                }

                part.line.position.x = part.position.x * factor;
                part.line.position.y = part.position.y * factor;
                part.line.length.MRADS = part.length * factor;
                part.line.thickness.MRADS = part.thickness * factor;
            }

            PruneReticleMeshes();

            for (int i = 0; i < reticle_meshes.Count; i++)
            {
                RebuildReticle(reticle_meshes[i]);
            }


        }

        /// <summary>
        /// Drops the cached reticle scale so the next frame re-applies it.
        ///
        /// The scale is what keeps the reticle the same apparent size at every magnification, and it
        /// is remembered in a static field so the rebuild it triggers is not run sixty times a second.
        /// The reticle *meshes*, on the other hand, belong to the mission and are made again from the
        /// tree on every conversion. A cached factor that survives the mission therefore says "already
        /// applied" about a mesh that has never been scaled, the rebuild is skipped, and the sight
        /// falls back to a reticle that grows with the zoom - the way it looked before the rescaling
        /// existed. Conversion calls this so the first frame of the new mission re-applies it.
        /// </summary>
        internal static void InvalidateReticleScale()
        {
            reticle_scale = -1f;
        }

        /// <summary>
        /// Takes note of a sight's reticle mesh so the rescaling can reach it. Called for both optics of
        /// every CROWS the mission builds, not just the one the player ends up in.
        /// </summary>
        private static void RegisterReticleMesh(ReticleMesh mesh)
        {
            PruneReticleMeshes();

            if (mesh == null)
            {
                return;
            }

            for (int i = 0; i < reticle_meshes.Count; i++)
            {
                if (reticle_meshes[i] == mesh)
                {
                    return;
                }
            }

            reticle_meshes.Add(mesh);


        }

        /// <summary>
        /// Forgets the meshes of the mission that has ended. Unity reports a destroyed object as equal to
        /// null, which is what makes the test work on a plain list of components.
        /// </summary>
        private static void PruneReticleMeshes()
        {
            for (int i = reticle_meshes.Count - 1; i >= 0; i--)
            {
                if (reticle_meshes[i] == null)
                {
                    reticle_meshes.RemoveAt(i);
                }
            }
        }

        private static void RebuildReticle(ReticleMesh mesh)
        {
            if (mesh == null)
            {
                return;
            }

            // A sight that is still drawing the donor's reticle has nothing of ours in it to rescale,
            // and rebuilding it would only throw that sight's geometry away for nothing.
            if (mesh.reticleSO != reticle_so)
            {
                return;
            }

            SkinnedMeshRenderer smr = mesh.GetComponent<SkinnedMeshRenderer>();

            if (smr != null)
            {
                smr.sharedMesh = null;
            }

            mesh.Load();
        }

        // ------------------------------------------------------------------ mounting

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
            Util.CreateUniformArmour(gun_col, "M2HB machine gun", 5f, 5f);
            UniformArmor gun_armour = gun_col.GetComponent<UniformArmor>();
            gun_armour.Unit = vic;
            gun_col.layer = 7;

            Util.CreateUniformArmour(mount_col, "CROWS", 15f, 15f);
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
            aimable_gun.LocalEulerLimits = new Vector2(-15f, 60f);
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

            // --- borrowed .50 cal, sight and computer ---
            // IMPORTANT: Instantiate fcs_go as INACTIVE so we can set ReferenceTransform BEFORE FCS.Awake() runs.
            // FCS.Awake() caches the reference position, so it must see the correct reference from the start.
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
            
            GameObject browning = UnityEngine.Object.Instantiate(m2_browning, gun.transform);
            browning.transform.localPosition = new Vector3(0f, 0f, 1.201f);

            // The donor's own Reparent scripts must not survive the clone: theirs point at the donor
            // tank's cupola, and Reparent.Awake() does `transform.parent = NewParent`, which lifts the
            // borrowed gun into the donor prefab's hierarchy where nothing on it ever runs.
            SanitizeBorrowed(browning, model.transform);
            SanitizeBorrowed(fcs_go, model.transform);

            GameObject gun_sight = UnityEngine.Object.Instantiate(m2_gun_sight, gun.transform);
            SanitizeBorrowed(gun_sight, model.transform);
            gun_sight.transform.localPosition = new Vector3(0f, -0.085f, 0.85f);
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
            fcs_reference.transform.localPosition = new Vector3(0f, -0.085f, 0.85f);
            fcs_reference.transform.localRotation = Quaternion.identity;

            // Shared ammo selection; CROWS keeps its 400-round belt and no reserve.
            AmmoType.AmmoClip clip = CrowsAmmo.Select(M1A1.crows_ammo.Value);
            if (clip == null) clip = m8_belt;

            GameObject rack_go = new GameObject("crows ammo rack");
            rack_go.transform.SetParent(gun.transform, false);
            rack_go.SetActive(false);

            GHPC.Weapons.AmmoRack ammo_rack = rack_go.AddComponent<GHPC.Weapons.AmmoRack>();
            ammo_rack.ClipTypes = new AmmoType.AmmoClip[] { clip };
            Reflect.Set(ammo_rack, "_initialClipCounts", new int[] { 0 });   // no spare belt: 400 + 0
            // The rack's Awake() reads both of those, so they are set before the object is activated.
            rack_go.SetActive(true);

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
            Reflect.Set(weapon, "_cycleTimeSeconds", 0.1091f);
            Reflect.Set(weapon.Feed, "_totalCycleTime", 0.1091f);
            weapon.WeaponSound.LoopEventPath = "event:/Weapons/MG_m2_550rmp";
            CrowsAmmo.AttachTracerLimit(weapon);

            // The HUD names a weapon from `WeaponSystem.WeaponData` (= its CodexEntry), so the clone
            // would keep introducing itself as the donor's M85. The donor's codex is cloned to keep
            // every other parameter (audio, jiggle, blur) and renamed.
            WeaponSystemCodexScriptable donor_codex = weapon.WeaponData;

            if (donor_codex != null)
            {
                WeaponSystemCodexScriptable codex = ScriptableObject.CreateInstance<WeaponSystemCodexScriptable>();
                Util.ShallowCopy(codex, donor_codex);
                codex.name = "crows_m2hb";
                codex.FriendlyName = CROWS_NAME;
                codex.CaliberMm = 12.7f;
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
            day_optic.LocalElevationLimits = new Vector2(-15f, 60f);

            fcs.LaserOrigin = lase.transform;
            fcs.LaserAim = LaserAimMode.ImpactPoint;
            fcs.MaxLaserRange = 4000f;
            fcs.LinkedWeaponSystems = new WeaponSystem[] { weapon };
            fcs.SuperelevateWeapon = true;
            fcs.SuperleadWeapon = true;
            fcs.CurrentStabMode = StabilizationMode.Vector;
            fcs.SetStabsActive(true);
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
            Reflect.Set(fcs, "_originalSuperelevationMode", true);
            Reflect.Set(fcs, "_originalSuperleadMode", true);

            weapon.FCS = fcs;

            // UsableOptic.Start() wires itself into whatever FCS it points at, so both optics
            // are pointed at the CROWS computer; only the day sight claims the main optic slot.
            day_optic.FCS = fcs;
            day_optic.RegisterAtStart = true;

            // --- reticle, thermal sight and range boxes ---
            ReticleMesh reticle_mesh = day_optic.reticleMesh;
            if (reticle_mesh != null)
            {
                reticle_mesh.reticleSO = reticle_so;
                Reflect.Set(reticle_mesh, "reticle", reticle_cached);

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
            gun_sight_flir.transform.localPosition = new Vector3(0f, -0.085f, 0.85f);
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
            flir_optic.LocalElevationLimits = new Vector2(-15f, 60f);
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
            crows_info.Name = CROWS_NAME;
            crows_info.FCS = fcs;
            crows_info.Weapon = weapon;
            crows_info.Role = WeaponSystemRole.RoofGun;
            crows_info.PreAimWeapon = WeaponSystemRole.MainGun;

            InstallWeaponInfo(vic, crows_info);

            Reflect.SetProperty(vic, "AimablePlatforms", Util.AppendToArray(vic.AimablePlatforms, aimable_mount));
            Reflect.SetProperty(vic, "AimablePlatforms", Util.AppendToArray(vic.AimablePlatforms, aimable_gun));
            Reflect.Set(vic, "_designatedCameraSlots", Util.AppendToArray(vic.DesignatedCameraSlots, optic_slot));

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

            manager.Weapons = Util.AppendToArray(manager.Weapons, crows_info);
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
        private static void DetachFromDonor(Component component, Transform model_root)
        {
            if (component == null)
            {
                return;
            }

            // Type.GetFields() does not return private fields declared on base classes, so
            // the hierarchy is walked by hand (same rule as ModUtil.Reflect).
            for (Type type = component.GetType(); type != null && type != typeof(Behaviour); type = type.BaseType)
            {
                foreach (System.Reflection.FieldInfo field in type.GetFields(
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    if (field.IsInitOnly || field.IsLiteral)
                    {
                        continue;
                    }

                    Type field_type = field.FieldType;

                    if (typeof(Component).IsAssignableFrom(field_type) || typeof(GameObject).IsAssignableFrom(field_type))
                    {
                        if (IsExternal(field.GetValue(component) as UnityEngine.Object, model_root))
                        {
                            field.SetValue(component, null);
                        }

                        continue;
                    }

                    if (!field_type.IsArray)
                    {
                        continue;
                    }

                    Type element_type = field_type.GetElementType();

                    if (!typeof(Component).IsAssignableFrom(element_type) && !typeof(GameObject).IsAssignableFrom(element_type))
                    {
                        continue;
                    }

                    Array array = field.GetValue(component) as Array;

                    if (array == null)
                    {
                        continue;
                    }

                    for (int i = 0; i < array.Length; i++)
                    {
                        if (IsExternal(array.GetValue(i) as UnityEngine.Object, model_root))
                        {
                            array.SetValue(null, i);
                        }
                    }
                }
            }

        }

        /// <summary>
        /// Switches the vanilla scope overlay off while a CROWS sight is the active camera slot: the
        /// CROWS ships its own black frame, and CameraManager would otherwise turn the shared "Scope
        /// Sprite" back on from the slot's SpriteType. The cached type is invalidated too, since
        /// SetActiveSprite() early-returns when the requested type matches it.
        ///
        /// The borrowed sight's own PostMeshComp must NOT be removed: that is what draws the reticle.
        /// </summary>
        public class CrowsScopeOverlay : MonoBehaviour
        {
            public CameraSlot day_slot;
            public CameraSlot night_slot;

            private CameraSpriteManager manager;
            private GameObject[] sprites;

            void Start()
            {
                manager = UnityEngine.Object.FindObjectOfType<CameraSpriteManager>();

                if (manager == null || manager.Sprites == null)
                {
                    enabled = false;
                    return;
                }

                List<GameObject> found = new List<GameObject>();

                foreach (CameraSpriteManager.CameraSpriteData data in manager.Sprites)
                {
                    if (data != null && data.SpriteObject != null)
                    {
                        found.Add(data.SpriteObject);
                    }
                }

                sprites = found.ToArray();
            }

            void Update()
            {
                CameraSlot active = CameraSlot.ActiveInstance;

                if (active != day_slot && active != night_slot)
                {
                    return;
                }

                foreach (GameObject sprite in sprites)
                {
                    if (sprite != null && sprite.activeSelf)
                    {
                        sprite.SetActive(false);
                    }
                }

                Reflect.Set(manager, "_activeType", (CameraSpriteManager.SpriteType)(-1));
            }
        }

        /// <summary>
        /// Picks the font for the sight overlay: VCR OSD Mono, built from the TrueType face because no
        /// Resources asset references a TMP asset for it. Arial is the fallback, then the mod's own
        /// markers' font, then the TMP default.
        /// </summary>
        private static TMP_FontAsset PickSightUiFont()
        {
            TMP_FontAsset vcr = VcrFontAsset();

            if (vcr != null)
            {
                return vcr;
            }

            TMP_FontAsset arial = Resources.Load<TMP_FontAsset>("fonts & materials/Arial SDF");

            if (arial != null)
            {
                return arial;
            }

            foreach (TMP_FontAsset font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (font != null && font.name.IndexOf("arial", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return font;
                }
            }

            if (box_canvas != null)
            {
                foreach (TMP_Text text in box_canvas.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (text != null && text.font != null)
                    {
                        return text.font;
                    }
                }
            }

            return TMP_Settings.defaultFontAsset;
        }

        private static TMP_FontAsset vcr_font_asset;
        private static bool vcr_font_tried;

        /// <summary>VCR OSD Mono as a TMP font asset, built at most once.</summary>
        private static TMP_FontAsset VcrFontAsset()
        {
            if (vcr_font_tried)
            {
                return vcr_font_asset;
            }

            vcr_font_tried = true;

            // a TMP asset for it may already be in memory (the April Fools prefabs use one)
            TMP_FontAsset tinted = null;

            foreach (TMP_FontAsset font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (font == null || font.name.IndexOf("VCR", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                // "VCR_OSD_MONO_1 green" / "... red" carry a coloured face, which would fight the
                // sight's own colour, so they are only used if nothing plain exists.
                if (font.name.IndexOf("green", StringComparison.OrdinalIgnoreCase) >= 0
                    || font.name.IndexOf("red", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (tinted == null)
                    {
                        tinted = font;
                    }

                    continue;
                }

                vcr_font_asset = font;
                return vcr_font_asset;
            }

            if (tinted != null)
            {
                vcr_font_asset = tinted;
                return vcr_font_asset;
            }

            Font face = VcrTrueTypeFace();

            if (face == null)
            {
                return null;
            }

            try
            {
                TMP_FontAsset built = TMP_FontAsset.CreateFontAsset(face);

                if (built == null || built.material == null || built.material.shader == null)
                {
                    return null;
                }

                built.name = "VCR OSD Mono (CROWS)";
                vcr_font_asset = built;
            }
            catch (Exception)
            {
            // The call is best-effort: a failure here leaves the previous state alone.
            }

            return vcr_font_asset;
        }

        /// <summary>The VCR OSD Mono TrueType face, reached through GHPC's damage-number prefab.</summary>
        private static Font VcrTrueTypeFace()
        {
            foreach (Font font in Resources.FindObjectsOfTypeAll<Font>())
            {
                if (font != null && font.name.IndexOf("VCR", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return font;
                }
            }

            GameObject prefab = Resources.Load<GameObject>("prefabs/fools/FoolsDamageNumber");

            if (prefab == null)
            {
                return null;
            }

            Font first = null;

            foreach (UnityEngine.UI.Text text in prefab.GetComponentsInChildren<UnityEngine.UI.Text>(true))
            {
                if (text == null || text.font == null)
                {
                    continue;
                }

                if (text.font.name.IndexOf("VCR", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return text.font;
                }

                if (first == null)
                {
                    first = text.font;
                }
            }

            return first;
        }

        /// <summary>
        /// Builds the CROWS sight frame: four screen-space canvases drawing the black border strips
        /// around the sight picture, parented to the optic so they come and go with it.
        /// </summary>
        private static void AddFrameBoxes(UsableOptic optic, Transform model_root)
        {
            if (box_canvas == null)
            {
                return;
            }

            if (optic == null)
            {
                return;
            }

            for (int i = 0; i < borders.Count; i++)
            {
                GameObject box = UnityEngine.Object.Instantiate(box_canvas, optic.transform);
                SanitizeBorrowed(box, model_root);

                // box_canvas is cloned from the Bradley GPS canvas, a third-party hierarchy whose child
                // count is only ever "expected" to be three - SanitizeBorrowed already defends against
                // there being more, so this defends against there being none. Without the check the two
                // assignments below threw and took the rest of the border strips with them.
                Transform child = box.transform.childCount > 0 ? box.transform.GetChild(0) : null;

                if (child != null)
                {
                    child.localPosition = borders[i][0];
                    child.localEulerAngles = borders[i][1];
                }

                if (i == 2 || i == 3)
                {
                    CanvasScaler scaler = box.GetComponent<CanvasScaler>();
                    if (scaler != null)
                    {
                        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Shrink;
                    }
                }

                box.SetActive(true);
            }
        }

        /// <summary>
        /// Removes the donor scripts that would move a cloned object out of the CROWS model.
        /// Reparent.Awake() does `transform.parent = NewParent` and the donor's NewParent points at its
        /// own tank, which is how the borrowed gun ended up inside an inactive prefab hierarchy where
        /// nothing on it ran; LateFollow would drag a part after a donor object the same way.
        /// </summary>
        private static void SanitizeBorrowed(GameObject borrowed, Transform model_root)
        {
            if (borrowed == null)
            {
                return;
            }

            foreach (Reparent reparent in borrowed.GetComponentsInChildren<Reparent>(true))
            {
                // DestroyImmediate on purpose: Destroy() would let Awake() run first.
                UnityEngine.Object.DestroyImmediate(reparent);
            }

            foreach (LateFollow follow in borrowed.GetComponentsInChildren<LateFollow>(true))
            {
                Transform target = follow.FollowTarget;

                if (target != null && target.IsChildOf(model_root) && !follow.ForceToRoot)
                {
                    continue;
                }

                UnityEngine.Object.DestroyImmediate(follow);
            }
        }

        /// <summary>
        /// Activates a cloned object and every ancestor up to (but not including) the CROWS model, so
        /// the components on it get their Awake/Start pass together with the model.
        /// </summary>
        private static void EnsureActive(GameObject target, Transform model_root)
        {
            if (target == null)
            {
                return;
            }

            for (Transform t = target.transform; t != null && t != model_root; t = t.parent)
            {
                if (!t.gameObject.activeSelf)
                {
                    t.gameObject.SetActive(true);
                }
            }
        }

        private static bool IsExternal(UnityEngine.Object value, Transform model_root)
        {
            if (value == null)
            {
                return false;
            }

            Component component = value as Component;
            GameObject game_object = value as GameObject;
            Transform transform = component != null
                ? component.transform
                : (game_object != null ? game_object.transform : null);

            // Assets (materials, meshes, scriptable objects) have no transform.
            return transform != null && !transform.IsChildOf(model_root);
        }

        /// <summary>
        /// ReticleMesh.cachedReticles is a private static dictionary whose value type is a private
        /// nested struct, so it is handled as object and read through the non-generic IDictionary view.
        /// </summary>
        private static object CachedReticles()
        {
            return Reflect.GetStatic<object>(typeof(ReticleMesh), "cachedReticles");
        }

        private static Vehicle FindDonor(string[] vehicle_names, string lookup_name)
        {
            // Prefer the game's Addressables prefab. Try both the internal lookup key and every
            // display-name alias because different game builds expose one or the other.
            string[] candidates = new string[vehicle_names.Length + 1];
            candidates[0] = lookup_name;
            Array.Copy(vehicle_names, 0, candidates, 1, vehicle_names.Length);

            for (int i = 0; i < candidates.Length; i++)
            {
                string candidate = candidates[i];
                if (string.IsNullOrEmpty(candidate))
                {
                    continue;
                }

                try
                {
                    Vehicle loaded = AssetUtil.LoadVanillaVehicle(candidate);
                    if (loaded != null)
                    {
                        if (!loaded_donors.Contains(loaded))
                        {
                            loaded_donors.Add(loaded);
                        }

                        return loaded;
                    }
                }
                catch (Exception error)
                {
                    Fail("Addressables template load failed for " + candidate + ": " + error.Message);
                }
            }

            // Compatibility fallback for older builds whose unit catalogue does not expose the prefab.
            // This path is only used when the asset catalogue cannot provide the template.
            foreach (Vehicle vehicle in Resources.FindObjectsOfTypeAll<Vehicle>())
            {
                if (vehicle == null)
                {
                    continue;
                }

                for (int i = 0; i < vehicle_names.Length; i++)
                {
                    if (vehicle.name == vehicle_names[i])
                    {
                        return vehicle;
                    }
                }
            }

            Fail("no usable template for " + lookup_name + " (aliases: " + string.Join(", ", vehicle_names) + ")");
            return null;
        }
        /// <summary>
        /// The vehicles pulled into the scene to borrow parts from, as opposed to ones the mission
        /// deployed. A donor is not a target: drawing a box on it brackets a vehicle that is not in the
        /// level.
        /// </summary>
        private static readonly List<Vehicle> loaded_donors = new List<Vehicle>();

        internal static bool IsDonor(Unit unit)
        {
            PruneLoadedDonors();

            if (unit == null || loaded_donors.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < loaded_donors.Count; i++)
            {
                Vehicle donor = loaded_donors[i];

                if (donor == null)
                {
                    continue;
                }

                if (donor == unit || unit.transform.IsChildOf(donor.transform))
                {
                    return true;
                }
            }

            return false;
        }

        private static GameObject FindChild(Transform root, params string[] paths)
        {
            foreach (string path in paths)
            {
                Transform found = root.Find(path);

                if (found != null)
                {
                    return found.gameObject;
                }
            }

            return null;
        }

        /// <summary>
        /// Switches off the ammunition indicator the borrowed sight canvas brings with it. See where it
        /// is called: it reacts to the gun's own ammunition selection, and its "HE selected" and
        /// "AP selected" labels show H and A at the bottom of the sight whenever the player switches
        /// belts. Named rather than pathed, and every match is taken, because the template is cloned
        /// per optic and a missed one would still show.
        /// </summary>
        private static void HideDonorAmmoIndicator(GameObject canvas)
        {
            if (canvas == null)
            {
                return;
            }

            Transform[] all = canvas.GetComponentsInChildren<Transform>(true);
            int hidden = 0;

            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null)
                {
                    continue;
                }

                string name = all[i].name;

                // The group, and the two labels inside it by name as well: the labels are what is
                // actually seen - two lines each, "H" over "E" and "A" over "P", of which the top line
                // is the one the player reads - so hiding them directly covers a donor whose hierarchy
                // puts them somewhere else, or a second copy the group search would have missed.
                if (name == "autocannon ammo types" || name == "HE selected" || name == "AP selected")
                {
                    all[i].gameObject.SetActive(false);
                    hidden++;
                }
            }

            // The names are deliberately matched rather than the hierarchy, so a renamed or localised
            // donor canvas would otherwise let the indicator reappear with nothing said about it.
            if (hidden == 0)
            {
                Fail("the donor sight canvas has no ammo indicator to hide - the donor hierarchy may have been renamed");
            }
        }
        private static void Fail(string reason)
        {
            if (!string.IsNullOrEmpty(reason))
            {
                setup_failures.Add(reason);
            }
        }

        private static void ReportFailures()
        {
            foreach (string failure in setup_failures)
            {
                MelonLogger.Error("[M1A1] CROWS setup failed: " + failure);
            }
        }
    }
}




