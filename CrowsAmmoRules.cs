using System;

namespace M1A1Abrams
{
    // Pure data/selection rules, also compiled by the headless validation harness.
    internal static class CrowsAmmoRules
    {
        internal const string M8 = "M8 API";
        internal const string Mk211 = "Mk 211 Mod 0";
        internal const string M903 = "M903 SLAP";
        internal const float Velocity = 1219f;
        internal const float M903Mass = 0.02315f;
        internal const float M962Mass = 0.02284f;
        internal const float CoreDiameter = 0.00762f;
        internal const float ReferenceDensity = 1.225f;
        internal const float TracerRange = 1500f;

        // GHPC: P = P0 * (v/v0)^2, dv/dx = -Coeff * Area * density / mass * v.
        // Fit the document's 34 mm @ 500 m and 19 mm @ 1500 m.
        // This gives 22.63 mm @ 1200 m (document: approximately 23 mm).
        internal static readonly float Area = (float)(Math.PI * CoreDiameter * CoreDiameter / 4);
        internal static readonly float DragPerMetre = (float)(Math.Log(34.0 / 19.0) / 2000.0);
        internal static readonly float MuzzlePenetration = (float)(34 * Math.Exp(2 * DragPerMetre * 500));

        internal static float DragCoefficient(float mass)
        {
            return DragPerMetre * mass / (Area * ReferenceDensity);
        }

        internal static string Normalize(string value)
        {
            string key = (value ?? "").Trim().ToUpperInvariant();
            switch (key)
            {
                case "M8": case "M8 API": return M8;
                case "MK211": case "MK 211": case "MK211 MOD 0": case "MK 211 MOD 0":
                case "MK 211 MOD 0 HEIAP": case "RAUFOSS": return Mk211;
                case "M903": case "M903 SLAP": return M903;
                default: return null;
            }
        }
    }
}
