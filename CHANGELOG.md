# Changelog

All notable changes to the **M1A1 Abrams (CROWS fix)** mod.

## [1.4.3] — 2026-10-19

### Fixed
- The CROWS no longer needs any vanilla donor vehicle. The station used to borrow its .50 cal, sight and fire-control computer from the M60A3 TTS, its reticle from the T-55A and its sight frame from the M2 Bradley; it now clones the Abrams' own cupola parts, builds its reticle in code and draws its sight frame itself.
- The station could fail to appear with nothing in the log. The conversion handler can be dropped by the game state machine; a fallback watcher now builds the station once the mission starts.
- The cupola search looked in the wrong branch: the .50 cal and its sight live under `CUPOLA/CUPOLA_GUN`, only the fire-control computer lives under `Cupola Scripts`.
- The vanilla commander's .50 cal is now hidden on every `CUPOLA_GUN` node (mesh and collider copies), not just the first one found.
- Debug logging removed; `CROWS.cs` split into partial classes (Model / Cupola / Sight / Reticle / Mount) and the DLL is smaller.

### 修复
- CROWS 不再依赖任何原版捐赠车：以前从 M60A3 TTS 借 .50 机枪/瞄具/火控计算机、从 T-55A 借分划、从 M2 Bradley 借瞄具边框，现在改为克隆艾布拉姆斯本车车长塔部件、代码内生成分划与边框。
- 修复 CROWS 有时不出现且日志无任何报错：转换处理器可能被游戏状态机丢弃，现在由兜底 watcher 在任务开始时装配。
- 修复车长塔搜索路径错误：.50 机枪与其瞄具在 `CUPOLA/CUPOLA_GUN` 下，只有火控计算机在 `Cupola Scripts` 下。
- 原版车长 .50 现在会在所有 `CUPOLA_GUN` 节点上隐藏（模型网格与碰撞体副本），不再只处理第一个。
- 移除调试日志；`CROWS.cs` 按职责拆分为 partial 类（Model / Cupola / Sight / Reticle / Mount），DLL 体积减小。

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
