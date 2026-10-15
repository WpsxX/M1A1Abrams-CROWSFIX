using System;
using GHPC;
using UnityEngine;

namespace M1A1Abrams
{
    /// <summary>
    /// The box each vehicle family is tracked with: a centre and a size in the vehicle's own frame.
    ///
    /// This is the table approach the Pact Increased Lethality mod uses for its own tracking gates
    /// (<c>Sights/TrackingDimensions.DIMS</c>), and the reasoning is worth keeping: a vehicle is a
    /// known object, someone has already looked at each of these models and measured where the hull
    /// actually sits, and a number looked up by name cannot be blown out by a stray mesh, a skinned
    /// bind pose or a decal sheet the way a measurement can. The table below is that mod's, ported
    /// value for value, with the three entries it got wrong or missed corrected and the families it
    /// never covered added.
    ///
    /// It is a *table of centres and sizes*, not of names: the matching is by substring on the unit's
    /// name, with a second pass over a normalized form of it, because the same vehicle is spelled
    /// "Mi24" in the prefab and "Mi-24" in the table, "T-72B3M" in one mod and "T72M" in the game -
    /// and a table that misses is a table that silently falls back to measuring a tank's barrel.
    ///
    /// <see cref="CrowsAutoTrack"/> prefers a match here and falls back to measuring the model for
    /// anything this table does not know (which is what covers the vehicle mods - a Fulda 1989
    /// T-72B3M matches the T-72 family, an entirely new vehicle does not). Whatever it does, it says
    /// both numbers in the log once per family, so a wrong row here can be seen and fixed.
    /// </summary>
    internal static class CrowsTrackingBoxes
    {
        /// <summary>A box in the vehicle's own frame: where its middle is, and how big it is.</summary>
        internal struct Box
        {
            public Vector3 centre;
            public Vector3 size;

            public Box(float x, float y, float z, float sx, float sy, float sz)
            {
                centre = new Vector3(x, y, z);
                size = new Vector3(sx, sy, sz);
            }
        }

        private struct Family
        {
            public string label;

            /// <summary>Substrings that identify the family, tried in order.</summary>
            public string[] names;

            public Box box;
        }

