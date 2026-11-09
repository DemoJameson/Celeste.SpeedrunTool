# SpeedrunTool 性能优化报告（perf/savestate-optimization 分支）

> 目标：降低 savestate 的内存开销、提高 save/load 速度、并让游戏在持有 savestate 时不再因
> 它产生额外卡顿（目标场景：8GB 内存旧机器游玩草莓酱 Grandmaster Heartside）。
> 分支：`perf/savestate-optimization`（基于 upstream master `752e6f5`）。
> 数字来自同一台开发机（16 线程 / 64GB RAM / Windows / .NET 8 / 无 vsync 限制或 60fps vsync）
> 上的自动化 benchmark：游戏目录 `D:\Games\SteamLibrary\steamapps\common\Celeste`
> （Everest 1.6662），场景为 Strawberry Jam 1.0.13 的
> `StrawberryJam2021/5-Grandmaster/ZZ-HeartSide`（Grandmaster Heartside，入口房间 `a0-Start`，
> 222 entities，464x1016）。benchmark 模组 SrtBench **不在本仓库内**，由 bench-env 另行维护。

## 0. 结论摘要

测于 Strawberry Jam `5-Grandmaster/ZZ-HeartSide` 入口房间（222 entities，60fps vsync，
20 iterations + 3 warmup；每项 2 次运行取中位数，原始数据见 `docs/bench-baseline.md`）：

| 指标（中位数） | upstream master | 优化后（Pre-clone On） | 优化后（Pre-clone Off，8GB 默认） |
|----------------|-----------------|------------------------|----------------------------------|
| save p50 | 17.47 ms | 13.10 ms（**-25%**） | 12.41 ms（**-29%**） |
| load p50 | 15.00 ms | 11.28 ms（**-25%**） | 14.72 ms（-2%） |
| load 所在帧 max | 89.84 ms | 55.10 ms（**-39%**） | 65.44 ms（**-27%**） |
| 存档 1 槽常驻 managed | +36.1 MB | +20.5 MB（**-43%**） | +13.0 MB（**-64%**） |
| 存档 2 槽常驻 managed | +50.7 MB | +35.7 MB（**-30%**） | +21.0 MB（**-59%**） |
| 存档 3 槽常驻 managed | +65.4 MB | +50.3 MB（**-23%**） | +28.4 MB（**-57%**） |
| load 后 managed 均值 | 1231 MB | 1200 MB | 1029 MB |
| load 后 1 秒内帧时间 | p50 16.67 ms | p50 16.67 ms，>33ms 帧 ≈0 | 同左 |

要点：
- save/load p50 降低约 1/4；load 帧尖峰降低 27%~39%（旧构建的尖峰主要来自 snapshot
  全屏捕获 / 8.3MB LOH 分配、以及更慢的克隆处理器）。
- 存档常驻内存明显下降：**Pre-clone Off 时 3 槽从 65.4MB 降到 28.4MB（-57%）**，这是 8GB
  目标机实际会走到的路径（Auto 在 ≤10GB 机器上选中 Off）；On 时 3 槽 50.3MB（-23%），因为
  Pre-clone 的第二份实体图本身就是“用内存换取读档速度”的设计。
- load 的尖峰只出现在 load 所在那一帧（被 load 的 wipe 动画覆盖）；load 之后 1 秒窗口帧时间
  稳定 60fps，没有“有了 savestate 之后持续卡顿”。normal 模式（含 freeze+wipe）的可见过渡
  约 520-550ms，来自 wipe 动画本身，期间最大帧 17-27ms。
- 8GB 机器上 `PreCloneMode=Auto` 生效为 Off：用 load p50 11.3→14.7ms（+3.4ms）换取每槽
  约一半常驻内存 —— 对目标机器是划算的取舍（内存压力 > 每次读档多 3ms）。
- **冷启动已知问题**：会话内**第一次** save/load 仍有一笔一次性开销（本机 profile：首次
  save `cloneLevel=215.9ms + saveActions=142.4ms`，首次 load `loadActions=61.7ms`，来自
  JIT + 每种类型的 cloner codegen）。后续 save/load 稳定在 13/11ms。见 §4 后续可做。

### 第二面 / zip 标准环境（最终基准，优先看这一组）

按"多数玩家"的 **zip mod** 布局（52 个下载 mod 转 zip）在用户实际感觉卡的**第二面**
`a1_18-Xplosives`（运行时 3451–3459 entities）重测：

