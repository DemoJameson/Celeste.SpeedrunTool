# 分支 review：`perf/savestate-optimization`（vs `master`）

> 只读 review，未修改源码。范围：`git diff -w --ignore-cr-at-eol master..HEAD`（19 个 commit，忽略纯 EOL/BOM 噪声）。
> 重点按要求覆盖：① `DeepClonerUtils` 处理器重写（ProcFlags）；② `StateManager`/`SaveLoadAction` 异常恢复；③ `Snapshot` 从 BeforeRender 改到 Render 后回读；④ `PreCloneEnabled` 关闭路径；⑤ EOL/BOM。
> 相关文档：`docs/audit.md`（原始审计）、`docs/audit-instrumentation.md`（插桩）、`docs/sj-compat.md`（SJ 兼容）、`docs/msil-plan.md`（未实施的 MSIL 方案）。

---

## 0. Top issues（按严重度）

| # | 严重度 | 位置 | 问题 |
|---|---|---|---|
| 1 | 🔴 | `StateManager.cs:185-187` → `:268-281`（RecoverFromFailedSave 未复位 `ClearBeforeSave`）+ `SaveLoadAction.cs:275-282` | 保存失败后 `ClearBeforeSave` 残留 `true`；之后按 Clear State 时 `OnClearState(true)` 用已失效的 id 查 `AllSavedValues`（或 dict 已为 null）→ **KeyNotFoundException/NRE 逃出 hotkey（无 try/catch）→ 崩溃**。`SavedByTas` 也可能残留设置 |
| 2 | 🟠 | `SaveLoadAction.cs:275-282` + `SaveSlotsManager.cs`（`RequireReInit`） | `OnClearState(clearBeforeSave:true)` 用 `dict[action.dictionaryId]` 无保护：当 mod 在运行期增删 SaveLoadAction 后再次保存（`InitSlots` 尚未重排 id）会 `KeyNotFoundException` → 保存失败（现在会被 recovery 吞掉，但用户看到失败） |
| 3 | 🟠 | `Snapshot.cs:82-155, 176-198` | 改为“整帧渲染后回读 backbuffer”：**存档发生在切屏/wipe 过程中时会截到黑屏/半屏**（旧实现会重新渲染房间）；且快照包含 HUD/overlay（cosmetic） |
| 4 | 🟠 | `StateManager.cs:391-408`（RecoverFromFailedLoad） | `preCloneTask = null` 时**没有等待/观察正在跑的 preclone 任务** → 下一次 save/load 可能与旧任务并发克隆同一 `savedLevel`（写 `ClonedEventInstancesWhenPreClone`/`VirtualAssets`、`DynDataUtils.IgnoreTypes` HashSet）→ 重复条目/竞态 |
| 5 | 🟡 | `SaveLoadInterop.cs:103-108,131-134` | 非 Entity 分支的 `AddReturnSameObjectType` 和 `AddReturnSameObjectProcessor` **每次调用都** `DeepCloner.ClearCaches()`（清 TypeCache/KnownTypes）：mod 循环注册 N 个类型 → N 次全清 → 下次 SL 重新 codegen（≈0.19ms/类型，见 audit 附录 1）。建议批量 API 或“dirty 标记 + 只在 SL 开始前清一次”（Entity 分支走 `IgnoreSaveLoadComponent.Ignore`，不清缓存，无此问题） |
| 6 | 🟡 | `SaveLoadInterop.cs`（Remove*） | `RemoveReturnSameObjectType/Processor` **没有**清缓存 → 运行期移除注册不生效（与 Add 不对称） |
| 7 | 🟡 | `DeepClonerUtils.cs:120-151` | ProcFlags 的 `[ThreadStatic]` 只缓存**最后一个类型**；对象类型交替时 Miss 率高。**实测：纯分派 mock 中新版比旧版慢 ~0.2ms/9.4k 对象**（详见 §2），赢面完全依赖“省掉 58–118ns/容器对象的反射”。需要在真实房间用 `SRT_PROFILE` 做 A/B，不能只靠推断 |
| 8 | 🟡 | `DeepClonerUtils.cs:200-360`（注释） | 注释声称“cloning is serialized by design (all entry points wait for a running pre-clone task)”**不完全成立**（跨槽操作可交错；recovery 路径会让任务继续跑；本分支新公开的 `SaveLoadInterop.DeepClone` 可被 mod 在任意线程调用）。去掉 per-object lock 本身没有回归（旧 lock 也保护不了那些共享集合），但注释应更正，且剩余共享状态应加锁/改成并发容器 |
| 9 | 🟡 | `StateManager.cs:662-665` + `SpeedrunToolSettings.cs:36-47` | `PreCloneEnabled` 可在运行期由 interop 修改，但无同步/无“仅在空闲时”约束：在 preclone 运行中关闭 → 任务仍活着（内存不释放）、`OnPreCloneEntities` 不再执行（`ClonedEventInstancesWhenPreClone` 等不清） |
| 10 | 🟡 | `StateManager.cs:548-628`（UpdateTimeAndDeaths） | 新增的 `Areas_Safe` 越界保护在 mod 地图（AreaKey.ID 超出当前 `Areas_Safe.Count`）时会**静默跳过** per-area 时间/死亡数合并（旧版会抛异常）；Session 级 Time/Deaths 仍正常。需在 SJ 图上确认“保留游玩时间”是否仍符合预期 |
| 11 | 🟡 | `SaveLoadAction.cs:377-395`（WarmUpOnFirstLevel） | warm-up 从“首次 save”挪到“首个 Level Begin”是对的，但：a) 无 `ModSettings.Enabled` 判断（SRT 被禁用也扫描全 mod 类型）；b) 失败后已退订，不会重试（首次 save 会再试，可接受） |
| 12 | 🟢 | `SaveLoadProfiler.cs` | 失败路径不调用 `End()`（无 profile 行）；`Line`/`Stopwatch` 为 static（仅主线程用，但 `LogBackgroundSection` 在后台线程写 `Logger`，建议确认 Everest logger 线程安全）；`Enabled` 只读一次 env（bench 必须在启动前设置） |
| 13 | 🟢 | `SpeedrunTool.csproj` | `NoWarn CS0649` 全局压制“字段未赋值”警告（ModInterop 生成字段），可能掩盖真实漏赋值；建议只在 ModInterop 目录用 `#pragma` |
| 14 | 🟢 | `SpeedrunToolSettings.cs:36-47` | `PreCloneMode.Auto` 以 `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes <= 10GB` 判定低内存；8GB 目标机上正确关闭 preclone ✓；但若运行时报告 `long.MaxValue`/容器限制异常值，会错误地开启（可加 `totalAvailableMemory > 1GB` 下界） |

