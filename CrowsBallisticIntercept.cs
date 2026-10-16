using System;

namespace M1A1Abrams
{
    // Unity-free so regression shots can use an independent trajectory integrator.
    internal struct CrowsProjectileModel
    {
        internal double MuzzleSpeed, Drag, TemperatureScale, Step;
        internal LeadVector Gravity;
        internal bool ErrorCorrection;

        internal bool Valid
        {
            get
            {
                return CrowsPredictor.Finite(MuzzleSpeed) && MuzzleSpeed >= 100
                    && CrowsPredictor.Finite(Drag) && Drag >= 0 && Drag < 0.1
                    && CrowsPredictor.Finite(TemperatureScale) && TemperatureScale > 0.5 && TemperatureScale < 1.5
                    && CrowsPredictor.Finite(Step) && Step >= 0.004 && Step <= 0.05 && Gravity.Finite;
            }
        }
    }

    internal struct CrowsBallisticSolution
    {
        internal LeadVector Direction;
        internal double FlightTime, Miss;
        internal int Iterations;
        internal bool Valid;
    }

    internal static class CrowsBallisticIntercept
    {
        private const double MaximumTime = 30;
        private const double Tolerance = 0.02; // metres at the intercept, not an angular threshold
        private const double Perturbation = 0.0001;
        private const int MaximumIterations = 8;

        /// <summary>
        /// Solve bore direction and time together. Target motion remains in world space; inherited
        /// vehicle velocity is added ONCE to muzzle velocity and is then subject to the same drag as
        /// the rest of the projectile. Subtracting vehicleVelocity * flightTime assumes no drag and
        /// overcompensates progressively with range. Gravity is integrated at the future intercept
        /// distance too, so a rapidly approaching/receding target gets the correct elevation.
        /// </summary>
        internal static CrowsBallisticSolution Solve(LeadVector line, LeadVector targetVelocity,
            LeadVector targetAcceleration, double confidence, LeadVector inherited,
            CrowsProjectileModel model, CrowsImm motion, double initialTime, CrowsBallisticSolution seed)
        {
            var result = new CrowsBallisticSolution();
            double range = line.Length;
            if (!line.Finite || range < 1 || range > 4500 || !targetVelocity.Finite
                || !targetAcceleration.Finite || !inherited.Finite || !model.Valid
                || targetVelocity.Length > 150 || inherited.Length > 150) return result;

            // The FCS table can clamp at its last elevation/range sample. A clamped time
            // is a poor long-range starting point; estimate the drag flight independently.
            double launchSpeed = (model.MuzzleSpeed + LeadVector.Dot(inherited, line) / range)
                * model.TemperatureScale;
            double dragTime = model.Drag > 1e-9
                ? (Math.Exp(Math.Min(8, model.Drag * range)) - 1) / (model.Drag * launchSpeed)
                : range / launchSpeed;
            double time = CrowsPredictor.Clamp(dragTime, 0.001, MaximumTime * 0.95);
            if (CrowsPredictor.Finite(initialTime) && initialTime > time * 0.7 && initialTime < time * 1.4)
                time = Math.Min(initialTime, MaximumTime * 0.95);
            LeadVector first = line + targetVelocity * time - inherited * (range / model.MuzzleSpeed)
                - model.Gravity * (0.5 * time * time);
            LeadVector direction = first * (1 / first.Length);
            if (seed.Valid && seed.Direction.Finite && seed.FlightTime > 0 && seed.FlightTime < MaximumTime
                && Math.Abs(seed.FlightTime - time) < Math.Max(0.25, time * 0.35)
                && LeadVector.Dot(seed.Direction, direction) > 0.98)
            {
                direction = seed.Direction;
                time = seed.FlightTime;
            }

            for (int iteration = 0; iteration < MaximumIterations; iteration++)
            {
                LeadVector hit, speed;
                if (!Fly(direction, inherited, time, model, out hit, out speed)) return result;
                LeadVector target = line + Displace(motion, targetVelocity, targetAcceleration, confidence, time);
                LeadVector error = target - hit;
                result.Iterations = iteration + 1;
                result.Miss = error.Length;
                if (!error.Finite) return result;
                if (result.Miss <= Tolerance)
                {
                    result.Direction = direction;
                    result.FlightTime = time;
                    result.Valid = true;
                    return result;
                }

                LeadVector axis = Math.Abs(direction.Y) < 0.9 ? new LeadVector(0, 1, 0) : new LeadVector(1, 0, 0);
                LeadVector right = LeadVector.Cross(axis, direction);
                right = right * (1 / right.Length);
                LeadVector up = LeadVector.Cross(direction, right);
                LeadVector rightDirection = direction + right * Perturbation;
                LeadVector upDirection = direction + up * Perturbation;
                rightDirection = rightDirection * (1 / rightDirection.Length);
                upDirection = upDirection * (1 / upDirection.Length);

                LeadVector rightHit, upHit, laterHit, ignored;
                if (!Fly(rightDirection, inherited, time, model, out rightHit, out ignored)
                    || !Fly(upDirection, inherited, time, model, out upHit, out ignored)
                    || !Fly(direction, inherited, time + 0.001, model, out laterHit, out ignored)) return result;
                LeadVector a = (rightHit - hit) * (1 / Perturbation);
                LeadVector b = (upHit - hit) * (1 / Perturbation);
                LeadVector laterTarget = line + Displace(motion, targetVelocity, targetAcceleration, confidence, time + 0.001);
                LeadVector c = ((laterHit - hit) - (laterTarget - target)) * 1000;
                double determinant = LeadVector.Dot(a, LeadVector.Cross(b, c));
                if (!CrowsPredictor.Finite(determinant) || Math.Abs(determinant) < 1e-6) return result;
                double yaw = LeadVector.Dot(error, LeadVector.Cross(b, c)) / determinant;
                double pitch = LeadVector.Dot(a, LeadVector.Cross(error, c)) / determinant;
                double dt = LeadVector.Dot(a, LeadVector.Cross(b, error)) / determinant;
                if (!CrowsPredictor.Finite(yaw) || !CrowsPredictor.Finite(pitch) || !CrowsPredictor.Finite(dt)) return result;

                // A failed/near-unreachable shot must not manufacture a huge correction.
                double scale = Math.Max(1, Math.Max(Math.Sqrt(yaw * yaw + pitch * pitch) / 0.2,
                    Math.Abs(dt) / Math.Max(0.25, time * 0.4)));
                direction = direction + right * (yaw / scale) + up * (pitch / scale);
                direction = direction * (1 / direction.Length);
                time += dt / scale;
                if (!CrowsPredictor.Finite(time) || time <= 0 || time >= MaximumTime) return result;
            }
            return result; // only a converged, verified shot is handed to the FCS
        }