| 指标 | upstream | final PreClone On | final PreClone Off |
|---|---|---|---|
| save p50 / p99 | 62.7 / 157.9 ms | **40.7 / 115.1 ms（-35%）** | 40.8 / 322.0 ms |
| load p50 / p99 | 50.2 / 143.6 ms | **32.7 / 285.3 ms（-35%）** | 38.3 / 115.4 ms |
| 3 槽 managed 增量 | 148.4 MB | 117.7 MB（-21%） | **72.4 MB（-51%）** |
| 持有 savestate 每帧 CPU（p50/p99） | 1.99/2.76 ms | **1.81/2.74 ms** | 1.97/2.69 ms |

- **最关键结论**：持有 savestate 时第二面每帧 CPU 与"无 savestate"（1.93/2.57）以及
  **完全禁用 SRT**（1.96/2.66）没有可测差异，帧 p50 16.67ms、p99 16.8–17.0ms → **第二面卡顿
  不是 SRT 造成的**，主要是 3451 实体 + helper mod 的 update/render 成本；TAS 真实操作回放
  也得到同样结论。
- SRT 的成本集中在 **save/load 边界**：首次 save 约 719–740ms（冷启动，见 §2.5，未解决）、
  稳态 save/load 40.7/32.7ms；每次 load 后 PreClone On 有一个 21–25ms / 24.5MB 的后台任务。
- **8GB 机器建议**：`PreCloneMode=Auto`（默认 Off）+ **≤2 槽**；3 槽 + PreClone On 时 managed
  峰值 937MB 并出现 1 次 GC/64ms 帧。GC 压力测试中 2GB/3GB heap limit 表现几乎相同（无法单靠
  GC 堆上限复现 8GB 真机压力）。


## 1. 方法

1. `code-audit` 通读 save/load 全链路，产出 `docs/audit.md`（内存/性能热点排序、bug 清单、
   隔离微基准）；`docs/review.md` 对本分支改动做独立 review。
2. bench 环境用 Strawberry Jam + 其全部依赖搭出真实场景，外部 benchmark 模组 SrtBench
   通过 SRT 的 ModInterop API 自动执行 save/load 循环，记录耗时、GC、托管/进程内存、
   帧时间、多槽常驻内存；结果由 bench-env 输出为 JSON。
3. 每个改动单独提交，按 upstream 构建 / 优化构建（Pre-clone On / Off）做 A/B。

## 2. 已落地改动

### 2.1 内存

| 改动 | 说明 | 收益 |
|------|------|------|
| Snapshot 缩略图 | 多槽存档缩略图从整屏纹理改为长边 ≤480px；捕获 buffer 复用；清除/覆盖/热重载时 Dispose | VRAM 8.3MB/槽 → ≤0.9MB/槽；每次 save 的 8.3MB LOH 垃圾 → 0 |
| Pre-clone 可开关（Auto/On/Off） | 预克隆在后台保留第二份**实体图**以加速读档；Auto 在 GC 可用内存 ≤10GB 的机器上关闭 | 关闭后 savestate 的实体图常驻内存约减半（映射表/静态值/Snapshot 仍在） |
| PreCloneEnabled 切换安全 | 关闭时先等待所有槽的 preclone 任务结束再释放引用，避免第二份图滞留到下次 save | 运行期切换真正释放内存 |
| 移除 load 路径的无用克隆 | `UpdateTimeAndDeaths` 里 Session/SaveData 克隆后从未使用；`UnloadLevel` 的 LINQ Distinct、`RestoreAudio_1` 的每实体 `ToArray()`、重复 VirtualAsset | 每次 load 少一次 SaveData 全量克隆和一批临时分配 |
| 纹理/事件实例生命周期修复 | Snapshot `ClearAll`/热重载 Dispose；同槽重复存档 Dispose 旧纹理；`readbackBuffer` 不跨重载常驻 | 消除 VRAM 泄漏 |

### 2.2 save/load 速度