**已验证没有问题的项（无需改）**
- ProcFlags 重写的分支顺序/条件与旧 `is` 链**语义等价**（VirtualAsset→Scene→SoundSource→CassetteBlockManager→EventInstance→WeakReference→WeakReference\<T\>→CWT→Entity→CustomDeepCloneObject；Post 的 HashSet\<Component\>→复杂 HashSet→IDictionary→DynData 顺序不变；`Count >= 0`→`> 0` 仅去掉空集 Clear 的空操作）。
- 去掉 per-object `lock(sourceObj)` 不会引入新的正确性回归（旧锁锁的是被克隆的游戏对象，游戏代码从不同步这些对象；共享集合本来就没有被它保护）。
- `Snapshot` 的纹理泄漏修复正确且完整：`ClearAll`/替换快照/`Unload` 都 Dispose（对应 audit D11）。
- `PreCloneEnabled=true` 与 `false` 两条 load 路径都能工作：false 时 `SetSharedDeepCloneState(null)` → `DeepCloneToShared` 自建 state → 同步整图克隆；`SaveLoadAction` 的 `DeepCloneShared` 会复用该 state，语义一致。
- `git diff -w --ignore-cr-at-eol` 确认 3 个 EOL/BOM commit **没有夹带内容改动**（`e8081ac`/`38b4c64`/`a550d27`）；`#nullable enable annotations` 两个文件（`HookHelper.cs`/`HotkeyRebase.cs`）只是注解上下文，无行为变化。
- `.gitignore` 锚定修复（`/Dialog`、`/Graphics`）避免了误忽略 mod 资源目录 —— 与新增 CJK 字体页（`Dialog/Fonts/chinese_*.png`）配套，正确。
- `docs/audit.md` 的 D5（action 增删导致 `AllSavedValues == null` → NRE）已被 `EnsureValueDictionaries` + `AreValuesComplete` + `IsAllowLoad` 版本检查覆盖（读档被明确拒绝而不是 NRE）。

---

## 1. 详细 findings

### 1.1 🔴 `ClearBeforeSave` 残留 → 后续 ClearState 可能 NRE 崩溃
- 位置：`StateManager.cs:185`（`ClearBeforeSave = true` 后紧接 `ClearStateImpl`）、`:187`（只在成功路径复位）、`:268-280`（`RecoverFromFailedSave` 只复位 `State/freezeType/FreezeTimer/sharedState`，**没有复位 `ClearBeforeSave`**）、`SaveLoadAction.cs:275-282`（`OnClearState(true)` 直接 `dict[action.dictionaryId]`）。
- 触发：任何一次“已有存档时再保存”在 `ClearStateImpl` 内抛异常（例如 issue #2 的 KeyNotFound、mod `clearState` 回调抛异常、`ReleaseEventInstances` 的 FMOD 调用抛异常）→ recovery 清理 `AllSavedValues`（`ClearStateImpl` 的 else 分支/其内部异常导致 `AllSavedValues` 为 null）→ 用户之后按 Clear State（此时 `IsSaved == false`，hotkey 允许）→ `OnClearState(ClearBeforeSave=true)` 读到 null dict → NRE → **hotkey 派发无 try/catch（`HotkeyConfigUi.cs:199-208`）→ 游戏崩溃**。
- 修法：
  1. `RecoverFromFailedSave()` 开头 `ClearBeforeSave = false;`（以及 `SavedByTas = false; LoadByTas = false;`，避免 stale 标志影响下次 save 的 `BetterCasualPlay`/TAS 判定）。
  2. `OnClearState(clearBeforeSave:true)` 改成 null/TryGetValue 安全：
     ```csharp
     if (clearBeforeSave && AllSavedValues is { } dict) {
         foreach (var action in SharedActions)
             if (dict.TryGetValue(action.dictionaryId, out var byType))
                 foreach (var pair in byType) pair.Value.Clear();
         slotInitialized = true;
     }
     ```
- 严重度：🔴（崩溃，且恢复路径本身会“再触发一次”同一 NRE —— 被 recovery 的 try/catch 吞掉但状态仍脏）。

