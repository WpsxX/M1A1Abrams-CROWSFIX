using System;

namespace M1A1Abrams
{
    // Deliberately Unity-free: the same source is compiled by the executable regression tests.
    internal struct LeadVector
    {
        internal double X, Y, Z;
        internal LeadVector(double x, double y, double z) { X = x; Y = y; Z = z; }
        internal double Length { get { return Math.Sqrt(Dot(this, this)); } }
        internal bool Finite { get { return CrowsPredictor.Finite(X) && CrowsPredictor.Finite(Y) && CrowsPredictor.Finite(Z); } }
        public static LeadVector operator +(LeadVector a, LeadVector b) { return new LeadVector(a.X+b.X,a.Y+b.Y,a.Z+b.Z); }
        public static LeadVector operator -(LeadVector a, LeadVector b) { return new LeadVector(a.X-b.X,a.Y-b.Y,a.Z-b.Z); }
        public static LeadVector operator *(LeadVector a, double b) { return new LeadVector(a.X*b,a.Y*b,a.Z*b); }
        internal static double Dot(LeadVector a, LeadVector b) { return a.X*b.X+a.Y*b.Y+a.Z*b.Z; }
        internal static LeadVector Limit(LeadVector a, double maximum) { double n=a.Length; return n>maximum ? a*(maximum/n) : a; }
        internal static LeadVector Cross(LeadVector a, LeadVector b)
        {
            return new LeadVector(a.Y*b.Z-a.Z*b.Y, a.Z*b.X-a.X*b.Z, a.X*b.Y-a.Y*b.X);
        }
    }

    internal sealed class CrowsMotionSampler
    {
        internal LeadVector Velocity, Acceleration;
        internal double Confidence { get; private set; }
        internal int Samples { get; private set; }
        internal double Age { get; private set; }
        private LeadVector previous;
        private double time;
        internal bool Fresh(double now) { return Samples >= 2 && now >= time && now-time <= 0.15; }
        private int target;
        internal void Reset() { Samples=0; Age=0; Confidence=0; Velocity=Acceleration=previous=new LeadVector(); }
        internal bool Sample(int targetId, double physicsTime, LeadVector raw, double flight)
        {
            if (!raw.Finite || !CrowsPredictor.Finite(physicsTime) || raw.Length>120) { Reset(); return false; }
            if (Samples>0 && targetId==target && physicsTime==time) return false;
            double dt=physicsTime-time;
            if (Samples==0 || targetId!=target || dt<=0 || dt>0.15)
            {
                Reset(); target=targetId; time=physicsTime; previous=Velocity=raw; Samples=1; return true;
            }
            time=physicsTime; Age+=dt; Samples++;
            // Preserve the prior adaptive velocity filter, but integrate it once per physics tick.
            double f=CrowsPredictor.Finite(flight) ? Math.Max(1,flight) : 1;
            double smoothing=2*CrowsPredictor.Clamp(f,1,4);
            if(raw.Length<1) smoothing=Math.Max(smoothing,6);
            double manoeuvre=CrowsPredictor.Clamp(((raw-Velocity).Length*f-0.15)/0.85,0,1);
            smoothing+= (30-smoothing)*manoeuvre;
            Velocity=Velocity+(raw-Velocity)*(1-Math.Exp(-smoothing*dt));
            LeadVector measured=(raw-previous)*(1/dt); previous=raw;
            // A collision/teleport is not evidence for future sustained acceleration.
            if(measured.Length>12 || raw.Length<0.3) { Confidence=0; Acceleration=new LeadVector(); return true; }
            bool coherent=measured.Length>0.15 && (Acceleration.Length<0.15 ||
                LeadVector.Dot(measured,Acceleration)>0.7*measured.Length*Acceleration.Length);
            Confidence=coherent ? Math.Min(0.65,Confidence+dt*2) : Math.Max(0,Confidence-dt*8);
            Acceleration=Acceleration+(LeadVector.Limit(measured,4)-Acceleration)*(1-Math.Exp(-6*dt));
            if(Age<0.2) Confidence=0;
            return true;
        }
    }

    /// <summary>
    /// The five motion hypotheses the carried lead is blended from: a target that has stopped, one
    /// holding a straight line, one carrying an acceleration, one holding a turn, and one braking to a
    /// stop. Straight-line is the fallback the design note asks for and every other answer has to earn
    /// its way past it.
    ///
    /// Every hypothesis is a closed form over the same filtered state - relative position, velocity,
    /// acceleration - so five of them cost no allocation, keep no per-model state that could diverge,
    /// and can be checked against each other offline. What decides between them is measurement: each
    /// is asked what the last half second of the target's motion should have looked like from points
    /// inside that window, and is weighted down by how wrong it was.
    ///
    /// The window is the point. Compared over one physics step the five forms differ by a few
    /// millimetres and the residuals are noise; compared over half a second they differ by what a two
    /// second flight would miss by, which is the quantity the lead is actually made of.
    ///
    /// Weights are recursive and can never reach zero: a hypothesis that stops being right - a target
    /// that straightens out of a turn, a brake that is released - has to be able to earn its weight
    /// back, which is the failure a plain argmax switch locks itself into.
    /// </summary>
    internal sealed class CrowsImm
    {
        internal const int Stationary = 0;
        internal const int Constant = 1;
        internal const int Accelerating = 2;
        internal const int Turning = 3;
        internal const int Braking = 4;
        internal const int Models = 5;

        /// <summary>
        /// How wrong each hypothesis may be, in metres, before it loses weight: a weight update is
        /// driven by the displacement a hypothesis predicted over a lag less the displacement measured
        /// over it. Metres and not metres per second on purpose - the residual then grows with the lag
        /// the way the quantity the lead is made of does, so a model that is a few centimetres out
        /// over a fifth of a second cannot outvote one that is right over a second and a half, which
        /// is the only span on which a brake and a carried acceleration can be told apart at all. The
        /// straight-line and stopped forms are held tightest and the manoeuvre forms loosest: a
        /// manoeuvre is only worth believing once the straight line has clearly failed.
        /// </summary>
        private static readonly double[] Residual = { 0.5, 0.4, 0.7, 0.9, 0.8 };

        /// <summary>How much of every update goes to every hypothesis regardless, per the mixing step of an IMM.</summary>
        private const double Mixing = 0.04;

        /// <summary>
        /// The share of the belief a hypothesis needs before its answer is worth blending at all: an
        /// even share. Under it no hypothesis is describing the target well enough to be preferred to
        /// the straight line, which needs no evidence at all.
        /// </summary>
        private const double Trust = 1.0 / Models;

        /// <summary>
        /// The lags the hypotheses are scored over, in seconds, doubling from a fifth of a second to
        /// a second and a half.
        ///
        /// The short lags are what let the weights follow a target that changes what it is doing; the
        /// long ones are what let them tell the manoeuvre models apart at all. Over 0.2 s a brake and
        /// a carried acceleration predict the same millimetres, and only a lag long enough for the
        /// difference to become a metre says anything worth weighing. One window cannot do both jobs,
        /// and scoring every slot in the ring would count the same measurement several times.
        /// </summary>
        private static readonly double[] Lag = { 0.1, 0.2, 0.4, 0.8, 1.6 };

        /// <summary>
        /// Fixed ring of measured motion, long enough at 50 Hz to hold the oldest lag with headroom
        /// and therefore longer at lower physics rates rather than shorter. Preallocated: a tick
        /// allocates nothing.
        /// </summary>
        private const int Slots = 96;

        private readonly double[] weight = { 0, 1, 0, 0, 0 };
        private readonly double[] error = new double[Models];
        private readonly int[] found = new int[Lag.Length];
        // The window ring, taken on the first sample rather than when the tracker is built; see
        // CrowsUkf.TakeStorage for why a station that never locks should not be paying for it.
        private double[] slot_time;
        private double[] slot_confidence;
        private LeadVector[] slot_position;
        private LeadVector[] slot_velocity;
        private LeadVector[] slot_acceleration;

        private int head, count, updates;
        private int dominant = Constant;
        private double last;

        /// <summary>Samples held in the window ring; diagnostics only.</summary>
        internal int Samples { get { return count; } }

        /// <summary>Whether enough motion has been measured for a weight update to mean anything.</summary>
        internal bool Primed { get { return updates > 0; } }

        /// <summary>The hypothesis that has held the lead long enough to be the one believed.</summary>
        internal int Dominant { get { return dominant; } }

        internal double Weight(int model) { return model >= 0 && model < Models ? weight[model] : 0; }

        internal void Reset()
        {
            weight[0] = 0; weight[1] = 1; weight[2] = 0; weight[3] = 0; weight[4] = 0;
            head = 0; count = 0; updates = 0;
            dominant = Constant; last = 0;
        }

