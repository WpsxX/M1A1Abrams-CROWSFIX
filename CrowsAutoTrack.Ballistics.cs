using GHPC;
using GHPC.Weapons;
using GHPC.World;
using ModUtil;
using UnityEngine;

namespace M1A1Abrams
{
    public partial class CrowsAutoTrack
    {
        private CrowsBallisticSolution ballistic_solution;
        private int ballistic_solution_frame = -1;
        private int ballistic_target;
        private Rigidbody ballistic_target_body;
        private AmmoType ballistic_round;
        private Vector3 ballistic_line, ballistic_inherited;

        private Vector3 InheritedLaunchVelocity()
        {
            // Attach runs before EnsureActive/Awake. A null cached there is not proof that
            // the weapon has no rigidbody; read it again once Awake has bound the host hull.
            if (weapon_rb == null && weapon != null)
                weapon_rb = Reflect.Get<Rigidbody>(weapon, "_rb");
            return weapon_rb == null ? Vector3.zero : weapon_rb.velocity;
        }

        private Vector3 BallisticTargetPoint()
        {
            Vector3 point = target.transform.TransformPoint(box_centre_local);
            if (ballistic_target_body != null)
            {
                // An interpolated Transform can be a physics tick behind Rigidbody.position.
                // Put the selected point into the current physics pose before predicting it.
                Transform visual = ballistic_target_body.transform;
                point = ballistic_target_body.position
                    + ballistic_target_body.rotation * Quaternion.Inverse(visual.rotation) * (point - visual.position);
            }
            float age = Mathf.Clamp(Time.time - Time.fixedTime, 0f, Time.fixedDeltaTime * 2f);
            // The round starts at the actual muzzle transform. Platform travel belongs in its
            // launch velocity, not in a second translation of the target's world position.
            return point + latest_target_velocity * age;
        }

        private bool TryGetBallisticLead(Vector3 inherited, float tableTime, out Vector3 offset)
        {
            offset = Vector3.zero;
            AmmoType ammo = weapon == null || weapon.Feed == null ? null : weapon.Feed.AmmoTypeInBreech;
            if (ammo == null) ammo = fcs.CurrentAmmoType;
            if (ammo == null || ammo.NoLeadCompensation || ammo.Guidance != AmmoType.GuidanceType.Unguided
                || ammo.Mass <= 0 || target == null) return false;

            // Solve from the current target position with the player's aim offset. The visual
            // aim is low-passed and may lag a fast target by metres; a fixed 80 ms guess for
            // that lag cannot be correct at every frame rate/speed. Keep sight smoothing and
            // solve the shot independently, without adding an assumed trigger delay.
            int targetId = target.GetInstanceID();
            if (targetId != ballistic_target || ballistic_round != ammo)
            {
                ballistic_solution = new CrowsBallisticSolution();
                ballistic_solution_frame = -1;
                ballistic_target = targetId;
                ballistic_round = ammo;
                ballistic_target_body = target.Chassis == null ? null : target.Chassis.Rigidbody;
                if (ballistic_target_body == null)
                    ballistic_target_body = target.GetComponentInChildren<Rigidbody>();
            }
            Vector3 line = OffsetAimPoint(BallisticTargetPoint()) - MuzzlePosition();
            Vector3 baseline = fcs.CompensatedWeaponVector(false); // no lead => no recursive Harmony call
            Vector3 axis = fcs.AimWorldVector.normalized;

            // FCS, both optics, UI and pre-cull can request the same solution repeatedly.
            // Reuse within a frame unless the muzzle/target or inherited velocity changed.
            if (ballistic_solution_frame != Time.frameCount
                || (line - ballistic_line).sqrMagnitude > 0.0004f
                || (inherited - ballistic_inherited).sqrMagnitude > 0.0001f)
            {
                var model = new CrowsProjectileModel
                {
                    MuzzleSpeed = ammo.MuzzleVelocity,
                    Drag = ammo.Coeff * ammo.SectionalArea * WorldEnvironmentManager.AirDensity / ammo.Mass,
                    TemperatureScale = 1 + 0.0004 * (WorldEnvironmentManager.AmmoTempFahrenheit - 70),
                    Gravity = MathVector(Physics.gravity),
                    Step = Mathf.Clamp(Time.smoothDeltaTime, 1f / 240f, 0.05f),
                    ErrorCorrection = ammo.UseErrorCorrection
                };
                ballistic_solution = CrowsBallisticIntercept.Solve(MathVector(line),
                    MathVector(RelativeLeadVelocity(Vector3.zero)), MathVector(LeadAcceleration()),
                    LeadConfidence(), MathVector(inherited), model, LeadMotionMixture(), tableTime, ballistic_solution);
                ballistic_solution_frame = Time.frameCount;
                ballistic_line = line;
                ballistic_inherited = inherited;
            }
            if (!ballistic_solution.Valid) return false;

            Vector3 bore = UnityVector(ballistic_solution.Direction);
            float along = Vector3.Dot(bore, axis);
            float distance = Vector3.Dot(baseline, axis);
            if (along < 0.5f || distance < 1f) return false;
            // FCS subtracts the lead and then adds its table drop/parallax. Encode the complete
            // firing direction relative to that baseline, cancelling the old drop exactly once.
            offset = baseline - bore * (distance / along);
            if (float.IsNaN(offset.sqrMagnitude) || float.IsInfinity(offset.sqrMagnitude)) return false;
            predictor_solution = new CrowsIntercept
            {
                Valid = true, Converged = true,
                FlightTime = ballistic_solution.FlightTime,
                TransverseDisplacement = MathVector(-offset),
                Residual = ballistic_solution.Miss,
                Iterations = ballistic_solution.Iterations
            };
            return true;
        }
    }
}
