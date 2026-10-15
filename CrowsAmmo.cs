using System;
using System.Collections.Generic;
using GHPC;
using GHPC.Effects;
using GHPC.Vehicle;
using GHPC.Weapons;
using GHPC.Weaponry;
using MelonLoader;
using ModUtil;
using UnityEngine;

namespace M1A1Abrams
{
    // Ammunition is independent of CROWS models/donors so a coax-only installation works.
    internal static class CrowsAmmo
    {
        private static readonly List<ScriptableObject> owned = new List<ScriptableObject>();
        private static readonly HashSet<int> converted = new HashSet<int>();
        private static readonly HashSet<string> warnings = new HashSet<string>();
        private static readonly Dictionary<string, AmmoType.AmmoClip> belts = new Dictionary<string, AmmoType.AmmoClip>();
        private static AmmoCodexScriptable ammo_codex_m8, ammo_codex_50tracer;
        private static AmmoCodexScriptable ammo_codex_raufoss, ammo_codex_raufoss_tracer;
        private static AmmoType ammo_raufoss, ammo_raufoss_tracer;
        private static AmmoType.AmmoClip clip_raufoss_500;
        private static AmmoClipCodexScriptable clip_codex_raufoss_500;
        internal static AmmoType TracerAmmo { get; private set; }

        internal static void Reset()
        {
            foreach (ScriptableObject asset in owned)
                if (asset != null) UnityEngine.Object.Destroy(asset);
            owned.Clear();
            belts.Clear();
            converted.Clear();
            warnings.Clear();
            ammo_codex_m8 = ammo_codex_50tracer = null;
            ammo_codex_raufoss = ammo_codex_raufoss_tracer = null;
            ammo_raufoss = ammo_raufoss_tracer = TracerAmmo = null;
            clip_raufoss_500 = null;
            clip_codex_raufoss_500 = null;
        }

        internal static bool Init()
        {
            if (belts.Count != 0 && ammo_codex_raufoss != null) return true;
            foreach (AmmoCodexScriptable codex in Resources.FindObjectsOfTypeAll<AmmoCodexScriptable>())
            {
                if (codex == null || codex.AmmoType == null) continue;
                if (codex.name == "ammo_M8" || codex.AmmoType.Name == "M8 API") ammo_codex_m8 = codex;
                if (codex.name == "ammo_50_tracer" || codex.AmmoType.Name == "M20 API-T") ammo_codex_50tracer = codex;
            }
            if (ammo_codex_m8 == null)
            {
                Warn("M8 API donor missing; CROWS/M2 ammunition is not ready.");
                return false;
            }
            if (ammo_codex_50tracer == null) ammo_codex_50tracer = ammo_codex_m8;

            // M962 is the same round with a tracer element, so it shares every
            // ballistic number with M903; UseTracer remains independent of the M8 impact effect.
            AmmoCodexScriptable m903 = BuildSlap(CrowsAmmoRules.M903, false);
            AmmoCodexScriptable m962 = BuildSlap(CrowsAmmoRules.M962, true);
            TracerAmmo = m962.AmmoType;
            BuildRaufossBelt();
            owned.Add(ammo_codex_raufoss);
            owned.Add(ammo_codex_raufoss_tracer);
            owned.Add(clip_codex_raufoss_500);

            belts[CrowsAmmoRules.M8] = Belt(CrowsAmmoRules.M8, ammo_codex_m8, ammo_codex_m8, ammo_codex_m8, ammo_codex_m8, ammo_codex_50tracer);
            belts[CrowsAmmoRules.Mk211] = clip_raufoss_500;
            belts[CrowsAmmoRules.M903] = Belt(CrowsAmmoRules.M903, m903, m903, m903, m903, m962);
            return true;
        }