### 1.2 🟠 `OnClearState(true)` 的 positional dictionaryId 可能过期
- 位置：`StateManager.cs:182-187`（`if (IsSaved) ClearStateImpl` 在 `SaveLoadAction.InitSlots()` **之前**）、`SaveLoadAction.cs:275-282`、`:439-452`（InitSlots 里才重排 id）。
- 触发：存档 A → 某 mod 运行期 `RegisterSaveLoadAction`/`Unregister`（`needInitializeDictionaryId = true`）→ 再存档 → `ClearStateImpl(true)` 里使用**旧 id**（新 action 的 `dictionaryId == 0`）→ `dict[0]` KeyNotFound → save 失败（recovery 后用户看到 “Failed to Save: please check the log”）。
- 修法：把 `SaveLoadAction.InitSlots()` 提到 `SaveStateImpl` 的 `if (IsSaved) ClearStateImpl` 之前（InitSlots 幂等）；或 `OnClearState` 用 TryGetValue + `EnsureValueDictionaries`（同 1.1 的改法）。
- 严重度：🟠（低概率：运行期增删 action 的 mod 很少；但一旦发生，表现为“保存永远失败”）。

### 1.3 🟠 Snapshot 回读：时机与内容
- 位置：`Snapshot.cs:176-198`（`On.Celeste.Level.Render += CaptureSnapshotsAfterRender; orig(self)` 后捕获）、`:82-155`（`GetRenderTargets().Length > 0` 才重渲染，否则直接 `GetBackBufferData`）。
- 风险：
  1. **切屏/wipe 期间保存**：SRT save 后立刻 `WaitingEntity.Render` → `DoScreenWipe`，而快照是在“save 后的下一次 `Level.Render` 末尾”读 backbuffer；若此时 wipe 已经开始绘制（或 save 本身发生在 transition 的 wipe 中），backbuffer 可能是黑屏/半屏。旧实现（BeforeRender 里重渲染房间）不受影响。**建议 bench 明确测：在电梯/门/切屏 wipe 中存档，检查快照是否黑**；修法：`if (level.Wipe is { Percent: > 0f })` 时走“重渲染”分支，或把快照捕获改到 wipe 创建前（`Level.BeforeRender` 且检查 `Wipe == null`）。
  2. **包含 HUD/overlay**：新实现是整帧回读，HUD、字幕、Tooltip 会进入快照（旧实现只渲染房间）。cosmetic，可接受，但要知道这是行为变化。
  3. **`GetRenderTargets()` 启发式**：若某个 mod 在 `Level.Render` 结束时仍绑定 RT（少见），会走重渲染分支 → 所有 mod 的 `Render` hook 再执行一次（副作用/性能）；若 RT 已解绑但 backbuffer 是上一帧内容（某些驱动/FNA 行为），会截到旧帧。需要真机验证（尤其 FNA 的 `GetBackBufferData` 在 Present 前是否可靠）。
  4. **`readbackBuffer` 常驻**：1080p 8.3MB、4K 33MB LOH，且 `EnsureBufferSize` 只增不减、`[Unload]` 不释放；对“8GB 目标机”是可接受的 8MB 代价，但 4K 下要留意；建议在 `Unload`/分辨率变化时清掉（`readbackBuffer = null; thumbnailBuffer = null;`）。
  5. 缩略图长边 480（`MaxSnapshotLongEdge`）在 1080p 下 ≈ 480×270，UI 单元约 620×335，略糊；可提到 640。
- 其它：`CaptureSnapshots` 里 `instance.snapshotTex?.Dispose()` 在 `GetBackBufferData` 成功后执行 ✓；`finally` 只在 `renderedHere` 时 `SpriteBatch.End` ✓。

### 1.4 🟠 `RecoverFromFailedLoad` 与未完成的 preclone 任务竞争
- 位置：`StateManager.cs:391-408`。
- 触发：load 在 `SetSharedDeepCloneState(preCloneTask?.Result)` 之后、`PreCloneSavedEntities()`（load 末尾）之前抛异常（如 `UnloadLevel` 抛 OOM、`OnLoadState` 中 mod 抛）→ 此时**本槽的旧 preclone 任务可能仍在跑**（例如上一次 load 末尾刚启动），recovery `preCloneTask = null` 直接丢弃引用 → 之后用户再 save/load：`PreCloneSavedEntities` 又启动一个新任务 → 两个线程同时 `DeepClone(savedLevel.Entities)`：
  - 写 `SaveLoadAction.ClonedEventInstancesWhenPreClone`（List）→ 可能重复/损坏；
  - 写 `GraphicResourcesHandler.VirtualAssets`（HashSet，非线程安全）→ 可能损坏；
  - `DynDataUtils.IgnoreTypes`（HashSet）读写在两线程 → 竞态。
- 修法：recovery 里 `try { preCloneTask?.Wait(); } catch { }` 后再置 null（或保留引用直到下次 `PreCloneSavedEntities` 覆盖；后者更简单：**不要清 `preCloneTask`**，下一次赋值会覆盖，旧任务完成后自然释放）；或给 preclone 加 `CancellationToken`。
- 严重度：🟠（触发条件苛刻，但后果是列表损坏 + 重复 FMOD 实例）。

