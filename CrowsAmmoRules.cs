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
        internal const float ReferenceDensity = 1.225f;
        internal const float TracerRange = 1500f;

        // The donor radius is retained so the cone geometry remains consistent with
        // the 7.62 mm flying core. BuildSlap raises SpallMultiplier to compensate for
        // the engine's Radius-based cone/count reduction, while keeping the round
        // kinetic (TntEquivalentKg remains zero).

        // GHPC's drag model is purely quadratic:
        //     dv/dt = -Coeff * SectionalArea * airDensity / Mass * v * |v|
        // so speed against distance is hyperbolic, 1/v = 1/v0 + k*x, with
        //     k = Coeff * SectionalArea * airDensity / Mass,
        // and LiveRound.CurrentPenRating scores penetration as
        //     P = RhaPenetration * (v / v0)^2 = RhaPenetration / (1 + k*v0*x)^2.
        // This is the same k the CROWS predictor derives in
        // CrowsPredictor.Configure, so the gun and the lead solver stay in step.
        //
        // The document supplies a retained-speed table and a penetration table
        // that cannot both hold under this law: its speed falls only 23% from
        // 500 m to 1,000 m where its penetration falls 26%, and P scales with
        // v squared, so speed would have to fall 14%. Penetration is what the
        // player feels, so the curve is anchored on the document's 34 mm at
        // 500 m and 19 mm at 1,500 m, which lands 1,000 m at 26.0 mm against a
        // documented 25 mm. Retained speed therefore runs faster than the
        // document's table (approx. 1,043 / 911 / 809 m/s at 500 / 1,000 /
        // 1,500 m), which is the price of that choice.
        internal static readonly float Area = (float)(Math.PI * CoreDiameter * CoreDiameter / 4);
        internal static readonly float DragPerMetre =
            (float)((Math.Sqrt(34.0 / 19.0) - 1.0) / (Velocity * 1000.0));
        internal static readonly float DragCoeff = DragPerMetre * CoreMass / (Area * ReferenceDensity);

        // The document's 34 mm at 500 m fixes the muzzle figure exactly:
        //     MuzzlePenetration = 34 * (1 + k*v0*500)^2.
        internal static readonly float MuzzlePenetration =
            (float)(34.0 * Math.Pow(1.0 + DragPerMetre * Velocity * 500.0, 2.0));

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