        private static LeadVector Displace(CrowsImm motion, LeadVector velocity, LeadVector acceleration,
            double confidence, double time)
        {
            return motion == null ? CrowsPredictor.TargetDisplacement(velocity, acceleration, confidence, time)
                : motion.Displace(velocity, acceleration, confidence, time);
        }

        private static LeadVector Acceleration(LeadVector velocity, CrowsProjectileModel model)
        {
            return model.Gravity - velocity * (model.Drag * velocity.Length);
        }

        private static bool Fly(LeadVector direction, LeadVector inherited, double time,
            CrowsProjectileModel model, out LeadVector position, out LeadVector velocity)
        {
            position = new LeadVector();
            // WeaponSystem adds hull velocity before LiveRound scales the full launch vector
            // by ammunition temperature. No angular GetPointVelocity term is added by the game.
            velocity = (direction * model.MuzzleSpeed + inherited) * model.TemperatureScale;
            if (!CrowsPredictor.Finite(time) || time <= 0 || time > MaximumTime + 0.01) return false;
            int count = (int)Math.Ceiling(time / model.Step);
            if (count > 7600) return false;
            for (int i = 0; i < count; i++)
            {
                double dt = model.Step;
                LeadVector before = position;
                LeadVector acceleration = Acceleration(velocity, model);
                if (model.ErrorCorrection)
                {
                    // Match BallisticEvaluatorRK4, including its extra full-step evaluation
                    // before the weighted four stages; ordinary RK4 has a different result.
                    LeadVector v2 = velocity + acceleration * dt;
                    LeadVector a2 = Acceleration(v2, model);
                    LeadVector v3 = velocity + a2 * (dt * 0.5);
                    LeadVector a3 = Acceleration(v3, model);
                    LeadVector v4 = velocity + a3 * (dt * 0.5);
                    LeadVector a4 = Acceleration(v4, model);
                    LeadVector v5 = velocity + a4 * dt;
                    LeadVector a5 = Acceleration(v5, model);
                    position = position + (v2 + v3 * 2 + v4 * 2 + v5) * (dt / 6);
                    velocity = velocity + (a2 + a3 * 2 + a4 * 2 + a5) * (dt / 6);
                }
                else
                {
                    position = position + velocity * dt;
                    velocity = velocity + acceleration * dt;
                }
                // LiveRound raycasts each complete frame's segment. The intercept can lie
                // inside its last segment; shortening the integrator step changes that segment.
                if (i == count - 1)
                    position = before + (position - before) * ((time - i * model.Step) / model.Step);
            }
            return position.Finite && velocity.Finite;
        }
    }
}
