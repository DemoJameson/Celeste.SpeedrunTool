# SpeedrunTool savestate 基准结果（SJ Grandmaster Heartside）

> 本文是 "降低 savestate 内存 / 提升 save/load 速度 / 避免卡顿" 优化的**基准测量报告**。
> 环境搭建方法见 [bench-setup.md](bench-setup.md)，原始数据在 benchmark 工作区
> `D:\DSH\Celeste\work\SpeedrunTool\bench\results\`（该目录不在本仓库内）。
>
> 测量机器 **不是** 8GB 目标机（详见 §2），因此绝对值只用于同机横向对比；
> 报告重点是可迁移的相对指标（耗时 p50/p99、GC 次数、分配量、managed 内存增量）。
>
> **环境版本**：§1–§8 的数字是"mod 文件夹安装"环境（2026-11 早些时候）。
> 自 2026-11-09 起基准环境改为 **zip-mod 标准环境**（52 个下载 mod 全部为 `.zip`，
> 见 §10），§10 是 zip 环境 + 重房间 `a1_18-Xplosives` 的最终数据，**优先使用 §10**。

---

## 1. 结论摘要（TL;DR）

场景：**Strawberry Jam 1.0.13 — Grandmaster Heartside**，起点房间 `a0-Start`
（`StrawberryJam2021/5-Grandmaster/ZZ-HeartSide`，areaId 141，222 entities，bounds 464x1016）。
每次运行 = 3 次热身 + 20 次 save/load 循环（TAS 变体，不 freeze/wipe）+ 1/2/3 存档槽内存 +
3 次普通 save/load（含 freeze/wipe），vsync 60fps。

| 指标（中位数，2 次重复） | upstream baseline | 优化后 PreClone **On** | 优化后 PreClone **Off** |
|---|---|---|---|
| save p50 | 17.47 ms | **13.10 ms**（−25%） | **12.41 ms**（−29%） |
| load p50 | 15.00 ms | **11.28 ms**（−25%） | 14.72 ms（−2%） |
| load 帧 max（含 load 那一帧） | 89.84 ms | **55.10 ms**（−39%） | 65.44 ms（−27%） |
| 存档 1 槽 managed 增量 | 36.1 MB | **20.5 MB**（−43%） | **13.0 MB**（−64%） |
| 存档 3 槽 managed 增量 | 65.35 MB | **50.3 MB**（−23%） | **28.4 MB**（−57%） |
| load 后常驻 managed（均值） | 1231.2 MB | 1199.6 MB | **1028.8 MB**（−16.5%） |
| GC 次数（20 次 save / 20 次 load） | 3.5 / 2 | ~2.5 / ~1.5 | 3 / 2.5 |

**核心结论**

1. 优化后 save/load 的 p50 都降到 ~12–13 ms（upstream 为 15–17.5 ms），load 尖峰帧从
   ~90 ms 降到 **55–65 ms**，玩家可感知的卡顿明显减小。
2. **PreClone 是明确的"内存 ↔ 速度"权衡开关**：
   - **On**：load 快 3.4 ms（11.28 vs 14.72 ms）、load 尖峰低 10 ms，但 3 槽常驻内存多 ~22 MB、
     load 后常驻 managed 多 ~171 MB、每次 save/load 多一个 ~9 MB 分配的后台 preclone 任务；
   - **Off**：常驻内存最低（3 槽 28.4 MB，比 upstream 少 57%），代价是 load 的 `cloneBack`
     从 ~0.5 ms 涨到 ~4.4 ms（+~1 ms `restoreSaveData`），load p50 与 upstream 相当。
   - 8GB 目标机建议默认 **Off / Auto**；高频 SL（TAS 类）用户建议 **On**。
3. load 后 1 秒窗口内：load 那一帧后 **通常只有 1–2 帧 >16.7ms 的二次尖峰**
   （首个渲染恢复帧，p99 ~22–65 ms），其余帧稳定 16.67 ms，**没有持续掉帧**。
4. 普通模式（含 freeze + wipe 的体感）主要由 wipe 动画决定：save/load 的 call 时间
   10–15 ms，但 **visible 时间 ~515–552 ms**（wipe 动画分帧播放，期间最大帧 17–30 ms），
   upstream 与优化后在这个维度差别不大——优化影响的是"action 本身的卡顿"，wipe 是设计行为。
5. GC：优化后每 20 次 save/load 只触发 2–4 次 Gen0/Gen1 GC、0 次 Gen2（upstream 类似次数，
   但见 §4.4 分配量；优化主要消除的是 LOH churn，GC 次数本身不是差异点）。

---

## 2. 环境与方法

| 项 | 值 |
|---|---|
| 游戏 | Celeste 1.4.0.0-fna + Everest 1.6662.0 stable，.NET 8.0.33，FNA/D3D11 |
| 显示 | 1280x720 窗口，**vsync 开**（正常帧 ≈ 16.67 ms） |
| 机器 | 16 logical CPU / 64 GB RAM / RTX 5070 Ti（**非** 8GB 目标机） |
| 场景 | SJ GM HeartSide `a0-Start`（222 entities）；补充场景 SJ GM `maya` `Insert Coin`（193 entities） |
| 存档槽 | SRT `SaveStateTas(string slot)` / `LoadStateTas(string slot)`（不 freeze、不 wipe） |
| 设置 | `FreezeAfterLoadStateType=Off`、`GcAfterLoadState=false`、`GcAfterClearState=false`、`NoMessageAfterSaveLoad=true` |
| 迭代 | 3 warmup + 20 测量（TAS）；3 次 normal（freeze+wipe）；1/2/3 槽顺序测 |
| 测量位置 | `Engine.Update` hook，**每帧只做一次 save 或 load**，保证长帧可归因 |
| 重复 | upstream 2 次、final On 2 次、final Off 2 次（文中给中位数 + 范围）；opt-v1 3 次 |

**计时定义**（详见 [bench-setup.md](bench-setup.md) §8）：

- `save_ms` / `load_ms`：`SaveStateTas`/`LoadStateTas` 同步调用耗时；
- `load_frame_ms`：包含 load 调用的那个引擎帧的耗时（≈ 玩家感知的卡顿尖峰）；
- `post_load_window_after_load_frame`：load 后 1s 内、**去掉 load 帧本身**的帧统计
  （p50/p99/max、>16.7ms 帧数）——"load 之后有没有持续掉帧"的关键指标；
- `slots[i].delta_managed_bytes`：存档 1/2/3 槽、等待 1.2s + 强制 GC 后相对进图 baseline 的
  managed 内存增量（private 受 LOH/碎片噪声影响大，只作参考）。

**构建标识**（SRT AssemblyVersion 恒为 1.0.0.0，只能靠 md5/artifact 目录区分）：

| 构建 | 位置 | SpeedrunTool.dll md5 | 说明 |
|---|---|---|---|
| upstream | `artifacts\upstream-mod` | `0e2e700d9ded…` | master 源码 + 内嵌 DeepCloner，无任何 perf 改动；无 ModInterop，用内部方法反射驱动 |
| opt-v1 | `artifacts\opt-v1` | `f83caf44bf61…` | 中间优化构建（供参考） |
| **final** | `artifacts\final` | `04301bfb7123…` | 最终交付构建（含 ModInterop、snapshot 缩略图、处理器缓存/去锁、PreClone Auto、profiling） |
| debug | `artifacts\debug-mod` | `0408312aa026…` | 带 `InGame_Profiling` 的 debug 构建（本次未用于最终数字） |

> 交付状态：`Mods\SpeedrunTool` 已部署 **final**（`04301bfb7123…`），未留 profiling flag。

---

## 3. 结果：upstream vs final

### 3.1 主表（SJ GM HeartSide `a0-Start`，222 entities）

| 指标 | upstream r1 / r2 | final On r1 / r2 | final Off r1 / r2 |
|---|---|---|---|
| save p50 (ms) | 17.56 / 17.38 | 13.09 / 13.10 | 12.34 / 12.48 |
| save p99 (ms) | 91.25 / 100.68 | 85.87 / 62.65 | 67.98 / 51.19 |
| load p50 (ms) | 16.25 / 13.74 | 11.14 / 11.42 | 14.02 / 15.41 |
| load p99 (ms) | 85.23 / 84.44 | 32.99 / 46.85 | 52.24 / 71.06 |
| load 帧 max (ms) | 90.09 / 89.59 | 58.42 / 51.77 | 55.19 / 75.68 |
| load 后窗口（去 load 帧）p99 (ms) | 25.27 / 30.47 | 38.92 / 47.00 | 64.77 / 22.60 |
| load 后窗口 >16.7ms 帧数（均值） | 2.65 / 3.75 | 2.80 / 2.10 | 2.10 / 2.20 |
| 存档槽 1/2/3 managed 增量 (MB) | 41.4/56.0/70.7、30.8/45.4/60.0 | 25.0/40.2/54.8、15.9/31.2/45.8 | 8.7/16.7/24.1、17.2/25.3/32.7 |
| baseline managed（进图/GC 后, MB） | 783.7 / 882.1 | 875.5 / 737.1 | 737.1 / 728.6 |
| load 后 managed 均值 (MB) | 1182.2 / 1280.3 | 1268.9 / 1130.2 | 1033.4 / 1024.1 |
| GC（20 save / 20 load） | 3.5/3.5/0 · 2/2/0 | 3/3/0 · 2/2/0；2/2/0 · 1/1/0 | 3/3/0 · 2/2/0；3/3/0 · 3/3/0 |
| 普通模式 save: call / visible (ms) | 13.62–15.23 / 542–547 | 10.85–11.50 / 524–552 | 10.65–10.87 / 524–552 |
| 普通模式 load: call / visible (ms) | 10.82–11.35 / 518–519 | 9.36–10.25 / 519–520 | 14.19–15.48 / 514–516 |
| 普通模式窗口最大帧 (ms) | 17–39 | 17–29 | 17–20 |

（`baseline managed` 在不同 run 间会随 GC 状态浮动 ±100 MB，是测量噪声，不宜直接比较；
比较用 `slots` 的增量。）

### 3.2 变化幅度（中位数对中位数）

| 指标 | upstream → final On | upstream → final Off |
|---|---|---|
| save p50 | −25.0% | −29.0% |
| load p50 | −24.8% | −1.9% |
| load 帧 max | −38.7% | −27.1% |
| 1 槽 managed 增量 | −43.2% | −64.1% |
| 3 槽 managed 增量 | −23.0% | −56.5% |
| load 后 managed 均值 | −2.6%（噪声内） | −16.5% |

> 说明：upstream 的 PreClone 无法关闭（没有 ModInterop 开关），所以 upstream 行对应
> "PreClone 常开"的实现；final On 是同口径对比，final Off 是 upstream 做不到的省内存模式。

### 3.3 中间构建 opt-v1（供参考）

| 指标（中位数） | opt-v1 On（3 次） | opt-v1 Off（2 次） |
|---|---|---|
| save p50 | 12.92 ms（12.49–12.99） | 12.01 ms（11.99–12.03） |
| load p50 | 11.62 ms（10.82–11.93） | 14.92 ms（14.80–15.03） |
| load 帧 max | 64.24 ms（30.24–68.41） | 64.78 ms（62.94–66.61） |
| 1/2/3 槽 managed 增量 | 15.2 / 30.4 / 44.9 MB | 13.2 / 21.3 / 28.7 MB |
| load 后 managed 均值 | 1270.0 MB | 1102.2 MB |

final 与 opt-v1 的差异在测量噪声范围内（final 的槽位增量略高 ~5 MB、p99 抖动更大，
主要来自测量方差；最终数字以 final 为准）。

### 3.4 场景规模对比（final/opt-v1，On）

| 场景 | entities | save p50 | load p50 | load 帧 max | 1/2/3 槽 managed |
|---|---|---|---|---|---|
| GM HeartSide `a0-Start` | 222 | 12.49–13.10 ms | 10.82–11.42 ms | 30.2–58.4 ms | 15.2–25.0 / 30.3–40.2 / 44.9–54.8 MB |
| GM `maya` `Insert Coin` | 193 | 7.58 ms | 8.92 ms | 16.73 ms | 3.9 / 9.7 / 14.8 MB |

HeartSide 房间实体更多、SJ 依赖更多，耗时/内存都显著更高；优化收益在大场景上更明显
（load 帧尖峰 16.7 → 30–58 ms 仍远低于 upstream 的 ~90 ms）。

---

## 4. 分项分析

### 4.1 PreClone 的"内存 ↔ 速度"权衡（final，profiling 数据）

用 `SRT_PROFILE=1` 采集 SRT 各阶段耗时（需要 `-p:SrtProfiling=true` 的 profiling 构建；交付的
Release 构建里 profiler 已被 `[Conditional]` 编译掉，零运行时开销，见
`results/PROFILE-profiling-final-on.txt` /
`-off.txt`，每次 save/load 一行）：

**PreClone On（稳态，非首次）**
```
save initSlots=0.00 clearOldState=0.05 beforeSave=0.01 cloneLevel=6.71  cloneSaveData=1.72
     saveActions=2.16  precloneStart=0.01   heapMB=883.8
