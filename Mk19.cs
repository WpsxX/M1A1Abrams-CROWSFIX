using System;
using System.Collections.Generic;
using System.IO;
using GHPC;
using GHPC.Camera;
using GHPC.Crew;
using GHPC.Effects;
using GHPC.Weapons;
using GHPC.Weaponry;
using MelonLoader;
using MelonLoader.Utils;
using ModUtil;
using UnityEngine;
using VUtil = VanillaAssetUtil.ModUtil.Util;

namespace M1A1Abrams
{
    /// <summary>
    /// Turns the borrowed .50 cal into an Mk 19: 40 mm HEDP ammunition assembled from the
    /// reference data, the launcher's own cycle rate and dispersion, and the mod's fire
    /// sound played once per grenade. The coaxial M2 is deliberately not touched here.
    /// </summary>
    internal static class Mk19
    {
        private const string ASSET_FOLDER = "m1a1crowsfix_assets";
        private const string SOUND_BUNDLE = "mk19_sfx";

        private static readonly List<ScriptableObject> owned = new List<ScriptableObject>();
        private static readonly HashSet<string> warnings = new HashSet<string>();
        private static readonly Dictionary<string, GameObject> prefab_cache = new Dictionary<string, GameObject>();

        private static AmmoCodexScriptable codex_m430;
        private static AmmoCodexScriptable codex_m430a1;
        private static AmmoType.AmmoClip clip_m430;
        private static AmmoType.AmmoClip clip_m430a1;

        private static AudioClip[] clips_interior;
        private static AudioClip[] clips_exterior;

        private static bool ammo_ready;
        private static bool audio_ready;

        internal static void Reset()
        {
            foreach (ScriptableObject asset in owned)
            {
                if (asset != null) UnityEngine.Object.Destroy(asset);
            }

            owned.Clear();
            warnings.Clear();
            prefab_cache.Clear();
            codex_m430 = codex_m430a1 = null;
            clip_m430 = clip_m430a1 = null;
            clips_interior = clips_exterior = null;
            ammo_ready = false;
            audio_ready = false;
        }

        /// <summary>
        /// The belt for the selected grenade, or null when the ammunition could not be built.
        /// </summary>
        internal static AmmoType.AmmoClip Select(string value)
        {
            if (!InitAmmo()) return null;

            string key = Mk19Rules.Normalize(value);

            if (key == null)
            {
                Warn("Unknown CROWS/MK19 Ammo '" + value + "'; using M430.");
                key = Mk19Rules.M430;
            }

            AmmoType.AmmoClip result = key == Mk19Rules.M430A1 ? clip_m430a1 : clip_m430;
            if (result == null) return null;

            // The fire control has to know the round before it can compute a superelevation
            // for a 241 m/s grenade; the 120 mm rounds it was built with tell it nothing here.
            EnsureFiringTable(result);

            return result;
        }

        /// <summary>
        /// Gives the fire control a firing table that actually covers the grenade.
        ///
        /// The game integrates a trajectory for ten seconds and sweeps elevation to thirty
        /// degrees, which is sized for a 1,500 m/s dart. A 241 m/s grenade runs out of that
        /// table at fourteen degrees and 1,580 m, and every solution past it comes back
        /// clamped: the barrel then gets a fraction of the elevation it needs and the rounds
        /// land short however well the sight is laid. The table is widened once per ammo
        /// type; the repository is checked first because a scene load throws its cache away.
        /// </summary>
        internal static void EnsureFiringTable(AmmoType.AmmoClip clip)
        {
            if (clip == null || clip.MinimalPattern == null) return;

            foreach (AmmoCodexScriptable codex in clip.MinimalPattern)
            {
                if (codex == null) continue;
                EnsureFiringTable(codex.AmmoType);
            }
        }

        internal static void EnsureFiringTable(AmmoType ammo)
        {
            if (ammo == null) return;

            try
            {
                BallisticComputer computer = BallisticComputerRepository.Instance.RegisterAmmoType(ammo);

                if (computer == null) return;

                if (computer.MaxFlightTimeSeconds >= Mk19Rules.FiringTableSeconds
                    && computer.MaxElevation >= Mk19Rules.FiringTableMaxElevation)
                {
                    return;
                }

                computer.MaxFlightTimeSeconds = Mk19Rules.FiringTableSeconds;
                computer.MaxElevation = Mk19Rules.FiringTableMaxElevation;
                computer.RefreshData();
            }
            catch (Exception e)
            {
                Warn("Firing table for " + ammo.Name + " could not be widened: " + e.Message);
            }
        }