| 改动 | 说明 | 收益（预期，实际见 §3） |
|------|------|------|
| DeepCloner 处理器优化 | 去掉每对象 2 次 `lock`；`is` 链、`IsHashSet`/`IsIDictionary`（会分配数组）等类型判断按类型缓存成 bitmask（4 槽 thread-local + ConcurrentDictionary 回退） | 去掉主要可量化开销（反射/数组分配/锁）；DynData/CWT 探测仍在，预计显著缩小而非“接近无处理器”，以真机 A/B 为准 |
| Snapshot 不再重复渲染色 | 从 `Level.BeforeRender` 手动再渲染改为读取当前帧 backbuffer；并等 save 的 wipe 结束后再回读 | save 帧少一次整房间渲染（回读的 GPU 同步仍在）；快照不再拍到 wipe |
| 动作表预热 | 全 mod 类型/静态字段扫描从“第一次 save”挪到“第一次进入关卡”（`WarmUpOnFirstLevel`） | 首次 save 不再承担扫描；首次进关仍有一次开销，但不在 save 热路径 |
| 去掉无用克隆/分配 | 同 §2.1 | load 时间下降 |
| 自适应 LOH compaction | 只有碎片 >64MB 或 >5% heap 时才请求 LOH compact，否则普通 forced gen2 | 清除存档/切章时的 GC 尖峰大幅下降（旧实现每次都 compact） |

### 2.3 稳定性 / 兼容性

- **save/load 失败不再冻结游戏**：`SaveStateImpl`/`LoadStateImpl` 全程 try/catch，失败时复位
  状态机（含 `ClearBeforeSave/SavedByTas/LoadByTas`）；load 失败且实体已卸载时自动 reload
  关卡；单个实体 `Removed()` 抛异常不再中断 load。（hotkey 派发边界与 GC 边界仍无兜底，见 §4）
- **恢复被写反的 desync 检测条件**：普通地图的 late check 之前永远不执行（Lua 状态实体保护失效）。
- **运行期增删 SaveLoadAction**：保存路径不再失败（`InitSlots` 提前 + `OnClearState` 防御性
  重建）；旧槽位值字典版本不一致时拒绝读档并提示重新保存，而不是 NRE/写错 action。
- **`Areas_Safe`（List）及 `Modes` 越界检查**，避免 mod 图缺条目时 load 中断。
- **`InitFields` 的 `&& ||` 优先级 bug**：SRT 自己的 Component/Renderer 类型不再被扫进静态字段表。
- **SJ 依赖兼容**：`YetAnotherHelper.SpikeJumpThruController`（实体级 `Spikes.OnCollide` hook
  在 load 后重建）、`CollabUtils2.CollabCrystalHeart`（collab area-complete hook 重建）；
  `MaxHelpingHand` 等 mod 自己已通过 ModInterop 接入的部分不重复实现（见 `docs/sj-compat.md`）。
- **D14（未做运行时缓存失效，改为文档约束）**：`DeepClone` 的类型缓存/安全类型判定在类型首次
  克隆时生成并缓存，因此 `Add/RemoveReturnSameObjectType/Processor` 必须在**第一次 save/load
  之前**注册。FrostHelper 的 `ISavestatePersisted` 注册发生在 `LoadContent`，满足该约束。
  曾尝试在注册时清 `DeepClonerSafeTypes.KnownTypes`，但实测会让游戏在进图时**无异常 native
  崩溃**（KnownTypes 预置了 `System.RuntimeType` 等类型，清空后重新判定会走坏），已回退。
- **热重载缓存清理**：asset reload 后清 DeepCloner 类型缓存与处理器 flags 缓存，避免钉住旧 mod 程序集。
- **补齐缺失的 CJK 字体 page**：fresh clone 构建出的模组在中文语言下启动即崩（`chinese.fnt`
  引用的 png 未入库）。
- **`.gitignore` 的 `Dialog`/`Graphics` 规则锚定到仓库根**，避免误伤 `SpeedrunTool/Dialog`、
  `SpeedrunTool/Graphics` 下的新资源。

### 2.4 工程/重构

- DeepCloner 从 git submodule 内嵌进仓库（含原工作区未提交的 WIP 改动），`git clone` 后直接
  可构建；`DeepCloner/UPSTREAM.md` 记录来源与原因。
- csproj：`CelestePrefix` / `CelesteTasPath` / `ModRoot` / `DeployDir` 可配置，不再要求必须
  在游戏目录内构建；新增 `DeployToGame` 目标；已知的 MSB3277/CS0436/CS0649 噪声按说明静默。
- `SaveLoadAction` 拆分为注册/派发（主文件）与内置 action 注册（`SaveLoadAction.BuiltinActions.cs`，
  逐字节等价移动，见 `docs/review.md` §1.14）。