        private static AmmoCodexScriptable BuildSlap(string name, bool tracer)
        {
            AmmoType ammo = new AmmoType();
            Util.ShallowCopy(ammo, (tracer ? ammo_codex_50tracer : ammo_codex_m8).AmmoType);
            ammo.Name = name;
            ammo.ShortName = AmmoType.AmmoShortName.Caliber50;
            ammo.Category = AmmoType.AmmoCategory.Penetrator;
            ammo.Caliber = 12.7f; // barrel caliber; sectional area describes the 7.62 mm flying core
            ammo.MuzzleVelocity = CrowsAmmoRules.Velocity;
            ammo.Mass = CrowsAmmoRules.CoreMass; // flying core only, not the 35 g bullet+sabot assembly
            ammo.SectionalArea = CrowsAmmoRules.Area;
            ammo.Coeff = CrowsAmmoRules.DragCoeff;
            ammo.RhaPenetration = CrowsAmmoRules.MuzzlePenetration;
            // SLAP uses the same physical penetrator radius as the API donor, but
            // the smaller tungsten core otherwise produces a very sparse and weak
            // behind-armour effect. Keep the radius (it controls the cone geometry)
            // and raise only the game's spall count/strength controls.
            ammo.SpallMultiplier = 2f;
            ammo.MinSpallRha = 3f;
            ammo.MaxSpallRha = 20f;
            ammo.UseErrorCorrection = true;
            ammo.TntEquivalentKg = 0f;
            ammo.UseTracer = tracer;
            ammo.VisualType = LiveRoundMarshaller.LiveRoundVisualType.Bullet; // game's red US bullet tracer
            ammo.ImpactAudio = GHPC.Audio.ImpactAudioType.Bullet;
            // An API donor can carry incendiary-specific effect flags. Describe SLAP
            // explicitly as a large kinetic .50 impact so the new effect database can
            // always select visible dust/spark hit VFX for both M903 and M962.
            ammo.ImpactEffectDescriptor = new ParticleEffectsManager.ImpactEffectDescriptor
            {
                HasImpactEffect = true,
                ImpactCategory = ParticleEffectsManager.Category.Kinetic,
                EffectSize = ParticleEffectsManager.EffectSize.Bullet,
                RicochetType = tracer ? ParticleEffectsManager.RicochetType.LargeTracer
                    : ParticleEffectsManager.RicochetType.Large,
                Flags = ParticleEffectsManager.ImpactModifierFlags.Large,
                MinFilterStrictness = ParticleEffectsManager.FilterStrictness.Low
            };
            // Decal selection is already correct for the game's M8 API and is copied
            // by value; only the particle effect needs the explicit SLAP descriptor.
            ammo.ImpactDecalDescriptor = ammo_codex_m8.AmmoType.ImpactDecalDescriptor;
            ammo.ArmorOptimizations = new AmmoType.ArmorOptimization[0];
            ammo.CachedIndex = -1;
            Util.CacheAmmo(ammo);
            AmmoCodexScriptable codex = ScriptableObject.CreateInstance<AmmoCodexScriptable>();
            codex.name = tracer ? "ammo_m962_slapt" : "ammo_m903_slap";
            codex.AmmoType = ammo;
            owned.Add(codex);
            return codex;
        }

        private static AmmoType.AmmoClip Belt(string name, params AmmoCodexScriptable[] pattern)
        {
            return new AmmoType.AmmoClip { Name = name, Capacity = 400, MinimalPattern = pattern };
        }

        internal static AmmoType.AmmoClip Select(string value)
        {
            if (!Init()) return null;
            string key = CrowsAmmoRules.Normalize(value);
            if (key == null)
            {
                Warn("Unknown CROWS/M2 Ammo '" + value + "'; using M8 API.");
                key = CrowsAmmoRules.M8;
            }
            AmmoType.AmmoClip result = belts[key];
            foreach (AmmoCodexScriptable codex in result.MinimalPattern)
                BallisticComputerRepository.Instance.RegisterAmmoType(codex.AmmoType);
            return result;
        }

        private static void Warn(string message)
        {
            warnings.Add(message);
        }

