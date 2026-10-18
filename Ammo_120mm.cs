using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC.Effects;
using GHPC.Weapons;
using UnityEngine;
using GHPC.Weaponry;
using ModUtil;
using VUtil = VanillaAssetUtil.ModUtil.Util;

namespace M1A1Abrams
{
    public class Ammo_120mm : ModUtil.Module
    {
        public static Dictionary<string, AmmoClipCodexScriptable> ap;
        public static Dictionary<string, AmmoClipCodexScriptable> heat;

        public static AmmoClipCodexScriptable clip_codex_m830;
        public static AmmoType.AmmoClip clip_m830;
        public static AmmoCodexScriptable ammo_codex_m830;
        public static AmmoType ammo_m830;

        public static AmmoClipCodexScriptable clip_codex_m830a1;
        public static AmmoType.AmmoClip clip_m830a1;
        public static AmmoCodexScriptable ammo_codex_m830a1;
        public static AmmoType ammo_m830a1;
        public static AmmoType m830a1_forward_frag = new AmmoType();

        public static AmmoClipCodexScriptable clip_codex_m827;
        public static AmmoType.AmmoClip clip_m827;
        public static AmmoCodexScriptable ammo_codex_m827;
        public static AmmoType ammo_m827;

        public static AmmoClipCodexScriptable clip_codex_m829;
        public static AmmoType.AmmoClip clip_m829;
        public static AmmoCodexScriptable ammo_codex_m829;
        public static AmmoType ammo_m829;

        public static AmmoClipCodexScriptable clip_codex_m829a1;
        public static AmmoType.AmmoClip clip_m829a1;
        public static AmmoCodexScriptable ammo_codex_m829a1;
        public static AmmoType ammo_m829a1;

        public static AmmoClipCodexScriptable clip_codex_m829a2;
        public static AmmoType.AmmoClip clip_m829a2;
        public static AmmoCodexScriptable ammo_codex_m829a2;
        public static AmmoType ammo_m829a2;

        public static AmmoClipCodexScriptable clip_codex_m829a3;
        public static AmmoType.AmmoClip clip_m829a3;
        public static AmmoCodexScriptable ammo_codex_m829a3;
        public static AmmoType ammo_m829a3;

        public static AmmoClipCodexScriptable clip_codex_m829a4;
        public static AmmoType.AmmoClip clip_m829a4;
        public static AmmoCodexScriptable ammo_codex_m829a4;
        public static AmmoType ammo_m829a4;

        public static GameObject ammo_m827_vis = null;
        public static GameObject ammo_m829_vis = null;
        public static GameObject ammo_m829a1_vis = null;
        public static GameObject ammo_m829a2_vis = null;
        public static GameObject ammo_m829a3_vis = null;
        public static GameObject ammo_m829a4_vis = null;
        public static GameObject ammo_m830_vis = null;
        public static GameObject ammo_m830a1_vis = null;


        public override void UnloadDynamicAssets()
        {
            DestroyVisual(ref ammo_m827_vis);
            DestroyVisual(ref ammo_m829_vis);
            DestroyVisual(ref ammo_m829a1_vis);
            DestroyVisual(ref ammo_m829a2_vis);
            DestroyVisual(ref ammo_m829a3_vis);
            DestroyVisual(ref ammo_m829a4_vis);
            DestroyVisual(ref ammo_m830_vis);
            DestroyVisual(ref ammo_m830a1_vis);
        }

        private static void DestroyVisual(ref GameObject visual)
        {
            if (visual != null)
            {
                GameObject.DestroyImmediate(visual);
                visual = null;
            }
        }

