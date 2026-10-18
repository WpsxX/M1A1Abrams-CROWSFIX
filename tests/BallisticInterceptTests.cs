using System;
using System.Diagnostics;
using System.Numerics;
using M1A1Abrams;

internal static class BallisticInterceptTests
{
    private static int passed;
    private static double worst;
    private static LeadVector V(double x = 0, double y = 0, double z = 0) { return new LeadVector(x, y, z); }
    private static Vector3 F(LeadVector v) { return new Vector3((float)v.X, (float)v.Y, (float)v.Z); }
    private static void Check(bool success, string message)
    {
        if (!success) throw new Exception("FAIL " + message);
        passed++;
    }

    private static CrowsProjectileModel Model(double muzzle = 979, double drag = 0.0004, double step = 1.0 / 60)
    {
        return new CrowsProjectileModel { MuzzleSpeed = muzzle, Drag = drag, Step = step,
            TemperatureScale = 1, Gravity = V(0, -9.81), ErrorCorrection = true };
    }

    // Independent float implementation of GHPC's live round evaluator. Unlike the solver,
    // this advances whole render frames and interpolates the final segment like a raycast.
    private static Vector3 Acceleration(Vector3 velocity, CrowsProjectileModel model)
    {
        float magnitudeSquared = velocity.LengthSquared();
        return F(model.Gravity) - (float)model.Drag * Vector3.Normalize(velocity) * magnitudeSquared;
    }

    private static void GameStep(ref Vector3 position, ref Vector3 velocity, float dt, CrowsProjectileModel model)
    {
        if (!model.ErrorCorrection)
        {
            position += velocity * dt;
            velocity += Acceleration(velocity, model) * dt;
            return;
        }
        Vector3 d1v = Acceleration(velocity, model);
        Vector3 d2x = velocity + d1v * dt;
        Vector3 d2v = Acceleration(d2x, model);
        Vector3 d3x = velocity + d2v * (dt * 0.5f);
        Vector3 d3v = Acceleration(d3x, model);
        Vector3 d4x = velocity + d3v * (dt * 0.5f);
        Vector3 d4v = Acceleration(d4x, model);
        Vector3 d5x = velocity + d4v * dt;
        Vector3 d5v = Acceleration(d5x, model);
        position += (d2x + 2 * d3x + 2 * d4x + d5x) * (dt / 6);
        velocity += (d2v + 2 * d3v + 2 * d4v + d5v) * (dt / 6);
    }

    private static Vector3 GamePosition(LeadVector direction, LeadVector own, double time, CrowsProjectileModel model)
    {
        Vector3 position = Vector3.Zero;
        Vector3 velocity = (F(direction) * (float)model.MuzzleSpeed + F(own)) * (float)model.TemperatureScale;
        int steps = (int)Math.Ceiling(time / model.Step);
        for (int i = 0; i < steps; i++)
        {
            Vector3 before = position;
            GameStep(ref position, ref velocity, (float)model.Step, model);
            if (i == steps - 1)
                position = Vector3.Lerp(before, position, (float)((time - i * model.Step) / model.Step));
        }
        return position;
    }

    private static CrowsBallisticSolution Verify(string name, LeadVector line, LeadVector target,
        LeadVector own, CrowsProjectileModel model, CrowsBallisticSolution seed = default,
        LeadVector acceleration = default, double confidence = 0, double initialTime = double.NaN,
        Func<double, LeadVector> targetTravel = null)
    {
        double estimate = model.Drag > 0
            ? (Math.Exp(model.Drag * line.Length) - 1) / (model.Drag * model.MuzzleSpeed * model.TemperatureScale)
            : line.Length / model.MuzzleSpeed;
        if (!double.IsNaN(initialTime)) estimate = initialTime;
        CrowsBallisticSolution s = CrowsBallisticIntercept.Solve(line, target, acceleration, confidence, own,
            model, null, estimate, seed);
        Check(s.Valid, name + " converges: " + s.Miss + " m after " + s.Iterations);
        Check(Math.Abs(s.Direction.Length - 1) < 1e-10, name + " retains muzzle speed");
        Vector3 desired = F(line) + (targetTravel == null ? F(target) * (float)s.FlightTime : F(targetTravel(s.FlightTime)));
        double miss = Vector3.Distance(GamePosition(s.Direction, own, s.FlightTime, model), desired);
        worst = Math.Max(worst, miss);
        Check(miss < 0.15, name + " independent game trajectory miss " + miss);
        return s;
    }