        internal static void AttachTracerLimit(WeaponSystem weapon)
        {
            weapon.Fired -= OnFired;
            weapon.Fired += OnFired;
        }

        private static void OnFired(AmmoType ammo, LiveRound round)
        {
            if (round == null || ammo != TracerAmmo) return;
            CrowsSlapTracer limiter = round.GetComponent<CrowsSlapTracer>();
            if (limiter == null) limiter = round.gameObject.AddComponent<CrowsSlapTracer>();
            limiter.Arm(round);
        }

        internal static void ConfigureVehicleCoax(Vehicle vehicle)
        {
            if (!M1A1.m2_coax.Value || vehicle == null || converted.Contains(vehicle.GetInstanceID())) return;
            WeaponsManager manager = vehicle.GetComponent<WeaponsManager>();
            if (manager == null || manager.Weapons == null || manager.Weapons.Length < 2) return;
            WeaponSystemInfo info = manager.Weapons[1];
            WeaponSystem gun = info == null ? null : info.Weapon;
            AmmoFeed feed = gun == null ? null : gun.Feed;
            if (feed == null || feed.ReadyRack == null) return;
            AmmoType.AmmoClip selected = Select(M1A1.crows_ammo.Value);
            if (selected == null) return;

            AmmoType.AmmoClip[] oldTypes = feed.ReadyRack.ClipTypes;
            if (oldTypes == null || oldTypes.Length == 0) return;
            if (feed.ReadyRack.UseVisibleRounds)
            {
                Warn("M2 coax conversion skipped: unexpected visible-round ready rack.");
                return;
            }
            Queue<AmmoType> queue = Reflect.Get<Queue<AmmoType>>(feed, "_feedClipMain");
            if (queue == null) return;
            int loadedCount = feed.CurrentClipRemainingCount;
            int capacity = feed.LoadedClipType == null ? oldTypes[0].Capacity : feed.LoadedClipType.Capacity;
            AmmoType.AmmoClip loaded = WithCapacity(selected, capacity);

            // Change the existing racks in place. Never re-run Awake()/Start(): they append
            // stored clips and subscribe crew events a second time.
            ReplaceRack(feed.ReadyRack, oldTypes, selected);
            var changedLoadouts = new List<LoadoutManager>();
            foreach (LoadoutManager loadout in vehicle.GetComponentsInChildren<LoadoutManager>(true))
            {
                bool ownsReadyRack = false;
                if (loadout.RackLoadouts == null) continue;
                foreach (LoadoutManager.RackLoadout entry in loadout.RackLoadouts)
                    if (entry.Rack == feed.ReadyRack) ownsReadyRack = true;
                if (!ownsReadyRack) continue;

                // LoadedAmmoList is a per-instance clone in GHPC. Replace the codex entries,
                // including restock/override references, without changing the donor assets.
                loadout.LoadedAmmoList.AmmoClips = ReplaceCodexes(loadout.LoadedAmmoList.AmmoClips, oldTypes, selected);
                foreach (LoadoutManager.RackLoadout entry in loadout.RackLoadouts)
                {
                    if (entry.Rack != feed.ReadyRack) ReplaceRack(entry.Rack, oldTypes, selected);
                    entry.OverrideInitialClips = ReplaceCodexes(entry.OverrideInitialClips, oldTypes, selected);
                }
                Reflect.Call(loadout, "RegisterAllBallistics");
                changedLoadouts.Add(loadout);
            }

            info.Name = "M2HB Coaxial Gun";
            if (gun.WeaponData != null)
            {
                WeaponSystemCodexScriptable codex = UnityEngine.Object.Instantiate(gun.WeaponData);
                codex.name = "m2hb_coax";
                codex.FriendlyName = info.Name;
                codex.CaliberMm = 12.7f;
                Reflect.Set(gun, "CodexEntry", codex);
                owned.Add(codex);
            }
            gun.SetCycleTime(0.133f); // AMP replacement's cycle time and dispersion
            Reflect.Set(feed, "_totalCycleTime", 0.133f);
            gun.BaseDeviationAngle = 0.025f;
            if (gun.WeaponSound != null)
            {
                gun.WeaponSound.StopLoop();
                gun.WeaponSound.LoopEventPath = "event:/Weapons/MG_m85_400rmp";
                gun.WeaponSound.SingleShotEventPaths = new[] { gun.WeaponSound.LoopEventPath };
            }
            feed.SetNextClipType(loaded);
            Reflect.Set(feed, "_queuedClipTypeLockedIn", loaded);
            Reflect.SetProperty(feed, "LoadedClipType", loaded);
            Reflect.SetProperty(feed, "AmmoTypeInBreech", null);
            queue.Clear();
            for (int i = 0; i < loadedCount; i++)
                queue.Enqueue(loaded.MinimalPattern[i % loaded.MinimalPattern.Length].AmmoType);
            if (loadedCount > 0) Reflect.Call(feed, "TryFinalFeedNewRound", false);
            else Reflect.Call(feed, "TryFinalFeedNewClip", -1);
            foreach (LoadoutManager loadout in changedLoadouts)
                Reflect.Call(loadout, "RefreshSnapshot");
            AttachTracerLimit(gun);
            converted.Add(vehicle.GetInstanceID());
        }

