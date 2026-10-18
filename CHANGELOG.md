# Changelog

All notable changes to the **M1A1 Abrams (CROWS fix)** mod.

## [1.4.2] — 2026-10-18

### Added
- Mk 19 Mod 3 grenade launcher option for the CROWS: `CROWS/MK19 Instead Of M2` swaps the station's .50 cal, `CROWS/MK19 Ammo` picks M430 or M430A1. The coaxial machine gun is unaffected.
- Mk 19 support: rebuilt fire-control table (40°, 30 s of flight), shaped-charge penetration, barrel superelevation with a sight that stays on the target, 96-round belt with two spare belts and a 10 s reload.
- README: configuration walkthrough and Mk 19 ammunition data.

### Changed
- Utility helpers now come from the shared `VanillaAssetUtil.dll` instead of a private copy, and dead code was removed.

### 新增
- CROWS 可装 Mk 19 Mod 3 榴弹发射器：`CROWS/MK19 Instead Of M2` 换掉武器站的 .50，`CROWS/MK19 Ammo` 选 M430 或 M430A1。同轴机枪不受影响。
- Mk 19 支持：重建火控射表（40°、30 秒飞行）、破甲弹种、枪管按射程抬升而瞄具保持在目标上、96 发弹链 + 2 条备用弹链、换弹 10 秒。
- README 增加配置项使用说明与 Mk 19 弹药数据。

### 变更
- 工具类改用共享的 `VanillaAssetUtil.dll`，不再自带一份；清理死代码。

## [1.4.1] — 2026-10-18

### Changed
- Renamed the asset folder from `m1a1assets` to `m1a1crowsfix_assets` so it no longer collides with the vanilla M1A1 Abrams mod.
- Preset bundle paths saved by older versions are migrated automatically.

### 变更
- 资源文件夹由 `m1a1assets` 改名为 `m1a1crowsfix_assets`，不再与原版 M1A1 Abrams mod 冲突。
- 旧版本保存的预设包路径会自动迁移。

## [1.4.0] — 2026-10-18

### Fixed
- Fixes the M1 designation conversion.

### 修复
- 修复 M1 的型号转换问题。

## [1.3.6] — 2026-10-14

### Fixed
- Fixes CITV.
- Fixes CROWS.

### 修复
- 修复 CITV。
- 修复 CROWS。

## [1.3.5] — 2026-10-14

### Changed
- Compatible with 20261014.

### Fixed
- Fixes bugs related to CROWS.