### 1.5 🟡 ProcFlags 的性能方向与残余开销（含实测）
见 §2。要点：
- 省掉的成本是实打实的：`IsHashSet+IsIDictionary` 在泛型容器对象上 **58–118 ns/对象**（`List<T>` 61–88、`Dictionary<K,V>` 83–118、`HashSet<T>` 58；非泛型类型 2ns）；两次 `lock` ≈30–40ns/对象；`IsWeakReference`/`IsConditionalWeakTable` 对泛型类型还有 `GetGenericTypeDefinition/IsAssignableFrom` 开销。
- 新增成本：每个对象 2 次 `GetProcFlags`（Pre/Post），thread-static 只记 1 个类型，交替类型会 Miss → ConcurrentDictionary 查找 ~15–25ns。
- **分派-only mock（9.4k 对象）实测：旧 1.08–1.30ms，新 1.29–1.47ms（新版反而慢 ~0.2ms）**，因为 mock 的旧链太便宜（3 个 `is` + 少量反射）；真实 Celeste 图里 List/Dictionary 占比高，旧链的反射成本（58–118ns/容器）应占主导，预期净收益。**结论：方向正确，但必须用 `SRT_PROFILE` 在真实房间做 before/after 确认；若发现回归，把 thread-static 单槽改成 4 槽直接映射数组（`RuntimeHelpers.GetHashCode(type) & 3`）即可把 Miss 率降下来。**
- 其它：`ClearProcFlagsCache()` 已挂在 `OnAssetReload.AfterReload`（`OnAssetReload.cs:32`）✓，热重载时不会钉住旧 mod 类型。
- 注释修正（issue #8）：把 “all entry points wait…” 改成 “在正常路径上由 pre-clone 的等待串联；recovery 路径/第三方 `SaveLoadInterop.DeepClone` 调用不是串行的”，并在 `DynDataUtils.IgnoreTypes`、`SaveLoadAction.ClonedEventInstances*`、`GraphicResourcesHandler.VirtualAssets` 上加锁或改并发容器。

### 1.6 🟡 `PreCloneEnabled` 关闭路径核查
- 已验证：`LoadStateImpl` 的 `SetSharedDeepCloneState(null)` → 同步克隆；`DeepCloneToShared` 内部 `InitSharedDeepCloneState()` 重新建 state；`SaveLoadAction` 的 `DeepCloneShared` 复用该 state → 语义一致。
- 遗留：
  1. 运行期关闭（interop）时若 `preCloneTask` 正在跑：任务继续（内存不释放），且 `OnPreCloneEntities()` 不再被调用 → `ClonedEventInstancesWhenPreClone`/`VirtualAssets` 的清理时机改变（`clearState` 仍会清；`VirtualAssets` 也会在 load 的 loadState 清）。建议 setter 里：`if (!value) { try { preCloneTask?.Wait(); } catch { } preCloneTask = null; }`。
  2. `PreCloneSavedEntities` 在 disabled 时直接 `preCloneTask = null`，**不**做 `OnPreCloneEntities()`；如果之前有任务，旧 state 的 clone 图会一直活到 GC（无引用后自然回收，OK）。
  3. Auto 判定（`SpeedrunToolSettings.cs:36-47`）在 8GB 机器上关闭 preclone → **加载会变慢**；对 bench-env 来说这是重要变量：测“卡顿”与“内存”时要分别跑 Auto/On/Off。
- 严重度：🟡。

### 1.7 🟡 `warm-up` 迁移的副作用
- 位置：`SaveLoadAction.cs:377-395`。
- 正面：把“扫描全 mod 类型/静态字段”的一次性开销从首次 save 移到首个 Level Begin（玩家已在读图）✓。
- 注意：
  1. 未判断 `ModSettings.Enabled`/`ModSettings.Hotkeys`；SRT 完全禁用时也会扫描（一次性，影响小）。
  2. `WarmUpOnFirstLevel` 先退订再 try；若 `InitSlots` 失败不会重试，首次 save 会再执行一次 `InitSlots`（会被 `SaveStateImpl` 的 catch 捕获 → 保存失败）→ 属于 D1 类但已有 recovery。
  3. warm-up 里 `InitSlots()` 会 `RequireReInit()` 所有槽（此时一般无存档，安全）。
- 严重度：🟢。

### 1.8 🟡 `UpdateTimeAndDeaths` 的越界保护语义
- 位置：`StateManager.cs:548-628`。
- 现状：`areaKey.ID < savedSaveData.Areas_Safe.Count && areaKey.ID < SaveData.Instance.Areas_Safe.Count && (int)areaKey.Mode < ...` 才写 per-area 时间/死亡；不再抛 `ArgumentOutOfRangeException`（对应 D12）✓。
- 注意：`Areas_Safe` 是 `List<AreaStats>`（Cecil 实测 SaveData 上为 `List<AreaStats>` 属性，`_cached_Areas_Safe` 为后备）→ 用 `Count` 做上界是“列表索引”语义，对 mod 图（AreaKey.ID 可能超出当前列表长度）会静默跳过。**如果 “读档不丢失游玩时间” 对 SJ 图重要，需要在 bench 里确认 per-area 统计是否更新；Session 级 Time/Deaths 是一定更新的。**
- 严重度：🟡（数据准确性，不影响稳定性）。

### 1.9 🟢 `UnloadLevel` 的 catch (Exception)
- 位置：`StateManager.cs:497-546`。
- 现在任何实体 `Removed`/Tracker 移除异常都只记 `Logger.Warn`（类型+消息，无堆栈）并继续 → 避免单个坏 mod 中断 load ✓；缺点是隐藏 mod bug，且若 `Tracker.EntityRemoved` 抛异常，live level 的 tracker 会短暂不一致（随后被 clone 覆盖）。建议：对非 `NullReferenceException` 的异常调一次 `Logger.LogDetailed`（同类异常只记第一次，避免刷屏）。

### 1.10 🟢 `SaveLoadInterop` 新 API 的副作用
- `SaveState(string slot)`/`LoadState(string slot)` 先 `SwitchSlot`（成功则当前槽已改变）再执行；如果 save/load 失败，**当前槽停留在新槽**。文档没写；bench/TAS harness 需要注意。
- `SetAllowSaveLoadWhenWaiting`/`SetPreCloneEnabled` 直接改静态状态，无“仅主线程/空闲时”约束；建议文档注明。
- `DeepClone(object)`（原有 API）依赖 `DeepCloneShared` 的静态 `sharedDeepCloneState` → **若 mod 在非主线程调用，会与主线程的 save/load 共享同一个 Dictionary**（无锁）→ 数据竞争。建议在 XML doc 里要求“只能主线程调用”。
- 严重度：🟢。