        /// <summary>
        /// One physics tick of measured motion in the hull's frame: where the target is, how fast it is
        /// going, how fast that is changing, and what the acceleration estimate is worth. The interval
        /// and the displacement are taken from the measurements themselves, so a gap, a switch of
        /// target or a teleport is a reset rather than a step nothing could have predicted.
        /// </summary>
        internal bool Sample(double now, LeadVector position, LeadVector velocity, LeadVector acceleration, double confidence)
        {
            if (!CrowsPredictor.Finite(now) || !position.Finite || !velocity.Finite || !acceleration.Finite)
            {
                Reset();
                return false;
            }

            if (count > 0)
            {
                double step = now - last;
                if (step <= 0 || step > 0.15 || (position - slot_position[head]).Length > 120 * step)
                {
                    Reset();
                    return false;
                }
            }

            if (slot_time == null) TakeRing();
            head = count == 0 ? 0 : (head + 1) % Slots;
            if (count < Slots) count++;
            slot_time[head] = now;
            slot_position[head] = position;
            slot_velocity[head] = velocity;
            slot_acceleration[head] = acceleration;
            slot_confidence[head] = confidence;
            last = now;

            Weigh();
            return true;
        }

        /// <summary>
        /// Scores every hypothesis over the lag set and moves the weights. One update per physics tick
        /// from residuals that already average the lags, rather than one per lag: the lags are said
        /// over the same motion, and treating each as fresh evidence would let a single manoeuvre bury
        /// a hypothesis for good.
        /// </summary>
        private void TakeRing()
        {
            slot_time = new double[Slots];
            slot_confidence = new double[Slots];
            slot_position = new LeadVector[Slots];
            slot_velocity = new LeadVector[Slots];
            slot_acceleration = new LeadVector[Slots];
        }

        private void Weigh()
        {
            int remaining = Lag.Length;
            for (int l = 0; l < Lag.Length; l++) found[l] = -1;

            // Walking back from the newest sample, the first slot at or past a lag is the closest
            // measurement to it; the ring is ordered by age, so the walk covers every lag in one pass.
            for (int back = 1; back < count && remaining > 0; back++)
            {
                int index = (head - back + Slots * 2) % Slots;
                double age = last - slot_time[index];
                for (int l = 0; l < Lag.Length; l++)
                {
                    if (found[l] >= 0 || age < Lag[l]) continue;
                    found[l] = index;
                    remaining--;
                }
            }

            int used = 0;
            for (int i = 0; i < Models; i++) error[i] = 0;

            for (int l = 0; l < Lag.Length; l++)
            {
                int index = found[l];
                if (index < 0) continue;
                double age = last - slot_time[index];
                LeadVector measured = slot_position[head] - slot_position[index];
                used++;
                for (int i = 0; i < Models; i++)
                {
                    LeadVector predicted = Hypothesis(i, slot_velocity[index], slot_acceleration[index],
                        slot_confidence[index], age);
                    double residual = (predicted - measured).Length;
                    error[i] += residual * residual;
                }
            }

            if (used < 2) return;

            double total = 0;
            for (int i = 0; i < Models; i++)
            {
                double mean = error[i] / used;
                weight[i] *= Math.Exp(-mean / (2 * Residual[i] * Residual[i]))
                    * Premise(i, slot_velocity[head], slot_acceleration[head], slot_confidence[head]);
                total += weight[i];
            }

            for (int i = 0; i < Models; i++)
            {
                weight[i] = total > 1e-9
                    ? (1 - Mixing) * (weight[i] / total) + Mixing / Models
                    : 1.0 / Models;
            }

            // Which hypothesis is carrying the answer, taken from the weights as they stand. There is no
            // dwell: the multi-lag likelihood and mixing floor keep one bad window from handing the lead
            // away, and no weight can reach zero, so
            // a single bad window cannot hand the lead away, and waiting a few updates before believing
            // the leader only makes the answer arrive late for a manoeuvre.
            dominant = 0;
            for (int i = 1; i < Models; i++) if (weight[i] > weight[dominant]) dominant = i;
            updates++;
        }

        /// <summary>
        /// A manoeuvre model that has become algebraically identical to the straight-line model must
        /// still release its old probability. Without this premise likelihood a turn whose measured
        /// lateral acceleration falls to zero predicts the same displacement as Constant and keeps its
        /// historic weight forever. The decay is deliberately slow enough that one noisy sample cannot
        /// switch models, but at 50 Hz it relinquishes a manoeuvre in roughly a second.
        /// </summary>
        private static double Premise(int model, LeadVector velocity, LeadVector acceleration, double confidence)
        {
            double speed = velocity.Length;
            double trusted = CrowsPredictor.Clamp(confidence, 0, 0.65);
            if (model == Accelerating) return trusted > 0.05 && acceleration.Length > 0.15 ? 1.0 : 0.97;
            if (model == Turning)
            {
                if (speed < 0.3 || trusted <= 0.05) return 0.96;
                LeadVector parallel = velocity * (LeadVector.Dot(acceleration, velocity) / (speed * speed));
                return (acceleration - parallel).Length / speed >= 0.02 ? 1.0 : 0.96;
            }
            if (model == Braking)
            {
                if (speed < 0.3 || trusted <= 0.05) return 0.96;
                double slowing = -LeadVector.Dot(acceleration, velocity) / speed;
                return slowing >= 0.05 ? 1.0 : 0.96;
            }
            return 1.0;
        }

        /// <summary>
        /// The blend's displacement over a horizon: every hypothesis' own answer, weighted. Until a
        /// weight update has been possible - two lags inside the window, so about a tenth of a second
        /// of lock - there is nothing to blend and the single-model solve is returned unchanged, which
        /// is what keeps an acquiring lock behaving exactly as it did before the mixture existed. With
        /// no hypothesis above an even share of the belief, the straight line is what is left.
        /// </summary>
        internal LeadVector Displace(LeadVector velocity, LeadVector acceleration, double confidence, double time)
        {
            if (!Primed) return CrowsPredictor.TargetDisplacement(velocity, acceleration, confidence, time);
            if (weight[dominant] < Trust) return Hypothesis(Constant, velocity, acceleration, confidence, time);

            // The mixing floor is what keeps a wrong hypothesis able to recover; it is not a belief,
            // so it is taken back out of the blend rather than allowed a vote. Left in, it would bias
            // every answer toward whichever hypothesis sits at the floor - the stopped one, for a
            // target that is plainly moving - and a lead a percent short is a miss at range.
            LeadVector sum = new LeadVector();
            double share = 0;
            for (int i = 0; i < Models; i++)
            {
                double w = weight[i] - Mixing / Models;
                if (w <= 0) continue;
                sum = sum + Hypothesis(i, velocity, acceleration, confidence, time) * w;
                share += w;
            }
            LeadVector result = share > 1e-9
                ? sum * (1 / share)
                : Hypothesis(Constant, velocity, acceleration, confidence, time);

            // However the blend came out, its vertical part has to fit inside what the target's own
            // vertical travel plus a bounded vertical acceleration could account for. A hull pitching
            // over rough ground is metres per second squared of vertical acceleration that is not a
            // climb, and a blend free to arc through it turns a bump into a metre of lead pointed at
            // the sky. The carried hypotheses apply the same bound to their own terms; this applies it
            // to the answer, so no hypothesis can route around it. A target actually climbing carries
            // it in its vertical velocity, which this leaves alone.
            if (CrowsPredictor.Finite(time) && time > 0)
            {
                double vertical = Math.Min(time, CrowsPredictor.VerticalHorizon);
                double centre = velocity.Y * time;
                double allowance = CrowsPredictor.VerticalAcceleration * (vertical * (time - 0.5 * vertical));
                result.Y = CrowsPredictor.Clamp(result.Y, centre - allowance, centre + allowance);
            }
            return result;
        }

        /// <summary>The closed form each hypothesis contributes; see the class comment.</summary>
        internal static LeadVector Hypothesis(int model, LeadVector velocity, LeadVector acceleration,
            double confidence, double time)
        {
            switch (model)
            {
                case Stationary: return new LeadVector();
                case Constant: return velocity * time;
                case Turning: return CrowsPredictor.Carried(velocity, acceleration, confidence, time, true, true);
                case Braking: return CrowsPredictor.Braking(velocity, acceleration, confidence, time);
                default: return CrowsPredictor.Carried(velocity, acceleration, confidence, time, true, false);
            }
        }
    }