        private static AmmoType.AmmoClip WithCapacity(AmmoType.AmmoClip selected, int capacity)
        {
            return new AmmoType.AmmoClip { Name = selected.Name, MinimalPattern = selected.MinimalPattern, Capacity = capacity };
        }

        private static bool Matches(AmmoType.AmmoClip clip, AmmoType.AmmoClip[] oldTypes)
        {
            if (clip == null) return false;
            foreach (AmmoType.AmmoClip old in oldTypes)
                if (old != null && clip.Equals(old)) return true;
            return false;
        }

        private static void ReplaceRack(GHPC.Weapons.AmmoRack rack, AmmoType.AmmoClip[] oldTypes, AmmoType.AmmoClip selected)
        {
            if (rack == null || rack.UseVisibleRounds) return;
            if (rack.ClipTypes != null)
            {
                AmmoType.AmmoClip[] types = (AmmoType.AmmoClip[])rack.ClipTypes.Clone();
                for (int i = 0; i < types.Length; i++)
                    if (Matches(types[i], oldTypes)) types[i] = WithCapacity(selected, types[i].Capacity);
                rack.ClipTypes = types;
            }
            for (int i = 0; i < rack.StoredClips.Count; i++)
                if (Matches(rack.StoredClips[i], oldTypes))
                    rack.StoredClips[i] = WithCapacity(selected, rack.StoredClips[i].Capacity);
        }

        private static AmmoClipCodexScriptable[] ReplaceCodexes(AmmoClipCodexScriptable[] source, AmmoType.AmmoClip[] oldTypes, AmmoType.AmmoClip selected)
        {
            if (source == null) return null;
            AmmoClipCodexScriptable[] result = (AmmoClipCodexScriptable[])source.Clone();
            for (int i = 0; i < result.Length; i++)
            {
                if (result[i] == null || !Matches(result[i].ClipType, oldTypes)) continue;
                AmmoClipCodexScriptable clone = UnityEngine.Object.Instantiate(result[i]);
                clone.ClipType = WithCapacity(selected, result[i].ClipType.Capacity);
                result[i] = clone;
                owned.Add(clone);
            }
            return result;
        }