        /// <summary>
        /// Applies the launcher's rate of fire, dispersion and report to a CROWS weapon.
        /// Call after the weapon's rack has been replaced with <see cref="Select"/>'s belt.
        /// </summary>
        internal static void Configure(WeaponSystem weapon)
        {
            if (weapon == null) return;

            weapon.SetCycleTime(Mk19Rules.CycleTime);
            if (weapon.Feed != null)
            {
                Reflect.Set(weapon.Feed, "_totalCycleTime", Mk19Rules.CycleTime);
            }

            weapon.BaseDeviationAngle = Mk19Rules.DispersionDegrees;

            ConfigureReload(weapon);

            // The borrowed M2 audio is a 550 rpm loop. An Mk 19 is a series of separate
            // reports, so the game's weapon audio is muted and the pack is played per round.
            WeaponAudio sound = weapon.WeaponSound;
            if (sound != null)
            {
                sound.StopLoop();
                sound.LoopEventPath = string.Empty;
                sound.SingleShotMode = true;
                sound.SingleShotEventPaths = new string[0];
            }

            if (!InitAudio())
            {
                // No pack on disk: put the .50 cal loop back rather than leave the gun silent.
                if (sound != null)
                {
                    sound.SingleShotMode = false;
                    sound.LoopEventPath = "event:/Weapons/MG_m2_550rmp";
                }
                return;
            }

            Transform muzzle = weapon.MuzzleIdentity == null ? weapon.transform : weapon.MuzzleIdentity;
            Mk19SfxPlayer player = muzzle.GetComponent<Mk19SfxPlayer>();
            if (player == null) player = muzzle.gameObject.AddComponent<Mk19SfxPlayer>();
            player.Arm(clips_interior, clips_exterior);

            weapon.Fired -= player.OnFired;
            weapon.Fired += player.OnFired;
        }

        /// <summary>
        /// Turns the borrowed feed's clip change into a silent ten second belt swap.
        ///
        /// The reload is timed entirely by the sum of the feed's reload stages, so replacing the
        /// .50 cal's staged clip animation with one silent stage gives both the duration and the
        /// quiet in one move. The cached total has to be cleared or the feed keeps the old sum.
        /// </summary>
        private static void ConfigureReload(WeaponSystem weapon)
        {
            AmmoFeed feed = weapon.Feed;
            if (feed == null) return;

            AmmoFeed.ReloadStage stage = new AmmoFeed.ReloadStage();
            Reflect.Set(stage, "Duration", Mk19Rules.ReloadSeconds);
            stage.LiveDuration = Mk19Rules.ReloadSeconds;
            stage.StageAudio = null;
            stage.StageClips = new AudioClip[0];
            stage.StageAudioEvent = string.Empty;
            stage.AnimatedParts = null;
            stage.EjectedPrefab = null;

            feed.ClipReloadStages = new AmmoFeed.ReloadStage[] { stage };

            // The donor feed starts its autoloader event from a private field; empty means silent.
            Reflect.Set(feed, "_clipReloadFMODEvent", string.Empty);
            Reflect.Set(feed, "_totalReloadTime", 0f);

            // Detaching the feed from the donor cleared the crew lookup, and the game's own
            // auto-reload path reads it without a null check. The CROWS is part of the tank,
            // so the tank's crew manager is the right answer.
            if (Reflect.GetBoxed(feed, "_crewManager") == null)
            {
                CrewManager crew = weapon.GetComponentInParent<CrewManager>();
                if (crew != null) Reflect.Set(feed, "_crewManager", crew);
            }

            Mk19Reload watcher = feed.gameObject.GetComponent<Mk19Reload>();
            if (watcher == null) watcher = feed.gameObject.AddComponent<Mk19Reload>();
            watcher.feed = feed;
        }

        // --- ammunition ---------------------------------------------------------------