        public override void LoadDynamicAssets()
        {
            // xm827
            ammo_m827 = new AmmoType();
            VUtil.ShallowCopy(ammo_m827, Assets.ammo_m833);
            ammo_m827.Name = "M827 APFSDS-T";
            ammo_m827.Caliber = 120;
            ammo_m827.RhaPenetration = 500f;
            ammo_m827.MuzzleVelocity = 1650f;
            ammo_m827.Mass = 4.64f;
            ammo_m827.SectionalArea = 0.0012f;
            ammo_m827.SpallMultiplier = 1.15f;

            VUtil.Coalesce(ref ammo_codex_m827);
            ammo_codex_m827.AmmoType = ammo_m827;
            ammo_codex_m827.name = "ammo_m827";

            clip_m827 = new AmmoType.AmmoClip();
            clip_m827.Capacity = 1;
            clip_m827.Name = "M827 APFSDS-T";
            clip_m827.MinimalPattern = new AmmoCodexScriptable[1];
            clip_m827.MinimalPattern[0] = ammo_codex_m827;

            VUtil.Coalesce(ref clip_codex_m827);
            clip_codex_m827.name = "clip_m827";
            clip_codex_m827.CompatibleWeaponSystems = new WeaponSystemCodexScriptable[1];
            clip_codex_m827.ClipType = clip_m827;

            // m829 
            ammo_m829 = new AmmoType();
            VUtil.ShallowCopy(ammo_m829, Assets.ammo_m833);
            ammo_m829.Name = "M829 APFSDS-T";
            ammo_m829.Caliber = 120;
            // The 1984 baseline round and the weakest dart in the file. Like every other round here its
            // figure is set from a 2,000 m target rather than from a muzzle value, because this dart has
            // the worst drag-to-mass ratio in the family - `Coeff` 0.20 against only 4.27 kg, the lightest
            // rod in the file - so it keeps just 0.9038 of its muzzle velocity at that range and only
            // 0.8168 of its penetration. The 490 mm the file asks for at 2,000 m therefore implies
            //         490 / 0.9038^2 = 490 / 0.8168 = 600 mm at the muzzle
            // which is what is written below. See the M829A4 block for the shared drag model.
            ammo_m829.RhaPenetration = 600f;
            ammo_m829.MuzzleVelocity = 1670f;
            ammo_m829.Mass = 4.27f;
            ammo_m829.SectionalArea = 0.0009f;
            ammo_m829.SpallMultiplier = 1.15f;
            ammo_m829.Coeff = 0.20f;

            // The original M829 is the round heavy ERA was proven against, and it lost: US trials on a
            // Kontakt-5 T-72 found the armour "immune to the DU penetrators of M829". Nothing about the
            // round resists a flyer plate - it is the 22 mm, 31:1 DU rod every later mark was built to
            // replace, with no precursor tip to detonate the ERA early and no added diameter to survive
            // the shear. 0.95 leaves almost the whole of Kontakt-5's 200-250 mm rating standing against it
            // (190 mm, or 237 mm on the top of the manufacturer's band), which is what "immune" means
            // here, and it is the only round in the family that gets essentially no relief.
            ammo_m829.ArmorOptimizations = HeavyEraOptimizations(0.95f);

            VUtil.Coalesce(ref ammo_codex_m829);
            ammo_codex_m829.AmmoType = ammo_m829;
            ammo_codex_m829.name = "ammo_m829";

            clip_m829 = new AmmoType.AmmoClip();
            clip_m829.Capacity = 1;
            clip_m829.Name = "M829 APFSDS-T";
            clip_m829.MinimalPattern = new AmmoCodexScriptable[1];
            clip_m829.MinimalPattern[0] = ammo_codex_m829;

            VUtil.Coalesce(ref clip_codex_m829);
            clip_codex_m829.name = "clip_m829";
            clip_codex_m829.CompatibleWeaponSystems = new WeaponSystemCodexScriptable[1];
            clip_codex_m829.ClipType = clip_m829;

            // m829a1
            ammo_m829a1 = new AmmoType();
            VUtil.ShallowCopy(ammo_m829a1, Assets.ammo_m833);
            ammo_m829a1.Name = "M829A1 APFSDS-T";
            ammo_m829a1.Caliber = 120;
            // The one dart in the file that sits high inside its published band rather than under it: the
            // band is 540-570 mm at 2,000 m (midpoint 555) and the file asks for 560, near its top. The
            // A1's lighter rod keeps 0.9180 of its muzzle velocity and 0.8427 of its penetration, so
            //         560 / 0.9180^2 = 560 / 0.8427 = 665 mm at the muzzle
            // which is what is written below. It still trails the A2 and every later mark at every range;
            // see the M829A4 block for the shared drag model.
            ammo_m829a1.RhaPenetration = 665f;
            ammo_m829a1.SpallMultiplier = 1.15f;
            ammo_m829a1.MaxSpallRha = 22f;
            ammo_m829a1.MinSpallRha = 5f;
            ammo_m829a1.MuzzleVelocity = 1575;
            ammo_m829a1.Mass = 4.6f;
            ammo_m829a1.SectionalArea = 0.00082f;
            ammo_m829a1.Coeff = 0.20f;

            // The "Silver Bullet" is the dart the heavy ERA was built to stop: its 22 mm, 31:1 DU rod has
            // no precursor tip, so a Kontakt-5 flyer plate shears it off-axis before it reaches the base
            // armour. It nevertheless proved itself in 1991 against Iraqi T-72Ms, so it is not helpless
            // against the armour - 0.85 leaves Kontakt-5 counting 170 mm and Relikt 212.5 mm, a real
            // penalty but a lighter one than the original M829 pays, which is what the round's better
            // penetrator and 1991 combat record are worth. It is still well short of the A2's brute-force
            // approach and nowhere near the A3/A4's precursor tips.
            ammo_m829a1.ArmorOptimizations = HeavyEraOptimizations(0.85f);

            VUtil.Coalesce(ref ammo_codex_m829a1);
            ammo_codex_m829a1.AmmoType = ammo_m829a1;
            ammo_codex_m829a1.name = "ammo_m829a1";

            clip_m829a1 = new AmmoType.AmmoClip();
            clip_m829a1.Capacity = 1;
            clip_m829a1.Name = "M829A1 APFSDS-T";
            clip_m829a1.MinimalPattern = new AmmoCodexScriptable[1];
            clip_m829a1.MinimalPattern[0] = ammo_codex_m829a1;

            VUtil.Coalesce(ref clip_codex_m829a1);
            clip_codex_m829a1.name = "clip_m829a1";
            clip_codex_m829a1.CompatibleWeaponSystems = new WeaponSystemCodexScriptable[1];
            clip_codex_m829a1.ClipType = clip_m829a1;

            // m829a2 
            ammo_m829a2 = new AmmoType();
            VUtil.ShallowCopy(ammo_m829a2, Assets.ammo_m833);
            ammo_m829a2.Name = "M829A2 APFSDS-T";
            ammo_m829a2.Caliber = 120;
            // 730 mm at the muzzle would hold the published 2,000 m band of 600-630 mm (615 midpoint)
            // exactly; the file sits 30 mm under that (700), for 581 mm at 2,000 m. The A2 keeps its
            // higher muzzle velocity, so it still out-penetrates the A1 at every range.
            ammo_m829a2.RhaPenetration = 700f;
            ammo_m829a2.SpallMultiplier = 1.15f;
            ammo_m829a2.MaxSpallRha = 22f;
            ammo_m829a2.MinSpallRha = 5f;
            ammo_m829a2.MuzzleVelocity = 1680f;
            ammo_m829a2.Mass = 4.6f;
            ammo_m829a2.SectionalArea = 0.00082f;
            ammo_m829a2.Coeff = 0.20f;

            // The interim answer to Kontakt-5, and the first round in the family designed against it: the
            // 1994 A2 exists because trials showed K-5 stopped the earlier DU darts. It does not carry a
            // precursor tip - the stated approach was to "brute force" through the ERA - so its relief
            // comes from a stronger DU penetrator, a composite sabot and ~100 m/s more muzzle velocity,
            // all of which cut the time the rod spends inside the flyer plate's path. 0.80 leaves
            // Kontakt-5 at 160 mm and Relikt at 200 mm: better than the A1, still far short of the A3.
            ammo_m829a2.ArmorOptimizations = HeavyEraOptimizations(0.80f);

            VUtil.Coalesce(ref ammo_codex_m829a2);
            ammo_codex_m829a2.AmmoType = ammo_m829a2;
            ammo_codex_m829a2.name = "ammo_m829a2";

            clip_m829a2 = new AmmoType.AmmoClip();
            clip_m829a2.Capacity = 1;
            clip_m829a2.Name = "M829A2 APFSDS-T";
            clip_m829a2.MinimalPattern = new AmmoCodexScriptable[1];
            clip_m829a2.MinimalPattern[0] = ammo_codex_m829a2;

            VUtil.Coalesce(ref clip_codex_m829a2);
            clip_codex_m829a2.name = "clip_m829a2";
            clip_codex_m829a2.CompatibleWeaponSystems = new WeaponSystemCodexScriptable[1];
            clip_codex_m829a2.ClipType = clip_m829a2;

            // m829a3
            ammo_m829a3 = new AmmoType();
            VUtil.ShallowCopy(ammo_m829a3, Assets.ammo_m833);
            ammo_m829a3.Name = "M829A3 APFSDS-T";
            ammo_m829a3.Caliber = 120;
            // 816 mm at the muzzle would hold the published 2,000 m band of 670-700 mm (685 midpoint)
            // exactly; the file sits 31 mm under that (785), for 659 mm at 2,000 m. The heavier,
            // better-shaped rod still loses less speed than the A1/A2, which is why it overtakes the faster
            // A2 at long range - 1,425 m/s retained against 1,538 at 2 km.
            ammo_m829a3.RhaPenetration = 785f;
            ammo_m829a3.SpallMultiplier = 1.2f;
            ammo_m829a3.MaxSpallRha = 22f;
            ammo_m829a3.MinSpallRha = 7f;
            ammo_m829a3.MuzzleVelocity = 1555f;
            ammo_m829a3.Mass = 5.2f;
            ammo_m829a3.SectionalArea = 0.00086f;
            ammo_m829a3.Coeff = 0.22f;

            // The "wall breaker": a ~100 mm sacrificial precursor tip that detonates the ERA and then
            // shears off, plus a 25 mm rod whose bending stiffness is ~46% up on the A1/A2's 22 mm, so the
            // main DU body crosses the disrupted ERA box still straight. What is left of a heavy ERA
            // section against it is the flyer plates themselves - 0.25 puts Kontakt-5 at 50 mm and Relikt
            // at 62.5 mm, the "30-50 mm" the round is credited with.
            ammo_m829a3.ArmorOptimizations = HeavyEraOptimizations(0.25f);

            VUtil.Coalesce(ref ammo_codex_m829a3);
            ammo_codex_m829a3.AmmoType = ammo_m829a3;
            ammo_codex_m829a3.name = "ammo_m829a3";

            clip_m829a3 = new AmmoType.AmmoClip();
            clip_m829a3.Capacity = 1;
            clip_m829a3.Name = "M829A3 APFSDS-T";
            clip_m829a3.MinimalPattern = new AmmoCodexScriptable[1];
            clip_m829a3.MinimalPattern[0] = ammo_codex_m829a3;

            VUtil.Coalesce(ref clip_codex_m829a3);
            clip_codex_m829a3.name = "clip_m829a3";
            clip_codex_m829a3.CompatibleWeaponSystems = new WeaponSystemCodexScriptable[1];
            clip_codex_m829a3.ClipType = clip_m829a3;

            // m829a4
            //
            // The round the station carries today, and the one whose absence was most visible against late
            // Pact ERA: a longer DU rod than the A3's, fired a little slower, with a tip built to break the
            // heavy ERA that stops the earlier darts.
            //
            // What GHPC can be told, and what it was given:
            //   * The whole round is ~22.3 kg and the penetrator assembly ~10 kg, but neither number belongs
            //     anywhere in the game: `Mass` is the drag mass of the thing that actually flies, and the
            //     sabot is discarded at the muzzle, so it takes the rod's own 5.5-6.0 kg (5.8 here). That is
            //     what the rest of this family does too (A1/A2 4.6, A3 5.2).
            //   * The rod is ~1,020 mm long - that is where the extra penetration comes from - but GHPC has
            //     no length field, so it is expressed entirely through the number that matters: penetration.
            //   * `SectionalArea` (0.0009) and `Coeff` (0.20) are this mod's APFSDS family values, and they
            //     are what decide how much of the muzzle velocity the dart keeps on the way out.
            //   * `MuzzleVelocity` 1,565 m/s, the middle of the published 1,550-1,580 for the 120 mm L/44
            //     M256 this vehicle carries.
            //   * Penetration is calibrated so the round reproduces its published 2,000 m band under this
            //     file's drag model, not so it reproduces an arbitrary muzzle figure. The published band is
            //     700-730 mm RHAe at 2,000 m, midpoint 715, and the rod keeps 0.9282 of its muzzle velocity
            //     at that range, so the muzzle value it implies is
            //         715 / 0.9282^2 = 715 / 0.8616 = 830
            //     The same rule governs every dart in this file, each against its own published band and
            //     its own drag figure: M829 490 / 0.9038^2 = 600, M829A1 560 / 0.9180^2 = 665,
            //     M829A2 615 / 0.9180^2 = 730, M829A3 685 / 0.9164^2 = 816. The A4 and the A1 are written
            //     at the figure their own band implies; the A2 and the A3 are held roughly 25-30 mm below
            //     theirs, so the newest round stays a visible step ahead instead of the family flattening
            //     into one value. That is why the A4 lands at 830 against the A3's 785: +5.7%.
            //
            //     GHPC takes no penetration table: it takes this single RHA figure, treats it as the muzzle
            //     value, and scales it by the square of the speed ratio (`LiveRound.CurrentPenRating`:
            //     `RhaPenetration * CurrentSpeed^2 / MaxSpeed^2`), so the 2,000 m figure falls out of the
            //     drag model used by `OldLiveRound.accelerate` / `BallisticEvaluatorRK4`:
            //         k         = Coeff * SectionalArea * air density / Mass
            //                   = 0.20 * 0.0009 * 1.2 / 5.8                = 3.724e-5 per metre
            //         v(2000 m) = v0 * exp(-k * 2000)                        = 0.9282 * v0
            //         pen(2000) = RhaPenetration * 0.9282^2 = 830 * 0.8616    = 715 mm RHAe
            //     against the A3's 659 mm at the same range (785 * 0.9164^2), i.e. +5.7% at the muzzle and
            //     +8.5% at 2,000 m: the dart holds its speed slightly better because that is what a longer,
            //     heavier rod with a better tip does. (Air density is 1.2 from `WorldScript.LocalAirDensity`
            //     or 1.25 from `WorldEnvironmentManager`; the other one moves these by under 1%.)
            //
            //     Note what is *not* asked of this number: the A3's "still 700 mm+ against the base armour
            //     after defeating heavy ERA" is not extra penetration, it is the ERA's reduced worth against
            //     these rounds - the `ArmorOptimizations` table below, which is 0.125 here against the A3's
            //     0.25.
            //   * The propellant (SCDB, insensitive, combustible case, 4340 steel base, electric primer) and
            //     the DU alloy itself are inert here: the game has no field for any of them, and none of them
            //     changes the trajectory or the penetration it computes.
            ammo_m829a4 = new AmmoType();
            VUtil.ShallowCopy(ammo_m829a4, Assets.ammo_m833);
            ammo_m829a4.Name = "M829A4 APFSDS-T";
            ammo_m829a4.Caliber = 120;
            ammo_m829a4.RhaPenetration = 830f;
            ammo_m829a4.SpallMultiplier = 1.25f;
            ammo_m829a4.MaxSpallRha = 24f;
            ammo_m829a4.MinSpallRha = 8f;
            ammo_m829a4.MuzzleVelocity = 1650f;
            ammo_m829a4.Mass = 5.8f;
            ammo_m829a4.SectionalArea = 0.0009f;
            ammo_m829a4.Coeff = 0.20f;

            // The A4's whole reason for existing: heavy ERA counts for half of what it counts for against the
            // A3. This is not a free parameter - it reads straight off the ERA's own numbers, which live in
            // the Pact Increased Lethality mod:
            //
            //     Kontakt5.schema.ke_rha = 200 mm      Relikt.schema.ke_rha = 250 mm
            //
            // and off what `LiveRound` does with them: the round compares `CurrentPenRating` against the
            // section's line-of-sight thickness, and a named armour's thickness is multiplied by this ratio
            // first (`LiveRound.penCheck`: `num10 = GetLosThickness(...) * armorOptimization.RhaRatio`,
            // then `num10 > CurrentPenRating` means no penetration). `GetNormalThickness` returns
            // `PrimarySabotRha` for a KE round, which is exactly the field `PactEra` fills with `ke_rha`,
            // so the ratio is a straight multiplier on the ERA's rated effectiveness. The whole family:
            //
            //     M829     0.95    Kontakt-5 190.0 mm    Relikt 237.5 mm    no precursor tip
            //     M829A1   0.85    Kontakt-5 170.0 mm    Relikt 212.5 mm    no precursor tip
            //     M829A2   0.80    Kontakt-5 160.0 mm    Relikt 200.0 mm    no precursor tip, brute force
            //     M829A3   0.25    Kontakt-5  50.0 mm    Relikt  62.5 mm    100 mm steel precursor tip
            //     M829A4   0.125   Kontakt-5  25.0 mm    Relikt  31.3 mm    precursor tip, further optimised
            //
            // The family splits where the hardware does. The three rounds with no precursor tip all pay most
            // of the ERA's rated penalty, descending as their penetrators improve; the two with a tip pay
            // little, because the tip detonates the ERA early so the flyer plates only partially reach the
            // main rod - in the A3's case a 100 mm steel tip ahead of a 630 mm DU rod whose diameter was
            // raised from 22 to 25 mm, a 67% strength gain specifically for resisting ERA bursting plates.
            // The A3/A4 figures are also what make their published claims true: 785 mm at the muzzle leaves
            // 785 - 50 = 735 mm against Kontakt-5, and 659 mm at 2,000 m leaves 609 mm; the A4's half-cost is
            // 830 - 25 = 805 mm, against 715 - 25 = 690 mm at 2,000 m. That is what "significantly enhanced
            // against heavy ERA" is worth here; it is deliberately *not* expressed as extra
            // `RhaPenetration`, which would double-count it.
            ammo_m829a4.ArmorOptimizations = HeavyEraOptimizations(0.125f);

            VUtil.Coalesce(ref ammo_codex_m829a4);
            ammo_codex_m829a4.AmmoType = ammo_m829a4;
            ammo_codex_m829a4.name = "ammo_m829a4";

            clip_m829a4 = new AmmoType.AmmoClip();
            clip_m829a4.Capacity = 1;
            clip_m829a4.Name = "M829A4 APFSDS-T";
            clip_m829a4.MinimalPattern = new AmmoCodexScriptable[1];
            clip_m829a4.MinimalPattern[0] = ammo_codex_m829a4;

            VUtil.Coalesce(ref clip_codex_m829a4);
            clip_codex_m829a4.name = "clip_m829a4";
            clip_codex_m829a4.CompatibleWeaponSystems = new WeaponSystemCodexScriptable[1];
            clip_codex_m829a4.ClipType = clip_m829a4;

            // m830
            ammo_m830 = new AmmoType();
            VUtil.ShallowCopy(ammo_m830, Assets.ammo_m456);
            ammo_m830.Name = "M830 HEAT-MP-T";
            ammo_m830.Caliber = 120;
            ammo_m830.RhaPenetration = 550;
            ammo_m830.TntEquivalentKg = 1.814f;
            ammo_m830.MuzzleVelocity = 1140f;
            ammo_m830.Mass = 13.5f;
            ammo_m830.CertainRicochetAngle = 8.0f;
            ammo_m830.ShatterOnRicochet = false;
            ammo_m830.DetonateSpallCount = 40;
            ammo_m830.SectionalArea = 0.0095f;

            VUtil.Coalesce(ref ammo_codex_m830);
            ammo_codex_m830.AmmoType = ammo_m830;
            ammo_codex_m830.name = "ammo_m830";

            clip_m830 = new AmmoType.AmmoClip();
            clip_m830.Capacity = 1;
            clip_m830.Name = "M830 HEAT-MP-T";
            clip_m830.MinimalPattern = new AmmoCodexScriptable[1];
            clip_m830.MinimalPattern[0] = ammo_codex_m830;

            VUtil.Coalesce(ref clip_codex_m830);
            clip_codex_m830.name = "clip_m830";
            clip_codex_m830.CompatibleWeaponSystems = new WeaponSystemCodexScriptable[1];
            clip_codex_m830.ClipType = clip_m830;

            // m830a1
            ammo_m830a1 = new AmmoType();
            VUtil.ShallowCopy(ammo_m830a1, Assets.ammo_m456);
            ammo_m830a1.Name = "M830A1 MPAT-T";
            ammo_m830a1.Coeff = ammo_m829.Coeff;
            ammo_m830a1.Caliber = 120;
            ammo_m830a1.RhaPenetration = 500;
            ammo_m830a1.TntEquivalentKg = 1.814f;
            ammo_m830a1.MuzzleVelocity = 1400f;
            ammo_m830a1.Mass = 11.4f;
            ammo_m830a1.CertainRicochetAngle = 5.0f;
            ammo_m830a1.ShatterOnRicochet = false;
            ammo_m830a1.AlwaysProduceBlast = true;
            ammo_m830a1.DetonateSpallCount = 60;
            ammo_m830a1.MaxSpallRha = 35f;
            ammo_m830a1.SectionalArea = 0.0055f;

            VUtil.Coalesce(ref ammo_codex_m830a1);
            ammo_codex_m830a1.AmmoType = ammo_m830a1;
            ammo_codex_m830a1.name = "ammo_m830a1";

            clip_m830a1 = new AmmoType.AmmoClip();
            clip_m830a1.Capacity = 1;
            clip_m830a1.Name = "M830A1 MPAT-T";
            clip_m830a1.MinimalPattern = new AmmoCodexScriptable[1];
            clip_m830a1.MinimalPattern[0] = ammo_codex_m830a1;

            VUtil.Coalesce(ref clip_codex_m830a1);
            clip_codex_m830a1.name = "clip_m830a1";
            clip_codex_m830a1.CompatibleWeaponSystems = new WeaponSystemCodexScriptable[1];
            clip_codex_m830a1.ClipType = clip_m830a1;

            m830a1_forward_frag.Name = "mpat forward frag";
            m830a1_forward_frag.RhaPenetration = 100f;
            m830a1_forward_frag.MuzzleVelocity = 600f;
            m830a1_forward_frag.Category = AmmoType.AmmoCategory.Penetrator;
            m830a1_forward_frag.Mass = 0.80f;
            m830a1_forward_frag.SectionalArea = 0.0055f;
            m830a1_forward_frag.Coeff = 0.5f;
            m830a1_forward_frag.UseTracer = false;
            m830a1_forward_frag.CertainRicochetAngle = 10f;
            m830a1_forward_frag.SpallMultiplier = 0.2f;
            m830a1_forward_frag.Caliber = 5f;
            m830a1_forward_frag.DetonateEffect = Resources.FindObjectsOfTypeAll<GameObject>().Where(o => o.name == "Sabot Impact").First();
            m830a1_forward_frag.ImpactEffectDescriptor = new ParticleEffectsManager.ImpactEffectDescriptor()
            {
                HasImpactEffect = true,
                ImpactCategory = ParticleEffectsManager.Category.Kinetic,
                EffectSize = ParticleEffectsManager.EffectSize.Bullet,
                RicochetType = ParticleEffectsManager.RicochetType.None,
                Flags = ParticleEffectsManager.ImpactModifierFlags.Small,
                MinFilterStrictness = ParticleEffectsManager.FilterStrictness.Low
            };

            ammo_m827_vis = GameObject.Instantiate(Assets.ammo_m833.VisualModel);
            ammo_m827_vis.name = "M829 visual";
            ammo_m827.VisualModel = ammo_m827_vis;
            ammo_m827.VisualModel.GetComponent<AmmoStoredVisual>().AmmoType = ammo_m827;
            ammo_m827.VisualModel.GetComponent<AmmoStoredVisual>().AmmoScriptable = ammo_codex_m827;

            ammo_m829_vis = GameObject.Instantiate(Assets.ammo_m833.VisualModel);
            ammo_m829_vis.name = "M829 visual";
            ammo_m829.VisualModel = ammo_m829_vis;
            ammo_m829.VisualModel.GetComponent<AmmoStoredVisual>().AmmoType = ammo_m829;
            ammo_m829.VisualModel.GetComponent<AmmoStoredVisual>().AmmoScriptable = ammo_codex_m829;

            ammo_m829a1_vis = GameObject.Instantiate(Assets.ammo_m833.VisualModel);
            ammo_m829a1_vis.name = "M829A1 visual";
            ammo_m829a1.VisualModel = ammo_m829a1_vis;
            ammo_m829a1.VisualModel.GetComponent<AmmoStoredVisual>().AmmoType = ammo_m829a1;
            ammo_m829a1.VisualModel.GetComponent<AmmoStoredVisual>().AmmoScriptable = ammo_codex_m829a1;

            ammo_m829a2_vis = GameObject.Instantiate(Assets.ammo_m833.VisualModel);
            ammo_m829a2_vis.name = "M829A2 visual";
            ammo_m829a2.VisualModel = ammo_m829a2_vis;
            ammo_m829a2.VisualModel.GetComponent<AmmoStoredVisual>().AmmoType = ammo_m829a2;
            ammo_m829a2.VisualModel.GetComponent<AmmoStoredVisual>().AmmoScriptable = ammo_codex_m829a2;

            ammo_m829a3_vis = GameObject.Instantiate(Assets.ammo_m833.VisualModel);
            ammo_m829a3_vis.name = "M829A3 visual";
            ammo_m829a3.VisualModel = ammo_m829a3_vis;
            ammo_m829a3.VisualModel.GetComponent<AmmoStoredVisual>().AmmoType = ammo_m829a3;
            ammo_m829a3.VisualModel.GetComponent<AmmoStoredVisual>().AmmoScriptable = ammo_codex_m829a3;

            // The stored-round model is the donor M833's, scaled 1:1. The A4 is ~40 mm longer than the A3
            // in reality, and 40 mm at rack scale is a couple of pixels; stretching the mesh would change
            // what every rack in the vehicle looks like to say it, so the difference is not modelled.
            ammo_m829a4_vis = GameObject.Instantiate(Assets.ammo_m833.VisualModel);
            ammo_m829a4_vis.name = "M829A4 visual";
            ammo_m829a4.VisualModel = ammo_m829a4_vis;
            ammo_m829a4.VisualModel.GetComponent<AmmoStoredVisual>().AmmoType = ammo_m829a4;
            ammo_m829a4.VisualModel.GetComponent<AmmoStoredVisual>().AmmoScriptable = ammo_codex_m829a4;

            ammo_m830_vis = GameObject.Instantiate(Assets.ammo_m456.VisualModel);
            ammo_m830_vis.name = "M830 visual";
            ammo_m830.VisualModel = ammo_m830_vis;
            ammo_m830.VisualModel.GetComponent<AmmoStoredVisual>().AmmoType = ammo_m830;
            ammo_m830.VisualModel.GetComponent<AmmoStoredVisual>().AmmoScriptable = ammo_codex_m830;

            ammo_m830a1_vis = GameObject.Instantiate(Assets.ammo_m456.VisualModel);
            ammo_m830a1_vis.name = "M830A1 visual";
            ammo_m830a1.VisualModel = ammo_m830a1_vis;
            ammo_m830a1.VisualModel.GetComponent<AmmoStoredVisual>().AmmoType = ammo_m830a1;
            ammo_m830a1.VisualModel.GetComponent<AmmoStoredVisual>().AmmoScriptable = ammo_codex_m830a1;

            AmmoCache.CacheAmmo(ammo_m827);
            AmmoCache.CacheAmmo(ammo_m829);
            AmmoCache.CacheAmmo(ammo_m829a1);
            AmmoCache.CacheAmmo(ammo_m829a2);
            AmmoCache.CacheAmmo(ammo_m829a3);
            AmmoCache.CacheAmmo(ammo_m829a4);
            AmmoCache.CacheAmmo(m830a1_forward_frag);
            AmmoCache.CacheAmmo(ammo_m830);
            AmmoCache.CacheAmmo(ammo_m830a1);

            ap = new Dictionary<string, AmmoClipCodexScriptable>()
            {
                ["M827"] = clip_codex_m827,
                ["M829"] = clip_codex_m829,
                ["M829A1"] = clip_codex_m829a1,
                ["M829A2"] = clip_codex_m829a2,
                ["M829A3"] = clip_codex_m829a3,
                ["M829A4"] = clip_codex_m829a4,
            };

            heat = new Dictionary<string, AmmoClipCodexScriptable>()
            {
                ["M830"] = clip_codex_m830,
                ["M830A1"] = clip_codex_m830a1,
            };
        }