        private static void BuildRaufossBelt()
        {
            ammo_raufoss = new AmmoType();
            Util.ShallowCopy(ammo_raufoss, ammo_codex_m8.AmmoType);
            ammo_raufoss.Name = "Mk 211 Mod 0 HEIAP";
            ammo_raufoss.UseTracer = false;
            ammo_raufoss.TntEquivalentKg = 0.035f;
            ammo_raufoss.RhaPenetration = 32f;
            ammo_raufoss.ImpactFuseTime = 0.002f;
            ammo_raufoss.MuzzleVelocity = 979f;
            ammo_raufoss.ImpactAudio = GHPC.Audio.ImpactAudioType.AutocannonExplosive;
            ammo_raufoss.Category = AmmoType.AmmoCategory.Explosive;
            ammo_raufoss.ImpactEffectDescriptor = new ParticleEffectsManager.ImpactEffectDescriptor()
            {
                HasImpactEffect = true,
                ImpactCategory = ParticleEffectsManager.Category.Kinetic,
                EffectSize = ParticleEffectsManager.EffectSize.Bullet,
                RicochetType = ParticleEffectsManager.RicochetType.None,
                Flags = ParticleEffectsManager.ImpactModifierFlags.Incendiary | ParticleEffectsManager.ImpactModifierFlags.VeryLarge,
                MinFilterStrictness = ParticleEffectsManager.FilterStrictness.Low
            };

            ammo_codex_raufoss = ScriptableObject.CreateInstance<AmmoCodexScriptable>();
            ammo_codex_raufoss.AmmoType = ammo_raufoss;
            ammo_codex_raufoss.name = "ammo_raufoss";
            Util.CacheAmmo(ammo_raufoss);

            ammo_raufoss_tracer = new AmmoType();
            Util.ShallowCopy(ammo_raufoss_tracer, ammo_raufoss);
            ammo_raufoss_tracer.Name = "Mk 30 Mod 0 HEIAP-T";
            ammo_raufoss_tracer.UseTracer = true;
            ammo_raufoss_tracer.ImpactEffectDescriptor.RicochetType = ParticleEffectsManager.RicochetType.LargeTracer;

            ammo_codex_raufoss_tracer = ScriptableObject.CreateInstance<AmmoCodexScriptable>();
            ammo_codex_raufoss_tracer.AmmoType = ammo_raufoss_tracer;
            ammo_codex_raufoss_tracer.name = "ammo_raufoss_tracer";
            Util.CacheAmmo(ammo_raufoss_tracer);

            clip_raufoss_500 = new AmmoType.AmmoClip();
            clip_raufoss_500.Capacity = 400;
            clip_raufoss_500.Name = "Mk 211 Mod 0 HEIAP";
            clip_raufoss_500.MinimalPattern = new AmmoCodexScriptable[] {
                ammo_codex_raufoss,
                ammo_codex_raufoss,
                ammo_codex_raufoss,
                ammo_codex_raufoss,
                ammo_codex_raufoss_tracer,
            };

            clip_codex_raufoss_500 = ScriptableObject.CreateInstance<AmmoClipCodexScriptable>();
            clip_codex_raufoss_500.name = "clip_raufoss_500rnd";
            clip_codex_raufoss_500.ClipType = clip_raufoss_500;

        }
    }

    // Each pooled round resets this limiter when fired. Hiding the tracer never kills the projectile.
    public sealed class CrowsSlapTracer : MonoBehaviour
    {
        private LiveRound round;
        private DynamicTracer tracer;
        private Light tracerLight;
        private Vector3 lastPosition;
        private float distance;

        private void OnDisable() { enabled = false; }

        internal void Arm(LiveRound firedRound)
        {
            round = firedRound;
            tracer = GetComponentInChildren<DynamicTracer>(true);
            tracerLight = GetComponentInChildren<Light>(true);
            lastPosition = transform.position;
            distance = 0;
            enabled = true;
        }

        private void LateUpdate()
        {
            if (round == null || round.Info != CrowsAmmo.TracerAmmo) { enabled = false; return; }
            distance += Vector3.Distance(lastPosition, transform.position);
            lastPosition = transform.position;
            if (distance < CrowsAmmoRules.TracerRange) return;
            if (tracer != null) tracer.Active = false;
            if (tracerLight != null)
            {
                tracerLight.enabled = false;
                foreach (Transform child in tracerLight.transform) child.gameObject.SetActive(false);
            }
            enabled = false;
        }
    }
}