        private static bool InitAmmo()
        {
            if (ammo_ready) return true;

            AmmoType donor = DonorAmmo();

            if (donor == null)
            {
                Warn("No donor round found; the Mk 19 ammunition was not built.");
                return false;
            }

            codex_m430 = BuildGrenade(
                donor, Mk19Rules.M430Name, "ammo_m430_hedp",
                Mk19Rules.TntM430, Mk19Rules.PenM430,
                Mk19Rules.FragmentsM430, 0.45f, 26f);

            codex_m430a1 = BuildGrenade(
                donor, Mk19Rules.M430A1Name, "ammo_m430a1_hedp",
                Mk19Rules.TntM430A1, Mk19Rules.PenM430A1,
                Mk19Rules.FragmentsM430A1, 0.65f, 38f);

            if (codex_m430 == null || codex_m430a1 == null)
            {
                Warn("Mk 19 ammunition could not be assembled.");
                return false;
            }

            clip_m430 = Belt(Mk19Rules.M430Name, codex_m430);
            clip_m430a1 = Belt(Mk19Rules.M430A1Name, codex_m430a1);

            ammo_ready = true;
            return true;
        }

        /// <summary>
        /// Some already-loaded explosive round, so the copy starts with a working visual
        /// model, guidance and decal setup instead of a blank AmmoType full of nulls.
        /// </summary>
        private static AmmoType DonorAmmo()
        {
            AmmoType explosive = null;
            AmmoType any = null;

            foreach (AmmoCodexScriptable codex in Resources.FindObjectsOfTypeAll<AmmoCodexScriptable>())
            {
                if (codex == null || codex.AmmoType == null) continue;

                AmmoType candidate = codex.AmmoType;
                if (any == null) any = candidate;

                if (candidate.Category == AmmoType.AmmoCategory.Explosive && candidate.DetonateEffect != null)
                {
                    explosive = candidate;
                    break;
                }
            }

            if (explosive != null) return explosive;

            // The mod always loads the 120 mm HEAT-MP round, so it is a dependable fallback
            // even when no plain HE round is in the mission.
            if (Assets.ammo_m456 != null) return Assets.ammo_m456;

            return any;
        }

        private static AmmoCodexScriptable BuildGrenade(
            AmmoType donor, string name, string codex_name,
            float tnt, float penetration, int fragments, float spall_multiplier, float max_spall_rha)
        {
            AmmoType ammo = new AmmoType();
            VUtil.ShallowCopy(ammo, donor);

            ammo.Name = name;
            ammo.ShortName = AmmoType.AmmoShortName.He;
            // HEDP is a dual-purpose round: a shaped-charge liner plus a fragmenting body. Filed
            // as plain Explosive, the game ignored RhaPenetration entirely and only applied the
            // 32 g blast, so armoured targets shrugged it off. ShapedCharge makes the 51/76 mm
            // penetration actually count while the blast and spall still work as before.
            ammo.Category = AmmoType.AmmoCategory.ShapedCharge;
            ammo.Caliber = Mk19Rules.CaliberMm;
            ammo.MuzzleVelocity = Mk19Rules.Velocity;
            ammo.Mass = Mk19Rules.Mass;
            ammo.SectionalArea = Mk19Rules.Area;
            ammo.Coeff = Mk19Rules.DragCoeff;
            ammo.MaximumRange = Mk19Rules.MaxRange;
            ammo.RhaPenetration = penetration;
            ammo.TntEquivalentKg = tnt;
            ammo.DetonateSpallCount = fragments;
            ammo.SpallMultiplier = spall_multiplier;
            ammo.MinSpallRha = 1f;
            ammo.MaxSpallRha = max_spall_rha;
            ammo.ArmingDistance = Mk19Rules.ArmingDistance;
            ammo.ImpactFuseTime = 0f;
            ammo.AlwaysProduceBlast = true;
            ammo.ShatterOnRicochet = false;
            ammo.UseTracer = false;
            ammo.UseErrorCorrection = true;
            ammo.IsSpallType = false;
            ammo.VisualType = LiveRoundMarshaller.LiveRoundVisualType.Bullet;
            ammo.ImpactAudio = GHPC.Audio.ImpactAudioType.AutocannonExplosive;

            // The AGS-17 recipe: a high-explosive autocannon-sized hit, which is the same
            // weight class as a 40 mm grenade.
            ammo.ImpactEffectDescriptor = new ParticleEffectsManager.ImpactEffectDescriptor
            {
                HasImpactEffect = true,
                ImpactCategory = ParticleEffectsManager.Category.HighExplosive,
                EffectSize = ParticleEffectsManager.EffectSize.Autocannon,
                RicochetType = ParticleEffectsManager.RicochetType.None,
                Flags = ParticleEffectsManager.ImpactModifierFlags.Large,
                MinFilterStrictness = ParticleEffectsManager.FilterStrictness.Low
            };

            GameObject blast = FindPrefab("HE_explosion");
            if (blast != null) ammo.DetonateEffect = blast;

            GameObject dirt = FindPrefab("HE Terrain Impact");
            if (dirt != null) ammo.TerrainImpactEffect = dirt;

            if (blast == null)
            {
                Warn("'HE_explosion' was not loaded; " + name + " keeps the donor's blast effect.");
            }

            ammo.ArmorOptimizations = new AmmoType.ArmorOptimization[0];
            ammo.CachedIndex = -1;
            AmmoCache.CacheAmmo(ammo);

            AmmoCodexScriptable codex = ScriptableObject.CreateInstance<AmmoCodexScriptable>();
            codex.name = codex_name;
            codex.AmmoType = ammo;
            owned.Add(codex);
            return codex;
        }

