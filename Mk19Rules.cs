using System;

namespace M1A1Abrams
{
    /// <summary>
    /// The Mk 19 Mod 3 numbers, kept out of the Unity code so they can be checked without
    /// the game running. Sources are the three reference notes supplied with the request:
    /// the M430/M430A1 data sheet, the Mk 19 rate of fire, and the dispersion note.
    /// </summary>
    internal static class Mk19Rules
    {
        internal const string M430 = "M430";
        internal const string M430A1 = "M430A1";

        // The names the rounds carry in game. They are also how the rest of the mod recognises
        // a Mk 19 grenade - the codex and clip objects are rebuilt every session, the names are not.
        internal const string M430Name = "M430 HEDP";
        internal const string M430A1Name = "M430A1 HEDP";

        // Mk 19 Mod 3 is rated 325-375 rds/min theoretical. The game fires one round per
        // cycle and the cycle is the cyclic rate, so the middle of the band is used.
        internal const float CyclicRpm = 350f;

        internal static float CycleTime { get { return 60f / CyclicRpm; } }

        // 2-4 mil from a steady mount, 5-8 mil once a burst is running. A single number has
        // to stand for both, so the middle of the burst band is used: 4.6 mil.
        internal const float DispersionDegrees = 0.26f;

        // 40 x 53 mm HEDP.
        internal const float CaliberMm = 40f;
        internal const float Mass = 0.340f;          // M430 340 g; M430A1 340-350 g
        internal const float Velocity = 241f;        // m/s at the muzzle
        internal const float MaxRange = 2200f;
        internal const float ArmingDistance = 25f;   // M549 PIBD, 18-30 m

        // The 96-round can the request asked for; the real cans are 32 and 48.
        internal const int BeltCapacity = 96;

        // Two spare belts in the rack - 192 rounds - and ten seconds to change one in. The
        // change is silent: the borrowed .50 cal feed would otherwise play its clip-swap audio.
        internal const int ReserveBelts = 2;
        internal const float ReloadSeconds = 10f;

        // The game integrates dv/dt = -Coeff * SectionalArea * density / Mass * v * |v|,
        // which is the quadratic drag law with the tabulated coefficient halved. The real
        // M430 tops out near 2,200 m, so the coefficient is trimmed to 0.100 (Cd 0.20) to put
        // the maximum range at about 2,230 m instead of 2,170 m.
        internal const float DragCoeff = 0.100f;     // half of Cd 0.20

        internal static float Area { get { return (float)(Math.PI * 0.040 * 0.040 / 4.0); } }

        // The game's firing table stops integrating after ten seconds of flight. At 241 m/s
        // that is about 1,580 m, so every solution past that range came back clamped and the
        // grenades fell short. A grenade needs roughly nineteen seconds to reach 2,150 m, so
        // the table is rebuilt with room to spare. The elevation sweep also has to reach past
        // the angle a slow round actually needs: the best part of 30 degrees at long range.
        internal const float FiringTableSeconds = 30f;
        internal const float FiringTableMaxElevation = 40f;

        internal const float PenM430 = 51f;          // RHA, mm
        internal const float PenM430A1 = 76f;
        internal const float TntM430 = 0.032f;       // kg of Comp A5
        internal const float TntM430A1 = 0.039f;
        internal const int FragmentsM430 = 110;      // 100-150 effective fragments
        internal const int FragmentsM430A1 = 150;

        /// <summary>
        /// Maps a config value onto a belt key, or null when the value is not one of the two
        /// grenades. Accepts the short names, the HUD names and any spacing/case variant.
        /// </summary>
        internal static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;

            string key = value.Trim().ToUpperInvariant().Replace(" ", string.Empty).Replace("-", string.Empty);

            if (key == "M430" || key == "M430HEDP") return M430;
            if (key == "M430A1" || key == "M430A1HEDP") return M430A1;
            return null;
        }
    }
}