        /// <summary>
        /// Ordered, most specific first: a name that matches several rows takes the first, so "M113"
        /// can never be caught by an M1 row and "M60A3" never by a "60" somewhere else.
        ///
        /// The rows marked [PIL] are ported unchanged from Pact Increased Lethality's table; the others
        /// are this mod's, from the vehicles' own published dimensions, and exist because that table
        /// covered only what that mod's vehicles needed.
        /// </summary>
        private static readonly Family[] families =
        {
            // --- helicopters first: their names collide with nothing, and a fuselage box is not a
            //     hull box, so they must not fall through to a tank row.
            new Family { label = "OH-58A", names = new[] { "OH-58", "OH58" },
                box = new Box(0f, 2.01f, 0.22f, 2f, 2f, 5f) },                          // [PIL]
            new Family { label = "AH-1", names = new[] { "AH-1", "AH1" },
                box = new Box(0f, 1.83f, 0.73f, 3.4f, 2.5f, 7f) },                        // [PIL]
            new Family { label = "Mi-24", names = new[] { "Mi-24", "Mi24", "MI24" },
                box = new Box(0f, 1.87f, -1.2f, 2.7f, 3f, 10f) },                         // [PIL] (their key
                                                                                          // never matched:
                                                                                          // the prefab is "Mi24")
            new Family { label = "Mi-8", names = new[] { "Mi-8", "Mi8", "MI8" },
                box = new Box(0f, 1.33f, -0.62f, 3.7f, 3.5f, 11f) },                      // [PIL]
            new Family { label = "Mi-2", names = new[] { "Mi-2", "Mi2" },
                box = new Box(0f, 1.2f, -0.3f, 2.5f, 2.4f, 5.5f) },                       // this mod's: theirs
                                                                                          // was copied from the
                                                                                          // BRDM-2 row and boxed a
                                                                                          // helicopter as an APC.

            // --- APCs and IFVs without a tank row to fall into.
            new Family { label = "M113/M901", names = new[] { "M113", "M901" },
                box = new Box(0f, 1.17f, 0.35f, 2.5f, 2.4f, 4.7f) },                      // [PIL]
            new Family { label = "BMP", names = new[] { "BMP" },
                box = new Box(0f, 1.002f, 0.12f, 3f, 2f, 6.95f) },                        // [PIL]
            new Family { label = "BTR", names = new[] { "BTR" },
                box = new Box(0f, 1.35f, 0f, 3f, 2.6f, 7.5f) },                           // [PIL]
            new Family { label = "BRDM-2", names = new[] { "BRDM" },
                box = new Box(0f, 1.228f, -0.221f, 2.25f, 2.3f, 5.75f) },                 // [PIL]
            new Family { label = "Marder/Bradley", names = new[] { "Marder", "Bradley", "M2A" },
                box = new Box(0f, 1.38f, 0.45f, 3f, 2.5f, 6f) },                          // [PIL]
            new Family { label = "Luchs", names = new[] { "Luchs" },
                box = new Box(0f, 1.35f, -0.3f, 3f, 2.6f, 7.4f) },                        // this mod's
            new Family { label = "PT-76", names = new[] { "PT76", "PT-76" },
                box = new Box(0f, 1.14f, 0.32f, 3f, 2.25f, 7f) },                         // [PIL]

            // --- tanks.
            new Family { label = "T-72/T-80/T-64", names = new[] { "T72", "T-72", "T80", "T-80", "T64", "T-64" },
                box = new Box(0f, 1.096f, -0.523f, 3.6f, 2.2f, 6.5f) },                   // [PIL]
            new Family { label = "T-54/T-55/T-62", names = new[] { "T54", "T-54", "T55", "T-55", "T62", "T-62" },
                box = new Box(0f, 1.124f, 0.087f, 3.25f, 2.35f, 6f) },                    // [PIL]
            new Family { label = "T-34-85", names = new[] { "T-34", "T34" },
                box = new Box(0f, 1.34f, 0.36f, 3f, 2.6f, 6f) },                          // [PIL]
            new Family { label = "M60/Leopard 1", names = new[] { "M60", "LEO" },
                box = new Box(0f, 1.32f, 0f, 3.6f, 3f, 7f) },                             // [PIL]
            new Family { label = "M1", names = new[] { "_M1", "M1A1", "M1A2", "M1IP" },
                box = new Box(0f, 1.21f, -0.25f, 3.5f, 2.4f, 7.8f) },                     // [PIL]

            // --- soft vehicles: the smallest boxes in the game, and the ones a measurement is most
            //     likely to inflate with a mirror or an open door.
            new Family { label = "M151", names = new[] { "M151" },
                box = new Box(0f, 0.77f, -0.25f, 1.7f, 1.5f, 3f) },                       // [PIL], width fixed:
                                                                                          // theirs was 0.5 m, which
                                                                                          // is a jeep 50 cm across.
            new Family { label = "UAZ-469", names = new[] { "UAZ" },
                box = new Box(0f, 0.9f, 0f, 2f, 1.9f, 4f) },                              // this mod's
            new Family { label = "M923", names = new[] { "M923", "M939" },
                box = new Box(0f, 1.37f, -0.78f, 2.3f, 2.8f, 7.5f) },                     // [PIL]
            new Family { label = "Ural", names = new[] { "Ural" },
                box = new Box(0f, 1.471f, -1.215f, 2.65f, 3f, 7.3f) },                    // [PIL]
        };