- 新增 **opt-in** 的 `SaveLoadProfiler`：默认 Release 构建里所有调用点被 `[Conditional]`
  编译消除（已用 Cecil 验证 IL 中 0 个调用点，二进制小 2KB），对低配机**零运行时开销**；
  用 `-p:SrtProfiling=true` 构建 profiling 版本后，log.txt 输出 save/load 分段耗时、
  堆/碎片/GC 计数，后台 preclone 单独输出 ms/allocMB。
- `StateManager` 按职责拆成 partial 文件：`StateManager.cs`（状态机/门面/GC）、
  `.Transitions.cs`（转场与死亡 hook）、`.Restore.cs`（读档后的时间/音频/磁带/存档数据恢复）、
  `.Freeze.cs`（freeze/wipe/out-of-freeze）；纯代码移动，`StateManager.Instance` 等公开契约不变。
- 新增 save/load ModInterop API（`SaveState/LoadState/SaveStateTas/LoadStateTas/ClearState/
  SwitchSlot/IsSaved`）供 TAS/工具使用；benchmark/自动化专用的开关（`SetPreCloneEnabled`、
  `SetAllowSaveLoadWhenWaiting`）单独放在 `SpeedrunTool.Bench` export 里，公开给 mod 的
  `SpeedrunTool.SaveLoad` 契约保持精简。
- 发布打包：删除旧的手工 `toZip.cmd`，改为 `-p:PackageMod=true -p:SrtPackageVersion=x.y.z`
  生成 `dist/SpeedrunTool-x.y.z.zip`（zip 根目录 `everest.yaml` + DLL + `Dialog/` + `Graphics/`）。
  不在每次 build 自动打包（版本号仍以 everest.yaml 为准，发布是显式步骤，适合放 CI 的 tag 流程）。

### 2.5 冷启动（首次 save/load）：已测量，未解决

profiling 显示**会话内第一次** save 比后续慢很多：入口房间 `cloneLevel` 216ms + `saveActions`
142ms（后续 ~13ms 总计）；在 3451 实体的第二面，**首次 save 达 719ms**（稳态 41ms），首次 load
的 `loadActions` 也有 ~60ms。根因不是类型 cloner 的 `Expression.Compile` codegen（这部分用后台
类型预热后 `cloneSaveData` 21.5→4.3ms、`cloneLevel` 只降 8%），而是**首次 JIT**：运行期第一次
执行那些生成的 cloner 委托和 save/load 的 lambda。

尝试过两种预热方案，均**已撤回**（`de9ebc5` → 后续 revert），没有进入交付版本：
1. 后台按类型元数据预生成 cloner：安全，但只覆盖 codegen，首次 save 只从 719→~700ms，且为
   数千个类型保留 cloner 缓存会增加常驻内存（对 8GB 不划算）。
2. 进图时主线程做一次"丢弃式 save/level clone"以触发 JIT：能把热路径跑一遍，但会污染
   DeepCloner 的类型/委托缓存，实测导致后续真实 save 抛
   `ArgumentException: Object of type 'System.RuntimeType' cannot be converted to type
   'Celeste.Player'`（每 20 次 save 全失败），风险不可接受。

因此冷启动开销保留为已知限制（见 §4），下一步的正确方向是**隔离**地预热（例如把预热产生的
缓存放到可丢弃的独立 AppDomain/AssemblyLoadContext，或在真实 save 前做一次隔离的 clone 校验），
而不是复用主缓存。

## 3. 验证

- 构建：`dotnet build -c Release -p:CelestePrefix=<Celeste> -p:DeployDir=<Celeste>/Mods/SpeedrunTool`。
- 场景：StrawberryJam `5-Grandmaster/ZZ-HeartSide` 入口房间（222 entities），60fps vsync。
- 对照：upstream master 构建 / 优化构建（Pre-clone On / Off）× 1 槽 / 3 槽；
  profiling 构建（`-p:SrtProfiling=true` + `SRT_PROFILE=1`）抓分段耗时；debug 构建抓每 action 明细。
- 交付构建（profiler 已编译掉）另跑一次完整矩阵（tag `verify-noprof`）：save p50 12.40ms /
  load p50 10.76ms / load 帧 max 48.1ms，与带 profiler 的构建在噪声范围内一致 —— 去掉
  profiler 没有性能回归。