### 1.11 🟢 `SaveLoadProfiler` 细节
- `End()` 只在成功路径调用；失败路径无 profile 行（不影响功能）。
- `Line`/`Stopwatch` static：主线程 ≤1 层嵌套（状态机保证 save/load 不重入）；`LogBackgroundSection` 只读 `Stopwatch.Frequency` 并调用 `Logger.Info`（后台线程）——若 Everest logger 非线程安全，建议后台线程只缓存字符串、回主线程再 log。
- `Enabled` 用 env var 一次性读取 → bench 必须在启动 Celeste 前设置（写进 `docs/audit-instrumentation.md` 的使用说明）。

### 1.12 🟢 新 ThirdParty 支持代码（Lead 落地）核查
- `YetAnotherHelperUtils.cs`：`On.Celeste.Spikes.OnCollide -= onCollideHook; += onCollideHook` 幂等（`OnCollideHook` 为 private static，`Delegate.CreateDelegate` 取到；SRT 既有 `PandorasBoxUtils`/`SpringCollab2020Utils` 用同样手法）✓；`SpikeHooked` 用私有静态字段 `SetValue` 校正 ✓；在 `ThirdParty.Support()` 中注册，`loadState` 顺序在 `SupportSimpleStaticFields`（同 order、先注册）之后 → 先恢复静态字段再校正 ✓。
- `CollabUtils2SavestateUtils.cs`：`unhook()`/`hook()` 是 private static 无参方法，`type.InvokeMethod("unhook")` 走 `FastInvoker`（`InvokeMethodImpl(null, type, …)`，空参数数组）✓；类型/方法缺失时跳过 ✓；GM Heartside 未使用该实体（bin 0 命中）→ 只影响其它 collab 图 ✓。
- 两者都在 `[Initialize]` 调 `Tracker.AddTypeToTracker`，会给每张图增加一个 per-type 跟踪列表（很小）。
- 严重度：✅。

### 1.13 🟢 EOL/BOM 与 csproj
- 3 个纯 EOL commit 用 `git diff -w --ignore-cr-at-eol` 验证无内容变化 ✓；`#nullable enable annotations` 仅注解上下文 ✓。
- `NoWarn` 新增 `CS0436`（Publicizer 的 `IgnoresAccessChecksToAttribute` 与 MonoMod 重复）合理；`CS0649` 会掩盖“字段从未赋值”——建议只对 `Source/ModInterop/**` 用 `#pragma warning disable CS0649`。
- `.gitignore` 锚定修复必要（原规则会忽略 `SpeedrunTool/Dialog`、`SpeedrunTool/Graphics`）；新增 CJK 字体页已入库 ✓。

---

## 2. 微基准（用于验证/反驳“处理器重写”的收益）

环境：Windows / .NET 8 / Release / x64；方法与 audit 附录 1 相同（把 vendored DeepCloner 源码复制到临时项目 + 引用游戏目录 `MonoMod.Utils.dll`/`Mono.Cecil.dll`）。

| 实验 | 结果 |
|---|---|
| 空处理器（raw `DeepClone`） | 1.5–1.7 ms / 14.8k 对象 |
| 旧处理器形状（2×lock + is 链 + 每对象 `IsHashSet`/`IsIDictionary` + DynData/CWT 探测） | **4.0–5.9 ms**（≈2.7–3.5×，与提交信息一致） |
| `IsHashSet+IsIDictionary` 单次成本（旧代码每个对象都跑） | `List<T>` 61–88 ns、`Dictionary<K,V>` 83–118 ns、`HashSet<T>` 58 ns、非泛型类型 2 ns |
| 分派-only mock（9.4k 对象，无 DynData）：旧 vs 新（ProcFlags） | 旧 1.08–1.30 ms；**新 1.29–1.47 ms**（新版在“纯类对象多、容器少”的图上反而慢 ~0.2ms） |

**解读**：旧实现的主要成本是“每个泛型容器对象 58–118ns 的反射 + 每对象 2 次 lock”；新实现把这些去掉，但引入“每对象 2 次 flags 查询（thread-static 只记 1 个类型）”。真实 Celeste/SJ 房间中 `List<T>`/`Dictionary<K,V>`（Tracker 的 per-type List、ComponentList、Sprite 动画、mod 容器）占比高 → 预期净收益；但**必须用 `SRT_PROFILE` 在真实房间做 before/after**（该分支已内置 profiler；`SRT_PROFILE=1` 启动游戏即可，见 audit-instrumentation）。若发现回归，最小改法：thread-static 单槽 → 4 槽直接映射数组，或对 `type.IsClass && !type.IsGenericType && !typeof(Entity).IsAssignableFrom(type)` 等常见路径先做快速判断（避免查表）。

---

## 3. 建议的修复顺序

1. **1.1（🔴）+ 1.2（🟠）**：`RecoverFromFailedSave` 复位 `ClearBeforeSave`/`SavedByTas`/`LoadByTas`；`OnClearState(true)` 改 null-safe + TryGetValue；`SaveStateImpl` 把 `InitSlots()` 提到 clear-before-save 之前。
2. **1.4（🟠）**：`RecoverFromFailedLoad` 不要直接丢 `preCloneTask`（保留引用或等待后再清）。
3. **1.3（🟠）**：Snapshot 在 wipe/transition 下回退到“重渲染房间”；`Unload` 清 `readbackBuffer/thumbnailBuffer`；缩略图长边考虑 640。
4. **1.5/issue#5/#6（🟡）**：`AddReturnSameObject*` 的 ClearCaches 改批量/延迟；`Remove*` 对称清缓存；ProcFlags 单槽→多槽（若实测回归）。
5. **1.6/1.11/1.12（🟡）**：`PreCloneEnabled` setter 等待任务；profiler 后台线程日志方式；文档补充。