        /// <summary>
        /// The family a unit belongs to, if the table knows it. Every name a unit answers to is tried -
        /// the GameObject's, the one the HUD prints and the mission's unique name - because the three
        /// differ and only one of them has to match.
        /// </summary>
        internal static bool TryGet(Unit unit, out Box box, out string label)
        {
            box = new Box();
            label = null;

            if (unit == null)
            {
                return false;
            }

            // The GameObject's name carries the prefab's ("T72M(Clone)"), the friendly name is what the
            // player reads, and the unique name is what the mission called it; any of the three can be
            // the one that names the family.
            string[] candidates = new string[3];

            candidates[0] = unit.gameObject == null ? null : unit.gameObject.name;

            try
            {
                candidates[1] = unit.FriendlyName;
                candidates[2] = unit.UniqueName;
            }
            catch (Exception)
            {
                // a unit that will not name itself still has a GameObject name
            }

            for (int c = 0; c < candidates.Length; c++)
            {
                if (string.IsNullOrEmpty(candidates[c]))
                {
                    continue;
                }

                for (int i = 0; i < families.Length; i++)
                {
                    if (!Matches(candidates[c], families[i].names))
                    {
                        continue;
                    }

                    box = families[i].box;
                    label = families[i].label;

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether a name belongs to a family. Two passes: the raw substring test the original table
        /// used, which is what its keys were written for, and then the same test on a normalized name
        /// with the separators taken out - which is what catches "T-72B3M" for a "T72" row and "Mi24"
        /// for a "Mi-24" one. Patterns shorter than three characters are raw-only, which is what keeps
        /// "_M1" from claiming an M113.
        /// </summary>
        private static bool Matches(string name, string[] patterns)
        {
            for (int i = 0; i < patterns.Length; i++)
            {
                if (name.IndexOf(patterns[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            string normalized = Normalize(name);

            for (int i = 0; i < patterns.Length; i++)
            {
                string pattern = Normalize(patterns[i]);

                if (pattern.Length >= 3 && normalized.IndexOf(pattern, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>A name with everything that is not a letter or a digit removed, in upper case.</summary>
        private static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            char[] buffer = new char[name.Length];
            int length = 0;

            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];

                if (char.IsLetterOrDigit(c))
                {
                    buffer[length++] = char.ToUpperInvariant(c);
                }
            }

            return new string(buffer, 0, length);
        }

        /// <summary>
        /// The table's box as half extents in the unit's own frame, clamped like a measured one: a row
        /// with a typo in it must not be able to draw a gate the size of a building, and the centre is
        /// kept within reach of the game's own centre point for the same reason.
        /// </summary>
        internal static void HalfExtents(Box box, Transform root, Vector3 aim,
            float min_half, float max_half, float max_centre_shift,
            out Vector3 local_centre, out Vector3 local_half)
        {
            // A destroyed or unset root would throw on the very next line. The caller wants a usable
            // box whatever happens, and a centre at the aim with the minimum half extent is the same
            // answer the fallback measurement would give for a model it could not measure.
            if (root == null)
            {
                local_centre = Vector3.zero;
                local_half = Vector3.one * Mathf.Max(min_half, 0f);
                return;
            }

            Vector3 scale = root.lossyScale;
            scale = new Vector3(Mathf.Max(Mathf.Abs(scale.x), 1e-4f),
                Mathf.Max(Mathf.Abs(scale.y), 1e-4f), Mathf.Max(Mathf.Abs(scale.z), 1e-4f));

            Vector3 reference = root.InverseTransformPoint(aim);
            local_centre = new Vector3(box.centre.x / scale.x, box.centre.y / scale.y, box.centre.z / scale.z);

            Vector3 shift = Vector3.Scale(local_centre - reference, scale);

            if (shift.magnitude > max_centre_shift)
            {
                local_centre = reference + (local_centre - reference) * (max_centre_shift / shift.magnitude);
            }

            local_half = new Vector3(
                Mathf.Clamp(box.size.x * 0.5f / scale.x, min_half / scale.x, max_half / scale.x),
                Mathf.Clamp(box.size.y * 0.5f / scale.y, min_half / scale.y, max_half / scale.y),
                Mathf.Clamp(box.size.z * 0.5f / scale.z, min_half / scale.z, max_half / scale.z));
        }
    }
}