load beforeLoad=0.00 waitPreclone=0.00 mergeTime=0.00 unload=2.97 cloneBack=0.56
     restoreSaveData=0.00 restoreAudio=0.01 loadActions=4.83 precloneStart=0.01 heapMB=883.8
（后台）preclone ms=4.9 allocMB=9.0   ← 每次 save/load 后 async preclone 任务
```

**PreClone Off（稳态）**
```
save cloneLevel=6.79 saveActions=2.08 (无 precloneStart)
load beforeLoad=0.00 waitPreclone=0.00 mergeTime=0.00 unload=2.74
     cloneBack=4.36 restoreSaveData=1.14 restoreAudio=0.01 loadActions=4.53
     heapMB=826.6（比 On 低 ~55 MB）
（无 preclone 后台行）
```

结论：
- **On 的收益**：load 的 `cloneBack` 从 ~4.4 ms 降到 **~0.5 ms**（快 8–9 倍），
  `restoreSaveData` 从 ~1.1 ms 降到 0；save 的 `cloneLevel` 两模式相同（~6.8 ms），
  PreClone 只在 load 时兑现。
- **On 的代价**：每次 save/load 触发后台 preclone，**~4.4–5.9 ms / 9 MB 分配**
  （会提高 GC 压力）；load 后 heap 约高 55 MB，3 槽常驻多 ~22 MB。
- 首次 save 的冷启动开销明显（`cloneLevel≈215 ms`、`saveActions≈142–158 ms`、
  首次 load `loadActions≈60 ms`、首次 `cloneBack≈4.5–24.5 ms`），这是一次性初始化，
  报告均用稳态中位数；warmup 里已经包含首次 save/load。

### 4.2 帧时间 / 卡顿

- TAS 模式：`all_frames.p50 = 16.67 ms`（60fps 满帧）；load 帧尖峰 30–90 ms 取决于构建，
  **load 后 1s 窗口内只有 1–2 帧 >16.7ms（首个渲染恢复帧），之后稳定 16.67 ms**。
  即：优化解决的是"load 瞬间"的卡顿，不是持续性掉帧。
- 普通模式（含 freeze + wipe）：wipe 动画把卡顿摊到多帧，`visible` 时间 ~515–552 ms，
  期间最大帧仅 17–39 ms；upstream/final 差异不大（wipe 是设计行为，不受优化影响）。
- 8GB 目标机关注点：load 帧尖峰（55–90 ms）+ preclone 后台分配（9 MB/次）叠加在
  内存压力下可能更明显，建议在目标机用 PreClone Off 复测。

### 4.3 内存

- **每槽 managed 增量**：final On ~20 MB/槽（3 槽 50 MB），Off ~13 MB/槽（3 槽 28 MB）；
  upstream 为 ~36 MB 首槽、~65 MB 三槽。这是"降低 savestate 内存"最直接的指标。
- private/WorkingSet 增量受 LOH、未压缩内存、运行时策略影响，重复间可差 100–500 MB，
  只作参考（JSON 里保留原始数据）。
- load 后常驻 managed：upstream 1231 MB → final Off 1029 MB（−16.5%），
  final On 1200 MB；绝对值的基线取决于 SJ + 60 个 mod 的运行时状态。

### 4.4 GC / 分配

- 20 次 save/load 期间 GC 次数：Gen0/Gen1 约 2–4 次、**Gen2 = 0**，upstream 与 final 接近。
  说明优化不是靠"减少 GC 次数"取胜，而是靠减少每次 save/load 的分配/拷贝量
  （cloneLevel/saveActions/loadActions 的绝对耗时为证）。
- 每次 save 的分段耗时（稳态）：`cloneLevel≈6.8 ms` + `saveActions≈2.1–2.7 ms`；
  load：`unload≈2.7–4.9 ms` + `loadActions≈4.5–5.6 ms` + `cloneBack`（0.5 或 4.4 ms）。
- 首次 save 的分段耗时异常高（一次性的缓存/处理器初始化），属于预期。

---

## 5. 已知测量噪声与注意事项

1. **槽位内存 ±10 MB 噪声**：managed 增量重复间可差 ~10 MB（GC/LOH 状态、SJ 惰性加载的
   纹理缓存），报告用 2–3 次中位数 + 范围。
2. **baseline managed 波动**：进图后 GC 后 managed 在 730–880 MB 之间浮动，不影响槽位增量
   和耗时对比。
3. **load p99 抖动大**（final On 33–47 ms、final Off 52–71 ms）：p99 只有 20 个样本，受
   单次 GC/纹理流影响；决策看 p50 + load 帧 max 更稳。
4. **vsync 60fps**：所有帧指标以 16.67 ms 为基准；如果目标机不是 60fps/有掉帧，
   `load 帧 max` 与 `>16.7ms 帧数` 仍有相对意义，但绝对值会变。
5. **8GB 目标机**：本机 64 GB，private/WS 绝对值不可外推；可迁移的是
   save/load 耗时、load 帧尖峰、managed 槽位增量、分配量。
6. **benchmark 环境改动**（不影响 SRT 本体，但报告中需注明）：
   - `Mods\FrostHelper\everest.yaml` 删除了 `OptionalDependencies`（否则 Everest
     delayed-retry 缺陷导致 FrostHelper + SJ 不加载，详见 bench-setup §4）；
   - harness 进图时 `session.StartedFromBeginning=false` 跳过 collab postcard；
   - 测试场景固定为 SJ GM HeartSide `a0-Start`，玩家不做输入，save 前 1.5s settle。

---

## 6. 未覆盖 / 后续

- **snapshot 缩略图验证**（门/电梯/wipe 中存档，确认缩略图不含 wipe、非黑屏）：
  需要读取 SRT 的 snapshot 截图，当前 harness 未覆盖（TAS save 不触发 wipe；normal save
  会触发 wipe 但未校验截图内容）。
- **音频恢复**（FrostHelper `ToggleSwapBlock` / SJ `WonkyCassetteBlockController` 房间
  save→触发声音→load→再触发，检查 FMOD 错误）：未覆盖。
- **ModInterop 运行期注册/注销 SaveLoadAction** 稳定性：未单独测试（benchmark 只做
  save/load/clear），建议在 final 上补一次 `RegisterStaticTypes` → save/load/clear →
  `Unregister` 的 smoke。
- **60s 固定路线帧统计**（含 save/load 各 3 次，P50/P95/P99、>16.7ms/>33.3ms 帧数）：
  当前只有"load 后 1s 窗口"，没有长路线帧分布；harness 可加一个 idle/走图模式。
- **8GB 目标机复测**：建议把 `configs\final-preclone-*.json` 拷到目标机，跑 1–2 次。
- **更多 SJ 房间**：HeartSide 中段实体更多的房间、`maya` 其它房间，验证收益随规模的变化。

---

## 7. 复现与原始数据

```cmd
:: 环境（一次性）：见 docs\bench-setup.md §3–§5
:: 构建/部署 harness
cd D:\DSH\Celeste\work\SpeedrunTool\bench && build.cmd