    /// <summary>
    /// The target's relative state as a nine-state unscented Kalman filter: position, velocity and
    /// acceleration, with the target's position - and, where the game reports one that is not the
    /// transform's own difference, its velocity - as measurements. The process and measurement models
    /// are currently affine, but the full sigma-point transform is retained so the estimator remains
    /// correct when the relative-frame or sensor model becomes nonlinear.
    ///
    /// What the filter does is unchanged, and it is where the accuracy came from: the acceleration is a
    /// state with a jerk-noise model behind it rather than a difference of a smoothed signal, so a target
    /// holding a steady manoeuvre is estimated from the whole history, and the covariance it carries is
    /// what lets the acceleration carry be gated on that acceleration being statistically real. It is
    /// honest about itself, too: over a straight line with 0.15 m of measurement noise the position
    /// estimate settles at 0.057 m rms while claiming a sigma of 0.058 m.
    ///
    /// Everything around that is unchanged: the normalised-innovation gate, the bounded adaptation of
    /// the jerk, the two-point warm start, the restart from any non-finite or non-positive-definite
    /// step, and the storage taken lazily on the first update rather than at construction.
    ///
    /// Like the rest of this module the filter is gravity-referenced: a yaw of the whole scene passes
    /// through it unchanged.
    /// </summary>
    internal sealed class CrowsUkf
    {
        internal const int States = 9;
        internal const int MaxMeasurements = 6;
        private const int SigmaPoints = States * 2 + 1;
        private const double UkfAlpha = 0.5;
        private const double UkfBeta = 2.0;
        private const double UkfKappa = 0.0;

        /// <summary>State offsets: position, velocity, acceleration, three world axes each.</summary>
        private const int Pos = 0, Vel = 3, Acc = 6;

        /// <summary>
        /// Sigma point spread and weighting. Alpha sets how far the points sit from the mean - small
        /// enough to be a local linearisation, large enough that the spread does not vanish into the
        /// arithmetic - beta carries the prior knowledge that the state is Gaussian, and kappa is the
        /// usual second-order correction, left at zero because the measurement model here is linear.
        /// </summary>

        /// <summary>
        /// Jerk the process model allows, in m/s^3: how hard this filter believes a vehicle can change
        /// its acceleration between one tick and the next. It is the single tuning number that decides
        /// the trade between following a manoeuvre and being led by noise, and it is adapted inside the
        /// bounds below by how well the filter is explaining its own measurements; see Adapt.
        /// </summary>
        private const double NominalJerk = 4.0;
        private const double MinimumJerk = 1.0;
        private const double MaximumJerk = 16.0;

        /// <summary>
        /// Measurement noise: how exactly the target's position relative to the hull is known, and how
        /// exactly its velocity is known where the game reports one. The position is read from a
        /// transform the physics step advanced, so the error is sub-decimetre rather than exact; the
        /// velocity is a solver value and is smoother still, but it can be one tick stale.
        /// </summary>
        private const double PositionNoise = 0.15;
        private const double VelocityNoise = 0.25;

        /// <summary>Prior spread when the filter has nothing but a position: 25 m/s and 8 m/s^2.</summary>
        private const double InitialVelocity = 25.0;
        private const double InitialAcceleration = 8.0;

        /// <summary>
        /// A measurement this far from what the filter expected, in its own covariance, is not the
        /// target moving - it is a teleport, a switch of target, or a bad frame. Skipped, never
        /// absorbed; see Update. The thresholds are the chi-square 0.999 points for three and six
        /// measurements.
        /// </summary>
        private const double GateThree = 16.3;
        private const double GateSix = 22.5;
        private const int MaximumRejections = 6;

        /// <summary>An acceleration smaller than this is not carried at all, whatever its sigma says.</summary>
        private const double AccelerationFloor = 0.15;

        private readonly double[] x = new double[States];
        private readonly double[] x_pred = new double[States];
        private readonly double[] covariance = new double[States * States];
        private readonly double[] covariance_pred = new double[States * States];
        // Sigma point, gain and decomposition storage. Deliberately taken on the first update rather
        // than here: a station carries a tracker whether or not it is ever the one holding a lock, and
        // there is no reason for every one of them to hold seven kilobytes of scratch it will never
        // touch. Nothing in this class allocates per tick, and Reset does not release it - a filter
        // that takes and drops its buffers on every target switch is worse than the memory it saves.

        private double[] innovation;
        private double[] s;
        private double[] s_factor;

        private double[] gain;
        private double[] scratch;
        private double[] compact;

        private double[] forward;
        private double[] rhs;
        private double[] solved;
        private double[] sigma;
        private double[] sigma_pred;
        private double[] sigma_measure;
        private double[] weight_mean;
        private double[] weight_covariance;
        private double[] state_factor;
        private double[] measurement_mean;
        private double[] cross_covariance;

        private double jerk;
        private double last;
        private bool seeded;
        private int samples, rejections;
        private double nis;

        internal CrowsUkf()
        {

            Reset();
        }

        /// <summary>Whether the filter may be believed: seeded, converged on at least one update, finite.</summary>
        internal bool Healthy { get; private set; }

        internal int Samples { get { return samples; } }
        internal int Rejections { get { return rejections; } }
        internal double Nis { get { return nis; } }
        internal double Jerk { get { return jerk; } }

        internal LeadVector Position { get { return At(Pos); } }
        internal LeadVector Velocity { get { return At(Vel); } }
        internal LeadVector Acceleration { get { return At(Acc); } }

        internal double PositionSigma { get { return Sigma(Pos); } }
        internal double VelocitySigma { get { return Sigma(Vel); } }
        internal double AccelerationSigma { get { return Sigma(Acc); } }

        /// <summary>
        /// How much of the acceleration estimate is worth carrying, on the same 0..0.65 scale the
        /// sampler's confidence uses so the models cannot tell which estimator fed them.
        ///
        /// It is the acceleration measured against its own uncertainty: an acceleration three sigma
        /// clear of zero is real and is carried in full, one that is not is carried in proportion, and
        /// one below the floor is not carried at all. That is the whole difference from the heuristic
        /// it replaces, which asked whether successive differences agreed with each other rather than
        /// whether the resulting acceleration was larger than the filter's own error bars.
        /// </summary>
        internal double Confidence
        {
            get
            {
                double magnitude = Acceleration.Length;
                if (!CrowsPredictor.Finite(magnitude) || magnitude < AccelerationFloor) return 0;
                double sigma = Math.Max(AccelerationSigma, 1e-3);
                return 0.65 * CrowsPredictor.Clamp(magnitude / (3 * sigma), 0, 1);
            }
        }

        /// <summary>
        /// The constant-acceleration prediction of the state over a horizon: where the target will be
        /// relative to where it is now. Offered for the same reason the mixture's hypotheses are - the
        /// filter holds the state the lead is extrapolated from.
        /// </summary>
        internal LeadVector Displacement(double time)
        {
            LeadVector p = Position, v = Velocity, a = Acceleration;
            if (!p.Finite || !v.Finite || !a.Finite || !CrowsPredictor.Finite(time) || time <= 0) return new LeadVector();
            return v * time + a * (0.5 * time * time);
        }

        internal void Reset()
        {
            Array.Clear(x, 0, States);
            Array.Clear(x_pred, 0, States);
            Array.Clear(covariance, 0, States * States);
            Array.Clear(covariance_pred, 0, States * States);
            seeded = false;
            Healthy = false;
            samples = 0;
            rejections = 0;
            nis = 0;
            jerk = NominalJerk;
            last = 0;
        }

        /// <summary>
        /// One physics tick. `position` and `velocity` are the target's state relative to the hull in
        /// world axes; pass velocityValid false when the velocity came from differencing the same
        /// transform the position did, because then it is not an independent measurement and feeding
        /// it back in would make the filter believe its own arithmetic twice.
        /// </summary>
        internal bool Update(double now, LeadVector position, LeadVector velocity, bool velocityValid)
        {
            if (!position.Finite || !CrowsPredictor.Finite(now) || (velocityValid && !velocity.Finite))
            {
                Reset();
                return false;
            }

            if (!seeded)
            {
                Seed(position);
                last = now;
                return false;
            }

            if (sigma == null) TakeStorage();

            double dt = now - last;
            if (dt <= 1e-4 || dt > 0.15)
            {
                // A gap, a switch of target, or two samples on one tick: nothing here is a step this
                // filter could have predicted, so it starts again from what it can see.
                Reset();
                Seed(position);
                last = now;
                return false;
            }
            last = now;

            if (samples == 0) WarmStart(position, dt);

            if (!Predict(dt)) { Reset(); Seed(position); last = now; return false; }

            double m = Measure(position, velocity, velocityValid);
            if (!CrowsPredictor.Finite(m))
            {
                // The innovation covariance would not decompose: the covariance has lost positive
                // definiteness and nothing downstream of it would mean anything.
                Reset();
                Seed(position);
                last = now;
                return false;
            }

            int count = velocityValid ? 6 : 3;
            double gate = count == 6 ? GateSix : GateThree;

            if (m > gate)
            {
                // Propagated, not corrected: the prediction is the best this filter has, and the
                // measurement is set aside rather than allowed to drag the state after it.
                Array.Copy(x_pred, 0, x, 0, States);
                Array.Copy(covariance_pred, 0, covariance, 0, States * States);
                rejections++;
                if (rejections >= MaximumRejections)
                {
                    // Six in a row is not an outlier, it is a different target.
                    Reset();
                    Seed(position);
                    last = now;
                }
                Healthy = false;
                return false;
            }

            rejections = 0;
            if (!Correct(count))
            {
                Reset();
                Seed(position);
                last = now;
                return false;
            }
            Adapt(m, count);
            samples++;
            Healthy = AllFinite();
            return Healthy;
        }

