using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC.Effects;
using GHPC.Weapons;
using UnityEngine;
using GHPC.Weaponry;
using ModUtil;

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
            Util.ShallowCopy(ammo_m827, Assets.ammo_m833);
            ammo_m827.Name = "M827 APFSDS-T";
            ammo_m827.Caliber = 120;
            ammo_m827.RhaPenetration = 500f;
            ammo_m827.MuzzleVelocity = 1650f;
            ammo_m827.Mass = 4.64f;
            ammo_m827.SectionalArea = 0.0012f;
            ammo_m827.SpallMultiplier = 1.15f;
            ammo_m827.CachedIndex = -1;

            Util.Coalesce(ref ammo_codex_m827);
            ammo_codex_m827.AmmoType = ammo_m827;
            ammo_codex_m827.name = "ammo_m827";

            clip_m827 = new AmmoType.AmmoClip();
            clip_m827.Capacity = 1;
            clip_m827.Name = "M827 APFSDS-T";
            clip_m827.MinimalPattern = new AmmoCodexScriptable[1];
            clip_m827.MinimalPattern[0] = ammo_codex_m827;

            Util.Coalesce(ref clip_codex_m827);
            clip_codex_m827.name = "clip_m827";
            clip_codex_m827.CompatibleWeaponSystems = new WeaponSystemCodexScriptable[1];
            clip_codex_m827.ClipType = clip_m827;

            // m829 
            ammo_m829 = new AmmoType();
            Util.ShallowCopy(ammo_m829, Assets.ammo_m833);
            ammo_m829.Name = "M829 APFSDS-T";
            ammo_m829.Caliber = 120;
            ammo_m829.RhaPenetration = 530f;
            ammo_m829.MuzzleVelocity = 1670f;
            ammo_m829.Mass = 4.27f;
            ammo_m829.SectionalArea = 0.0009f;
            ammo_m829.SpallMultiplier = 1.15f;
            ammo_m829.Coeff = 0.20f;
            ammo_m829.CachedIndex = -1;

            Util.Coalesce(ref ammo_codex_m829);
            ammo_codex_m829.AmmoType = ammo_m829;
            ammo_codex_m829.name = "ammo_m829";

            clip_m829 = new AmmoType.AmmoClip();
            clip_m829.Capacity = 1;
            clip_m829.Name = "M829 APFSDS-T";
            clip_m829.MinimalPattern = new AmmoCodexScriptable[1];
            clip_m829.MinimalPattern[0] = ammo_codex_m829;

            Util.Coalesce(ref clip_codex_m829);
            clip_codex_m829.name = "clip_m829";
            clip_codex_m829.CompatibleWeaponSystems = new WeaponSystemCodexScriptable[1];
            clip_codex_m829.ClipType = clip_m829;

            // m829a1
            ammo_m829a1 = new AmmoType();
            Util.ShallowCopy(ammo_m829a1, Assets.ammo_m833);
            ammo_m829a1.Name = "M829A1 APFSDS-T";
            ammo_m829a1.Caliber = 120;
            ammo_m829a1.RhaPenetration = 580;
            ammo_m829a1.SpallMultiplier = 1.15f;
            ammo_m829a1.MaxSpallRha = 22f;
            ammo_m829a1.MinSpallRha = 5f;
            ammo_m829a1.MuzzleVelocity = 1575;
            ammo_m829a1.Mass = 4.6f;
            ammo_m829a1.SectionalArea = 0.00082f;
            ammo_m829a1.Coeff = 0.20f;
            ammo_m829a1.CachedIndex = -1;

            Util.Coalesce(ref ammo_codex_m829a1);
            ammo_codex_m829a1.AmmoType = ammo_m829a1;
            ammo_codex_m829a1.name = "ammo_m829a1";

            clip_m829a1 = new AmmoType.AmmoClip();
            clip_m829a1.Capacity = 1;
            clip_m829a1.Name = "M829A1 APFSDS-T";
            clip_m829a1.MinimalPattern = new AmmoCodexScriptable[1];
            clip_m829a1.MinimalPattern[0] = ammo_codex_m829a1;

            Util.Coalesce(ref clip_codex_m829a1);
            clip_codex_m829a1.name = "clip_m829a1";
            clip_codex_m829a1.CompatibleWeaponSystems = new WeaponSystemCodexScriptable[1];
            clip_codex_m829a1.ClipType = clip_m829a1;

            // m829a2 
            ammo_m829a2 = new AmmoType();
            Util.ShallowCopy(ammo_m829a2, Assets.ammo_m833);
            ammo_m829a2.Name = "M829A2 APFSDS-T";
            ammo_m829a2.Caliber = 120;
            ammo_m829a2.RhaPenetration = 605f;
            ammo_m829a2.SpallMultiplier = 1.15f;
            ammo_m829a2.MaxSpallRha = 22f;
            ammo_m829a2.MinSpallRha = 5f;
            ammo_m829a2.MuzzleVelocity = 1680f;
            ammo_m829a2.Mass = 4.6f;
            ammo_m829a2.SectionalArea = 0.00082f;
            ammo_m829a2.Coeff = 0.20f;
            ammo_m829a2.CachedIndex = -1;

            Util.Coalesce(ref ammo_codex_m829a2);
            ammo_codex_m829a2.AmmoType = ammo_m829a2;
            ammo_codex_m829a2.name = "ammo_m829a2";

            clip_m829a2 = new AmmoType.AmmoClip();
            clip_m829a2.Capacity = 1;
            clip_m829a2.Name = "M829A2 APFSDS-T";
            clip_m829a2.MinimalPattern = new AmmoCodexScriptable[1];
            clip_m829a2.MinimalPattern[0] = ammo_codex_m829a2;

            Util.Coalesce(ref clip_codex_m829a2);
            clip_codex_m829a2.name = "clip_m829a2";
            clip_codex_m829a2.CompatibleWeaponSystems = new WeaponSystemCodexScriptable[1];
            clip_codex_m829a2.ClipType = clip_m829a2;

            // m829a3
            ammo_m829a3 = new AmmoType();
            Util.ShallowCopy(ammo_m829a3, Assets.ammo_m833);
            ammo_m829a3.Name = "M829A3 APFSDS-T";
            ammo_m829a3.Caliber = 120;
            ammo_m829a3.RhaPenetration = 690f;
            ammo_m829a3.SpallMultiplier = 1.2f;
            ammo_m829a3.MaxSpallRha = 22f;
            ammo_m829a3.MinSpallRha = 7f;
            ammo_m829a3.MuzzleVelocity = 1555f;
            ammo_m829a3.Mass = 5.2f;
            ammo_m829a3.SectionalArea = 0.00086f;
            ammo_m829a3.Coeff = 0.22f;
            ammo_m829a3.CachedIndex = -1;

            ammo_m829a3.ArmorOptimizations = HeavyEraOptimizations(0.25f);

            Util.Coalesce(ref ammo_codex_m829a3);
            ammo_codex_m829a3.AmmoType = ammo_m829a3;
            ammo_codex_m829a3.name = "ammo_m829a3";

            clip_m829a3 = new AmmoType.AmmoClip();
            clip_m829a3.Capacity = 1;
            clip_m829a3.Name = "M829A3 APFSDS-T";
            clip_m829a3.MinimalPattern = new AmmoCodexScriptable[1];
            clip_m829a3.MinimalPattern[0] = ammo_codex_m829a3;

            Util.Coalesce(ref clip_codex_m829a3);
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
            //   * Penetration is calibrated against this mod's own M829A3, not against an absolute figure.
            //     The A3 in this file carries `RhaPenetration = 690`, and the published bands for the two
            //     rounds are 750-800 mm RHAe for the A3 and 800-850 for the A4, so the ratio to carry
            //     across is the ratio of their midpoints:
            //         690 * (825 / 775) = 734.5  ->  735
            //     That keeps the A4 on the same scale as the A3 that is already in the game instead of
            //     inventing a second one - writing the A4's *absolute* 800-850 here (960, as it was first
            //     done) makes it 39% better than the A3, which is three times the improvement the two real
            //     rounds differ by.
            //
            //     GHPC takes no penetration table: it takes this single RHA figure, treats it as the muzzle
            //     value, and scales it by the square of the speed ratio (`LiveRound.CurrentPenRating`), so
            //     the 2,000 m figure falls out of the drag model used by `OldLiveRound.accelerate` /
            //     `BallisticEvaluatorRK4`:
            //         k         = Coeff * SectionalArea * air density / Mass
            //                   = 0.20 * 0.0009 * 1.2 / 5.8                = 3.724e-5 per metre
            //         v(2000 m) = v0 * exp(-k * 2000)                        = 0.9282 * v0
            //         pen(2000) = RhaPenetration * 0.9282^2 = 735 * 0.8616    = 633 mm RHAe
            //     against the A3's 579 mm at the same range (690 * 0.9164^2), i.e. +6.5% at the muzzle and
            //     +9.4% at 2,000 m: the dart holds its speed slightly better because that is what a longer,
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
            Util.ShallowCopy(ammo_m829a4, Assets.ammo_m833);
            ammo_m829a4.Name = "M829A4 APFSDS-T";
            ammo_m829a4.Caliber = 120;
            ammo_m829a4.RhaPenetration = 735f;
            ammo_m829a4.SpallMultiplier = 1.25f;
            ammo_m829a4.MaxSpallRha = 24f;
            ammo_m829a4.MinSpallRha = 8f;
            ammo_m829a4.MuzzleVelocity = 1565f;
            ammo_m829a4.Mass = 5.8f;
            ammo_m829a4.SectionalArea = 0.0009f;
            ammo_m829a4.Coeff = 0.20f;

            // The A4's whole reason for existing: heavy ERA counts for half of what it counts for against the
            // A3. This is not a free parameter - it reads straight off the ERA's own numbers, which live in
            // the Pact Increased Lethality mod:
            //
            //     Kontakt5.schema.ke_rha = 200 mm      Relikt.schema.ke_rha = 250 mm
            //
            // and off what `LiveRound.penCheck` does with them: passing a layer costs the dart speed
            // (`CurrentSpeed = sqrt(v^2 * (1 - layer / penetration))`), and since penetration goes as the
            // square of the speed, a layer costs exactly `layer` millimetres of penetration. A layer's
            // thickness is its nominal RHA *times this ratio*, so the ratio is a millimetre figure:
            //
            //     A3, 0.25    Kontakt-5 200 * 0.25 =  50 mm      Relikt 250 * 0.25 = 62.5 mm
            //     A4, 0.125   Kontakt-5 200 * 0.125 = 25 mm      Relikt 250 * 0.125 = 31.3 mm
            //
            // The A3's 0.25 is what makes its published claim true - 750-800 mm RHAe, and "still 700 mm+
            // against the base armour after defeating heavy ERA" is 775 - 50 = 725, or 750 - 50 = 700 at the
            // bottom of the band. So the ratio is already calibrated by that sentence, and the A4 has to be
            // read on the same scale: half the cost is 735 - 25 = 710 mm left after Kontakt-5, against 633 -
            // 25 = 608 mm at 2,000 m. That is what "significantly enhanced against heavy ERA" is worth here;
            // it is deliberately *not* expressed as extra `RhaPenetration`, which would double-count it.
            ammo_m829a4.ArmorOptimizations = HeavyEraOptimizations(0.125f);
            ammo_m829a4.CachedIndex = -1;

            Util.Coalesce(ref ammo_codex_m829a4);
            ammo_codex_m829a4.AmmoType = ammo_m829a4;
            ammo_codex_m829a4.name = "ammo_m829a4";

            clip_m829a4 = new AmmoType.AmmoClip();
            clip_m829a4.Capacity = 1;
            clip_m829a4.Name = "M829A4 APFSDS-T";
            clip_m829a4.MinimalPattern = new AmmoCodexScriptable[1];
            clip_m829a4.MinimalPattern[0] = ammo_codex_m829a4;

            Util.Coalesce(ref clip_codex_m829a4);
            clip_codex_m829a4.name = "clip_m829a4";
            clip_codex_m829a4.CompatibleWeaponSystems = new WeaponSystemCodexScriptable[1];
            clip_codex_m829a4.ClipType = clip_m829a4;

            // m830
            ammo_m830 = new AmmoType();
            Util.ShallowCopy(ammo_m830, Assets.ammo_m456);
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
            ammo_m830.CachedIndex = -1;

            Util.Coalesce(ref ammo_codex_m830);
            ammo_codex_m830.AmmoType = ammo_m830;
            ammo_codex_m830.name = "ammo_m830";

            clip_m830 = new AmmoType.AmmoClip();
            clip_m830.Capacity = 1;
            clip_m830.Name = "M830 HEAT-MP-T";
            clip_m830.MinimalPattern = new AmmoCodexScriptable[1];
            clip_m830.MinimalPattern[0] = ammo_codex_m830;

            Util.Coalesce(ref clip_codex_m830);
            clip_codex_m830.name = "clip_m830";
            clip_codex_m830.CompatibleWeaponSystems = new WeaponSystemCodexScriptable[1];
            clip_codex_m830.ClipType = clip_m830;

            // m830a1
            ammo_m830a1 = new AmmoType();
            Util.ShallowCopy(ammo_m830a1, Assets.ammo_m456);
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
            ammo_m830a1.CachedIndex = -1;

            Util.Coalesce(ref ammo_codex_m830a1);
            ammo_codex_m830a1.AmmoType = ammo_m830a1;
            ammo_codex_m830a1.name = "ammo_m830a1";

            clip_m830a1 = new AmmoType.AmmoClip();
            clip_m830a1.Capacity = 1;
            clip_m830a1.Name = "M830A1 MPAT-T";
            clip_m830a1.MinimalPattern = new AmmoCodexScriptable[1];
            clip_m830a1.MinimalPattern[0] = ammo_codex_m830a1;

            Util.Coalesce(ref clip_codex_m830a1);
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
            m830a1_forward_frag.CachedIndex = -1;

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

            Util.CacheAmmo(ammo_m827);
            Util.CacheAmmo(ammo_m829);
            Util.CacheAmmo(ammo_m829a1);
            Util.CacheAmmo(ammo_m829a2);
            Util.CacheAmmo(ammo_m829a3);
            Util.CacheAmmo(ammo_m829a4);
            Util.CacheAmmo(m830a1_forward_frag);
            Util.CacheAmmo(ammo_m830);
            Util.CacheAmmo(ammo_m830a1);

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
        /// this ratio when the ammo's table names that armour (<c>LiveRound</c>,
        /// <c>AmmoType.ArmorOptimizations</c>). The A3 carries 0.25 against Kontakt-5 and Relikt; a round
        /// built specifically to defeat heavy ERA carries less, and that is the whole difference in the
        /// penetration model between the two.
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


