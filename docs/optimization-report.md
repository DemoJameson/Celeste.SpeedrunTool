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
  SwitchSlot/IsSaved/SetPreCloneEnabled/SetCloneWarmUpEnabled/...`）供 TAS/工具/benchmark 使用。

### 2.5 冷启动预热（首次 save/load）

profiling 显示**会话内第一次** save 比后续慢约 360ms（`cloneLevel` 216ms + `saveActions`
142ms），第一次 load 的 `loadActions` 也有 ~62ms，来源是 DeepCloner 为运行期遇到的每个类型做
`Expression.Compile` 的类型 cloner codegen（隔离微基准 ≈0.19ms/类型，累计数百个类型）+ 首次
JIT；之后 save/load 稳定在 ~13/11ms。

- 新增 `CloneWarmUp`：在第一次 `Level.Begin`（`InitSlots` 之后）用 `Task.Run` 后台遍历
  **纯类型元数据** —— 所有 `Entity`/`Component`/`Renderer` 子类、被存档的静态字段/模块字段、
  `Level`/`SaveData`/`Session`/各 mod 的 Session/SaveData 根、以及它们字段类型闭包 ——
  调用 `DeepCloner.WarmUpCloner` / `WarmUpCloneToCloner` 预生成（不调用）cloner 并写入现有
  cache。**不读取任何活对象**，因此可以安全地在后台线程跑；真实 save 时直接复用同一份 cache。
- 只预热 codegen，不强制 JIT：首次 save 仍要为每个 delegate 付一次（小得多的）JIT，但剩下的
  远低于原来的 360ms；预热本身藏在进图加载屏期间（只占一个后台核心）。
- 类型数上限 6000，避免为完全用不到的类型保留过多 cloner 缓存；可用 ModInterop
  `SetCloneWarmUpEnabled(false)` 关闭（benchmark A/B 用）。

## 3. 验证（进行中，数字由 bench 环境回填）

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
- **benchmark 结果文件**：JSON/文档由 bench-env 在仓库外提供（本仓库 `docs/` 只保留分析与本报告）。