        private static AmmoType.AmmoClip Belt(string name, AmmoCodexScriptable codex)
        {
            return new AmmoType.AmmoClip
            {
                Name = name,
                Capacity = Mk19Rules.BeltCapacity,
                MinimalPattern = new AmmoCodexScriptable[] { codex }
            };
        }

        /// <summary>
        /// A loaded prefab by name, cached because the scan walks every loaded object and both
        /// grenades ask for the same two effects. Only hits are cached: a miss has to look again
        /// in case the effect database loads later.
        /// </summary>
        private static GameObject FindPrefab(string name)
        {
            GameObject cached;
            if (prefab_cache.TryGetValue(name, out cached) && cached != null) return cached;

            foreach (GameObject candidate in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (candidate == null || candidate.name != name) continue;

                prefab_cache[name] = candidate;
                return candidate;
            }

            return null;
        }

        // --- fire sound ---------------------------------------------------------------

        private static bool InitAudio()
        {
            if (audio_ready) return true;

            string path = Path.Combine(MelonEnvironment.ModsDirectory, ASSET_FOLDER, SOUND_BUNDLE);
            AssetBundle bundle = AssetBundle.LoadFromFile(path);

            if (bundle == null)
            {
                Warn("Fire sound bundle missing: " + path);
                return false;
            }

            try
            {
                AudioClip[] all = bundle.LoadAllAssets<AudioClip>();
                List<AudioClip> interior = new List<AudioClip>();
                List<AudioClip> exterior = new List<AudioClip>();

                foreach (AudioClip clip in all)
                {
                    if (clip == null) continue;

                    // The pack keeps the AGS-17 naming: _1p_ is heard from inside the
                    // vehicle, _3p_ is the report everyone else hears.
                    if (clip.name.Contains("_1p_")) interior.Add(clip);
                    else if (clip.name.Contains("_3p_")) exterior.Add(clip);

                    clip.hideFlags = HideFlags.DontUnloadUnusedAsset;
                }

                if (interior.Count == 0 || exterior.Count == 0)
                {
                    Warn("Fire sound bundle holds no 1p/3p clips: " + path);
                    return false;
                }

                clips_interior = interior.ToArray();
                clips_exterior = exterior.ToArray();
            }
            finally
            {
                bundle.Unload(false);
            }

            audio_ready = true;
            return true;
        }

        private static void Warn(string message)
        {
            if (!warnings.Add(message)) return;
            MelonLogger.Warning("[M1A1] " + message);
        }
    }

    /// <summary>
    /// Plays one report per grenade from the mod's own sound pack. It lives on the muzzle
    /// so the report is positioned where the round leaves the barrel.
    /// </summary>
    public sealed class Mk19SfxPlayer : MonoBehaviour
    {
        private AudioSource source;
        private AudioClip[] interior;
        private AudioClip[] exterior;
        private int next_interior;
        private int next_exterior;

        internal void Arm(AudioClip[] interior_clips, AudioClip[] exterior_clips)
        {
            interior = interior_clips;
            exterior = exterior_clips;

            if (source == null) source = gameObject.GetComponent<AudioSource>();
            if (source == null) source = gameObject.AddComponent<AudioSource>();

            source.playOnAwake = false;
            source.loop = false;
            source.dopplerLevel = 0f;
            source.spread = 20f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 25f;
            source.maxDistance = 550f;
            source.volume = 1f;
            source.spatialBlend = 1f;
        }

        internal void OnFired(AmmoType ammo, LiveRound round)
        {
            if (source == null) return;

            bool inside = IsPlayerInside();
            AudioClip[] pool = inside ? interior : exterior;
            if (pool == null || pool.Length == 0) return;

            // Round-robin through the takes so a burst does not machine-gun the same sample.
            int index = inside ? next_interior : next_exterior;
            AudioClip clip = pool[index];

            if (inside) next_interior = (index + 1) % pool.Length;
            else next_exterior = (index + 1) % pool.Length;

            source.spatialBlend = inside ? 0f : 1f;
            source.PlayOneShot(clip, inside ? 1f : 0.9f);
        }