        /// <summary>
        /// The ERA codices a dart was designed against, with the fraction of each section's nominal RHA
        /// that still counts against it: the game multiplies an ERA section's line-of-sight thickness by
        /// this ratio when the ammo's table names that armour (<c>LiveRound.penCheck</c>,
        /// <c>AmmoType.ArmorOptimizations</c>). The whole M829 family is on this table, and it splits at
        /// the point the hardware does - tip versus no tip - rather than descending smoothly: M829 0.95,
        /// M829A1 0.85 and M829A2 0.80 carry no precursor tip and pay most of the penalty; M829A3 0.25 and
        /// M829A4 0.125 do carry one and pay little. Against Kontakt-5's 200 mm and Relikt's 250 mm that is
        /// 190/238, 170/213, 160/200, 50/63 and 25/31 mm respectively.
        ///
        /// The codices themselves live in another mod (Pact Increased Lethality), which is optional, so
        /// they are found through reflection and the whole table is dropped when it is not loaded - the
        /// same way this file treats every other optional armour source. Returns null rather than throwing:
        /// an optional mod changing its internals must not take the M1A1's ammunition down with it.
        /// </summary>
        private static AmmoType.ArmorOptimization[] HeavyEraOptimizations(float rha_ratio)
        {
            try
            {
                Type era_schema_type = Type.GetType(Assembly.CreateQualifiedName(
                    "PactIncreasedLethality", "PactIncreasedLethality.EraSchema"));
                Type kontakt5_type = Type.GetType(Assembly.CreateQualifiedName(
                    "PactIncreasedLethality", "PactIncreasedLethality.Kontakt5"));
                Type relikt_type = Type.GetType(Assembly.CreateQualifiedName(
                    "PactIncreasedLethality", "PactIncreasedLethality.Relikt"));

                if (era_schema_type == null || kontakt5_type == null || relikt_type == null)
                {
                    return null;
                }

                FieldInfo era_so = era_schema_type.GetField("era_so", BindingFlags.Public | BindingFlags.Instance);

                if (era_so == null)
                {
                    return null;
                }

                AmmoType.ArmorOptimization[] optimizations = new AmmoType.ArmorOptimization[]
                {
                    new AmmoType.ArmorOptimization() {
                        Armor = EraCodex(era_so, kontakt5_type),
                        RhaRatio = rha_ratio
                    },

                    new AmmoType.ArmorOptimization() {
                        Armor = EraCodex(era_so, relikt_type),
                        RhaRatio = rha_ratio
                    }
                };

                if (optimizations[0].Armor == null || optimizations[1].Armor == null)
                {
                    return null;
                }

                return optimizations;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// One ERA schema's armour codex, out of the schema instance its class publishes as a static field.
        /// </summary>
        private static ArmorCodexScriptable EraCodex(FieldInfo era_so, Type era_type)
        {
            FieldInfo schema = era_type.GetField("schema", BindingFlags.Static | BindingFlags.Public);

            if (schema == null)
            {
                return null;
            }

            object instance = schema.GetValue(null);

            return instance == null ? null : era_so.GetValue(instance) as ArmorCodexScriptable;
        }
    }
}


