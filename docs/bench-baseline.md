# SpeedrunTool savestate 基准结果（SJ Grandmaster Heartside）

> 本文是 "降低 savestate 内存 / 提升 save/load 速度 / 避免卡顿" 优化的**基准测量报告**。
> 环境搭建方法见 [bench-setup.md](bench-setup.md)，原始数据在 benchmark 工作区
> `D:\DSH\Celeste\bench\results\`（该目录不在本仓库内）。
>
> 测量机器 **不是** 8GB 目标机（详见 §2），因此绝对值只用于同机横向对比；
> 报告重点是可迁移的相对指标（耗时 p50/p99、GC 次数、分配量、managed 内存增量）。

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

用 `SRT_PROFILE=1` 采集 SRT 各阶段耗时（`results/PROFILE-profiling-final-on.txt` /
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
cd D:\DSH\Celeste\bench && build.cmd

:: 切到某个构建后跑 tag
python swap_srt.py final
run.cmd final-on-r1 420
run.cmd final-off-r1 420

:: 汇总
python collect_results.py        :: → results\SUMMARY.md
```

原始数据（benchmark 工作区 `D:\DSH\Celeste\bench\results\`）：

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