:: 切到某个构建后跑 tag
python swap_srt.py final
run.cmd final-on-r1 420
run.cmd final-off-r1 420

:: 汇总
python collect_results.py        :: → results\SUMMARY.md
```

原始数据（benchmark 工作区 `D:\DSH\Celeste\work\SpeedrunTool\bench\results\`）：

| 文件 | 内容 |
|---|---|
| `baseline-upstream.json` | upstream 构建完整数据（每次迭代、normal、slots） |
| `final-on-r1.json` / `final-on-r2.json` | final PreClone On 两次重复 |
| `final-off-r1.json` / `final-off-r2.json` | final PreClone Off 两次重复 |
| `baseline-preclone-on.json` / `-off.json` | opt-v1 中间构建数据 |
| `baseline-preclone-on-maya.json` | `maya` 场景 |
| `baseline.json` | 任务口径的 upstream baseline（= `baseline-upstream.json` 副本） |
| `profiling-final-on.json` / `-off.json` + `PROFILE-profiling-final-*.txt` | `SRT_PROFILE` 分段耗时原始行 |
| `log-<tag>.txt` | 每次运行的游戏 log.txt |
| `SUMMARY.md` | 所有 tag 的机器汇总（`collect_results.py` 生成） |

---

## 8. 附：构建回归与 bisect 记录（2026-11，供排查参考）

在 final 之前，`opt-v2`（以及 bisect 的 `26a0af0`、`9d4f3cb`）在当前环境**一进图就无痕崩溃**：
`LevelEnter.Go` 后约 1s、LevelLoader 读图期间进程直接消失，无 managed 异常、
`errorLog.txt` 不更新、`wevtutil` 无事件——符合 StackOverflow / native FastFail 特征。
原版 1A 也复现，与 SJ 内容无关。

bisect 结论（同一 `smoke-vanilla` 配置：进原版 1A + 2 次 save/load）：

| 构建 | 结果 |
|---|---|
| `61ffd4f`（≈opt-v1 附近） | ok |
| `6dc8f52` | ok |
| `26a0af0`（SJ hook 兼容 + DeepCloner.ClearCaches 引入） | **crash（2/2）** |
| `9d4f3cb` | crash |
| `opt-v2` | crash |
| `variantA-no-compat`（保留 ClearCaches） | crash |
| `variantB-no-tracker`（保留 ClearCaches） | crash |
| `variantC-no-clearcache`（去掉 ClearCaches 调用） | **ok** |
| `variantD-clear-cloner-only`（只清 DeepClonerCache） | ok |
| `variantE-clear-knowntypes-only`（只清 KnownTypes） | **crash（2/2）** |

→ 罪魁是 **`DeepClonerSafeTypes.KnownTypes.Clear()`**；清 DeepClonerCache 本身安全。
final 已移除 `DeepCloner.ClearCaches()` 及所有调用（KnownTypes 永不清空），
`Add/RemoveReturnSameObject*` 改为"必须在首次 save/load 前注册"的文档约束
（FrostHelper 在 LoadContent 注册，满足），并保留安全的 ProcFlags 缓存清理。

## 补充：交付构建（profiler 编译掉）的复核

交付的 Release 构建不包含 profiling 代码（`[Conditional("SRT_PROFILE")]` 使所有调用点在编译期
被消除，Cecil 验证 IL 调用点 = 0）。用该构建重跑同一矩阵（tag `verify-noprof`，20 iters）：

| 指标 | 带 profiler 的 final（2 次中位数） | 交付构建 verify-noprof |
|---|---|---|
| save p50 | 13.10 ms | 12.40 ms |
| load p50 | 11.28 ms | 10.76 ms |
| load 帧 max | 55.10 ms | 48.13 ms |

差异在测量噪声范围内 → **去掉 profiler 没有性能回归**（理论上只会更省：没有调用点/分支）。


---

# 10. zip 标准环境 + 重房间（Grandmaster Heartside `a1_18-Xplosives`）

> 用户反馈：HeartSide 第一面 `a0-Start` 开销很小，**第二面 `a1_18-Xplosives` 才是实体多、旧机卡的地方**。
> 同时用户要求基准环境与真实玩家一致：**下载的 mod 用 zip 安装**（玩家就是这样装的），
> 只有用户自有/开发 mod 保持文件夹。本节所有数字都在该 zip 标准环境下测得。

## 10.1 zip 标准环境

- 转换脚本：`bench/convert_to_zip.py`（dry-run / execute / rollback；把原文件夹移到
  `D:\DSH\Celeste\backup\mods-folders\<timestamp>\`，不删除；blacklist 备份并只删除命中 `<mod>.zip` 的行）。
- 52 个下载 mod → `Mods/<原文件夹名>.zip`（根目录直接是 `everest.yaml`，deflate；排除 `.git/.vs/obj`；
  校验每个 zip 声明 DLL 存在）。原始 1272.3 MB → zip 1173.2 MB。
- 保持文件夹：SpeedrunTool、CeilingUltra、CeilingUltraGym、BodyDouble、CelesteTAS-EverestInterop、
  TAS-Helper、LozenTestMod、TestMap、ForILSpy、GhostModForTas、LeaveTheCastle、NiceToCU、SlashDash、SrtBench、Cache。
- blacklist.txt：备份后删除 **52 行**匹配 `<mod>.zip` 的条目（用户其它 182 行保留；现 234 行）。
- **验证**：连续两次启动，`Loaded 60 modules`（与文件夹环境一致），FrostHelper 1.81.1 +
  StrawberryJam2021 1.0.13 正常注册，`AreaData.Areas=143` / SJ 128 图，
  probe `target_resolved=true, areaId=141`。
- 启动耗时：第 1 次（zip relink 缓存重建）`DONE LOADING 11642ms`；第 2 次（暖）`11587ms`，
  run.cmd 总耗时 24s（文件夹环境约 24–26s，无明显差异）。

## 10.2 房间枚举（ZZ-HeartSide，33 个 LevelData）

| room | LevelData entities | bounds | 备注 |
|---|---|---|---|
| a0-Start | 10 | 464x1016 | 第一面（运行时 222 entities） |
| **a1_18-Xplosives** | **2461** | **6160x640** | **第二面，全场实体最多；运行时 3451–3459 entities** |
| a2_17-Todd | 379 | 12656x392 | |
| a3_16-Cookie | 770 | 1312x1688 | |
| b2_14-Ecl1psed | 1515 | 3584x1368 | |
| d1_09-xlibiza | 2325 | 2544x1440 | |
| f3_01-Hydro | 2256 | 4672x8024 | |
（其余房间见 `bench/results/probe-probe-sj.json` 的 `map_rooms`）

## 10.3 帧时间归因（a1_18-Xplosives，final 构建，zip 环境，3451 entities）

每段 12s 采样；**CPU 时间**= `Engine.Update` 开始到 `Engine.Draw` 结束（排除 vsync 等待）。

| 实验 | 段 | 采样帧 | CPU p50 / p99 / max (ms) | 帧间隔 p50 / p99 / max (ms) | >20ms 帧 | managed 末值 |
|---|---|---|---|---|---|---|
| final，无 savestate | A | 720 | 1.93 / 2.57 / 316.80 | 16.67 / 16.81 / 319.20 | 2 | 756 MB |
| final，持 1 槽 | B | 720 | 1.81 / 2.74 / 5.05 | 16.67 / 17.02 / 19.75 | 0 | 833 MB |
| final，8 次 load 后 | C | 720 | 1.81 / 2.49 / 6.29 | 16.67 / 16.79 / 21.12 | 3 | 1011 MB |
| final，持 3 槽 | B | 720 | 1.99 / 2.49 / **44.10** | 16.67 / 16.76 / **58.58** | 1 | 922 MB |
| final，PreClone Off，A/B/C | A/B/C | 720/720/720 | 1.96–1.97 / 2.69–2.77 / max 309（首帧） | p50 16.67，B max 20.3、C max 18.6 | 0–2 | 757/813/990 MB |
| upstream，A/B/C | A/B/C | 720/720/720 | 1.99 / 2.57–2.76 / max 330（首帧） | p50 16.67，B max 41.7、C max 18.2 | 1–2 | 901/970/1160 MB |
| **SRT 完全禁用**，A | A | 721 | **1.96 / 2.66 / max 300（首帧）** | 16.67 / 16.85 / 310 | 4 | 754 MB |

关键结论：
1. **持有 savestate 不增加每帧 CPU**：A vs B vs C 的 CPU p50 都是 1.8–2.0ms；SRT 完全禁用的 A 段
   （1.96/2.66）与启用 SRT 的 A 段（1.93/2.57）一致。**第二面卡顿不是 SRT 常驻造成的**。
2. 唯一明显尖峰：房间**首帧**（A 段 max 300–330ms，渲染 3451 实体），属 room load；
   3 槽时偶发一次 ~44ms CPU / 58ms 帧（GC，该段 GC 1/1/0）。
3. save/load 本身的卡顿见下：**首次 save 693–747ms**、随后 load 15–17ms（PreClone On）；
   PreClone Off 时 load 抖动大（34–123ms/次）。
4. `ab3` 3 槽 save 耗时：718 / 41.2 / 40.1 ms（首槽冷、后续稳态）。

## 10.4 重房间 save/load 矩阵（20 iters + 3 warmup + 3 slots，zip 环境）

| 指标 | upstream-mod | final PreClone On | final PreClone Off |
|---|---|---|---|
| save p50 / p99 (ms) | 62.70 / 157.86 | **40.73 / 115.14** | 40.75 / 321.98 |
| load p50 / p99 (ms) | **50.19** / 143.63 | **32.68** / 285.29 | 38.31 / 115.43 |
| load 帧 p50 / p99 (ms) | 54.62 / 152.28 | **36.69 / 290.43** | 42.26 / 132.12 |
| 存档 1/2/3 槽 managed 增量 | **85.4 / 116.9 / 148.4 MB** | 52.1 / 86.4 / 117.7 MB | **41.0 / 58.0 / 72.4 MB** |
| load 后 managed 均值 | 1564 MB | 1268 MB | 1186 MB |
| GC（20 save / 20 load） | 9/9/0 · 9/9/1 | 8/8/0 · 8/8/1 | 7/7/1 · 7/7/0 |

结论（重房间）：
- **save p50：final 40.7ms vs upstream 62.7ms（−35%）**；load p50：32.7 vs 50.2（−35%）。
  重房间把 SRT save/load 的绝对耗时放大了约 3 倍，但优化收益同比例保留。
- **槽位内存：final On 3 槽 117.7MB / Off 72.4MB vs upstream 148.4MB**（On −21%、Off −51%）；
  首槽 On 52.1 / Off 41.0 / upstream 85.4 MB（重房间单槽是 a0-Start 的 ~3–3.5 倍）。
- load p99 的 285ms 尖峰对应一次 Gen2 GC（gc2=1，20 次里 1 次）；PreClone Off 的 save p99 322ms
  也是 GC 尖峰——**重房间 + 8GB 机器的主要风险是 GC 尖峰，而不是 sv 常驻**。
- 首次 save 冷开销：693–747ms（3451 entities + 首次 JIT）；matrix 的 p50 已包含 3 次 warmup。

## 10.5 重房间 profiling 分段（profiling 构建，5 iters，3 slots）

> 该轮 profiling 用的是实验分支的 profiling 构建（warm-up 已撤回，稳态分段与 final 一致）。

| 分段 | 首次 | 稳态 |
|---|---|---|
| save cloneLevel | 283.6 ms | **56.8–65.7 ms**（a0-Start 为 6.7） |
| save cloneSaveData | 4.2 ms | 1.7–1.8 |
| save saveActions | 127.0 ms | 3.5–3.6 |
| load unload | 9.8 ms | **6.9–7.3 ms** |
| load cloneBack | 1.4 ms | **1.15–1.43 ms** |
| load loadActions | 60.4 ms | 6.5–7.0 |
| **后台 preclone** | 44.9ms / 24.7MB | **20.8–25.4ms / 24.5MB 每次 save/load** |

→ 重房间里 PreClone On 的隐藏成本是**每 save/load 约 21–25ms CPU、约 24.5MB 分配**（a0-Start 是 4–6ms/9MB）；
这是 8GB 机器上需要权衡的点（PreClone Off 更省内存/分配，但 load 慢 ~2–3 倍）。

## 10.6 TAS 回放（真实游玩卡顿）——正确启动路径版

### 启动方式（最终采用，旧 trim/teleport 描述已废弃）

TAS：`D:\Games\SteamLibrary\steamapps\common\Celeste\TAS Files\StrawberryJamTAS-main\5_Grandmaster\grandmaster_heartside.tas`
（`RecordCount 57969`）。CelesteTAS 3.46.2；CeilingUltra 已被用户整体禁用（blacklist），TAS 侧不再需要 `TasEnableAll` 反射开关。

1. **TAS 文件完全不裁剪**（保留第 3 行 `console load StrawberryJam2021/5-Grandmaster/ZZ-HeartSide`），
   `Controller.FilePath` 直接指向原文件。
2. **从稳定 Overworld 起播**：Boot 到 Overworld → `SaveData.Start` → 连续 60 帧确认
   `Engine.Scene is Celeste.Overworld` 且 scene 引用不变（日志 `tas settle`）→ 才 `EnableRun`；
   TAS 自己的 `console load` 按录制路径加载地图，**不用 LevelEnter/传送/中途跳帧**。
3. **快进到目标房间**（缩短等待）：保持 `Hotkeys.FastForward.OverrideCheck = true`（CelesteTAS 的"按住快进"热键），
   并通过 `CelesteTasSettings.Instance.StudioShared.FastForwardSpeed = 400` 把速度拉到 400x；
   进入目标房间立即 `OverrideCheck=false` → `PlaybackSpeed=1` 开始 1x 采样；离开目标房间立即停 TAS + `Environment.Exit(0)`。
   FF 结束那一帧（400 次 update，dt≈3–5.5s）从统计中剔除。
4. 启动后窗口移动到屏幕左上角 `(0,0)`、**保持 1280×720 原尺寸**（`SetWindowPos(..., SWP_NOSIZE|SWP_NOACTIVATE)`），
   日志 `window placed: x=0 y=0 w=1296 h=759`（外框；客户区 1280×720），不再最小化/移出屏幕。

### 完整 25 房间（`tasfull-unmodified`，1x 全程，deathsMax=1）

| room | 进入帧 | 帧数 | p50 | p99 | max | >33 | >50 | GC g0/g1/g2 | CPU p50 |
|---|---|---|---|---|---|---|---|---|---|
| a0-Start | 1 | 281 | 16.67 | 25.43 | 2001.5 | 2 | 1 | 0/0/0 | 0.65 |
| a1_18-Xplosives | 315 | 751 | 16.65 | 22.49 | 112.2 | 1 | 1 | 0/0/0 | 3.80 |
| a2_17-Todd | 1071 | 717 | 16.66 | 19.96 | 44.8 | 1 | 0 | 0/0/0 | 1.57 |
| a3_16-Cookie | 1788 | 1195 | 16.67 | 17.76 | 43.7 | 1 | 0 | 0/0/0 | 1.22 |
| b0-Humility | 2983 | 116 | 16.66 | 18.05 | 18.6 | 0 | 0 | 0/0/0 | 0.77 |
| b1_15-Alisticious | 3099 | 714 | 16.67 | 18.57 | 44.8 | 1 | 0 | 0/0/0 | 1.15 |
| b2_14-Ecl1psed | 3813 | 1256 | 16.66 | 18.02 | 53.1 | 1 | 1 | 0/0/0 | 1.75 |
| b3_13-TheDavSmasher | 5070 | 1943 | 16.66 | 17.43 | 58.8 | 1 | 1 | 0/0/0 | 1.24 |
| c0-Divinity | 7014 | 62 | 16.66 | 19.85 | 19.8 | 0 | 0 | 0/0/0 | 0.66 |
| c1_12-RedBatNick | 7076 | 624 | 16.66 | 18.51 | 55.6 | 2 | 1 | 0/0/0 | 1.26 |
| c2_11-DeathKontrol | 7701 | 1758 | 16.66 | 17.40 | 50.5 | 1 | 1 | 0/0/0 | 1.33 |
| c3_10-fishtank | 9460 | 1003 | 16.66 | 17.45 | 32.9 | 0 | 0 | 0/0/0 | 1.11 |
| d0-Purity | 10463 | 110 | 16.67 | 31.12 | 60.0 | 1 | 1 | 0/0/0 | 0.67 |
| d1_09-xlibiza | 10574 | 1884 | 16.66 | 18.75 | 35.1 | 1 | 0 | 0/0/0 | 2.38 |
| d2_08-Aiden | 12458 | 1252 | 16.67 | 17.78 | 220.6 | 2 | 2 | 0/0/0 | 1.28 |
| d3_07-Maya | 13722 | 1947 | 16.67 | 17.53 | 69.8 | 3 | 1 | 0/0/0 | 1.38 |
| e0-Clarity | 15671 | 76 | 16.66 | 35.93 | 35.9 | 1 | 0 | 0/0/0 | 0.86 |
| e1_06-tofu | 15747 | 550 | 16.66 | 22.96 | 37.3 | 1 | 0 | 0/0/0 | 2.00 |
| e2_05-elio | 16297 | 2352 | 16.66 | 17.80 | 99.1 | 4 | 1 | 0/0/0 | 1.83 |
| e3_04-Linj | 18651 | 816 | 16.66 | 17.36 | 52.7 | 2 | 1 | 0/0/0 | 1.12 |
| f0-Icarus | 19468 | 75 | 16.66 | 22.00 | 22.0 | 0 | 0 | 0/0/0 | 0.82 |
| f1_03-tobyaaa | 19543 | 943 | 16.66 | 17.39 | 53.7 | 1 | 1 | 0/0/0 | 1.60 |
| f2_02-Soloiini | 20487 | 1821 | 16.66 | 17.92 | 74.2 | 1 | 1 | 0/0/0 | 2.17 |
| f3_01-Hydro | 22310 | 4232 | 16.66 | 17.99 | 177.5 | 2 | 1 | 1/1/0 | 2.83 |
| gg_Heart | 26551 | 383 | 16.66 | 18.31 | 44.2 | 1 | 0 | 0/0/0 | 0.69 |

- 房间**全部按序通过（25 个，含 gg_Heart）**；房间进入帧偏移与录制标签的差异是录制器冻结帧记账方式不同，**不是 desync**，且不累积放大。
- `deathsMax=1`：d2_08-Aiden 处tasFrame≈12470 有一次死亡（死前 30 帧已取证：dt 51.8ms 一帧、随后 `upd=0` 的重生帧，GC 0/0/0、transitioning=0），与录制内刻意死亡/既有路线一致，不是状态错位。

### f3_01-Hydro（FF 到 f3 → f3 内 1x，`tasfull-ff-f3`）

| 指标 | 值 |
|---|---|
| 采样帧数 | 4204（`tasfull-unmodified` 1x 全程为 4232） |
| 帧 p50 / p99 / max | **16.66 / 18.08 / 37.3 ms** |
| >33ms / >50ms | **1 / 0** |
| GC g0/g1/g2 | **0 / 0 / 0（无任何 gen2）** |
| alloc 合计 | 667 MB（~70s） |

**重要更正**：之前的"f3 有 8 次 ~290ms gen2 停顿"是**错误启动路径的产物**（trim 掉 `console load` + LevelEnter/中途跳播，导致 session 状态与录制不一致、大量额外分配）。
按正确路径（unmodified + console load + 稳定 Overworld）播放时，**f3 没有 gen2 停顿**；1x 全程与 400x 快进两条路径结论一致。

### a1_18-Xplosives 的 A/B/C（`tasplay-ff-a1-abc`，FF 到 a1 → 1x，save/load 后立即停）

| 段 | 动作 | save/load | 帧数* | 帧 p50 | p99 | max | CPU p50 |
|---|---|---|---|---|---|---|---|
| A 无 savestate | — | — | 86 | 16.65 | 46.41 | 46.41 | 3.26 |
| B 建 1 槽继续 | SaveStateTas | **733.0ms** | 120 | 16.64 | 51.18 | 751.0（save 帧） | 4.02 |
| C 读档后继续 | LoadStateTas | **84.9ms** | 132 | 16.66 | 99.42 | 254.6（load 帧） | 2.28 |

\* 采样帧数受 Draw-hook 覆盖影响小于实际；p50/save/load 时间准确。C 段结束后检测到一次死亡（load 把状态倒回、TAS 输入继续前进导致错位）——这是"load 会破坏 TAS 连续性"的预期现象，所以 A/B/C 必须与全程分开跑、且 C 后立即停。

### 结论（回答用户"旧机 f3 卡顿"）

- f3 在真实 TAS 操作下 **p99 18.08ms、max 37.3ms、GC 0/0/0**：**既不是 SRT（本 run SRT 常驻但无 save/load），也不是 gen2 GC**。
- 第二面（a1）同样无持续掉帧；可感知卡顿只发生在 save/load 动作本身（重房间首次 save ~733ms、load ~85ms）。
- 旧机若在 f3 仍卡，方向应转向 **GPU/驱动/分辨率/其它 mod 的 per-frame 开销**，而不是 SRT 或 GC（见 §10.11 的 GC 参数结论）。

## 10.6b 窗口放置（bench 可见模式）

- 启动后由 harness 用 `SetWindowPos(hwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOSIZE|SWP_NOACTIVATE|SWP_SHOWWINDOW)`
  把窗口移到屏幕左上角，**不改变窗口尺寸**；随后 `GetWindowRect` 记录实际外框尺寸。
- 实测：`window_x=0 window_y=0 window_w=1296 window_h=759`（客户区 1280×720），`window_placed=true`；
  该信息写入结果 JSON env（`window_x/y/w/h/placed`），便于区分与旧数据的窗口状态。
- 旧的"最小化+移出屏幕"静默模式已移除（用户要求窗口可见、放左半屏观看）。

## 10.11 GC 参数结论（f3 更正）

- 之前的 2GB/3GB `HeapHardLimit`、`gcServer`、`SustainedLowLatency` 等实验都建立在"f3 有 290ms gen2"的错误前提上；
  正确启动路径下 **f3 没有 gen2**，因此 **GC 参数矩阵取消**，不再作为优化方向。
- 作为交叉验证：`gc_latency_mode=interactive`（强制 .NET 默认）时 f3 仍为 **GC 0/0/0、p99 17.97、max 38.6**；
  与 Celeste 默认的 `SustainedLowLatency` 无差异。
- 历史数据（a1 房间 25s、2GB heap、PreClone Off）：SRT 开 p99 17.00 / CPU p50 2.04 / >33ms 1 帧；SRT 完全禁用 p99 16.93 / CPU p50 2.02 / >33ms 1 帧——
  同样说明 **SRT 常驻且无 savestate 不是帧尖峰来源**（该结论仍成立）。

## 10.7 冷启动预热实验（2026-11-09，已撤回，仅记录）

为降低"会话内首次 save ~360ms（a0-Start）/ ~740ms（重房间）"，尝试了三版 SRT 侧预热（均在 benchmark 环境测得）：

| 版本 | 机制 | 结果 |
|---|---|---|
| v1 | 类型 codegen 预热（5936 类型，后台 247ms） | 首次 cloneSaveData 21.5→4.3ms（−80%），cloneLevel 216→198ms（−8%），**saveActions 142ms / loadActions 62ms 基本不变** |
| v2 | 主线程 dry-save（丢弃式，覆盖 action lambda） | dry-save 33–36ms/0 失败；首次 save 仍 205/139/62ms，**无改善** |
| v3 | 增加"真实对象图丢弃式 Level 克隆" | Level warm-up 8ms 后 NRE abort（WarmingUp 时 post-processor 返回 null 导致图被污染） |
| v4 | 修复 post-processor 返回值 | warm-up 正常（100ms/0 失败），但**之后所有 save 失败**：`ArgumentException: RuntimeType → Celeste.Player`，DeepCloner per-type 缓存被 warm-up 污染 |

结论：**预热方案全部撤回**（SRT 仓库已 revert，见 `docs/optimization-report.md §2.5`），
保留已有的 cloneSaveData 优化。首次 save 的 `saveActions`/`loadActions` 大头是首次真实执行/JIT，
在不污染 cloner 缓存的前提下无法安全预热；8GB 机器上建议通过"进图后先做一次用户不可见的 save/load"
（由用户操作触发）来掩盖冷启动成本。

## 10.8 复现

```cmd
:: zip 标准环境（已长期保留；回滚见 convert_to_zip.py rollback）
python D:\DSH\Celeste\work\SpeedrunTool\bench\collect_results.py