        private void TakeStorage()
        {

            innovation = new double[MaxMeasurements];
            s = new double[MaxMeasurements * MaxMeasurements];
            s_factor = new double[MaxMeasurements * MaxMeasurements];

            gain = new double[States * MaxMeasurements];
            scratch = new double[States * MaxMeasurements];
            compact = new double[MaxMeasurements * MaxMeasurements];
            forward = new double[MaxMeasurements];
            rhs = new double[MaxMeasurements];
            solved = new double[MaxMeasurements];
            sigma = new double[SigmaPoints * States];
            sigma_pred = new double[SigmaPoints * States];
            sigma_measure = new double[SigmaPoints * MaxMeasurements];
            weight_mean = new double[SigmaPoints];
            weight_covariance = new double[SigmaPoints];
            state_factor = new double[States * States];
            measurement_mean = new double[MaxMeasurements];
            cross_covariance = new double[States * MaxMeasurements];

            double lambda = UkfAlpha * UkfAlpha * (States + UkfKappa) - States;
            double spread = States + lambda;
            weight_mean[0] = lambda / spread;
            weight_covariance[0] = weight_mean[0] + (1.0 - UkfAlpha * UkfAlpha + UkfBeta);
            for (int i = 1; i < SigmaPoints; i++)
            {
                weight_mean[i] = 1.0 / (2.0 * spread);
                weight_covariance[i] = weight_mean[i];
            }

        }

        private void Seed(LeadVector position)
        {
            Array.Clear(x, 0, States);
            Array.Clear(covariance, 0, States * States);
            x[Pos] = position.X; x[Pos + 1] = position.Y; x[Pos + 2] = position.Z;
            for (int axis = 0; axis < 3; axis++)
            {
                covariance[(Pos + axis) * States + (Pos + axis)] = PositionNoise * PositionNoise;
                covariance[(Vel + axis) * States + (Vel + axis)] = InitialVelocity * InitialVelocity;
                covariance[(Acc + axis) * States + (Acc + axis)] = InitialAcceleration * InitialAcceleration;
            }
            seeded = true;
            Healthy = false;
            samples = 0;
            rejections = 0;
        }