## 4. 我无法静态验证、需要 bench 的事项

- `GetBackBufferData` 在“`Level.Render` 内、Present 前”读到的 backbuffer 是否稳定（FNA/驱动差异）；存档在 wipe/transition 中时快照是否黑。
- ProcFlags 在真实房间的净收益（`SRT_PROFILE` 的 `save`/`load` 行、`preclone` 行）。
- `UpdateTimeAndDeaths` 在 SJ mod 图（AreaKey.ID 较大）下 per-area 时间/死亡是否更新。
- `PreCloneMode.Auto` 在 8GB 机器上的实际内存/加载时间 trade（bench 应分别跑 Auto/On/Off 三组）。
- 运行期增删 SaveLoadAction 的真实 mod 场景（1.1/1.2 的触发条件）——可用 `SaveLoadInterop.RegisterSaveLoadAction` 写一个测试 mod/命令触发。

### 1.14 ✅ 内置 action 拆分（`SaveLoadAction.BuiltinActions.cs`）等价性 —— 已用脚本逐方法验证
用 `git show master:...SaveLoadAction.cs` 与 `BuiltinActions.cs` 逐方法做“去掉所有空白后 md5 对比”：
- `SupportSimpleStaticFields`、`SupportModModuleFields`、`FixSaveLoadIcon`、`BetterCasualPlay`、`SupportExternalMember`、`SupportCalcRandom`、`SupportSettings`、`SupportMInput`、`SupportInput`、`SupportAudioMusic`、`FixVertexLight`、`ExternalAction`、`SupportModSessionAndSaveData`、`SaveStaticMemberValues`、`LoadStaticMemberValues` → **逐字节相同（忽略空白）**；
- `ReleaseEventInstances` → 唯一差异是文件末尾 `}}` 合并成一行（方法体相同）。
- `InitActions()` 里各 `InternalSafeAdd` 的调用顺序与 master 完全一致（顺序决定 loadState 执行次序，见 audit 对 `SupportSimpleStaticFields` 先于 ThirdParty 的依赖）。
结论：**拆分本身是纯移动，无行为变化**；行为变化只来自本分支明确修改的 `OnSaveState/OnLoadState`（`??=` + `EnsureValueDictionaries`）、`InitSlots`（`ActionsVersion`）、`InitFields`（括号修复）、warm-up 迁移、以及新增的 `IsAllowLoad` 版本检查。

### 1.15 ✅ `InitFields` 的优先级 bug 修复正确
`SaveLoadAction.cs:545-553` 现在的过滤是 `... && (IsSameOrSubclassOf(Entity) || Component || Renderer)`，与 audit D4 指出的 `&&/||` 优先级问题一致修复；`type.IsSubclassOf(...)` 的异常仍由外层 try/catch 处理。

---

## 5. 建议 patch（文本形式，未改源码）

### 5.1 修 issue #1（`ClearBeforeSave` 残留）+ 复位 TAS 标志
`SpeedrunTool/Source/SaveLoad/StateManager.cs` `RecoverFromFailedSave()`：
```csharp
private void RecoverFromFailedSave() {
    Logger.Warn("SpeedrunTool/SaveState", "Recovering from a failed save...");
    State = State.None;
    freezeType = FreezeType.None;
    Engine.FreezeTimer = 0f;
    ClearBeforeSave = false;   // <-- 新增：避免残留导致后续 ClearState 走 clearBeforeSave 分支
    SavedByTas = false;        // <-- 新增
    LoadByTas = false;         // <-- 新增
    DeepClonerUtils.ClearSharedDeepCloneState();
    try { ClearStateImpl(hasGc: false); }
    catch (Exception exception) { Logger.LogDetailed(exception, "SpeedrunTool"); }
}
```

### 5.2 修 issue #1/#2 的根源：`OnClearState` 做成 null/旧 id 安全
`SpeedrunTool/Source/SaveLoad/SaveLoadAction.cs` `OnClearState`：
```csharp
internal static void OnClearState(bool clearBeforeSave = false) {
    if (clearBeforeSave && AllSavedValues is { } dict) {
        foreach (SaveLoadAction saveLoadAction in SharedActions) {
            if (!dict.TryGetValue(saveLoadAction.dictionaryId, out var byType)) {
                continue; // id 过期（运行期增删过 action）或该槽未初始化
            }
            foreach (KeyValuePair<Type, Dictionary<string, object>> pair in byType) {
                pair.Value.Clear();
            }
        }
        slotInitialized = true;
    }
    else if (!clearBeforeSave) {
        AllSavedValues = null;
        slotInitialized = false;
    }
    foreach (SaveLoadAction saveLoadAction in SharedActions) {
        saveLoadAction.clearState?.Invoke();
    }
}
```
> 注意：`clearBeforeSave==true` 但 `AllSavedValues==null` 时什么都不做（保持 `slotInitialized` 不变），比 NRE 安全。

### 5.3 消除 issue #2 的触发窗口（可选）
`StateManager.SaveStateImpl` 里把 `SaveLoadAction.InitSlots()` 提到 `if (IsSaved) { ClearBeforeSave = true; ClearStateImpl(...); }` **之前**（`InitSlots` 幂等；这样 clear-before-save 时 id 一定是最新的）。

