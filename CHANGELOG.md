# Changelog

All notable changes to the **M1A1 Abrams (CROWS fix)** mod.

## [1.4.7] — 2026-11-02

### Fixed
- The Mk 19 station no longer renders as flat pale grey. Its materials are rebuilt on the game's
  own Standard shader with the albedo, normal and packed metal/gloss maps re-bound by hand; the
  bundle's own compiled copy of Standard never sampled them. The donor .50 cal station keeps its
  bundled materials untouched - rebuilding those drops their `_MetallicGlossMap` and turns the
  station dark and plasticky.
- The asset bundle is kept alive for the session. Unloading it before the model was instantiated
  left live materials pointing at released textures, which rendered as bare white surfaces under
  lighting.
- Switching "CROWS/MK19 Instead Of M2" now reloads the station model instead of mounting the old
  shell with the new weapon's sight and ammunition.
- The CROWS seat height is four independent absolute constants - Mk 19 hatch 1.2955 / alt 1.15,
  .50 cal hatch 0.9855 / alt 0.84 - so tuning one weapon can no longer move the other.
- The commander's camera slot is written absolutely in both placements, with the vanilla offset
  captured once per vehicle, so the two placements cannot leak into each other.

### 修复
- MK19 武器站不再整体发灰发白：其材质改用游戏自带的 Standard 着色器重建，并手动重新绑定反照率、
  法线与打包的金属/光泽贴图（包内自带的 Standard 副本不采样这些贴图）。捐赠的 .50 cal 武器站保持
  包内材质不动——重建会丢掉它的 `_MetallicGlossMap`，反而使台站发暗发假。
- 资源包在整局会话内常驻：此前在模型实例化之前卸载资源包，会让仍在使用的材质指向已释放的贴图，
  在光照下变成纯白色表面。
- 切换「CROWS/MK19 Instead Of M2」后会重新加载武器站模型，而不再用旧外壳配上新武器的瞄具与弹药。
- CROWS 座位高度改为四个互相独立的绝对常量——MK19 前座 1.2955 / 侧座 1.15，.50 cal 前座 0.9855 /
  侧座 0.84——调整其中一个不会带动另一个。
- 指挥官摄像机位在两种摆放方式下都按绝对值写入，原版偏移按车记录一次，两种摆放不会互相污染。

## [1.4.6] — 2026-10-25

### Changed
- Replaced the CROWS and M2 models, with the textures rebuilt at 2048.

### Fixed
- The CROWS body and the M2 wooden grips no longer render washed-out grey.
- M1 → M1IP conversions keep the M1E1 settings, so the M1A1 and M1E1 configuration sets stay independent.
- Corrected the front CROWS seat height, separately for the M2 and the MK 19.

### 变更
- 更换 CROWS 与 M2 的模型，贴图重做为 2048。

### 修复
- 车身与 M2 木柄不再发灰发白。
- M1 → M1IP 转换后保留 M1E1 设置，M1A1 与 M1E1 两套配置互相独立。
- 修正正前方 CROWS 座位高度，M2 与 MK19 各自独立。

## [1.4.5] — 2026-10-22

### Fixed
- The CROWS gun station could end up permanently stabilised: with the stabiliser switched off the fire control still pushed an absolute world aim to the mount every frame, so the station held its world bearing instead of riding the turret - and once the sight had been opened the stabiliser could no longer be switched off at all. The station's stabiliser is now locked on: `FireControlSystem.SetStabsActive` is intercepted for this fire control only, so a switch-off request is refused before it reaches the mounts, with a per-frame fallback that re-arms it if anything else clears it. The main gun's stabiliser is untouched.

### 修复
- CROWS 武器站可能变成永久稳定：关闭稳定器后火控仍每帧把绝对世界方向喂给炮座，炮座因此保持世界方位而不是跟着炮塔走；开镜之后甚至再也关不掉稳定器。现在本站稳定器强制常开：只对本站火控拦截 `FireControlSystem.SetStabsActive`，关闭请求在到达炮座前就被拒绝，另有每帧兜底重新打开。主炮稳定器行为不变。

## [1.4.4] — 2026-10-19

### Fixed
- Compatibility with **Fulda 1989**: the mod released every vanilla asset handle shared through `VanillaAssetUtil` when a menu scene loaded, which broke Fulda's Leopard 2A4 spawn (the mission hung on the loading screen). That release call is gone.
- MPAT proximity fuse can only be armed on the main gun with M830A1 loaded. It used to be switchable with any round, and even from the coaxial machine gun.
- Main gun thermal sight: the reticle no longer enlarges for a single frame when switching between the main gun and the coaxial gun or changing magnification, and no longer grows slightly while the digital zoom settles from blurred to sharp.
- Mk 19: fire-control table now uses the world's air density and ammunition temperature, removing the ~23 m shortfall at 1500 m; depression limit lowered to -30°; drag coefficient corrected to 0.100.

### 修复
- 修复与 **Fulda 1989** 的兼容性问题：本 mod 在菜单场景会释放 `VanillaAssetUtil` 共享的原版资源句柄，导致 Fulda 的豹 2A4 无法生成、任务卡在加载界面。已移除该释放调用。
- MPAT 近炸只在主炮且装填 M830A1 时可切换；此前任意弹种都能切，同轴机枪也能切。
- 主炮热像：主炮与同轴切换、切换倍率时不再出现单帧放大，数字放大从模糊转清晰时也不再轻微变大。
- Mk 19：射表改用世界空气密度与弹药温度，修复 1500 m 约 23 m 的落点偏近；俯角下限放宽到 -30°；阻力系数修正为 0.100。

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