- **对照数据（同一场景，20 iterations，已扣除 3 次 warmup；多次运行取区间）**：
  - save：upstream p50 17.4-17.6ms / 优化后（On）12.5-13.1ms；p99 91-101ms → 58-86ms
  - load：upstream p50 13.7-16.3ms / 优化后（On）10.8-11.4ms；p99 84-85ms → 12-47ms
  - load 所在帧 max：upstream 89.6ms / 优化后（On）51.8-58.4ms；load 之后 1s 窗口内均
    p50 16.67ms、>33ms 帧 ≈0
  - 存档常驻内存（强制 GC 后 vs 进图 baseline，managed）：
    upstream 1/2/3 槽 +30.8/+45.4/+60.0MB；优化后（On）+15.9~25.0/+31.2~40.2/+45.8~54.8MB；
    优化后（Off）+8.7~17.2/+16.7~25.3/+24.1~32.7MB（每槽测量有 ±10MB 噪声）
  - GC（20 save + 20 load）：gen0/gen1/gen2 ≈ 3/3/0（save）、3/3/0（load）
  - normal 模式（含 freeze+wipe）：save 调用 ~10.8-12.7ms → 可见过渡 ~520-550ms（wipe 动画），
    期间最大帧 17-27ms（分帧动画，不是单帧卡顿）
  - 备用场景 `5-Grandmaster/maya` 房间 Insert Coin（193 entities，opt-v1）：save p50 7.6ms /
    load p50 8.9ms，槽内存 +3.9/+9.7/+14.8MB —— 与场景规模同向
  - 测量机是 16 线程/64GB 开发机（**不是** 8GB 目标机）；跨构建横向对比有效，绝对值仅供参考
- 上面的一轮是**文件夹 mod**环境；之后按"多数玩家的标准布局"把下载的 52 个 mod 全部转成了
  **zip mod** 重新验证（详情见 §3.1），功能等价（60 modules / 143 areas / 128 SJ areas），
  warm 启动 `DONE LOADING` ≈11.6s。

### 3.1 第二面归因：`a1_18-Xplosives`（zip 标准环境）

用户在旧机上感觉卡的是 Grandmaster Heartside 的**第二面** `a1_18-Xplosives`（LevelData 2461
entities，运行时 **3451** entities，6160x640），而不是入口 `a0-Start`（运行时 222）。在 zip 环境
（53 mods）用"每帧真实 CPU 时间"（`On.Engine.Update` 开始 → `On.Engine.Draw` 结束，排除 vsync
等待）做了 A/B/C + 对照：