:: 重房间帧归因
run.cmd frames-a1_18-Xplosives-abc 420
run.cmd frames-a1_18-Xplosives-ab3 420
run.cmd frames-a1_18-Xplosives-abc-off 420
python toggle_srt.py disable && run.cmd frames-nosrt-a1_18-Xplosives-a 300 && python toggle_srt.py enable

:: 重房间矩阵
run.cmd room2-final-on 420 / room2-final-off 420
python swap_srt.py upstream-mod && run.cmd room2-upstream 420 && python swap_srt.py final

:: TAS 回放（需 CelesteTAS 启用；BodyDouble 的 dev auto.tas 需临时改名）
run.cmd tasplay-final-a1-mid 300
```

原始数据：`bench/results/` 下 `room2-*.json`、`frames-*.json`、`tasplay-*.json`、
`PROFILE-profiling-room2-on.txt`、`probe-probe-sj.json`、`SUMMARY.md`；zip 清单在
`bench/zip-conversion-manifest.json`。


## 10.9 GC 压力测试（模拟 8GB 旧机，2026-11-09）

方法：bash 直接启动游戏并传 `DOTNET_GCHeapHardLimit`（`0xC0000000`=3GB / `0x80000000`=2GB），
在 `a1_18-Xplosives` 跑 frames A/B/C（12s/段，1 槽；另加 3 槽组）。
**注意：Steam 版 Celeste 直接启动会经 Steam 重启并丢失自定义环境变量**，测试时临时放了
`steam_appid.txt`（504230）阻止重启；测后已删除。JSON 里 `gc_total_available_bytes` 验证
3.0GB / 2.0GB 限制确实生效（`env_DOTNET_GCHeapHardLimit` 非空）。

| 实验 | 段 | GC gen0/1/2 | managed 末值/峰值 (MB) | >33ms 帧 | >50ms 帧 | 帧 max (ms) |
|---|---|---|---|---|---|---|
| final，3GB，1 槽 | A | 0/0/0 | 771 / 771 | 1 | 1 | 340.1（房间首帧） |
| | B | 0/0/0 | 840 / 839 | 0 | 0 | 18.4 |
| | C | 1/1/0 | 1002 / 1018 | 1 | 1 | 60.2 |
| final，2GB，1 槽 | A | 0/0/0 | 772 / 772 | 1 | 1 | 340.3 |
| | B | 0/0/0 | 841 / 841 | 0 | 0 | 19.1 |
| | C | 1/1/0 | 1004 / 1019 | 1 | 1 | 54.7 |
| final，3GB，**3 槽** | A | 0/0/0 | 772 / 772 | 1 | 1 | 339.4 |
| | B（3 槽持有） | **1/1/0** | **908 / 937** | 1 | **1** | **64.2** |
| upstream，3GB，1 槽 | A | 0/0/0 | 949 / 949 | 1 | 1 | 358.8 |
| | B | **1/1/0** | 994 / 1023 | 0 | 0 | 32.7 |
| | C | 1/1/0 | 1173 / **1206** | 1 | 1 | 58.8 |

save/load：final 3GB `save=729.5ms`，8 次 load `111.9 / 16.9 / 16.1 / 15.2 / 52.8 / 15.2 / 15.0 / 16.1`；
final 2GB `save=739.9ms`，load `109.2 / 16.7 / 16.3 / 15.1 / 55.7 / 15.1 / 15.0 / 15.8`（与 3GB 基本一致）；
3 槽组 save `724.2 / 39.4 / 59.4ms`；upstream 3GB `save=803.6ms`，load `87.5 / 19.4 / 18.6 / 19.4 / 19.1 / 65.8 / 17.0 / 35.3`。

**结论**
1. **2GB 与 3GB 限制下表现几乎相同**：managed heap 峰值只有 ~1.0–1.2GB，远低于限制，
   单纯压 GC 堆上限并不能复现"8GB 旧机"的真实压力（真实瓶颈更可能在进程 native 内存 +
   系统其它占用 + 更小的可用地址空间；8GB 机器上进程 private 已达 ~3GB）。
2. **真正放大风险的是"3 槽 + PreClone On"**：3 槽时 managed 峰值 937MB、段内出现 1 次 GC、
   单帧 64ms（>50ms）。8GB 机器建议 **≤2 槽**，或 3 槽时用 **PreClone Off**（内存/分配更省，
   代价是 load 慢 2–3 倍）。
3. final vs upstream（3GB，1 槽）：upstream managed 峰值更高（1206 vs 1018MB）、B/C 段各多 1 次 GC；
   **优化后内存/GC 压力更低**，save/load 更快（save 729.5 vs 803.6ms）。
4. GC 尖峰 50–65ms 在 1 槽时也会出现（load 后第一次 GC），与 heap limit 无关，属正常 GC 行为；
   load p50 仍稳定 15–17ms（final，PreClone On）。

---

# 附录 A：ZZ-HeartSide 33 个房间（LevelData 实体数 / Bounds）

sid: StrawberryJam2021/5-Grandmaster/ZZ-HeartSide  |  LevelData count: 33  |  source: bench/results/probe-probe-sj.json

| # | room | LevelData entities | bounds (WxH@x,y) |
|---|---|---|---|
| 0 | a0-Start | 10 | 464x1016@0,0 |
| 1 | a1_18-Xplosives | 2461 | 6160x640@464,0 |
| 2 | a2_17-Todd | 379 | 12656x392@6624,320 |
| 3 | a3_16-Cookie | 770 | 1312x1688@19280,-1024 |
| 4 | b0-Humility | 8 | 320x180@20184,-1208 |
| 5 | b1_15-Alisticious | 589 | 880x1008@20504,-2032 |
| 6 | b2_14-Ecl1psed | 1515 | 3584x1368@20752,-3400 |
| 7 | b3_13-TheDavSmasher | 720 | 2440x928@26304,-3608 |
| 8 | c0-Divinity | 4 | 320x180@28424,-3792 |
| 9 | c1_12-RedBatNick | 455 | 1048x400@28744,-3856 |
| 10 | c2_11-DeathKontrol | 570 | 320x2640@29792,-6136 |
| 11 | c3_10-fishtank | 298 | 320x1616@30112,-7560 |
| 12 | d0-Purity | 14 | 320x180@30112,-7744 |
| 13 | d1_09-xlibiza | 2325 | 2544x1440@30432,-8936 |
| 14 | d2_08-Aiden | 524 | 1408x440@33016,-7936 |
| 15 | d3_07-Maya | 1004 | 2120x2504@34080,-10448 |
| 16 | e0-Clarity | 10 | 360x180@35856,-10632 |
| 17 | e1_06-tofu | 794 | 3200x424@36216,-10680 |
| 18 | e2_05-elio | 1146 | 320x3728@39416,-14256 |
| 19 | e3_04-Linj | 174 | 18440x1496@39296,-15752 |
| 20 | f0-Icarus | 24 | 320x180@57416,-15936 |
| 21 | f1_03-tobyaaa | 481 | 1320x304@57736,-16024 |
| 22 | f2_02-Soloiini | 1475 | 2624x2272@59056,-17992 |
| 23 | f3_01-Hydro | 2256 | 4672x8024@61680,-25776 |
| 24 | gg_Heart | 6 | 1760x320@68720,-27200 |
| 25 | z00 | 0 | 104x48@19280,664 |
| 26 | z01 | 0 | 992x16@30432,-7496 |
| 27 | z02 | 0 | 16x64@36200,-10448 |
| 28 | z03 | 0 | 104x104@39312,-10784 |
| 29 | z04 | 0 | 320x80@39416,-10528 |
| 30 | z05 | 0 | 16x56@57720,-15992 |
| 31 | z06 | 0 | 176x56@58880,-16080 |
| 32 | z74 | 0 | 48x32@30112,-5944 |

---

## 11. scene-state 跨章节读档验证（scenefix af3e8bd，2026-11-10）

使用 `feat/scene-persistent-state`（commit `af3e8bd`）构建的 SpeedrunTool.dll（md5 `5f0a625106c0016e4e35185b5932e44b`）在 bench 实例验证。

**AutoClear 注意**：用户旧设置文件里 `AutoClearStateOnSceneSwitch: true` 会在场景切换时清档（初始测试全部失败的原因）；scenefix 新默认只影响新设置。bench 测试时 harness 覆盖为 false。

| # | 场景 | 结果 | 关键数据 |
|---|---|---|---|
| B1 | 同章节同图换房间 → 读档 | PASS | LoadState 成功回到保存房间 |
| B2 | A(SJ HeartSide)→B(ForsakenCity) 跨章节 | PASS | `PendingLoad: Entering '…'` → **ready after 1452 ms** → 恢复 A |
| B4 | 不同原版存档文件（只读） | PASS | 槽 1/2 切换、无 SaveData.Save；**ready after 1505 ms**，未写回存档 |
| B6 | 切场景后立刻读档 | PASS（happy path） | **ready after 1495 ms**；abort/retry 边角未触发 |
| B8 | 跨章节耗时 | 数据 | pending 恢复 1452–1505 ms（B2/B4/B6/B8：1452/1505/1495/1476 ms）；整体 wall save→restored ~3.5 s（含 1 s 观察 pause）；memory 未采样 |

B3（Overworld→A）、B5（CollabUtils2 helper）、B7（加载失败回滚）需要手动 UI/改 mod 文件验证，步骤见 `bench/results/scene-state-b1-b8-report.md`。