### 5.4 修 issue #4（recovery 与 preclone 任务竞争）
`StateManager.RecoverFromFailedLoad`：不要直接把引用丢掉
```csharp
// 旧任务若还在跑，等它结束（异常只记录），避免下一次 SL 再启动一个任务并发克隆同一 savedLevel
try { preCloneTask?.Wait(); }
catch (Exception exception) { Logger.LogDetailed(exception, "SpeedrunTool"); }
preCloneTask = null;
```
或更简单：**不置 null**（下一次 `PreCloneSavedEntities()` 会覆盖引用，旧任务完成后自然回收），但需要确认没有代码依赖 `preCloneTask == null` 表示“无 preclone”（当前 `SetSharedDeepCloneState(preCloneTask?.Result)` 会等待，所以保留引用更安全但会阻塞下一次 load——推荐“等待后再 null”）。

### 5.5 Snapshot wipe 兜底（issue #3）
`Snapshot.cs:93` 的 fallback 条件改为：
```csharp
bool wipeActive = level.Wipe is { Percent: > 0f };
if (graphicsDevice.GetRenderTargets().Length > 0 || wipeActive) {
    // 重渲染房间，避免把 wipe/HUD 截进快照
    ...
}
```
以及 `[Unload]` 里 `readbackBuffer = null; thumbnailBuffer = null;`。

---

## 6. 修复状态（截至 `9d4f3cb` "fix(saveload): address the issues found by the independent review"）

| issue | 状态 | 备注 |
|---|---|---|
| #1 `ClearBeforeSave`/TAS 标志残留 | ✅ 已修 | `RecoverFromFailedSave` 复位 `ClearBeforeSave/SavedByTas/LoadByTas` |
| #2 `OnClearState(true)` 旧 id | ✅ 已修 | `AllSavedValues ??= InitValueDictionary()` + `EnsureValueDictionaries` + `TryGetValue`；`SaveStateImpl` 把 `InitSlots()` 提到 clear-before-save 之前 |
| #3 Snapshot 时机 | ✅ 已缓解 | `Level.Wipe != null` 时最多等 180 帧（`captureWaitFrames`）再回读；`[Unload]` 释放 `readbackBuffer/thumbnailBuffer`。**仍包含 HUD/overlay**（cosmetic）；wipe 持续 >180 帧时仍可能截到 wipe（fallback） |
| #4 `preCloneTask` 竞争 | ✅ 已修 | `RecoverFromFailedLoad` 先 `try { preCloneTask?.Wait(); } catch {}` 再置空；`PreCloneEnabled` setter 关闭时等所有槽任务结束 |
| #5 `AddReturnSameObject*` 反复清缓存 | ⏳ 部分 | 仍每次清缓存；未做批量/dirty 标记。实际影响：mod 一次性注册少量类型时可接受；若循环注册 N 个类型需注意（每次清 TypeCache+KnownTypes，下次 SL 重新 codegen） |
| #6 `RemoveReturnSameObject*` 不清缓存 | ✅ 已修（`3377cf5`） | Add/Remove 都清缓存 |
| #7 ProcFlags 单槽 thread-static | ✅ 已改 | 4 槽 direct-mapped（`RuntimeHelpers.GetHashCode(type) & 3`）；真实收益仍需 `SRT_PROFILE` 验证（见 §2） |
| #8 注释/线程模型 | ⏳ 文档 | 代码未变；建议按 §1.5 的措辞更正注释，并在 `DeepClone` XML doc 注明“主线程调用” |
| #9 `PreCloneEnabled` 同步 | ✅ 已修 | setter 关闭时 `WaitForPreCloneTask()`（所有槽） |
| #10 `Areas_Safe` 静默跳过 | ⏳ 待验证 | 代码未变；需 bench 在 SJ 图确认 per-area 时间/死亡是否更新 |
| #11 warm-up 细节 | ⏳ 低优 | 未判 `ModSettings.Enabled`；失败后不重试（首次 save 会再试） |
| #12 Profiler 失败路径/后台线程日志 | ⏳ 低优 | 功能可用；失败路径无 profile 行 |
| #13 `NoWarn CS0649` 作用域 | ⏳ 低优 | 仍为全局 |
| #14 `PreCloneMode.Auto` 判定 | ⏳ 低优 | **`docs/optimization-report.md` §4 写作“按物理内存选择”，实现是 `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes`**（≈GC 可用内存），措辞需统一 |
| #15 GC 行为（review 之外的新改动） | ✅ 已优化 | `40bc769`：仅当碎片 >64MB 或 >5% 才请求 LOH compact；仍 `GC.Collect(2, Forced, blocking:true)`，若 mod 处于 `NoGCRegion` 会抛 `InvalidOperationException`（pre-existing，低概率） |

---

## 7. 对 `9d4f3cb`（修复提交）的追加 review 发现

> 修复本身与 §5 的 patch 等价或更保守；以下只列“修复提交新引入/遗留”的点。