        /// <summary>
        /// True only for the player's own vehicle seen from inside; every other Mk 19 in the
        /// mission, and the player's own gun in third person, gets the distant report.
        /// </summary>
        private bool IsPlayerInside()
        {
            CameraManager manager = CameraManager.Instance;
            if (manager == null || manager.ExteriorMode) return false;

            UnityEngine.Camera camera = CameraManager.MainCam;
            if (camera == null) return false;

            return Vector3.Distance(camera.transform.position, transform.position) < 20f;
        }
    }

    /// <summary>
    /// Puts the fire control's ballistic solution into the barrel, not into the mount.
    ///
    /// The station's sight is parented to the gun, and the game derives its aim vector from that
    /// sight every LateUpdate - so letting the fire control superelevate the mount does not just
    /// tilt the barrel, it drags the gunner's whole view up with it. For a .50 cal that is
    /// fractions of a degree and nobody notices; for a 241 m/s grenade it is degrees, the sight
    /// climbs off the target and the far ground leaves the screen.
    ///
    /// So the mount is left on the aim line (SuperelevateWeapon is off for this weapon) and the
    /// compensation is applied to a pivot at the trunnion that carries the visible gun and the
    /// borrowed weapon object owning MuzzleIdentity. The barrel shows the ballistic angle, the
    /// round leaves along it because the muzzle forward turns with it, the sight never moves off
    /// the aim line, and the aim feedback loop the fire control runs through the sight stays stable.
    ///
    /// With no range the pivot returns to identity in one step: the computer has no solution, and
    /// the barrel dropping back onto the aim line is the honest picture.
    /// </summary>
    public sealed class Mk19BallisticMuzzle : MonoBehaviour
    {
        internal FireControlSystem fcs;

        /// <summary>Pivot at the trunnion; parent of the visible gun and the muzzle anchor.</summary>
        internal Transform ballistic;

        /// <summary>The gun transform the sight and the aim line hang from; never rotated here.</summary>
        internal Transform gun;

        private float table_timer;

        private void LateUpdate()
        {
            if (fcs == null || ballistic == null || gun == null) return;

            // The repository drops its cached computers on every scene load, so the widened
            // table has to be re-applied after one. Twice a second is enough to catch it and
            // costs a dictionary lookup when nothing has changed.
            table_timer -= Time.deltaTime;
            if (table_timer <= 0f)
            {
                table_timer = 2f;
                Mk19.EnsureFiringTable(fcs.CurrentAmmoType);
            }

            Quaternion target = Quaternion.identity;

            if (fcs.CurrentRange > 0f)
            {
                Vector3 solution;
                try
                {
                    solution = fcs.CompensatedWeaponVector(true);
                }
                catch (Exception)
                {
                    solution = Vector3.zero;
                }

                if (solution.sqrMagnitude > 1e-6f)
                {
                    Vector3 direction = solution.normalized;
                    Vector3 up = gun.up;

                    // LookRotation is undefined when the solution lines up with the up hint; the
                    // gun's right axis is the fallback that keeps the roll well behaved.
                    if (Mathf.Abs(Vector3.Dot(direction, up)) > 0.99f) up = gun.right;

                    Quaternion world = Quaternion.LookRotation(direction, up);
                    target = Quaternion.Inverse(gun.rotation) * world;
                }
            }

            ballistic.localRotation = target;
        }
    }

    /// <summary>
    /// Starts a belt change when the launcher runs dry.
    ///
    /// The game's own auto-reload is wired to the donor's crew manager, which the detach pass
    /// clears, so this does the trigger itself. FeedNewClip refuses while a reload is already
    /// running, so it is safe if the game's path does fire too.
    /// </summary>
    public sealed class Mk19Reload : MonoBehaviour
    {
        internal AmmoFeed feed;

        private float next_check;

        private void Update()
        {
            if (feed == null) return;
            if (Time.time < next_check) return;
            next_check = Time.time + 0.5f;

            if (feed.Reloading) return;
            if (feed.CurrentClipRemainingCount > 0) return;
            if (feed.ReadyRack == null || feed.ReadyRack.IsEmpty) return;

            feed.FeedNewClip();
        }
    }
}