| 指标 | final PreClone On | final PreClone Off | upstream | SRT 完全禁用 |
|------|-------------------|--------------------|----------|--------------|
| 运行时 entities | 3451 | 3451 | 3451 | 3451 |
| A 段（无 savestate）CPU p50/p99 | 1.93/2.57 ms | 1.96/2.77 | 1.99/2.57 | 1.96/2.66 |
| B 段（1 槽持有）CPU p50/p99 | 1.81/2.74 | 1.97/2.69 | 1.99/2.76 | — |
| B 段帧 p50/p99/max | 16.67/17.02/19.8 | 16.67/16.83/20.3 | 16.67/16.83/**41.7** | — |
| C 段（8 次 load 后）CPU p50/p99 | 1.81/2.49 | 1.97/2.69 | 1.99/2.73 | — |
| 3 槽持有 B 段帧 max | **58.6 ms**（GC 1/1/0） | — | — | — |
| 首次 save | **719 ms** | 747 ms | — | — |
| 稳态 save p50 | 41.4 ms | — | — | — |
| 稳态 load p50 | 32.9 ms | — | — | — |
| 8 次 load（稳态/尖峰） | 14–15 ms（首次 101.6、末次 57.7） | **34–123 ms** | — | — |
| preclone 后台任务 | **20.8–25.4 ms / 24.5 MB**（每次 load 一个） | 无 | — | — |
| 存档常驻 managed（1/2/3 槽） | **+52.1 / +86.4 / +117.7 MB** | +41.0 / +58.0 / +72.4 MB | +85.4 / +116.9 / +148.4 MB | — |
| save p50 / p99（20 iters） | **40.7 / 115.1 ms** | 40.8 / 322.0 ms | 62.7 / 157.9 ms | — |
| load p50 / p99（20 iters） | **32.7 / 285.3 ms** | 38.3 / 115.4 ms | 50.2 / 143.6 ms | — |

**GC 压力测试**（`DOTNET_GCHeapHardLimit` 3GB / 2GB，Steam 版需临时 `steam_appid.txt` 才能传环境变量）：
2GB 与 3GB 表现几乎相同（managed 峰值只有 1.0–1.2GB，远低于限制），说明单靠压 GC 堆上限复现不了
8GB 旧机的真实压力（瓶颈更可能在进程 native/private ~3GB + 系统占用）。但在该压力下：
- final 3GB 的 B/C 段 managed 峰值 840/1018MB，upstream 为 994/1206MB 且 B/C 各多 1 次 GC；
  final save 729.5ms vs upstream 803.6ms —— **优化后内存/GC 压力更低**。
- **"3 槽 + PreClone On" 是真正的放大因素**：managed 峰值 937MB、段内 1 次 GC、单帧 64ms。
  8GB 机器建议 **≤2 槽**，3 槽时用 **PreClone Off**（更省内存/分配，代价 load 慢 2–3 倍）。
- load 后 50–65ms 的 GC 尖峰在 1 槽也存在（属正常 GC），load p50 仍稳在 15–17ms（PreClone On）。

重房间 **20 iters 矩阵**（warmup 3 次，3 slots）进一步确认：save p50 final 40.7ms vs upstream
62.7ms（**−35%**），load p50 32.7 vs 50.2（**−35%**）；3 槽常驻 managed On 117.7MB / Off
72.4MB vs upstream 148.4MB（On −21%、Off −51%）；load p99 285ms 与 PreClone Off 的 save p99
322ms 都对应偶发 Gen2 GC（20 次里 1 次）——**重房间 + 8GB 机器的主要风险是 GC 尖峰，不是
savestate 常驻**。

结论：
1. **持有 savestate 对第二面的每帧 CPU 没有可测影响**：B 段 CPU p50 1.81ms、p99 2.74ms，
   与无 savestate（1.93/2.57）和完全禁用 SRT（1.96/2.66）都在噪声范围内；B 段帧 max 只有
   19.8ms，12 秒内没有 >20ms 帧。**第二面卡顿不是 SRT 造成的**，主要是 3451 实体 + helper mod
   的 update/render 成本（这台机器 60fps 下还有余量，8GB 旧机的 CPU/GC 压力会放大问题）。
2. SRT 的成本集中在 save/load 边界：**首次 save 719ms**（冷启动，见 §2.5，暂未解决）、
   稳态 save 41.4ms / load 32.9ms；每次 load 后有一个 **20.8–25.4ms / 24.5MB 的后台 preclone
   任务**（PreClone On）。
3. **重房间里 PreClone 的收益比入口房间大得多**：On 时稳态 load 14–15ms，Off 时 34–123ms
   （首末次尖峰），8 次 load 中位 ~39ms。8GB 机器默认 Off 是内存取舍，不是速度取舍 —— 如果
   目标机内存压力可以接受，重房间手动开 On 读档会快很多。
4. 重房间单槽常驻 **~69MB**（3 槽 134MB），是入口房间（15/45MB）的 4.6/3 倍；**3 槽同时在
   手时创建过程会出现一次 ~58ms 的 GC 帧**（本机 16GB），8GB 旧机建议只用 PreClone Off +
   1–2 槽，避免同时持有 3 份大房间存档。
5. upstream 在同一房间的 B 段出现过 41.7ms 帧（GC 1/1/0），final 没有 —— 说明减少单槽分配量
   对"建槽那一下"的尖峰是有帮助的。

### 3.2 zip mod 与文件夹 mod 的性能差异（回答"标准玩家布局"的问题）

Everest 的加载路径（`Everest.Content.cs` / `Everest.Loader.cs` / `Everest.Relinker.cs`）：

| 方面 | zip mod（大多数玩家的布局） | 文件夹 mod（开发布局） |
|------|------------------------------|--------------------------|
| 每帧 update/render | **无差别**：资源加载后在内存中，帧内不读磁盘 | 无差别 |
| mod DLL | 从 zip 解压一次；之后走 `Mods/Cache` 的已重链接程序集缓存（`.sum` 校验），后续启动不再解压 | 同样走 `Mods/Cache` |
| 资源首次加载 | 从 zip **流式解压**；同一个 zip 的所有读取共用**一把锁**（`SynchronizedZipEntryStream`），并发加载会串行化；流 seek 会重开 entry 从头解压 | 直接读文件，无解压；但每个文件夹 mod 挂一个 `FileSystemWatcher`（热重载用） |
| 内存 | 保持打开的 `ZipArchive` + 资源索引（一个句柄/mod） | 每个 mod 一个 watcher/句柄 |

结论：**zip 本身不会导致"实体多时掉帧"**，帧内没有区别；差异只在加载期和资源重新读取
（进图/首次使用资源、以及 SRT 读档时对已 dispose 的虚拟资源 `Reload()`）。旧 HDD 上 zip 顺序
读甚至可能更快，弱 CPU 上解压会多一点；把 zip 全转成文件夹反而会多出几十个 watcher，不建议
盲目转换。`upstream`/`final`/归因数字都是在 **zip 标准环境**（52 个下载 mod 转 zip、用户自己的
开发 mod 保持文件夹）下测的，与玩家实际布局一致。

### 3.3 真实 TAS 回放验证（TAS 驱动实际输入，全程 + 第二面 A/B/C）

用 `StrawberryJamTAS-main/5_Grandmaster/grandmaster_heartside.tas`（Grandmaster Heartside 全图
TAS）做真实操作回放。为避免 CommunalHelper+CelesteTAS 在**主菜单/Overworld**（无 Player）时
的第三方 NRE（`Elytra.Input_UpdateGrab`，与 SRT 无关），harness 先 `LevelEnter.Go` 进 a0-Start
再开始 playback，结束时直接 `Environment.Exit`；CeilingUltra 保持加载、用 `TasEnableAll=false`
关闭其功能。

**结论修正：全程 TAS 没有 desync。** 之前看到的"a1 在 TAS 帧 315 而不是录制的 246"是**冻结帧
记账**：转场/加载/wipe 帧期间输入帧不推进，所以每次转场都会累积偏移（a1 315、a2 1071、
f3 22310…），但 24 个房间全部按路线通过、全程仅 1 次死亡（d3_07-Maya 处）。因此逐房间统计
以**实际进入/离开房间 + 墙钟帧**为准，不依赖 TAS 帧标签。

**全程 24 个房间的逐房间帧统计**（zip 环境、53 mods、silent 模式；完整表见
`docs/bench-baseline.md`）：所有房间 p50 都是 16.66–16.67ms（60fps）；除个别尖峰外 p99 17–19ms。
按 CPU p50 排，最重的是 **a1_18-Xplosives（4.02ms/帧）**，其次是 d1_09-xlibiza（2.65）、
f3_01-Hydro（2.34）、f2_02-Soloiini（1.88）、e1_06-tofu（1.76）。**帧尖峰最多的是
`f3_01-Hydro`（9 帧 >50ms，max 294.7ms）和 `d2_08-Aiden`（max 219.6ms）**，a1 的 max 是
89.2ms（1 帧 >50ms）——也就是说旧机最该担心的"大尖峰"未必是第二面，f3/d2 这类大房间更明显
（GC/资源加载/特定 mod 事件），这属于游戏+mod 侧，不是 SRT 常驻开销。

**第二面 A/B/C**（进 a1 后 TAS 从帧 246 跳播，真实输入；silent 模式）：

| 阶段 | 帧 p50 / p95 | 动作 | 备注 |
|------|--------------|------|------|
| A 无 savestate | 16.65 / 18.44 | — | 含进房首帧（305ms）|
| B 建 1 槽后继续玩 | 16.65 / 18.16 | save **777.6ms** | B 段 max 792.8ms = save 那一帧 |
| C 读档后继续玩 | 16.65 / 17.94 | load **93.8ms** | C 段 max 245.6ms = load + 随后 GC |

剔除 save/load 所在帧后，A/B/C 的稳态帧/Cpu 完全一致（p50 16.65ms、CPU p50 2.19–2.21ms）——
**持有 savestate 不影响 TAS 实际游玩中的每帧耗时**，与 §3.1 的 idle 归因一致；卡顿只在
save/load 那一帧（重房间首次 save 的 JIT 冷启动 ~0.78s，见 §2.5；load ~94ms）。

> **TAS 与 save/load 不要混在一次 run 里做**：在 TAS 播放中 load 会把游戏状态倒回，而输入帧
> 继续前进，之后必然错位（实测继续跑会回到 a0 并累积死亡）。所以"全程逐房间统计"必须由
> **一次纯播放的 clean run** 产生，A/B/C 单独跑。

**2GB heap + PreClone Off 下 SRT 开/关（a1，25s 长窗口，用户最关心的"平时游玩"）**：

| 条件 | 帧 p50 | p99 | max | >33ms | CPU p50 | CPU p99 | GC |
|------|--------|-----|-----|-------|---------|---------|-----|
| SRT 开（持有模组但不建 savestate） | 16.665 | 17.00 | 342.7（进房首帧） | 1 | 2.04 | 2.89 | 1/0/0 |
| SRT 完全禁用 | 16.666 | 16.93 | 327.5（进房首帧） | 1 | 2.02 | 2.66 | 1/0/0 |

→ **SRT 常驻（无 savestate）不是帧尖峰来源**：p99 差 0.07ms、CPU p50 差 1%，两边唯一 >33ms 帧都是
"进房首帧"；2GB 压力下没有额外 GC。也就是说旧机上游玩时的尖峰来自**游戏/其它 mod 的内容**（例如
f3_01-Hydro 的 9 帧 >50ms、d2_08-Aiden 的 219.6ms），不是 SRT。

> 静默模式验证：游戏窗口最小化/移出屏幕后，`frames-a1_18-Xplosives-a` 的帧 p50 仍为 16.67ms、
> CPU p50 2.14ms（前台 2.20ms，差 <5%）→ benchmark 数据不受静默模式影响。

## 4. 已知限制 / 后续可做

- **pre-clone 的取舍是本质的**：读档速度 vs 常驻实体图内存二选一，目前用 Auto 按
  `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes`（≈物理内存上限，含容器限制）选择；
  若要两者兼得，需要把模板压缩成“实体清单 + 重建 Tracker”的方案（`docs/audit.md` B1 档 3）。
- **`ForceSetField`**（`DeepCloneTo` 的 root 类型链上的 readonly 字段，如 Level/Scene 的
  `LoadOverrides`/`EntityLoaders`；其它 `DeepCloneToShared` 调用点也会经过）仍是反射实现，
  开销小但存在跨线程改写 `FieldInfo` 属性的理论竞态；`DeepClonerExprGenerator` 的 readonly
  简单字段隐患（D3）在本机 `IsSafeVariant()==false` 下不会触发，平台差异下可能变成崩溃。
  统一改成 IL 生成的方案见 `docs/msil-plan.md`。
- **DynData / DynamicData 探测**仍是每对象成本（CWT 查找 + 逐基类查找），没有在本轮优化中改动。
- **第三方 mod 支持仍是白名单式**：FrostHelper 无专门支持；其它 SJ helper 的 hook/静态状态
  结论见 `docs/sj-compat.md` §4/§7。mod 的 `Add/RemoveReturnSameObject*` 注册必须在首次
  save/load 之前（见 §2.3 D14）。热重载后 DeepCloner 类型缓存仍会钉住旧 mod 类型（audit D15，
  内存量很小，未处理）。裸 `EventInstance` 字段（`FrostHelper.ToggleSwapBlock`、
  `SJ2021.WonkyCassetteBlockController` 等）的通用重建**未做**（生命周期风险大于收益，见
  `docs/sj-compat.md` §6）。
- **异常兜底边界**：save/load 主路径已覆盖；hotkey 派发（`HotkeyConfigUi`）与 `GC.TryStartNoGCRegion`
  冲突等边界仍未兜底。
- **会话内首次 save/load 的冷启动开销未消除**：入口房间首次 save ~380ms、3451 实体的第二面
  首次 save **719–741ms**、首次 load ~111ms，后续稳定在 41ms/33ms（本机）。根因是 JIT（不是
  codegen），两次预热方案（后台类型预热、进图丢弃式 clone）分别因收益太小 / 会污染 DeepCloner
  缓存导致后续 save 失败而撤回（详见 §2.5）。后续方向：隔离预热（独立 AssemblyLoadContext 或
  可丢弃的缓存层）或接受"每次会话一次、且 normal 模式 save 本身带 freeze"。
- **benchmark 结果文件**：harness（SrtBench/脚本/原始 JSON）在仓库外 `D:\DSH\Celeste\ench`；
  仓库内保留 `docs/bench-baseline.md`（结果）与 `docs/bench-setup.md`（复现方法）。
