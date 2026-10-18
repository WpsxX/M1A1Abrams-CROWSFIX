using System;

namespace M1A1Abrams
{
    // Pure data/selection rules, also compiled by the headless validation harness.
    internal static class CrowsAmmoRules
    {
        internal const string M8 = "M8 API";
        internal const string Mk211 = "Mk 211 Mod 0";
        internal const string M903 = "M903 SLAP";
        internal const string M962 = "M962 SLAP-T";

        // The flying 7.62 mm WHA core, not the 35 g bullet-plus-sabot assembly.
        internal const float Velocity = 1219f;
        internal const float CoreMass = 0.02315f;
        internal const float CoreDiameter = 0.00762f;
        // The engine's standard air density: WorldEnvironmentManager.AirDensity defaults
        // to 1.25 kg/m^3, and LiveRound integrates drag with that value.
        internal const float ReferenceDensity = 1.25f;
        internal const float TracerRange = 1500f;

        // The donor radius is retained so the cone geometry remains consistent with
        // the 7.62 mm flying core. BuildSlap raises SpallMultiplier to compensate for
        // the engine's Radius-based cone/count reduction, while keeping the round
        // kinetic (TntEquivalentKg remains zero).

        // GHPC's drag model is purely quadratic:
        //     dv/dt = -Coeff * SectionalArea * airDensity / Mass * v * |v|
        // Integrating that in TIME gives 1/v = 1/v0 + k*t, but the law the player
        // actually flies is the distance form, which is exponential:
        //     v(x) = v0 * exp(-k*x),   k = Coeff * SectionalArea * airDensity / Mass,
        // and LiveRound.CurrentPenRating scores penetration as
        //     P(x) = RhaPenetration * (v / v0)^2 = RhaPenetration * exp(-2*k*x).
        // Earlier builds solved the hyperbolic TIME form in x instead, which made k
        // roughly a thousand times too small and held the round at its muzzle figure
        // for the whole flight; the coefficients below now come from the exponential
        // law. This is the same k the CROWS predictor derives in
        // CrowsPredictor.Configure, so the gun and the lead solver stay in step.
        //
        // Penetration is what the player feels, so the curve is anchored on the
        // documented 34 mm at 500 m and 19 mm at 1,500 m. Under the exponential law
        // those two points fix both k and the muzzle figure, and they land 1,200 m
        // on 22.61 mm against the documented 22.62 mm - the check that the anchors
        // and the engine's own scoring agree.
        internal static readonly float Area = (float)(Math.PI * CoreDiameter * CoreDiameter / 4);

        // Design-intent anchors from the round document (README "The new SLAP rounds").
        internal const float PenetrationAt500 = 34f;
        internal const float PenetrationAt1500 = 19f;

        // exp(-2k*1500) / exp(-2k*500) = 19/34 fixes k for the 1,000 m span.
        internal static readonly float DragPerMetre =
            (float)(Math.Log(PenetrationAt500 / (double)PenetrationAt1500) / (2.0 * (1500.0 - 500.0)));

        // Coeff is calibrated at the engine's standard air density, so the k the game
        // integrates is exactly the k above.
        internal static readonly float DragCoeff = DragPerMetre * CoreMass / (Area * ReferenceDensity);

        // exp(2*k*500) turns the 500 m anchor back into the muzzle figure.
        internal static readonly float MuzzlePenetration =
            (float)(PenetrationAt500 * Math.Exp(2.0 * DragPerMetre * 500.0));

        internal static string Normalize(string value)
        {
            string key = (value ?? "").Trim().ToUpperInvariant();
            switch (key)
            {
                case "M8": case "M8 API": return M8;
                case "MK211": case "MK 211": case "MK211 MOD 0": case "MK 211 MOD 0":
                case "MK 211 MOD 0 HEIAP": case "RAUFOSS": return Mk211;
                case "M903": case "M903 SLAP": return M903;
                case "M962": case "M962 SLAP-T": case "M962 SLAPT": return M962;
                default: return null;
            }
        }
    }
}