        /// <summary>
        /// The first interval is the only velocity measurement that costs nothing, and a filter started
        /// at rest would spend the next second discovering how fast the target is going; the velocity
        /// state is set from the interval between the first two positions, with the variance that
        /// differencing two positions implies.
        /// </summary>
        private void WarmStart(LeadVector position, double dt)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                double measured = (Component(position, axis) - x[Pos + axis]) / dt;
                x[Vel + axis] = measured;
                covariance[(Vel + axis) * States + (Vel + axis)] =
                    2 * PositionNoise * PositionNoise / (dt * dt) + 1e-3;
                covariance[(Pos + axis) * States + (Pos + axis)] = PositionNoise * PositionNoise;
            }
        }

        private LeadVector At(int offset)
        {
            return new LeadVector(x[offset], x[offset + 1], x[offset + 2]);
        }

        private double Sigma(int offset)
        {
            double variance = 0;
            for (int axis = 0; axis < 3; axis++) variance += covariance[(offset + axis) * States + (offset + axis)];
            return Math.Sqrt(Math.Max(variance / 3, 0));
        }

        private static double Component(LeadVector v, int axis)
        {
            return axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
        }

        private bool AllFinite()
        {
            for (int i = 0; i < States; i++) if (!CrowsPredictor.Finite(x[i])) return false;
            for (int i = 0; i < States * States; i++) if (!CrowsPredictor.Finite(covariance[i])) return false;
            return true;
        }

        /// <summary>
        /// The sigma points of a state and its covariance: the mean, and one pair per state dimension
        /// offset along a column of the covariance's Cholesky factor. The factor is taken of the
        /// covariance itself and the columns scaled by the spread, which is the same set of points as
        /// factorising the scaled covariance and avoids one scaled copy of it.
        /// </summary>
        /// <summary>
        /// Propagates every sigma point through the constant-acceleration process model and forms the
        /// mean and covariance of the result, then adds the process noise.
        /// </summary>
        private bool Predict(double dt)
        {
            if (!Cholesky(covariance, States, state_factor)) return false;
            double spread = Math.Sqrt(UkfAlpha * UkfAlpha * (States + UkfKappa));
            for (int i = 0; i < States; i++) sigma[i] = x[i];
            for (int point = 1; point <= States; point++)
            {
                int column = point - 1;
                int plus = point * States;
                int minus = (point + States) * States;
                for (int row = 0; row < States; row++)
                {
                    double delta = spread * state_factor[row * States + column];
                    sigma[plus + row] = x[row] + delta;
                    sigma[minus + row] = x[row] - delta;
                }
            }

            for (int point = 0; point < SigmaPoints; point++)
            {
                int baseIndex = point * States;
                int output = point * States;
                for (int axis = 0; axis < 3; axis++)
                {
                    sigma_pred[output + Pos + axis] = sigma[baseIndex + Pos + axis]
                        + sigma[baseIndex + Vel + axis] * dt
                        + sigma[baseIndex + Acc + axis] * (0.5 * dt * dt);
                    sigma_pred[output + Vel + axis] = sigma[baseIndex + Vel + axis]
                        + sigma[baseIndex + Acc + axis] * dt;
                    sigma_pred[output + Acc + axis] = sigma[baseIndex + Acc + axis];
                }
            }

            Array.Clear(x_pred, 0, States);
            for (int point = 0; point < SigmaPoints; point++)
            {
                int baseIndex = point * States;
                double w = weight_mean[point];
                for (int i = 0; i < States; i++) x_pred[i] += w * sigma_pred[baseIndex + i];
            }
            Array.Clear(covariance_pred, 0, States * States);
            for (int point = 0; point < SigmaPoints; point++)
            {
                int baseIndex = point * States;
                double w = weight_covariance[point];
                for (int row = 0; row < States; row++)
                {
                    double dr = sigma_pred[baseIndex + row] - x_pred[row];
                    for (int column = 0; column < States; column++)
                        covariance_pred[row * States + column] += w * dr * (sigma_pred[baseIndex + column] - x_pred[column]);
                }
            }
            ProcessNoise(dt);
            return true;
        }

        /// <summary>
        /// The measurement half of the tick: the sigma points are propagated through the measurement
        /// model - position, and velocity where the game reports one that is not the transform's own
        /// difference - and their mean, innovation covariance and cross covariance are formed. Returns
        /// the normalised innovation squared: how far the measurement sits from the prediction, counted
        /// in the filter's own covariance. NaN where the covariance would not decompose.
        /// </summary>
        private double Measure(LeadVector position, LeadVector velocity, bool velocityValid)
        {
            int m = velocityValid ? 6 : 3;
            Array.Clear(measurement_mean, 0, m);
            for (int point = 0; point < SigmaPoints; point++)
            {
                int stateBase = point * States;
                int measurementBase = point * MaxMeasurements;
                sigma_measure[measurementBase] = sigma_pred[stateBase + Pos];
                sigma_measure[measurementBase + 1] = sigma_pred[stateBase + Pos + 1];
                sigma_measure[measurementBase + 2] = sigma_pred[stateBase + Pos + 2];
                if (velocityValid)
                {
                    sigma_measure[measurementBase + 3] = sigma_pred[stateBase + Vel];
                    sigma_measure[measurementBase + 4] = sigma_pred[stateBase + Vel + 1];
                    sigma_measure[measurementBase + 5] = sigma_pred[stateBase + Vel + 2];
                }
                double w = weight_mean[point];
                for (int a = 0; a < m; a++) measurement_mean[a] += w * sigma_measure[measurementBase + a];
            }

            innovation[0] = position.X - measurement_mean[0];
            innovation[1] = position.Y - measurement_mean[1];
            innovation[2] = position.Z - measurement_mean[2];
            if (velocityValid)
            {
                innovation[3] = velocity.X - measurement_mean[3];
                innovation[4] = velocity.Y - measurement_mean[4];
                innovation[5] = velocity.Z - measurement_mean[5];
            }

            Array.Clear(s, 0, MaxMeasurements * MaxMeasurements);
            Array.Clear(cross_covariance, 0, States * MaxMeasurements);
            for (int point = 0; point < SigmaPoints; point++)
            {
                int stateBase = point * States;
                int measurementBase = point * MaxMeasurements;
                double w = weight_covariance[point];
                for (int a = 0; a < m; a++)
                {
                    double dz = sigma_measure[measurementBase + a] - measurement_mean[a];
                    for (int b = 0; b < m; b++)
                        s[a * MaxMeasurements + b] += w * dz * (sigma_measure[measurementBase + b] - measurement_mean[b]);
                    for (int row = 0; row < States; row++)
                        cross_covariance[row * MaxMeasurements + a] += w
                            * (sigma_pred[stateBase + row] - x_pred[row]) * dz;
                }
            }
            for (int axis = 0; axis < 3; axis++)
            {
                s[axis * MaxMeasurements + axis] += PositionNoise * PositionNoise;
                if (velocityValid) s[(3 + axis) * MaxMeasurements + (3 + axis)] += VelocityNoise * VelocityNoise;
            }

            for (int a = 0; a < m; a++)
            {
                for (int q = 0; q < m; q++) compact[a * m + q] = s[a * MaxMeasurements + q];
            }
            if (!Cholesky(compact, m, s_factor)) return double.NaN;

            for (int a = 0; a < m; a++) rhs[a] = innovation[a];
            if (!SolveSymmetric(s_factor, m, rhs, solved)) return double.NaN;

            double total = 0;
            for (int a = 0; a < m; a++) total += innovation[a] * solved[a];
            return total;
        }

        /// <summary>
        /// The Kalman correction written through the unscented cross covariance:
        /// x = x- + K (z - z-), P = P- - K S K^T, with K = P_xz S^-1 taken by solving S against each
        /// row of the cross covariance rather than by inverting it.
        /// </summary>
        private bool Correct(int m)
        {
            for (int i = 0; i < States; i++)
            {
                for (int a = 0; a < m; a++) rhs[a] = cross_covariance[i * MaxMeasurements + a];
                if (!SolveSymmetric(s_factor, m, rhs, solved)) return false;
                for (int a = 0; a < m; a++) gain[i * MaxMeasurements + a] = solved[a];
            }

            for (int i = 0; i < States; i++)
            {
                double sum = x_pred[i];
                for (int a = 0; a < m; a++) sum += gain[i * MaxMeasurements + a] * innovation[a];
                x[i] = sum;
            }

            for (int i = 0; i < States; i++)
            {
                for (int a = 0; a < m; a++)
                {
                    double sum = 0;
                    for (int q = 0; q < m; q++) sum += gain[i * MaxMeasurements + q] * s[q * MaxMeasurements + a];
                    scratch[i * MaxMeasurements + a] = sum;
                }
            }

            for (int i = 0; i < States; i++)
            {
                for (int j = 0; j < States; j++)
                {
                    double sum = 0;
                    for (int a = 0; a < m; a++) sum += scratch[i * MaxMeasurements + a] * gain[j * MaxMeasurements + a];
                    covariance[i * States + j] = covariance_pred[i * States + j] - sum;
                }
            }

            // A covariance is symmetric by construction; rounding and a repeated subtraction of a
            // nearly equal matrix are how it stops being one, and a covariance that is not symmetric
            // cannot be decomposed on the next tick.
            for (int i = 0; i < States; i++)
            {
                for (int j = i + 1; j < States; j++)
                {
                    double mean = 0.5 * (covariance[i * States + j] + covariance[j * States + i]);
                    covariance[i * States + j] = mean;
                    covariance[j * States + i] = mean;
                }
            }
            for (int i = 0; i < States; i++)
            {
                if (covariance[i * States + i] < 1e-9) covariance[i * States + i] = 1e-9;
            }
            return true;
        }

        /// <summary>
        /// The process noise of a constant-acceleration step driven by white jerk, per world axis: the
        /// classic five by twenty, four by eight, three by six block that integrating a jerk over one
        /// step produces. Written per axis rather than as a Kronecker product because the state is
        /// stored flat, and because the axes really are independent here.
        ///
        /// The acceleration state also gets a floor of its own: a target holding a straight line
        /// shrinks that variance toward nothing between manoeuvres, and an acceleration whose variance
        /// has reached nothing is a claim the filter could no longer revise when the target turns.
        /// </summary>
        private void ProcessNoise(double dt)
        {
            double jerk2 = jerk * jerk;
            double dt2 = dt * dt, dt3 = dt2 * dt, dt4 = dt3 * dt, dt5 = dt4 * dt;
            double q11 = jerk2 * dt5 / 20.0, q12 = jerk2 * dt4 / 8.0, q13 = jerk2 * dt3 / 6.0;
            double q22 = jerk2 * dt3 / 3.0, q23 = jerk2 * dt2 / 2.0, q33 = jerk2 * dt;

            for (int axis = 0; axis < 3; axis++)
            {
                Add(q11, Pos + axis, Pos + axis);
                Add(q12, Pos + axis, Vel + axis);
                Add(q12, Vel + axis, Pos + axis);
                Add(q22, Vel + axis, Vel + axis);
                Add(q13, Pos + axis, Acc + axis);
                Add(q13, Acc + axis, Pos + axis);
                Add(q23, Vel + axis, Acc + axis);
                Add(q23, Acc + axis, Vel + axis);
                Add(q33 + 1e-4, Acc + axis, Acc + axis);
            }
        }

        private void Add(double value, int row, int column)
        {
            covariance_pred[row * States + column] += value;
        }

        /// <summary>
        /// Bounded adaptation of the jerk the model allows: a filter whose innovations keep arriving
        /// larger than its own covariance leads one to expect has under-estimated how hard the target
        /// can manoeuvre, and one whose innovations are consistently smaller is carrying more process
        /// noise than it needs. A few percent per tick inside fixed bounds, so a single frame cannot
        /// move it and it cannot run away.
        /// </summary>
        private void Adapt(double normalised, int m)
        {
            if (normalised > 2 * m) jerk = Math.Min(MaximumJerk, jerk * 1.05);
            else if (normalised < m) jerk = Math.Max(MinimumJerk, jerk * 0.995);
        }


        /// <summary>
        /// Lower Cholesky factor, in place into `result`. False when the matrix is not positive
        /// definite - which, for a covariance, means it has stopped being one and no sigma point taken
        /// from it would mean anything.
        /// </summary>
        private static bool Cholesky(double[] a, int n, double[] result)
        {
            Array.Clear(result, 0, n * n);
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j <= i; j++)
                {
                    double sum = a[i * n + j];
                    for (int k = 0; k < j; k++) sum -= result[i * n + k] * result[j * n + k];
                    if (i == j)
                    {
                        if (!(sum > 1e-18)) return false;
                        result[i * n + i] = Math.Sqrt(sum);
                    }
                    else
                    {
                        result[i * n + j] = sum / result[j * n + j];
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Solves a symmetric positive definite system from its Cholesky factor: forward substitution
        /// with the factor, back substitution with its transpose.
        /// </summary>
        private bool SolveSymmetric(double[] factor, int n, double[] right, double[] result)
        {
            for (int i = 0; i < n; i++)
            {
                double sum = right[i];
                for (int k = 0; k < i; k++) sum -= factor[i * n + k] * forward[k];
                double diagonal = factor[i * n + i];
                if (!(Math.Abs(diagonal) > 1e-18)) return false;
                forward[i] = sum / diagonal;
            }
            for (int i = n - 1; i >= 0; i--)
            {
                double sum = forward[i];
                for (int k = i + 1; k < n; k++) sum -= factor[k * n + i] * result[k];
                result[i] = sum / factor[i * n + i];
            }
            return true;
        }
    }

    /// <summary>
    /// The game's own ballistic model, run here: the flight time a round has to a range, rather than the
    /// one read back out of the table the game pre-computes.
    ///
    /// BallisticComputer samples elevations every 0.1 degrees, integrates each with the RK scheme below,
    /// and answers a query by interpolating between the two samples that bracket the range. Two things
    /// follow, and both grow with range. The samples are hundreds of metres apart - measured at 740 to
    /// 810 m between 1500 m and 3500 m for a 1565 m/s fin round - and every sample's flight time is an
    /// accumulated count of 20 ms integration steps, so the interpolated answer carries a sawtooth of
    /// about +/-10 ms on top of the interpolation error. Ten milliseconds of flight time is 20 cm of
    /// lead against a target crossing at 20 m/s, which is not dispersion and does not shrink with
    /// practice: it is the table.
    ///
    /// The model is small enough to run exactly - quadratic drag and gravity, integrated with the fire
    /// control's own scheme at its own step, so this answers the same quantity without the quantisation.
    /// The elevation that reaches a range is found by bisection over the part of the curve the game
    /// tabulates (range rises monotonically with elevation up to the far peak), and the answers are
    /// cached per metre of range, which is finer than they can be told apart at any range this fires at.
    ///
    /// It is deliberately not trusted on its own: CrowsAutoTrack offers its answer only where it agrees
    /// with the game's table to within the table's own sawtooth. A round whose drag, mass, area or
    /// muzzle velocity this does not know - a custom ballistic computer, a temperature far from the one
    /// the table was built at - makes the two disagree, and the game's table is what gets used.
    /// </summary>
    internal static class CrowsBallistics
    {
        private const double Gravity = 9.81;
        private const double Step = 0.02;
        private const double MaximumTime = 12.0;
        /// <summary>
        /// The elevation the bracket closes at: the angle of the far peak, past which range falls as
        /// elevation climbs. Bisection needs range to rise with elevation across the whole bracket, so
        /// the top of it has to be the peak itself and not the end of a convenient range of angles. This
        /// gun peaks near 2.31 degrees; 3.0 degrees is over the top of that curve, where the round is
        /// still in the air when the integration runs out and the shot has no range to compare at all.
        /// The peak is flat, so the exact angle matters far less than being on the near side of it.
        /// </summary>
        private const double Ceiling = 2.31;
        private const int Bisections = 22;
        private const int CacheSize = 64;

        private static double muzzle, drag;
        private static bool ready;

        private static readonly double[] cache_range = new double[CacheSize];
        private static readonly double[] cache_time = new double[CacheSize];
        private static readonly double[] cache_fall = new double[CacheSize];
        private static int cache_used, cache_next;

        internal static bool Ready { get { return ready; } }

        /// <summary>
        /// The round being solved for, in the game's own terms: muzzle velocity in m/s and
        /// k = Coeff * SectionalArea * airDensity / Mass, which is the whole of the drag model.
        /// </summary>
        internal static void Configure(double muzzleVelocity, double coefficient, double sectionalArea, double mass)
        {
            double wanted = coefficient * sectionalArea * 1.2 / mass;

            if (ready && muzzle == muzzleVelocity && drag == wanted) return;   // same round: keep the cache

            ready = false;
            cache_used = 0;
            cache_next = 0;

            if (!CrowsPredictor.Finite(muzzleVelocity) || !CrowsPredictor.Finite(coefficient)
                || !CrowsPredictor.Finite(sectionalArea) || !CrowsPredictor.Finite(mass)
                || muzzleVelocity < 100 || mass <= 0 || sectionalArea <= 0 || coefficient <= 0) return;

            muzzle = muzzleVelocity;
            drag = wanted;                                        // the game's STANDARD_AIR_DENSITY
            ready = true;
        }

        /// <summary>
        /// One shot: the game's integration, step for step. Note the weighted sum starts at the second
        /// evaluation rather than the first - that is what BallisticComputer does (its RK4 does not use
        /// k1 in the sum), and a replica that "fixed" it would disagree with every round the game fires.
        /// </summary>
        private static bool Shot(double elevationDegrees, out double range, out double time, out double fall)
        {
            range = 0;
            time = 0;
            fall = 0;

            double radians = elevationDegrees * Math.PI / 180.0;
            double px = 0, py = 0, pz = 0;
            double vx = 0, vy = muzzle * Math.Sin(radians), vz = muzzle * Math.Cos(radians);
            double elapsed = 0;
            double crossed = 0;

            while (elapsed < MaximumTime)
            {
                double above = py;
                double m1 = Math.Sqrt(vx * vx + vy * vy + vz * vz);
                double a1x = -drag * vx * m1, a1y = -Gravity - drag * vy * m1, a1z = -drag * vz * m1;
                double k1x = vx, k1y = vy, k1z = vz;

                double v2x = k1x + a1x * Step, v2y = k1y + a1y * Step, v2z = k1z + a1z * Step;
                double m2 = Math.Sqrt(v2x * v2x + v2y * v2y + v2z * v2z);
                double a2x = -drag * v2x * m2, a2y = -Gravity - drag * v2y * m2, a2z = -drag * v2z * m2;

                double v3x = k1x + a2x * 0.5 * Step, v3y = k1y + a2y * 0.5 * Step, v3z = k1z + a2z * 0.5 * Step;
                double m3 = Math.Sqrt(v3x * v3x + v3y * v3y + v3z * v3z);
                double a3x = -drag * v3x * m3, a3y = -Gravity - drag * v3y * m3, a3z = -drag * v3z * m3;

                double v4x = k1x + a3x * 0.5 * Step, v4y = k1y + a3y * 0.5 * Step, v4z = k1z + a3z * 0.5 * Step;
                double m4 = Math.Sqrt(v4x * v4x + v4y * v4y + v4z * v4z);
                double a4x = -drag * v4x * m4, a4y = -Gravity - drag * v4y * m4, a4z = -drag * v4z * m4;

                double v5x = k1x + a4x * Step, v5y = k1y + a4y * Step, v5z = k1z + a4z * Step;
                double m5 = Math.Sqrt(v5x * v5x + v5y * v5y + v5z * v5z);
                double a5x = -drag * v5x * m5, a5y = -Gravity - drag * v5y * m5, a5z = -drag * v5z * m5;

                px += (v2x + 2 * v3x + 2 * v4x + v5x) / 6.0 * Step;
                py += (v2y + 2 * v3y + 2 * v4y + v5y) / 6.0 * Step;
                pz += (v2z + 2 * v3z + 2 * v4z + v5z) / 6.0 * Step;
                vx += (a2x + 2 * a3x + 2 * a4x + a5x) / 6.0 * Step;
                vy += (a2y + 2 * a3y + 2 * a4y + a5y) / 6.0 * Step;
                vz += (a2z + 2 * a3z + 2 * a4z + a5z) / 6.0 * Step;

                if (py <= 0 && elapsed > Step)
                {
                    // The crossing is inside the last step, and reporting the step boundary is exactly what
                    // quantises the game's own table to its 20 ms integration step. Interpolating the
                    // crossing is the whole of what this class is here for.
                    double fraction = above > 0 ? above / (above - py) : 0;
                    crossed = elapsed + CrowsPredictor.Clamp(fraction, 0, 1) * Step;
                    break;
                }

                elapsed += Step;
            }

            // Falling out of the loop means the shot had not come down inside MaximumTime, and the
            // position it has reached is then not a range at all: the game's own measure of the range
            // for a round that is still in the air is the distance to where it is, which climbs as long
            // as it climbs. The game hands that number to a display; this class hands it to a bisection
            // that assumes range rises with elevation, so an unconverged shot is reported as no answer.
            // Past the apogee the returning leg would give a genuine crossing, and it is beyond the
            // flight time the table is built for, so nothing that fires inside the table can reach here.
            if (crossed <= 0) return false;

            range = Math.Sqrt(px * px + py * py + pz * pz);      // the game's own measure of the range
            time = crossed;
            fall = range * Math.Tan(radians);                    // and the game's own definition of the fall
            return true;
        }

        /// <summary>The elevation that reaches a range, by bisection, and the flight time it has.</summary>
        private static bool Solve(double want, out double time, out double fall)
        {
            time = 0;
            fall = 0;

            if (!ready || !CrowsPredictor.Finite(want) || want < 50 || want > 12000) return false;

            double low = 0.02, high = Ceiling;
            double foundRange, foundTime, foundFall;
            Shot(low, out foundRange, out foundTime, out foundFall);

            if (foundRange >= want)
            {
                time = foundTime;
                fall = foundFall;
                return true;
            }

            Shot(high, out foundRange, out foundTime, out foundFall);

            if (foundRange < want) return false;      // past the far side of the curve; the game clamps here

            for (int i = 0; i < Bisections; i++)
            {
                double mid = 0.5 * (low + high);
                Shot(mid, out foundRange, out foundTime, out foundFall);
                if (foundRange < want) low = mid; else high = mid;
            }

            Shot(0.5 * (low + high), out foundRange, out foundTime, out foundFall);
            time = foundTime;
            fall = foundFall;
            return CrowsPredictor.Finite(time) && time > 0;
        }

        /// <summary>
        /// The flight time to a range, and the fall of shot at it, from the cache when it has been asked
        /// for before to the metre - which is finer than the two answers can be told apart at any range
        /// these guns fire at - and by integration when it has not.
        /// </summary>
        internal static bool FlightTime(double range, out double time, out double fall)
        {
            time = 0;
            fall = 0;
            if (!ready || !CrowsPredictor.Finite(range)) return false;

            double key = Math.Round(range);

            for (int i = 0; i < cache_used; i++)
            {
                if (cache_range[i] == key)
                {
                    time = cache_time[i];
                    fall = cache_fall[i];
                    return true;
                }
            }

            double solved, dropped;

            if (!Solve(key, out solved, out dropped)) return false;

            int slot = cache_used < CacheSize ? cache_used++ : cache_next;
            cache_next = (slot + 1) % CacheSize;
            cache_range[slot] = key;
            cache_time[slot] = solved;
            cache_fall[slot] = dropped;

            time = solved;
            fall = dropped;
            return true;
        }

        internal static bool FlightTime(double range, out double time)
        {
            double fall;
            return FlightTime(range, out time, out fall);
        }

    }
    /// <summary>
    /// The multi-shot correction: what the last few rounds landed away from the target, carried as a
    /// bounded hold-off that the next one is fired with.
    ///
    /// This is the one term in the solution that no measurement of the target can supply. It is not the
    /// target's motion - that is the lead - and it is not the range - that is the table. It is
    /// everything the game's model leaves out and this module cannot see: a boresight that is a tenth of
    /// a degree out, a superelevation the donated mount does not quite match, wind the game applies and
    /// nothing reports. Those are constant over a mission and invisible until a round is fired, which is
    /// why a fire control computer that never learns from its own fall of shot is always a little wrong
    /// in the same direction.
    ///
    /// Angular, not linear: a hold-off of a tenth of a degree is 1.7 m at a kilometre and 3.5 m at two,
    /// which is how a boresight error and a drift both behave, and why the correction is stored in
    /// degrees and turned into metres against the range of the shot being taken.
    ///
    /// The blend is deliberately slow. One round's fall of shot carries the dispersion of the round, the
    /// aim error of the moment and the target's own movement during the flight time; a bias that took
    /// each of those at face value would chase the scatter rather than the error. A quarter of each
    /// observation, bounded against the limit so that a single wild round cannot swing the aim, settles
    /// in about four rounds of fire and moves less than the dispersion it is drawn from.
    /// </summary>
    internal sealed class CrowsBallisticBias
    {
        /// <summary>The design note's limits, in degrees: 0.10-0.15 across and 0.08-0.12 up.</summary>
        internal const double LimitRight = 0.12;
        internal const double LimitUp = 0.10;

        /// <summary>How much of each observation is taken; see the class comment.</summary>
        private const double Blend = 0.25;

        /// <summary>
        /// And no single observation may claim more than the limit itself, however far that round went:
        /// one shot carries the round's own dispersion and a bad ranging, and a correction that believed
        /// it outright would be chasing a flier. What bounds a single round's effect on the aim is the
        /// blend below, not this.
        /// </summary>
        private const double ObservationLimit = 1.0;

        /// <summary>
        /// Seconds without a shot for the bias to halve. A correction that has not been confirmed by
        /// fire recently is a correction for conditions that may have passed - a repositioned vehicle,
        /// a different target, a different round - so it fades rather than persisting forever.
        /// </summary>
        private const double Halflife = 25;

        /// <summary>
        /// Range bands, and the correction each one holds. A metre short at 700 m is a different fault
        /// from a metre short at 2 km - a boresight error does not change with range, a drag or drift
        /// error does - and evidence from one band is not evidence about another.
        /// </summary>
        private const int Bands = 4;
        private static readonly double[] BandStart = { 0, 500, 1000, 1500 };

        /// <summary>
        /// The far edge of the last band, in metres, and the range past which the correction table has
        /// nothing to say. Shared so the band a range falls in and the range Observe will accept cannot
        /// disagree: an upper bound only Observe knew about would let a band be written by Confirm at a
        /// range no observation is ever taken at, leaving that band to hold its opening value forever.
        /// </summary>
        private const double BandEnd = 5000;

        private readonly double[] right = new double[Bands];
        private readonly double[] up = new double[Bands];

        internal int Shots { get; private set; }

        internal double Right(double range) { int band = BandOf(range); return band < 0 ? 0 : right[band]; }
        internal double Up(double range) { int band = BandOf(range); return band < 0 ? 0 : up[band]; }

        /// <summary>Which band a range falls in, or -1 when it is outside every band.</summary>
        private static int BandOf(double range)
        {
            if (!CrowsPredictor.Finite(range) || range < BandStart[0] || range >= BandEnd) return -1;
            for (int band = Bands - 1; band >= 0; band--) if (range >= BandStart[band]) return band;
            return -1;
        }

        internal void Reset()
        {
            for (int band = 0; band < Bands; band++)
            {
                right[band] = 0;
                up[band] = 0;
            }
            Shots = 0;
        }

        /// <summary>
        /// One round's fall of shot, in metres across and up from the point that was aimed at, and the
        /// range it was fired at. The correction stored is the negated error: the aim has to move the
        /// other way from where the round went.
        /// </summary>
        internal void Observe(double rightError, double upError, double range)
        {
            if (!CrowsPredictor.Finite(rightError) || !CrowsPredictor.Finite(upError)
                || !CrowsPredictor.Finite(range) || range < 50 || range > BandEnd) return;

            int band = BandOf(range);

            if (band < 0) return;

            double toDegrees = 180.0 / (Math.PI * range);
            double takeRight = CrowsPredictor.Clamp(-rightError * toDegrees, -LimitRight * ObservationLimit, LimitRight * ObservationLimit);
            double takeUp = CrowsPredictor.Clamp(-upError * toDegrees, -LimitUp * ObservationLimit, LimitUp * ObservationLimit);

            right[band] = CrowsPredictor.Clamp(right[band] * (1 - Blend) + takeRight * Blend, -LimitRight, LimitRight);
            up[band] = CrowsPredictor.Clamp(up[band] * (1 - Blend) + takeUp * Blend, -LimitUp, LimitUp);
            Shots++;
        }

        /// <summary>A round that hit what was aimed at is evidence the bias is right, not absent.</summary>
        internal void Confirm(double range)
        {
            int band = BandOf(range);

            if (band < 0) return;

            right[band] *= 1 - Blend * 0.5;
            up[band] *= 1 - Blend * 0.5;
            Shots++;
        }

        /// <summary>Fades the correction while nothing is being fired; see Halflife.</summary>
        internal void Decay(double seconds)
        {
            if (!CrowsPredictor.Finite(seconds) || seconds <= 0) return;
            double factor = Math.Pow(0.5, seconds / Halflife);

            for (int band = 0; band < Bands; band++)
            {
                right[band] *= factor;
                up[band] *= factor;

                if (Math.Abs(right[band]) < 1e-4 && Math.Abs(up[band]) < 1e-4)
                {
                    right[band] = 0;
                    up[band] = 0;
                }
            }
        }
    }

    internal struct CrowsIntercept
    {
        internal LeadVector TransverseDisplacement;
        internal double FlightTime, Residual;
        internal int Iterations;
        internal bool Converged, Valid;
    }

    internal static class CrowsPredictor
    {
        internal static bool Finite(double n) { return !double.IsNaN(n) && !double.IsInfinity(n); }
        internal static double Clamp(double n,double lo,double hi) { return Math.Max(lo,Math.Min(hi,n)); }

        /// <summary>How long an acceleration estimate may be carried: the design note's 0.4 - 0.75 s.</summary>
        internal const double AccelerationHorizon = 0.75;

        /// <summary>
        /// The same carry for a turn, and shorter: a turn rate measured over a fifth of a second is a
        /// far weaker statement about the next one than a speed is.
        /// </summary>
        internal const double TurnHorizon = 0.5;

        /// <summary>
        /// How long the vertical axis may be carried. Hull pitch over a bump is a couple of metres per
        /// second squared of vertical acceleration that is not a climb, and over a flight time it is a
        /// metre of lead pointing at the sky when the target is on the level.
        /// </summary>
        internal const double VerticalHorizon = 0.3;

        /// <summary>And the ceiling that carry is taken off, over the shorter span above.</summary>
        internal const double VerticalAcceleration = 3;

        /// <summary>The ceiling on a horizontal acceleration estimate, unchanged from the prior solve.</summary>
        internal const double AccelerationCeiling = 4;

        /// <summary>
        /// Weight a time-of-flight step carries when it reverses the one before it; see SolveRelative.
        /// The remainder is what a step keeps when the iteration is converging monotonically, which is
        /// every case but the approaching target the damping exists for.
        /// </summary>
        internal const double IterationDamping = 0.65;

        /// <summary>
        /// The acceleration extrapolation is allowed to start from: capped in magnitude, scaled by how
        /// much the estimate is worth, and tighter on the world-vertical axis than on the other two.
        /// </summary>
        private static LeadVector Carry(LeadVector acceleration, double confidence)
        {
            LeadVector a = LeadVector.Limit(acceleration, AccelerationCeiling)
                * Clamp(Finite(confidence) ? confidence : 0, 0, 0.65);
            a.Y = Clamp(a.Y, -VerticalAcceleration, VerticalAcceleration);
            return a;
        }

        /// <summary>
        /// An acceleration carried over a horizon: the full span on the horizontal axes, the shorter
        /// gravity-referenced one vertically. Written per axis because the vertical term is the whole
        /// of what the shorter span means.
        /// </summary>
        private static LeadVector Share(LeadVector acceleration, double horizon, double time)
        {
            double vertical = Math.Min(horizon, VerticalHorizon);
            LeadVector carried = acceleration * (horizon * (time - 0.5 * horizon));
            carried.Y = acceleration.Y * (vertical * (time - 0.5 * vertical));
            return carried;
        }

        /// <summary>
        /// The constant-turn-rate arc, shared by the legacy solve and the turning hypothesis so the two
        /// cannot drift apart. `conservative` selects the gravity-referenced carry of the parallel
        /// acceleration inside the arc; the legacy solve passes false and keeps its original
        /// arithmetic, which the offline tests pin to the last bit.
        /// </summary>
        private static LeadVector Arc(LeadVector velocity, LeadVector parallel, LeadVector turn,
            double horizon, double time, bool conservative)
        {
            double speed = velocity.Length;
            double turnRate = Math.Min(turn.Length / speed, 1.5);
            LeadVector axis = LeadVector.Cross(velocity, turn);
            axis = axis * (1 / axis.Length);
            LeadVector perpendicular = velocity;
            double angle = turnRate*horizon;
            double sin = Math.Sin(angle), cos = Math.Cos(angle);
            LeadVector rotated = perpendicular*cos + LeadVector.Cross(axis, perpendicular)*sin
                + axis*LeadVector.Dot(axis, perpendicular)*(1-cos);
            LeadVector carried = conservative
                ? Share(parallel, horizon, horizon)
                : parallel*(0.5*horizon*horizon);
            LeadVector arc = perpendicular*(sin/turnRate)
                + LeadVector.Cross(axis, perpendicular)*((1-cos)/turnRate)
                + carried;
            return arc + (rotated+parallel*horizon)*(time-horizon);
        }

        // Extrapolate acceleration briefly, then coast. Braking can stop longitudinal
        // travel, but stale samples cannot manufacture a reversal.
        internal static LeadVector TargetDisplacement(LeadVector velocity, LeadVector acceleration, double confidence, double time)
        {
            if(!velocity.Finite || !acceleration.Finite || !Finite(time) || time<=0) return new LeadVector();
            LeadVector a=LeadVector.Limit(acceleration,4)*Clamp(Finite(confidence)?confidence:0,0,0.65);
            double h=Math.Min(time,0.75), speed=velocity.Length;
            if(speed<0.3) return velocity*time;
            else
            {
                double along=LeadVector.Dot(a,velocity)*(1/speed);
                if(along<0) h=Math.Min(h,speed/-along);
            }
            // Constant-turn-rate integration for the perpendicular part of acceleration.
            // This is the CTRV case in a form that also works on slopes and does not need
            // a matrix allocation. Parallel acceleration keeps the bounded braking rule.
            LeadVector parallelAcceleration = velocity * (LeadVector.Dot(a, velocity) / (speed * speed));
            LeadVector turn = a - parallelAcceleration;
            double turnRate = turn.Length / speed;
            if (turnRate < 0.02 || turn.Length < 1e-5)
                return velocity*time+a*(h*(time-0.5*h));

            return Arc(velocity, parallelAcceleration, turn, h, time, false);
        }

        /// <summary>
        /// One hypothesis' displacement short of the private pieces: the bounded acceleration carry,
        /// with the turn arc where the hypothesis allows one and the braking clamp where it does not
        /// coast past a stop.
        /// </summary>
        internal static LeadVector Carried(LeadVector velocity, LeadVector acceleration, double confidence,
            double time, bool braking, bool turning)
        {
            if (!velocity.Finite || !acceleration.Finite || !Finite(time) || time <= 0) return new LeadVector();
            LeadVector a = Carry(acceleration, confidence);
            double speed = velocity.Length;
            // A stopped target cannot accelerate from stale history; the same rule as the legacy solve.
            if (speed < 0.3) return velocity * time;

            double horizon = Math.Min(time, AccelerationHorizon);
            if (braking)
            {
                double along = LeadVector.Dot(a, velocity) * (1 / speed);
                if (along < 0) horizon = Math.Min(horizon, speed / -along);
            }

            LeadVector parallel = velocity * (LeadVector.Dot(a, velocity) / (speed * speed));
            LeadVector turn = a - parallel;
            if (!turning || turn.Length < 1e-5 || turn.Length / speed < 0.02)
                return velocity * time + Share(a, horizon, time);

            return Arc(velocity, parallel, turn, Math.Min(horizon, TurnHorizon), time, true);
        }

        /// <summary>
        /// The hard-brake hypothesis: the target comes to rest and stays there. Every other model
        /// coasts past the stop at the velocity the target had reached; this one does not, which is
        /// what makes it worth carrying separately from the bounded carry above.
        /// </summary>
        internal static LeadVector Braking(LeadVector velocity, LeadVector acceleration, double confidence, double time)
        {
            if (!velocity.Finite || !acceleration.Finite || !Finite(time) || time <= 0) return new LeadVector();
            LeadVector a = Carry(acceleration, confidence);
            double speed = velocity.Length;
            if (speed < 0.3) return new LeadVector();

            double slowing = -LeadVector.Dot(a, velocity) * (1 / speed);
            // The hypothesis' premise is that the target is slowing; where it is not, the honest answer
            // for a model that says "this ends at a stop" is nothing at all. Falling back to the
            // straight line here instead would put a second copy of the constant-velocity model in the
            // blend, and a target that is accelerating would have its lead diluted by a model that
            // disagreed with it from a position of no evidence.
            if (slowing < 0.05) return new LeadVector();

            double stop = Math.Min(time, speed / slowing);
            return velocity * stop + Share(a, stop, stop);
        }

        // The game owns the ballistic table. We solve only relative motion and query
        // that table a bounded number of times, with no per-frame allocations.
        internal static CrowsIntercept Solve(LeadVector line, LeadVector velocity, LeadVector inherited,
            LeadVector acceleration, double confidence, Func<double,double> flightTable)
        {
            if(!line.Finite || !velocity.Finite || !inherited.Finite) return new CrowsIntercept();
            return SolveRelative(line, velocity-inherited, acceleration, confidence, flightTable);
        }

        internal static CrowsIntercept SolveRelative(LeadVector line, LeadVector relativeVelocity,
            LeadVector relativeAcceleration, double confidence, Func<double,double> flightTable)
        {
            return SolveRelative(line, relativeVelocity, relativeAcceleration, confidence, flightTable, null, 0);
        }

        /// <summary>
        /// The same solve with a motion mixture standing in for the single displacement model, and a
        /// launch delay carried on top of the time of flight.
        ///
        /// The delay is the fraction of a second between the solution being asked for and the round
        /// leaving - input, fire control and stabilisation lag - and it is flown like extra time of
        /// flight, because that is what it is: the target and the platform keep moving over
        /// (v_target - v_platform) while the station is still settling onto the lead it was given.
        ///
        /// The time-of-flight iteration is damped only when a step reverses the one before it. A
        /// crossing target's iteration is monotone and damping would only cost it iterations and, at
        /// this tolerance, accuracy; an approaching target's alternates around the fixed point, which
        /// is the case the damping exists for. The residual the convergence test reads stays the true
        /// difference to the table's answer, so a damped iteration still has to reach the fixed point
        /// to be called converged.
        /// </summary>
        internal static CrowsIntercept SolveRelative(LeadVector line, LeadVector relativeVelocity,
            LeadVector relativeAcceleration, double confidence, Func<double,double> flightTable,
            CrowsImm motion, double launchDelay)
        {
            var result=new CrowsIntercept(); double range=line.Length;
            if(!line.Finite || !relativeVelocity.Finite || !relativeAcceleration.Finite ||
                range<1 || range>4000 || flightTable==null) return result;
            double initial=flightTable(range);
            if(!Finite(initial) || initial<=0 || initial>15) return result;
            double t=initial, step=0;
            bool stepped=false;
            for(int i=0;i<6;i++)
            {
                LeadVector displacement=Displacement(motion,relativeVelocity,relativeAcceleration,confidence,t);
                double predictedRange=(line+displacement).Length;
                if(!Finite(predictedRange) || predictedRange<1 || predictedRange>4000) break;
                double next=flightTable(predictedRange);
                result.Iterations=i+1;
                if(!Finite(next) || next<=0 || next>15 || Math.Abs(next-initial)>Math.Max(0.5,initial*0.35)) break;
                result.Residual=Math.Abs(next-t);
                double advance=next-t;
                if(stepped && advance*step<0) advance*=IterationDamping;
                t+=advance; step=advance; stepped=true;
                if(result.Residual<=0.001) { result.Converged=true; break; }
            }
            double horizon=t+(result.Converged && Finite(launchDelay) && launchDelay>0 ? launchDelay : 0);
            LeadVector travel=result.Converged
                ? Displacement(motion,relativeVelocity,relativeAcceleration,confidence,horizon)
                : relativeVelocity*initial;
            if(!result.Converged) t=initial;
            LeadVector direction=line*(1/range);
            result.TransverseDisplacement=travel-direction*LeadVector.Dot(travel,direction);
            result.FlightTime=t;
            result.Valid=result.TransverseDisplacement.Finite && result.TransverseDisplacement.Length<=1000;
            return result;
        }

        private static LeadVector Displacement(CrowsImm motion, LeadVector velocity, LeadVector acceleration,
            double confidence, double time)
        {
            return motion == null
                ? TargetDisplacement(velocity, acceleration, confidence, time)
                : motion.Displace(velocity, acceleration, confidence, time);
        }
    }
}