| # | 严重度 | 位置 | 问题 | 建议 |
|---|---|---|---|---|
| 7.1 | 🟠 | `StateManager.cs:475-497`（`PreCloneEnabled` setter / `WaitForPreCloneTask`） | `Task.Wait()` **无超时**：preclone 任务本身不等主线程，但 mod 的 `SaveLoadInterop.AddCustomDeepCloneProcessor` 在后台线程里可能等主线程资源/本身很慢 → 从菜单切 PreCloneMode 或 `RecoverFromFailedLoad` 时主线程会**无限期卡住**；此外 setter **遍历所有槽**串行等待，多个槽各有刚启动的 preclone 时会等“所有克隆时间之和”（菜单明显卡顿） | 两个 Wait 点都用有界等待（如 `task.Wait(TimeSpan.FromSeconds(2))`，超时记 Warn 后置 null）；setter 只等当前槽，其它槽用“延迟释放”标记在下一次 Save/Load/Clear 入口处理；XML doc 注明“仅主线程” |
| 7.2 | 🟡 | `SaveLoadAction.cs:275-298` | `OnClearState(clearBeforeSave:true)` 的 `slotInitialized = true` 语义正确（表示“字典结构已按当前布局建好”），但 `AllSavedValues ??= InitValueDictionary()` 的**重建路径**没有同步 `SaveSlotsManager.Slot.AllSavedValuesVersion = ActionsVersion` → 之后 `AreValueDictionariesComplete()` 可能因版本陈旧拒绝读档（触发条件苛刻，但修法零成本） | 在 `slotInitialized = true;` 后补一行版本同步 |
| 7.3 | 🟡 | `Snapshot.cs`（`RequireCaptureSnapshot`） | `captureWaitFrames` 只在捕获成功/`[Unload]` 时归零；等待 wipe 期间若再排一次快照（同槽/异槽），计数从旧值继续累加，可能提前到 180 帧上限、截到 wipe | `RequireCaptureSnapshot` 里 `captureWaitFrames = 0;` |
| 7.4 | 🟢 | `RecoverFromFailedLoad` | `preCloneTask?.Wait()` 在 `State=None` 之后、调度 reload 之前 ✓（无 TOCTOU）；若采纳超时方案，超时后仍需 `preCloneTask=null`，否则下次 `LoadStateImpl` 的 `.Result` 会阻塞 | — |
| 7.5 | 🟢 | `SaveLoadProfiler` | `InitSlots()` 提前后，`beforeSave` 段现在包含“clear 旧状态 + OnBeforeSaveState”，解读 `SRT_PROFILE` 时需注意 | 可在 clear 前后各加 Mark |
| 7.6 | 🟢 | `DeepClonerUtils.cs` ProcFlags 4 槽 / `GcCollect` | 4 槽 direct-mapped 正确性无问题（撞槽只影响命中率）；`ClearProcFlagsCache` 只清当前线程 ThreadStatic 数组（后台池线程各残留 ≤4 个 Type 引用，可忽略）；`GC.Collect(2, Forced, blocking:true)` 与旧 `GC.Collect()` 等价，条件 LOH compact 是净收益；`NoGCRegion` 下抛 `InvalidOperationException` 为 pre-existing 低概率风险 | — |

---

## 8. （原任务 ③）`BuiltinActions` 拆分后的 load/clear 执行顺序核对

**结论：顺序语义与 master 完全一致，拆分/预热/`InitSlots` 提前都没有破坏依赖。**

依据：
1. `InitActions()` 的方法调用顺序与 master 一致（`InitFields → SupportSimpleStaticFields → SupportModModuleFields → … → SupportAudioMusic → FixVertexLight → MuteAudioUtils → ExternalAction → SupportModSessionAndSaveData → ThirdParty.Support()`），且 `SaveLoadAction.BuiltinActions.cs` 中这些方法体的逐字节等价性已用脚本验证（§1.14）。
2. `InitSlots()` 用 `OrderBy(executeOrder)`（稳定排序）：内置 action 都是 `Order_InternalCoreAction`；外部 mod 的 `SafeAdd` 是 `Order_ExternalAction(10000)`；`GraphicResourcesHandler.AddSaveLoadAction()` 与 `ReleaseEventInstances()` 在 `contextOrder = Order_ExecuteAtLast(1000000)` 下注册 → 仍是最后两个。**注册顺序 = load 执行顺序**，与 master 相同。
3. 关键依赖满足：
   - `SupportSimpleStaticFields`（order 0、最先注册）→ 恢复 static 字段（`SpikeHooked`/`hooked`/`_hooksLoaded` 等）**先于** `ThirdParty.Support()` 里注册的 hook 重建 loadState（order 0、最后注册）✓（这正是 `YetAnotherHelperUtils`/`CollabUtils2SavestateUtils` 需要的“先恢复静态值再校正 hook”的次序）。
   - `ExternalAction.loadState`（`IgnoreSaveLoadComponent.ReAddAll`、`EndPoint.AllReadyForTime()`）在 ThirdParty 之前执行 → 被 ignore 的实体会先回到 `Tracker`，ThirdParty 的 `GetEntitiesTrackIfNeeded` 能看见它们 ✓。
   - `SaveLoadProfiler` 日志里 `loadActions` 段覆盖“内置 action + 外部 action + order-last action”的全部；`SupportAudioMusic` 在 `RestoreAudio_1`（收集 SoundSource 实例）之后、`RestoreAudio_2`（wipe 后 start）之前，与 master 一致 ✓。
   - `clearState` 顺序：order 0（含 `ExternalAction.clearState` 清 shared state/DynData 缓存）→ 外部 → `GraphicResourcesHandler.clearState`（VirtualAssets.Clear）→ `ReleaseEventInstances.clearState`（释放克隆的 FMOD 实例）——与 master 相同，且“先清缓存/资源、后释放原生句柄”的顺序正确。
   - `preCloneEntities` 顺序同上：`GraphicResourcesHandler`/`ReleaseEventInstances` 最后，仍是清空收集列表 ✓。
4. 拆分后的新变量（`ActionsVersion`）只用于 `AreValueDictionariesComplete`/`AllSavedValuesVersion`，与执行顺序无关；`SaveStateImpl` 把 `InitSlots()` 提前只让“clear-before-save 时的 id 更新”，不改变 action 的执行顺序。
5. 备注：`ReleaseEventInstances` 只有 `clearState`/`preCloneEntities`（没有 saveState/loadState），其 order 只影响清理阶段——与 master 相同。