    private static void Main()
    {
        var clock = Stopwatch.StartNew();
        // Drag-heavy .50 test profiles plus this mod's actual M903 ballistic parameters.
        CrowsProjectileModel[] models = { Model(), Model(887, 0.0006),
            Model(CrowsAmmoRules.Velocity, CrowsAmmoRules.DragCoeff * CrowsAmmoRules.Area * 1.25 / CrowsAmmoRules.CoreMass) };
        double[] distances = { 300, 1000, 2000, 3000, 3500, 4000 };
        foreach (CrowsProjectileModel model in models)
        foreach (double range in distances)
        foreach (double own in new double[] { -25, 0, 25 })
        foreach (LeadVector target in new[] { V(), V(30), V(-60), V(20, 0, -40), V(20, 0, 30) })
        {
            // The slowest/strongest-drag profile cannot reach these receding targets
            // within the bounded low-arc solution. Rejection is checked separately.
            if (model.Drag > 0.0005 && range >= 3500 && target.Z > 0) continue;
            if (model.Drag > 0.0005 && range >= 4000 && target.X <= -60) continue;
            Verify($"{model.MuzzleSpeed}/{range}/own{own}/target{target.X},{target.Z}",
                V(0, 0, range), target, V(own), model);
        }
        foreach (double fps in new double[] { 30, 60, 120, 240 })
        foreach (bool corrected in new[] { false, true })
        {
            var model = Model(step: 1 / fps);
            model.ErrorCorrection = corrected;
            Verify($"{fps} fps, corrected={corrected}", V(0, 100, 2600), V(55, -3, -30), V(-22, 4, 15), model);
        }
        foreach (double temp in new double[] { 0.97, 1.04 })
        {
            var model = Model(); model.TemperatureScale = temp;
            Verify("temperature scales inherited vector too", V(0, -120, 2500), V(-50, 2, 30), V(25, -2, -20), model);
        }
        foreach (var model in models)
        foreach (double range in new double[] { 1000, 3000, 4000 })
        foreach (double tableTime in new double[] { 1, 50 })
            Verify("clamped/stale FCS flight time", V(0, 0, range), V(30), V(25), model, initialTime: tableTime);

        // Independent target paths: carry acceleration for 0.75 s, then coast.
        foreach (double acceleration in new double[] { -2, 2 })
            Verify("bounded acceleration/braking", V(0, 0, 3500), V(30), V(-25), Model(),
                acceleration: V(acceleration), confidence: 0.65,
                targetTravel: t => V(30 * t + acceleration * 0.65 * 0.75 * (t - 0.375)));
        Verify("bounded turning", V(0, 0, 3500), V(30), V(25), Model(),
            acceleration: V(0, 0, 4), confidence: 0.65, targetTravel: t =>
            {
                double rate = 4 * 0.65 / 30, angle = rate * 0.75;
                return V(30 * Math.Sin(angle) / rate + 30 * Math.Cos(angle) * (t - 0.75), 0,
                    30 * (1 - Math.Cos(angle)) / rate + 30 * Math.Sin(angle) * (t - 0.75));
            });

        var standard = Model();
        var stopped = Verify("stationary at 3000 m", V(0, 0, 3000), V(), V(), standard);
        var moving = Verify("own lateral speed at 3000 m", V(0, 0, 3000), V(), V(25), standard);
        double inheritedTime = -moving.Direction.X * (3000 / moving.Direction.Z) / 25;
        Check(inheritedTime < moving.FlightTime * 0.8, "drag prevents treating own velocity as constant throughout flight");
        // Feed the old relative-velocity formula the same flight table, then measure its actual miss.
        Func<double, double> table = r => (Math.Exp(standard.Drag * r) - 1) / (standard.Drag * standard.MuzzleSpeed);
        var old = CrowsPredictor.Solve(V(0, 0, 3000), V(), V(25), V(), 0, table);
        LeadVector oldBore = stopped.Direction * (3000 / stopped.Direction.Z) + old.TransverseDisplacement;
        oldBore *= 1 / oldBore.Length;
        double oldMiss = Vector3.Distance(GamePosition(oldBore, V(25), moving.FlightTime, standard), new Vector3(0, 0, 3000));
        Check(oldMiss > 10, "reproduces legacy far/high-speed error: " + oldMiss);
        Console.WriteLine($"3000 m, own 25 m/s: legacy miss {oldMiss:F2} m; new shot < 0.15 m (simulation)");

        var seed = moving;
        long ticks = 0;
        long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 120; i++)
        {
            long before = Stopwatch.GetTimestamp();
            seed = CrowsBallisticIntercept.Solve(V(i * 0.3, 0, 3000 - i * 0.2), V(18, 0, -12), V(), 0,
                V(25), standard, null, moving.FlightTime, seed);
            ticks += Stopwatch.GetTimestamp() - before;
            Check(seed.Valid && seed.Iterations <= 4, "moving-frame warm start converges");
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - bytesBefore;
        Check(allocated == 0, "hot solver allocates no managed memory");
        Console.WriteLine($"Warm solver: {ticks * 1000.0 / Stopwatch.Frequency / 120:F3} ms/solve on this machine; {allocated} allocated bytes");

        Check(!CrowsBallisticIntercept.Solve(V(double.NaN), V(), V(), 0, V(), standard, null, 1, default).Valid,
            "nonfinite line rejected");
        Check(!CrowsBallisticIntercept.Solve(V(0, 0, 3000), V(200), V(), 0, V(), standard, null, 1, default).Valid,
            "teleport/unphysical velocity rejected");
        Check(!CrowsBallisticIntercept.Solve(V(0, 0, 4000), V(0, 0, 100), V(), 0, V(), models[1], null, 15, default).Valid,
            "unreachable receding target rejected");
        var invalid = standard; invalid.Drag = double.NaN;
        Check(!CrowsBallisticIntercept.Solve(V(0, 0, 3000), V(), V(), 0, V(), invalid, null, 1, default).Valid,
            "invalid model rejected");
        Console.WriteLine($"TOTAL {passed} passed; worst independent shot miss {worst:F4} m; elapsed {clock.Elapsed.TotalSeconds:F2} s");
    }
}
