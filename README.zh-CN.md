[English](README.md) | [简体中文](README.zh-CN.md)

---

# 素材出处
M153 CROWS 模型来自："MRAP Oshkosh (West)"（https://marketplace.vigilante.us/product/mrap-oshkosh-west/），作者 Vigilante

CITV 与 M256 模型来自："WIP - Abrams M1A2 TUSK 2"（https://skfb.ly/VUwX），作者 DavidFalke

Mk 19 榴弹发射器与 CROWS 武器站模型来自："M1126 Stryker (Mk. 19)"（https://sketchfab.com/3d-models/m1126-stryker-mk-19-34414f9f33894b7db547546c962147ff），作者 42manako，许可 CC BY-NC 4.0

---

# 致原作者

**本仓库是 [thebeninator/M1A1Abrams](https://github.com/thebeninator/M1A1Abrams) 的 Fork。**


同时感谢 **Vigilante** 提供 M153 CROWS 模型、**DavidFalke** 提供 CITV 与 M256 模型。

> 上游 `master` 仍然是这个 mod 的权威版本。如果你要的是不含 CROWS 的原版 M1A1 Abrams mod，
> 请使用 [thebeninator 的仓库](https://github.com/thebeninator/M1A1Abrams)。

# M1A1 Abrams
Gunner, HEAT, PC! 的 mod，需要 [MelonLoader](https://github.com/LavaGang/MelonLoader/) 以及共享的
`VanillaAssetUtil.dll`。

适配 20261014。

修复 CITV。

修复 CROWS。

玩法/安装教程：https://www.youtube.com/watch?v=e_qNIyBb2SA

如果通过 Steam 启动报错，请直接运行游戏主程序。

![Screenshot_9](https://github.com/thebeninator/M1A1Abrams/assets/89621837/0fcbec7f-dc15-4ef2-850e-8e48c5783260)
![image](https://github.com/thebeninator/M1A1Abrams/assets/89621837/c80ef9c8-7e42-485c-957e-6cba0dd20c0c)

---

带预设（Preset）系统，可自定义随机化

<img width="1146" height="611" alt="how_the_preset_system_works" src="https://github.com/user-attachments/assets/a1db3167-fe3e-4bc4-a45f-47e382da0fbd" />

---

<p>
把所有 M1IP 转换为 M1A1，装备：
	<ul>
	<li>120mm M256 火炮</li>
	<li>M829 APFSDS-T（22 发）</li>
	<li>M830 HEAT-MP-T（18 发，带弹头引信）</li>
	</ul>
</p>

# 额外功能 / 设置（在 mod 配置里）
- 可选穿甲弹：M827、M829、M829A1、M829A2、M829A3、M829A4
  （M829A4：长杆贫铀弹，1,650 m/s，炮口 830 mm、2,000 m 715 mm，比本 mod 的 M829A3 分别高 5.7% 与
  8.5%，重型爆炸反应装甲对它的作用只有对 A3 的一半——见下文穿甲弹章节）
- 可选破甲弹：M830A1（带近炸引信，双击该弹种热键切换）
- 自定义每辆 M1A1/E1 携带多少发 M829 与 M830
- 以下选项默认关闭：
	- 把非 IP 的 M1 转换为 M1E1
	- 随机化 M1A1/E1 改装
	- M1A1/E1 提前量修正时的水平瞄准线稳定
	- 车长 CITV
	- M1A1 的贫铀炮塔侧装甲
	- M153 CROWS（由本 Fork 移植并修好，见下文）
	- CROWS 上装 Mk 19 榴弹发射器，发射 M430 / M430A1
	- 炮长热成像的不同设置

# 改装谱系

<p>M1A1：所有设置关闭</p>
<p>M1A1HA：贫铀装甲（gen = 1）</p>
<p>M1A1HC：贫铀装甲（gen &gt; 1）</p>
<p>M1A2：方位旋转、CITV、贫铀装甲（gen &gt;= 1）</p>
<p>M1A2 SEP：M1A2、FLIR 代次 = 3 或 CROWS</p>

---

# 本 Fork 增加了什么

| | 上游 `master` | 本 Fork |
|---|---|---|
| M153 CROWS II | 配置项声明了，但从不创建、从不使用，README 标着 *"CURRENTLY NOT FUNCTIONAL"* | 移植进来、装上车、端到端可用 |
| CROWS 自动跟踪 | — | 目标锁定、目标框、提前量解算、自动测距 |
| CROWS 瞄具 | — | 白天 + 热成像两套瞄具，1X/5X/10X/15X，瞄准镜内 UI，方位环 |
| CROWS 弹药 | — | 可选 M8 API / Mk 211 Mod 0 / **M903 SLAP + M962 SLAP-T** 弹链，或把整门武器换成 **Mk 19 40 mm 榴弹发射器**（M430 / M430A1） |
| 同轴机枪 | M240 | 可选 M2HB，与 CROWS 共用同一套弹链选择 |
| M829A4 APFSDS-T | — | **新增弹种**，穿深按本 mod 自己的 M829A3 折算 |

其余一切——预设系统、改装谱系、贫铀装甲、CITV、FLIR 代次、M830A1、原有穿甲弹家族——都是上游的成果，
行为与上游完全一致。

---

# M153 CROWS II

**M153 CROWS II**（Common Remotely Operated Weapon Station，通用遥控武器站）把车长那挺暴露在外的
.50 机枪换成遥控的 M2HB 武器站：车长在装甲下瞄准射击，由稳定式瞄具、激光测距仪和火控计算机完成弹道解算。

![M153 CROWS II 热成像瞄具，15X，已锁定目标](images/crows-thermal-sight.jpg)

## 怎么打开

在 `Bin/UserData/MelonPreferences.cfg` 的 `[M1A1-CROWSFIX]` 节：

```toml
"CROWS (M1E1)" = true              # M1E1（由非 IP 的 M1 改装而来）
"CROWS (M1A1)" = true              # M1A1 / M1IP
"Alternative Position" = false     # true = 武器站移到车长右侧
"CROWS/M2 Ammo" = "M8 API"         # M8 API | Mk 211 Mod 0 | M903 SLAP
"M2 Coax" = false                  # true = 同轴换成 M2HB，共用同一套弹链
"CROWS/MK19 Instead Of M2" = false # true = 武器站换成 Mk 19 榴弹发射器
"CROWS/MK19 Ammo" = "M430"         # M430 | M430A1，仅装 Mk 19 时生效
```

资源包 `m1a1crowsfix_assets/crows` 与 `m1a1crowsfix_assets/crowmk19` 必须在游戏的 `Mods` 目录里，
另外还需要共享的 `VanillaAssetUtil.dll`。

别的构建留下的旧键名（`CROWS (M1E1) <DISABLED>`、`CROWS (M1A1) <DISABLED>`、`CROWS Alt Placement`、
`CROWS: Alternate Placement (Global)`、`Use Mk 211 Mod 0`、`CROWS Raufoss`、
`CROWS: Raufoss Ammo (Global)`、`M2 SLAP`）会直接从配置文件里读取并在首次运行时迁移，所以老配置不会失效。
启动时日志会打印一行当前生效值。

## 功能

**武器站本体**
- 尾部鞭状天线切换成直立双天线构型，避免与武器站穿模。只改生成的车辆实例，未解包的 prefab 保持原样。

**瞄具**
- 白天与热成像两套瞄具，各自是完整注册进游戏相机系统的相机槽。
- 倍率 **1X / 5X / 10X / 15X**，对应 30° / 6° / 3° / 2° 视场角。
- 准星按瞄具缩放，并使用武器站自己的瞄具颜色绘制。
- 热成像的抖动模糊与抖动量归零，与白天瞄具保持一致。

**伺服**
- 额定横转 **90°/s**、俯仰 **50°/s**，俯仰限位 **-15° / +60°**
- 枪管按射程抬升，瞄具保持在目标上，1 km 外画面不再朝天翻

**武器**
- 默认 M2HB .50，可选弹链（M8 API / Mk 211 Mod 0 / M903 SLAP）
- 也可把整门武器换成 **Mk 19 榴弹发射器**，见下一章

**自动跟踪（ATT）**
- **鼠标中键**锁定 / 解锁。
- 锁定后：目标被框住、提前量解算完成、瞄具被驱动到目标上。
- 锁定时切断玩家自己的瞄向输入，武器站不会跟跟踪器对着干。
- **瞄准镜内 UI**：`LRF <米数>`、`AMMO <余弹>`、`ARMED`、`Firing Enabled` / `Firing Blocked`、
  `TRACK` / `TRACK LEAD`、倍率标签，以及画面左下角的方位环。

---

# Mk 19 榴弹发射器

在 `Bin/UserData/MelonPreferences.cfg` 的 `[M1A1-CROWSFIX]` 节里有两个键：

```toml
"CROWS/MK19 Instead Of M2" = true   # 武器站的 .50 换成 Mk 19
"CROWS/MK19 Ammo" = "M430"          # M430 | M430A1
```

改之前先关掉游戏（MelonLoader 退出时会覆写该文件），改完重新载入任务。只换武器站那门枪，
同轴机枪不受影响。

| | M430 HEDP | M430A1 HEDP |
|---|---|---|
| 口径 | 40 × 53 mm | 40 × 53 mm |
| 弹重 | 340 g | 340–350 g |
| 初速 | 241 m/s | 241 m/s |
| 破甲厚度（RHA） | 51 mm | 76 mm |
| 装药 | 32 g A5 | 39 g A5 |
| 有效破片 | 约 110 枚 | 约 150 枚 |
| 引信解脱距离 | 25 m | 25 m |
| 最大射程 | 2,200 m | 2,200 m |

理论射速 350 发/分，96 发弹链 + 2 条备用弹链（共 192 发），换弹 10 秒、无换弹音效。
火控射表按榴弹重建（40°、30 秒飞行）才能打到 2,200 m；枪管按射程抬升，瞄具保持在目标上。

# CROWS 与同轴 .50 弹药

武器站和可选的 M2HB 同轴**共用同一套弹链选择**，由 `CROWS/M2 Ammo` 决定：

| 配置值 | 弹链 |
|---|---|
| `M8 API` | 4 发 M8 API + 1 发 M20 API-T（原版） |
| `Mk 211 Mod 0` | 4 发 Mk 211 HEIAP + 1 发 Mk 30 HEIAP-T，保留高爆燃烧效果 |
| `M903 SLAP` | 4 发 **M903 SLAP** + 1 发 **M962 SLAP-T** |

`M962` 是 M903 弹链里的曳光弹，不单独提供选项。

`M2 Coax = true` 把 M240 同轴换成 M2HB

---
## 新增的 SLAP 弹种
- 初速均为 **1,219 m/s**；

  | 距离 | 穿深（mm RHA） |
  |---|---|
  | 500 m | 34.0 |
  | 1,200 m | 22.62 |
  | 1,500 m | 19.0 |

---
# 120 mm 穿甲弹

穿甲弹由 `AP Round (M1E1)` / `AP Round (M1A1)` 选择。下面每一发都是本 mod 按各自公开的 2,000 m 指标
标定的，整组数据互相自洽。

| | M829 | M829A1 | M829A2 | M829A3 | **M829A4** |
|---|---|---|---|---|---|
| 初速 | 1,670 m/s | 1,575 m/s | 1,680 m/s | 1,555 m/s | **1,650 m/s** |
| 阻力质量 | 4.27 kg | 4.6 kg | 4.6 kg | 5.2 kg | **5.8 kg** |
| 截面积 | 0.0009 | 0.00082 | 0.00082 | 0.00086 | **0.0009** |
| 阻力系数 | 0.20 | 0.20 | 0.20 | 0.22 | **0.20** |
| `RhaPenetration`（炮口） | 600 | 665 | 700 | 785 | **830** |
| 2,000 m 穿深 | 490 mm | 560 mm | 590 mm | 659 mm | **715 mm** |
| 重型 ERA 系数 | 0.95 | 0.85 | 0.80 | 0.25 | **0.125** |
| 破片倍率 / 上限 / 下限 | 1.15 | 1.15 / 22 / 5 | 1.15 / 22 / 5 | 1.2 / 22 / 7 | **1.25 / 24 / 8** |

`RhaPenetration` 是炮口值，游戏按速度比的平方缩放（`CurrentPenRating`），所以 2,000 m 那一列是各自
阻力模型算出来的，不是手填的。重型 ERA 系数会先乘到 Kontakt-5 的 200 mm、Relikt 的 250 mm 上再判定：
没有前置弹头的 M829 是 0.95，M829A4 的前置弹头只按 0.125 算。

## M829A4 APFSDS-T

这一组里最新的一发，也是面对后期 Pact 重型爆炸反应装甲时缺得最明显的那一发：比 A3 更长的贫铀弹芯
（约 1,020 mm）、更重的阻力质量、更高的初速，以及专门对付重型 ERA 的前置弹头。

- 炮口 **830 mm**、2,000 m **715 mm**——比 M829A3（785 / 659 mm）分别高 **5.7%** 与 **8.5%**。
- 多出来的穿深来自更长更重的弹芯，而不是单纯堆初速：出膛 1,650 m/s，到 2,000 m 仍保有 0.9282，
  略优于 A3 的 0.9164。
- 对付重型 ERA 时，A4 的前置弹头只付 A3 四分之一的代价：对 Kontakt-5 是 830 − 25 = 805 mm，
  2,000 m 处是 715 − 25 = 690 mm。

---

# 环境要求与构建

## 安装发行版

- Gunner, HEAT, PC!（20261014），已安装 [MelonLoader](https://github.com/LavaGang/MelonLoader/)。
- 把压缩包解到 `Bin/Mods`，目录结构如下：

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

本 mod **硬依赖 `VanillaAssetUtil.dll`**——它既不在 MelonLoader 里也不在游戏本体里，必须和 mod DLL
放在一起。缺资源包时 mod 会在日志里报错并退回原版车辆，不会崩游戏。

## 从源码构建

目标框架 .NET Framework 4.7.2，引用来自 `Bin/GHPC_Data/Managed`、`Bin/MelonLoader/net35` 与
`Bin/Mods/VanillaAssetUtil.dll`，用 .NET SDK 的 `csc` 按 `M1A1Abrams.csproj` 编译。

---
