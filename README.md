[English](README.md) | [简体中文](README.zh-CN.md)

---

# Attributions
M153 CROWS model from: "MRAP Oshkosh (West)" (https://marketplace.vigilante.us/product/mrap-oshkosh-west/) by Vigilante

CITV & M256 model pulled from: "WIP - Abrams M1A2 TUSK 2" (https://skfb.ly/VUwX) by DavidFalke

---

# Thanks to the original author

**This repository is a fork of [thebeninator/M1A1Abrams](https://github.com/thebeninator/M1A1Abrams).**

Thanks also to **Vigilante** for the M153 CROWS model and to **DavidFalke** for the CITV and M256 models.

> Upstream `master` remains the authoritative version of the mod. If you want the vanilla M1A1 Abrams mod
> without the CROWS work, use [thebeninator's repository](https://github.com/thebeninator/M1A1Abrams) instead.

# M1A1 Abrams
A mod for Gunner, HEAT, PC! Requires [MelonLoader](https://github.com/LavaGang/MelonLoader/) and the
shared `VanillaAssetUtil.dll`.

Compatible with 20261014.

Fixes CITV.

Fixes CROWS.

Gameplay/Install guide: https://www.youtube.com/watch?v=e_qNIyBb2SA

If you get an error launching through Steam you will need to run the game executable directly.

![Screenshot_9](https://github.com/thebeninator/M1A1Abrams/assets/89621837/0fcbec7f-dc15-4ef2-850e-8e48c5783260)
![image](https://github.com/thebeninator/M1A1Abrams/assets/89621837/c80ef9c8-7e42-485c-957e-6cba0dd20c0c)

---

Features the preset system for customizable randomization

<img width="1146" height="611" alt="how_the_preset_system_works" src="https://github.com/user-attachments/assets/a1db3167-fe3e-4bc4-a45f-47e382da0fbd" />

---

<p>
Converts all M1IPs to M1A1s equipped with:
	<ul>
	<li>120mm M256 Gun</li>
	<li>M829 APFSDS-T (22 rounds)</li>
	<li>M830 HEAT-MP-T (18 rounds, has shoulder fuze)</li>
	</ul>
</p>

# Extra Features/Settings (in mod config)
- Alternate APFSDS rounds: M827, M829, M829A1, M829A2, M829A3, M829A4
  (M829A4: long-rod DU, 1,565 m/s, +6.5% penetration at the muzzle over the mod's M829A3 and twice as hard
  on heavy ERA — see the M829A4 chapter below)
- Alternate HEAT rounds: M830A1 (proximity fuse, toggled by double-pressing that round's hotkey)
- Customize how many M829 and M830 rounds every M1A1/E1 carries
- The following are disabled by default:
	- Convert non-IP M1s to M1E1s
	- Randomize M1A1/E1 conversions
	- Horizontal sight stabilization for M1A1/E1s when applying lead
	- Commander CITV
	- Depleted uranium cheek armour for M1A1s
	- M153 CROWS (ported in and made functional by this fork — see below)
	- Mk 19 grenade launcher on the CROWS, firing M430 / M430A1
	- Different gunner's thermal settings

# Conversions

<p>M1A1: All settings disabled</p>
<p>M1A1HA: DU Armour (gen = 1)</p>
<p>M1A1HC: DU Armour (gen > 1)</p>
<p>M1A2: Rotate Azimuth, CITV, DU Armour (gen >= 1) </p>
<p>M1A2 SEP: M1A2, FLIR generation = 3 or CROWS</p>

---

# What this fork adds

| | Upstream `master` | This fork |
|---|---|---|
| M153 CROWS II | Declared in the config, never built, marked *"CURRENTLY NOT FUNCTIONAL"* | Ported in, mounted, and working end to end |
| CROWS automatic tracking | — | Target lock, target box, lead solution, automatic ranging |
| CROWS sight | — | Day + thermal optics, 1X/5X/10X/15X, sight HUD, azimuth ring |
| CROWS ammunition | — | Selectable M8 API / Mk 211 Mod 0 / **M903 SLAP + M962 SLAP-T** belts, or swap the whole weapon for an **Mk 19 40 mm grenade launcher** (M430 / M430A1) |
| Coax machine gun | M240 | Optional M2HB sharing the CROWS belt selection |
| M829A4 APFSDS-T | — | **New round**, calibrated against this mod's own M829A3 |

Everything else — the preset system, the conversion tiers, DU armour, CITV, FLIR generations, M830A1, the
existing APFSDS family — is upstream's work and behaves exactly as it does upstream.

---

# M153 CROWS II

The **M153 CROWS II** (Common Remotely Operated Weapon Station) replaces the commander's exposed .50 cal
with a remotely operated M2HB station: the commander aims and fires it from under armour, with a stabilized
sight, a laser rangefinder and a fire-control computer doing the ballistic work.

![M153 CROWS II thermal sight at 15X with a target locked](images/crows-thermal-sight.jpg)

## Enabling it

In `Bin/UserData/MelonPreferences.cfg`, section `[M1A1-CROWSFIX]`:

```toml
"CROWS (M1E1)" = true              # M1E1 (converted non-IP M1s)
"CROWS (M1A1)" = true              # M1A1 / M1IP
"Alternative Position" = false     # true = station sits to the commander's right
"CROWS/M2 Ammo" = "M8 API"         # M8 API | Mk 211 Mod 0 | M903 SLAP
"M2 Coax" = false                  # true = M2HB coax on the same belt selection
"CROWS/MK19 Instead Of M2" = false # true = Mk 19 grenade launcher instead of the .50 cal
"CROWS/MK19 Ammo" = "M430"         # M430 | M430A1, only read when the Mk 19 is mounted
```

The bundles `m1a1crowsfix_assets/crows` and `m1a1crowsfix_assets/crowmk19` must be in the game's
`Mods` folder, together with the shared `VanillaAssetUtil.dll`.

Legacy key names from other builds (`CROWS (M1E1) <DISABLED>`, `CROWS (M1A1) <DISABLED>`,
`CROWS Alt Placement`, `CROWS: Alternate Placement (Global)`, `Use Mk 211 Mod 0`, `CROWS Raufoss`,
`CROWS: Raufoss Ammo (Global)`, `M2 SLAP`) are read straight out of the config file and migrated on first
run, so an old install keeps working. The log prints the effective values once at startup.

## Features

**The station**
- The rear whip antennas are switched to the upright two-antenna configuration so they do not intersect the
  station. Only the spawned vehicle instance is touched; the unpacked prefab is left alone.

**Optics**
- Two sights, day and thermal, each a full camera slot registered with the game's camera manager.
- Magnification **1X / 5X / 10X / 15X**, mapped as 30° / 6° / 3° / 2° field of view.
- The reticle is scaled to the sight and drawn in the station's own sight colour.
- Thermal vibration blur and shake are zeroed so the thermal sight matches the day sight.

**Slewing**
- Rated traverse **90°/s**, elevation **50°/s**, elevation limits **-15° / +60°**
- The barrel superelevates for range while the sight stays on the target, so the picture no longer climbs
  into the sky past 1 km

**Armament**
- M2HB .50 cal by default, with a selectable belt (M8 API / Mk 211 Mod 0 / M903 SLAP)
- Optionally the whole weapon is swapped for an **Mk 19 grenade launcher** — see the chapter below

**Automatic target tracking (ATT)**
- **Middle mouse button** locks and unlocks.
- On lock: the target is boxed, the lead solution is computed and the sight is driven onto the target.
- While locked, the player's own aim input is cut off, so the station does not fight the tracker.
- **Sight HUD**: `LRF <metres>`, `AMMO <rounds left>`, `ARMED`, `Firing Enabled` / `Firing Blocked`,
  `TRACK` / `TRACK LEAD`, a zoom label, and an azimuth ring in the lower left of the frame.

---

# Mk 19 grenade launcher

Two keys in `Bin/UserData/MelonPreferences.cfg`, section `[M1A1-CROWSFIX]`:

```toml
"CROWS/MK19 Instead Of M2" = true   # swap the station's .50 cal for an Mk 19
"CROWS/MK19 Ammo" = "M430"          # M430 | M430A1
```

Close the game before editing the file — MelonLoader rewrites it on exit — then reload the mission.
Only the CROWS weapon is swapped; the coaxial machine gun is untouched.

| | M430 HEDP | M430A1 HEDP |
|---|---|---|
| Caliber | 40 × 53 mm | 40 × 53 mm |
| Projectile mass | 340 g | 340–350 g |
| Muzzle velocity | 241 m/s | 241 m/s |
| HEAT penetration (RHA) | 51 mm | 76 mm |
| Explosive filler | 32 g Composition A5 | 39 g Composition A5 |
| Effective fragments | ~110 | ~150 |
| Fuze arming distance | 25 m | 25 m |
| Maximum range | 2,200 m | 2,200 m |

350 rounds/min cyclic, a 96-round belt plus 2 spare belts (192 rounds), 10 s reload, no reload sound.
The fire-control table is rebuilt for the grenade (40°, 30 s of flight) so the computer can actually
reach 2,200 m, and the barrel superelevates for range while the sight stays on the target.

# CROWS and coaxial .50 cal ammunition

The station and the optional M2HB coax **share one belt selection**, set by `CROWS/M2 Ammo`:

| Config value | Belt |
|---|---|
| `M8 API` | 4 × M8 API + 1 × M20 API-T (vanilla) |
| `Mk 211 Mod 0` | 4 × Mk 211 HEIAP + 1 × Mk 30 HEIAP-T — keeps the high-explosive incendiary effect |
| `M903 SLAP` | 4 × **M903 SLAP** + 1 × **M962 SLAP-T** |

`M962` is the tracer of the M903 belt and is not offered separately.

`M2 Coax = true` swaps the M240 coax for an M2HB.

---
## The new SLAP rounds
- Muzzle velocity **1,219 m/s** for both;

  | Range | Penetration (mm RHA) |
  |---|---|
  | 500 m | 34.0 |
  | 1,200 m | 22.62 |
  | 1,500 m | 19.0 |

---
# M829A4 APFSDS-T

A new 120 mm APFSDS-T round, added to the sabot-round selection alongside M827 / M829 / M829A1 / M829A2 / M829A3.

| | M829A3 (existing) | **M829A4 (new)** |
|---|---|---|
| Muzzle velocity | 1,555 m/s | **1,565 m/s** |
| Drag mass | 5.2 kg | **5.8 kg** |
| Sectional area | 0.00086 | **0.0009** |
| Drag coefficient | 0.22 | **0.20** |
| `RhaPenetration` | 690 | **735** |
| Heavy-ERA ratio | 0.25 | **0.125** |
| Spall multiplier / max / min | 1.2 / 22 / 7 | **1.25 / 24 / 8** |
| Penetration at 2,000 m | 579 mm RHAe | **633 mm RHAe** |

---

# Requirements and building

## Installing a release

- Gunner, HEAT, PC! (20261014) with [MelonLoader](https://github.com/LavaGang/MelonLoader/) installed.
- Unpack the archive into `Bin/Mods`, so that it looks like this:

```
Bin/Mods/
├── M1A1Abrams-CROWSFIX.dll
├── VanillaAssetUtil.dll
└── m1a1crowsfix_assets/
    ├── crows
    ├── crowmk19
    ├── mk19_sfx
    ├── citv
    ├── citv_monitor
    ├── du_mats
    ├── m256
    └── PresetBundles/
```

The mod **hard-depends on `VanillaAssetUtil.dll`** — it is not bundled with MelonLoader or the game, so
copy it next to the mod DLL. If a bundle is missing the mod logs the error and falls back to the vanilla
vehicle instead of crashing.

## Building from source

Targets .NET Framework 4.7.2. References come from `Bin/GHPC_Data/Managed`, `Bin/MelonLoader/net35` and
`Bin/Mods/VanillaAssetUtil.dll`; the assembly is compiled with `csc` from the .NET SDK against
`M1A1Abrams.csproj`.

---
