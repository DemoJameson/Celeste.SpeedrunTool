# SpeedrunTool SaveLoad 代码审计（perf/savestate-optimization 分支）

审计对象：`SpeedrunTool/Source/SaveLoad/**`、`MoreSaveSlotsUI/**`、`ModInterop/SaveLoadInterop.cs`、`SpeedrunTool/DeepCloner/**`
审计方式：通读源码 + Celeste.dll 元数据（Mono.Cecil）字段/IL 实测 + DeepCloner 隔离微基准（.NET 8 Release）。
所有结论都标了 `file:line`；估算值明确标注“估算”。

---

## 0. 结论摘要（TL;DR）

**Top 5 内存热点**
1. **每个存档槽保存了 2 份实体图**：`savedLevel`（完整 Level 深克隆，模板）+ `preCloneTask` 的第二次完整克隆（`StateManager.cs:195`、`:208`、`:538-549`）。3 槽 = 约 6 份实体图。这是最大的可优化项。
2. **preclone 的 `DeepCloneState` 映射表随槽常驻**：`savedLevel` 整图每个对象一条 `Dictionary<object,object>` 条目（实测我们的样例图 14.8k 对象 ≈ 15.6k 条目），每个条目 ≈ 32–48B，外加它引用的所有克隆对象不可回收（`DeepClonerUtils.cs:22`、`:348-350`、`StateManager.cs:33`）。
3. **Snapshot 全分辨率 Texture2D + 8MB `Color[]` 临时数组**：每槽一张屏幕大小纹理（1920×1080 = 8.3MB VRAM），最多 9 槽 ≈ 75MB；`ClearAll` 不 Dispose、重复存档覆盖不 Dispose（`Snapshot.cs:61,70,92,121`、`:29-32`）。
4. **静态字段兜底克隆**（`SaveLoadAction.cs:595-621`）：把所有 mod 的 Entity/Component/Renderer 类的 static 简单字段（含 `MTexture`/`Sprite`/`Solid`/`Level`/`Player` 引用）逐个 `DeepCloneShared()` 并存进每槽字典；由于 `InitFields` 的 `&& ||` 优先级 bug（`:466-472`），扫描范围比设计更大。
5. **MTexture 未被共享**：`KnownTypeProcessor` 只共享 `Atlas`/`VirtualMap<MTexture>`/`VirtualMap<char>`/`VirtualAsset`（`DeepClonerUtils.cs:26-92`），`MTexture` 是 25 字段的类（实测），每个不同 MTexture 每次存档克隆一份，且它引用的 `VirtualTexture`/`ModAsset` 又会带来共享对象图（`MTexture.<Texture>` 是 `VirtualAsset` 子类 → 走 PreClone 返回原对象，OK）。

**Top 5 性能热点**
1. **每对象 2 次 `lock(sourceObj)` + 2 次委托 + 一长串类型判断 + DynData 反射探测**（`DeepClonerUtils.cs:96-230`、`:234-323`）。实测：无处理器 14.8k 对象 1.5–1.7ms；加上等价的处理器形状后 4.0–5.9ms，即 **≈2.7–3.5×，每对象多 ≈150–250ns**。真实房间 5万–20万对象时，仅这一项就是 30–120ms/次克隆。
2. **Scene.Begin 时对每个已保存槽重跑 preclone，且主线程 `Wait()` 等后台克隆完成**（`SaveSlotsManager.cs:139-152` + `StateManager.cs:107-129`、`:538-549`）。3 槽时 = 3 份整图克隆 + 主线程阻塞，直接表现为切章/死亡重载卡顿。
3. **存/读期间 `level.Update` 被整体跳过**（`GameFreezeManager.cs:91-109`）：Waiting 状态只更新 Wipe/HiresSnow/Backdrop/Tooltip。`FreezeAfterLoadStateType` 相关的“按任意键解冻”本身不是大开销，但解冻前的 `DoScreenWipe` + 一次完整 `BeforeRender`（Snapshot）才是 save 端的大头。
4. **Snapshot 捕获 = 再渲染一帧 + `GetBackBufferData`**（`Snapshot.cs:54-94`）：GPU 同步点 + `new Color[w*h]`（1080p = 8.3MB LOH），是 save 卡顿与 LOH 碎片的固定来源。
5. **首次克隆每类型 codegen ≈0.19ms**（实测：40 个不同闭合泛型类型冷克隆 7.4ms，热 0.03ms），且 codegen 由主线程在 save/load 时触发；一次会话内每个类型只付一次，但换房间/换 mod（新类型）时继续付。首次 save 还有 `InitFields` 全 mod 类型扫描（`SaveLoadAction.cs:462-521`）与 `FilterStaticFields` 的 `GetValue(null)`（会触发 mod 静态构造器，`:571-593`）。

**Top bug**
1. **save/load 无异常保护，`State` 无 finally 复位 → 崩溃或永久冻结**：异常从 hotkey 派发（`HotkeyConfigUi.cs:187-211`，无 try/catch）逃出 `MInput.Update` → 进程级崩溃；不抛异常时 `State` 也可能停在 `Saving/Loading`（wipe 回调/WaitingEntity 生命周期异常），此时所有 save/load/clear 入口被 `IsAllFree()` 挡住。`StateManager.cs:157-240`、`:243-319`、`GameFreezeManager.cs:91-98`、`SaveSlotsManager.cs:64-73`。严重度：高。
2. **`SaveData.Instance` / `EverestModule._Session/_SaveData` 被“整体替换成克隆”**（`StateManager.cs:281`、`SaveLoadAction.cs:911-931`）：外部 mod 缓存的引用全部失效，且 `SaveData` 的 Runtime 状态（队列/事件）被丢弃。严重度：中高（desync/兼容性）。
3. **mod 在运行期增删 SaveLoadAction 会让其它槽的 `AllSavedValues` 变 null 但 `IsSaved` 仍为 true → 读档 NRE**（`SaveLoadAction.cs:161-167,356-382` + `SaveSlotsManager.cs:181-186`）。严重度：中。
4. **Snapshot 纹理泄漏**：`ClearAll` 不 Dispose；同槽重复存档时 `SnapshotsDict[slot] = new Snapshot(...)` 直接覆盖旧纹理（`Snapshot.cs:29-32`、`:115-124`）。严重度：中（VRAM，长时间多槽反复存档会累积）。
5. **FrostHelper / CommunalHelper 等 SJ 依赖 mod 没有专门支持**（`ThirdPartySupport/ThirdParty.cs:16-24` 只有 PandorasBox/SC2020/ExtVar/Spirialis/DeathTracker/Brokemia/EmoteMod；`FrostHelper` 零处理）：FrostHelper 里有大量可状态化实体（FrostTemple 系列等），是 Grandmaster Heartside 的 desync 高危来源。严重度：中高（与目标地图直接相关）。

---

## A. 架构与数据流

### A.1 一次 Save 做了什么（`StateManager.SaveStateImpl`，`StateManager.cs:157-240`）

| # | 步骤 | 位置 | 说明 |
|---|------|------|------|
| 1 | 合法性检查 | `:158-178` | 必须是 Level；`IsAllowSave`（不能 paused/dead/skipping cutscene）；春游章节面板打开时禁止；`DesyncRiskAnalyzer` 前置检查 |
| 2 | 若已有存档 → 先清空 | `:180-184` | `ClearStateImpl(hasGc:false)` → 内含 `preCloneTask?.Wait()`（**主线程阻塞**） |
| 3 | `SaveLoadAction.InitSlots()` | `:191` | 首次调用时执行 `InitActions()`：注册全部内置 action、扫描全 mod 类型、`FilterStaticFields` |
| 4 | `State = Saving` | `:192` | 之后 `GameFreezeManager` 会跳过 `level.Update` |
| 5 | `BeforeSaveState` 回调 | `:194` | 移除 `IgnoreSaveLoadComponent`/`ClearBeforeSaveComponent` 实体、清 PB 时间、清 TrailManager、移除 CelesteNet ghost、清 DynData 缓存 |
| 6 | **整 Level 深克隆到“未初始化对象”** | `:195` | `level.DeepCloneToShared((Level)RuntimeHelpers.GetUninitializedObject(typeof(Level)))` → `ClonerToExprGenerator`（Expr 生成器）逐字段复制：Session / Entities / Tracker / TagLists / RendererList / Particles / SolidTiles / Background / Camera / Pathfinder / HiresSnow / Wipe / mod 动态数据… |
| 7 | SaveData 克隆 | `:196` | `SaveData.Instance.DeepCloneShared()` |
| 8 | 过渡协程克隆 | `:198` | `savedTransitionRoutine`（切屏时存档用） |
| 9 | `OnSaveState` 回调 | `:199` | 40+ 个 `SaveLoadAction` 依次执行，把“不属于 Level 图”的状态写进本槽字典：静态字段、mod 模块字段、音频事件名/参数、输入状态、扩展变体、第三方 mod 字段… |
| 10 | 清 shared state | `:200` | `ClearSharedDeepCloneState()`：丢弃本次克隆的 `Dictionary<object,object>`（临时垃圾，等 GC） |
| 11 | **`PreCloneSavedEntities()`** | `:208` → `:538-549` | `Task.Run`：再次深克隆 `savedLevel.Entities` + `savedLevel.RendererList` + `savedSaveData`，得到一个 `DeepCloneState`（= 第二份实体图 + 源→克隆映射表）。目的：把 load 端“克隆实体”的成本搬到后台 |
| 12 | 冻结 + 屏（wipe） | `:210-221` | `FreezeGame(Save)`；TAS 模式直接结束；否则 `level.Add(new WaitingEntity())`，下一帧 `DoScreenWipe` → `State=Waiting` |
| 13 | Snapshot | `SaveSlotsManager.cs:84` | `Snapshot.RequireCaptureSnapshot` → 下一帧 `Level.BeforeRender` 里再渲染一帧并 `GetBackBufferData` |

### A.2 一次 Load 做了什么（`StateManager.LoadStateImpl`，`:243-319`）

| # | 步骤 | 位置 | 说明 |
|---|------|------|------|
| 1 | 检查 | `:245-268` | `IsAllowLoad`、TAS 限制 |
| 2 | `State = Loading` | `:270` | 同样冻结 `level.Update` |
| 3 | `BeforeLoadState` | `:272` | 再移除 IgnoreSaveLoad 实体 |
| 4 | **挂上 preclone state** | `:274` | `SetSharedDeepCloneState(preCloneTask?.Result)`：`.Result` 会**同步阻塞主线程**直到后台克隆完成（正常情况已完成，极端情况下是卡顿尖峰） |
| 5 | 时间/死亡数合并 | `:276` → `:443-480` | 除非 `SaveTimeAndDeaths`：把当前 Session/SaveData 中较大的时间/死亡数写进“模板”，再克隆一份 |
| 6 | **`UnloadLevel(level)`** | `:277` → `:399-432` | 对当前 Level 每个实体调用 `Removed()`（停音、Dispose 资源、解 hook）、`TagLists/Tracker/Pooler.EntityRemoved`；`Global` 实体默认跳过（除 `CassetteBlockManager`、Spirialis TimeController）；最后移除剩余 `SoundSource` |
| 7 | `Tracker.Refresh(savedLevel)` | `:279` | 重建模板的 Tracker 索引 |
| 8 | **`savedLevel.DeepCloneToShared(level)`** | `:280` | 把模板写回**当前 Level 对象**；因为挂了 preclone state，几乎所有引用都会命中 `GetKnownRef` → 直接返回 preclone 对象，**不再克隆**。这就是“load 快”的关键：load ≈ 指针重映射 |
| 9 | SaveData / 过渡协程 | `:281-284` | 整体替换 `SaveData.Instance`；恢复切屏协程 |
| 10 | 音频两阶段恢复 | `:286` → `:500-517`、`:521-536` | 第 1 步收集新 Level 里 `SoundSource { Playing: true }` 的 EventInstance；第 2 步等 wipe 完成后再 `start()` |
| 11 | `OnLoadState` | `:288` | 40+ action 的读档回调：静态成员写回（注意每个值又 `DeepCloneShared()` 一次，见 `SaveLoadAction.cs:304`/`:307`）、音频事件重设、输入恢复、VertexLight 修复、第三方 hook 重建… |
| 12 | **再次 `PreCloneSavedEntities()`** | `:290` | 为下一次 load 重新准备一份克隆（又一份整图克隆，跑在后台线程） |
| 13 | 非 TAS：恢复时间 + 冻结 + wipe | `:296-300` | `RestoreLevelTime`；`State=Waiting`（视设置）；wipe 完成后 `OutOfFreeze` → `LoadStateComplete`（清 shared state、`RestoreAudio_2`、Cassette 第二段） |
| 14 | 可选 GC | `:302-304` | `GcCollect(force:false)`：进程内存 > 2.5GB 才真正 `GC.Collect()`+LOH compact（`MemoryThreshold`，`:355`） |

### A.3 对象分类（深克隆 / 保留原对象 / 条件处理）

**直接返回原对象（共享，不克隆）**——`DeepClonerUtils.KnownTypeProcessor`（`:26-92`）：
- `Celeste`、`Settings`
- Everest：`ModAsset` 子类、`EverestModule` 子类、`EverestModuleSettings`、`EverestModuleMetadata`
- Monocle：`GraphicsDevice`、`GraphicsDeviceManager`、`Commands`、`BitTag`、**`Atlas`**、**`VirtualMap<MTexture>`**、**`VirtualMap<char>`**（Level.SolidsData/BgData/TileGrid.Tiles）
- 所有 `GraphicsResource` 子类（纹理/RenderTarget/Effect…）→ 图形资源不克隆、不重建
- NLua / KeraLua / LuaBase、MonoMod `ILHook`/`LegacyILHook`/`IDetourBase`
- CelesteNet 非 Entity 类型
- 通过 `SaveLoadInterop.CanReturnSameObject` 注册的类型/谓词（`SaveLoadInterop.cs:14-16`）
- `VirtualAsset`（特判在 `PreCloneProcessor`，`DeepClonerUtils.cs:102-108`）：返回原对象，并记录到 `GraphicResourcesHandler.VirtualAssets`，读档后对 `IsDisposed` 的做 `Reload()`
- `Scene`/`Level`（`DeepClonerUtils.cs:110-120`）：返回 `Engine.Scene` 而不是克隆——**注意：这会让模板中所有指向 Level 的引用都指向当前活动 Level**（多槽/多 Level 场景的行为依赖它，见 `:123-127` 注释）

**自行特殊处理（不进普通克隆）**——`DeepClonerUtils.PreCloneProcessor`（`:96-230`）：
- `SoundSource { Playing: true, instance: … }`：`NeedManualClone` + 记录 timeline，然后**返回 null**（克隆结果为 null，实际由 `EventInstance` 分支重建）
- `CassetteBlockManager`：`sfx`/`snapshot` 标记 manual clone，返回 null
- 被标记 `NeedManualClone` 的 `EventInstance` → `EventInstanceUtils.Clone()`（新建 FMOD 实例，复制参数/timeline）；主线程 Saving 时记 `ClonedEventInstancesWhenSave`，后台线程记 `…WhenPreClone`
- `WeakReference` / `WeakReference<T>` / `ConditionalWeakTable<K,V>`：手工重建（CWT：Entity-key 的只克隆“已知对象图内的 key”，`:200-222`）
- `SaveLoadInterop.CustomDeepCloneObject`（第三方自定义处理器）

**克隆后处理**——`PostCloneProcessor`（`:234-323`）：
- `HashSet<Component>` 重建哈希（专用快路径）；其它非简单 `HashSet<T>`/`IDictionary` 通过反射 `Clear`+重新 `Add`（修正未重写 GetHashCode 的对象）
- `DynData<T>.Data`（MonoMod.Utils 的按对象附加数据）逐层基类查找并克隆；`DynamicData._DataMap` 同理
- `DesyncRiskAnalyzer.Check(entity)`：Saving 且主线程且首轮时，递归检查字段里是否含 Lua 对象

**普通深克隆**：其余一切——Level/Entity/Component 以及它们的所有字段（List/Dictionary/数组/Sprite/Animation/MTexture/StateMachine/Coroutine…）。

### A.4 3 个槽位长期驻留什么

每个 `SaveSlot`（`SaveSlotsManager.cs:232-253`）持有：
1. `StateManager.savedLevel`：**整 Level 深克隆**（模板）
2. `StateManager.savedSaveData` + `savedTransitionRoutine`
3. `StateManager.preCloneTask`：**第二份**实体+RendererList+SaveData 克隆，**以及它的 `DeepCloneState._loops` 映射表**（`DeepClonerUtils.cs:340-350` 不清空，只替换引用）
4. `AllSavedValues`（每 action 一个 `Dictionary<Type,Dictionary<string,object>>`；`SaveSlot.AllSavedValues`）
5. `ClonedEventInstancesWhenSave` / `WhenPreClone`（FMOD 实例，native 资源）
6. 全局 `Snapshot.SnapshotsDict[slot]`：全屏 `Texture2D`

估算（**房间实体图规模是唯一关键未知量，需 bench-env 实测**）：
- 一个 Grandmaster Heartside 房间按 200–400 实体、2万–8万个对象估，单份图 4–15MB（对象头+字段+各种 List/Dict 的 capacity 浪费）；两份 ≈ 8–30MB；映射表 ≈ 1–5MB；Snapshot 1080p ≈ 8MB VRAM。**3 槽合计 ≈ 30–100MB 托管堆 + 24MB VRAM**。
- 若房间更大（SJ 终章几张大图）或装了很多 mod（DynData 放大），上不封顶。

### A.5 音频处理
- 存档：`SupportAudioMusic` 保存 `Audio` 当前的 music/ambience/altMusic 事件名 + 参数（`SaveLoadAction.cs:933-978`）；不克隆 `EventInstance` 本身。
- 正在播放的 `SoundSource.instance` 被 `NeedManualClone` 标记 → 克隆时用 `EventInstanceUtils.Clone` 新建 FMOD 实例（`EventInstanceUtils.cs:83-108`），参数与 timeline 复制；新实例**不 start**，由 `RestoreAudio_2` 在 wipe 后 start（`StateManager.cs:511-517`）。
- `CassetteBlockManager.sfx/snapshot` 同样手工克隆，分两步恢复（`:521-536`）。
- **Mono.Cecil 实测：`Level.PauseSnapshot` / `DialogSnapshot` / `AssistSpeedSnapshot` 都是 `static` 字段**（`Level._PauseSnapshot` 是 static 只读属性）→ 它们不在 Level 的实例字段图里，**克隆不会复制它们**；`SupportAudioMusic` 的 loadState 只在 `!level.Paused && Level._PauseSnapshot != null` 时 `ReleaseSnapshot` + 置 null（`SaveLoadAction.cs:972-975`）。也就是说这三个 snapshot 句柄既不保存也不恢复，只做“读档时清掉暂停快照”的兜底（见 D7）。
- 清除存档：`ReleaseEventInstances` 只 `Audio.ReleaseSnapshot` 那些克隆实例（`SaveLoadAction.cs:993-1005`）。
- `MuteAudioUtils`：存档后新建的 9 类音效实例被记录，读档时音量置 0 并清空列表（`MuteAudioUtils.cs:32-59`）——列表只在 load/clear 清，长时间“存档后继续玩”会累积（内存/句柄小泄漏）。

### A.6 图形资源处理
- 纹理/图集/RenderTarget 全部共享（`Atlas`、`GraphicsResource` 为 KnownType），**不会克隆纹理数据**——这点设计是对的，也是为什么“实体图克隆”才是内存主项。
- `VirtualAsset`（含 `VirtualTexture`/`VirtualRenderTarget`）：返回原对象，记录集合，读档时对 `IsDisposed` 的 `Reload()`；`TrailManager.buffers` 单独 Reload（`GraphicResourcesHandler.cs:50-84`）。
- `WaveDashPresentation.Gfx`（Atlas）每次读档 Dispose + 重建（`:98-112`）。
- Snapshot：见 B3（VRAM + LOH）。

### A.7 加载时如何“重建场景”
没有走 Celeste 的 `LevelLoader`，而是：
1. 移除当前 Level 所有实体（触发 `Removed` 清理 hook/资源/声音）
2. 保留**同一个 `Level` 实例**和**同一个 `Gaze`/`Camera`/`Renderer` 对象**（因为 Level 是“克隆到已有对象” `DeepCloneToShared`）
3. Tracker/TagLists/EntityList 由模板克隆（新 List/Dict 实例，元素指向 preclone 实体）
4. 模板中的实体对象 = preclone 对象（`GetKnownRef` 命中），因此**实体的 `Added()` 不会被调用**；只有被 `IgnoreSaveLoadComponent` 摘掉的实体会 `ReAddAll` → `Added()`
5. 因此“hook 在 Added 里注册、在 Removed 里注销”的 mod 实体在读档后会**丢失 hook**；SRT 只对少数 mod 手写重建（如 `SpirialisHelperUtils.cs:47-57`、`SpringCollab2020Utils`）。这是 desync 的系统性来源之一。

---

## B. 内存热点排序（按预估收益）

> 说明：`file:line` 均指本仓库；标注“估算”的是按对象布局推算，需 bench-env 验证。

### B1. 每槽保存两份实体图（收益最大）
- 位置：`StateManager.cs:195`（模板克隆）、`:208` + `:538-549`（preclone 第二份）、`:290`（每次 load 后又重做一份）
- 机制：save 时 `DeepCloneTo` 克隆整个 Level 图；随后 `Task.Run` 又把 `savedLevel.Entities`+`RendererList`+`savedSaveData` **再克隆一遍**，并存下映射表。load 时用映射表把模板“重映射”成新图（很快），所以第二份图是为了“load 速度”而长期常驻的。
- 为什么占内存：实体图是所有对象的主要来源（每实体 1 entity + N component + Sprite/Animation/MTexture + collider + 各种 List/Dict/数组 + DynData）。两份 = 2×；3 槽 = 6×。
- 优化方向（三档，风险递增）：
  1. **低风险**：`RendererList` 的第二份没必要独立于 `savedLevel.RendererList`（load 时 Level 克隆会整图重映射，preclone RendererList 只是为了让渲染器字段也走 knowref）——把 preclone 范围缩到 `RendererList` 中“含复杂状态”的渲染器（如 `LightingRenderer` 的 `VertexLight[]`、`DisplacementRenderer`），而不是全部；实测 `LightingRenderer` 一个实例就带 11520×16B + 384×24B + 11520×4B ≈ 240KB 数组。
  2. **中风险**：preclone 改为“按需/分帧”——只在 load 前对实体做一次分帧克隆（每帧 ≤2ms），或者只对“下一次 load 大概率需要”的实体（当前房间实体）克隆。牺牲一点 load 时间换常驻内存。
  3. **高风险（根治）**：让“模板”只保留 Level 自身字段 + 一份**实体清单**（类型 + 顺序 + Tracker 归属 + tag），实体本体只保留 preclone 那一份；load 时按清单重建 Tracker/EntityList（`Tracker.AddTypeToTracker`/`EntityList.entities` 顺序/depth 排序都要复刻）。可省 ≈40–50% 槽内存，但改动面大、易出错。

### B2. preclone 映射表（`DeepCloneState`）常驻
- 位置：`DeepClonerUtils.cs:22`（`sharedDeepCloneState`）、`:344-350`；`StateManager.cs:33`（`preCloneTask`）
- 机制：`DeepCloneState._loops` 是 `Dictionary<object,object>`，每个经过克隆的对象都有一条（实测样例 14.8k 对象 → 15,588 条）。preclone 完成后**整张表常驻**（只在下次 save/load 被替换），表里的 value 又是整个第二份图。
- 为什么占内存：表本身 ≈ 条目数 × (32B 条目 + 数组余量)，5万–20万对象 ≈ 2–8MB/槽；同时它把第二份图钉在堆上。
- 优化方向：
  - 用**紧凑映射**：克隆时按类型分组、按到达顺序编号（源对象 → 复制槽位 `int`）存 `object[]`，或至少 `Dictionary<object,int>` + `object[]`，可省 30–50% 表开销。
  - load 后立即 `preCloneTask = null` 前先确认新 preclone 不会再需要旧表（现在 `:290` 会重做，旧表可提前释放，但旧表里的对象已是活动对象，丢弃表即可，无需等 GC 引用链断开）。
  - 若 kill 掉 preclone 机制（见 B1 档 3），这张表会缩小到“只含实体”的一小部分。
- 风险：低（改数据结构）/中（去掉 preclone）。

### B3. Snapshot：全屏 Texture2D + 8.3MB 临时 `Color[]`
- 位置：`Snapshot.cs:54-94`（`new Color[width*height]`、`GetBackBufferData`、`new Texture2D(width,height)`）、`:115-124`（覆盖不 Dispose）、`:29-32`（`ClearAll` 不 Dispose）、`:459-469`（显示区域只有屏幕 ~1/3×~1/3）
- 为什么占内存：1920×1080 → 8.3MB VRAM/张；2560×1440 → 14.7MB。9 槽 ≈ 75–130MB VRAM（旧机 8GB 内存 + 显存共享，代价很高）。每次捕获还有 8.3MB LOH 垃圾。
- 优化方向：**捕获缩略图**——`GetBackBufferData` 后按 1/3 或 1/4 下采样（显示区域只有 0.32×0.31 屏幕；1280×720 甚至 640×360 足够），VRAM/捕获成本降 4–16×；`Texture2D` 用 `SurfaceFormat.Color` 保持；`ClearAll`/覆盖前 `Dispose()`。
- 风险：低。注意 `RemoveSnapshot` 已 Dispose，`ClearAll` 与覆盖路径漏了。

### B4. 静态字段兜底克隆（`SupportSimpleStaticFields`）
- 位置：`SaveLoadAction.cs:595-621`（`dictionary[type] = values.DeepCloneShared()`，每次 save 都克隆）、`:462-521`（类型扫描）、`:571-593`（`GetValue(null)` 触发 mod cctor）
- 机制：对每个 mod 类型（Entity/Component/Renderer 子类）的 static 字段，只要类型是“简单类”（且额外允许 `Level/Player/MTexture/Solid/CrystalStaticSpinner/Renderer 子类/VirtualAsset 子类` 及其容器），就 `GetValue` + 深克隆存进槽。
- 为什么占内存：mod 里 `static Sprite/MTexture/Solid/List<MTexture>` 很常见；`Level`/`Player` 静态引用虽然命中 knownRef 不额外占图，但 `Sprite`/`MTexture` 会新增克隆；每 save 一遍，每槽一份。
- 优化方向：
  - 把 `MTexture` 加入 KnownType（共享）——`MTexture` 字段（实测）：`VirtualTexture Texture`、`Rectangle ClipRect`、`string AtlasPath`、`Vector2 DrawOffset/Center`、`int Width/Height`、`float L/R/T/B UV`、`MTexture Parent`、`Atlas _Atlas`、`ModAsset Metadata`、`_Orig*`、`List<ModAsset> _ModAssets`、`_ScaleFix`；**除 Everest 资源重载会改 `Metadata/_ModAssets` 外基本只读**。共享可同时省 B4 与 B5。
  - `InitFields` 的过滤条件修正（见 D4），别把 SRT 自己/`Oui` 系类型也扫进来。
  - 对 `IsSubclassOf(Renderer)` 的静态字段，跳过 `Sprite` 之外的（或只保留 `MTexture`）——需逐 mod 验证。
- 风险：中（共享 `MTexture` 若被 mod 原地修改会串味；需先确认 Celeste/Everest 不改 MTexture 的可变字段）。

### B5. `MTexture` 未共享
- 位置：`DeepClonerUtils.cs:26-92`（KnownType 白名单里没有 `MTexture`）
- 机制：`MTexture` 是普通类（25 个字段、≈150B），每个不同 MTexture 在每次克隆图里生成一份；`Sprite.animations` 的每帧 `MTexture` 都会命中。
- 为什么占内存：一张图 300 种图块 × 每槽 2 份 × 3 槽 ≈ 54 万 B 级别（不大），但 mod 图（SJ 的自定义实体、粒子 `Source`）会放大到几千个 MTexture；且 `MTexture.Parent`/`_OrigTexture` 链会带来额外复制。
- 优化方向：将 `MTexture` 加入 shared（或只共享 `Parent == null` 的“根 MTexture”）。
- 风险：中（同上）。建议先做“统计有多少 MTexture 被克隆”的插桩再决定。

### B6. List/Dictionary 容量与 `Tracker`/`EntityList`/`TagLists` 冗余结构
- 位置：`Monocle.Tracker`（实测：2 个 `Dictionary<Type,List<Type>>` + 2 个 `HashSet<Type>` + `Dictionary<Type,List<Entity>>` + `Dictionary<Type,List<Component>>`）、`EntityList`（4 List + 3 HashSet）、`TagLists`（`List<Entity>[N]` + `bool[N]`）
- 机制：克隆时全部按原容量新建；`EntityList.toAdd/toAwake/toRemove`、`Tracker` 的每个 per-type List 都会有额外 capacity 余量；`TagLists` 数组长度 = tag 数。
- 为什么占内存：一个 300 实体的房间，`Tracker.Components` 里几十个 per-type List + 实体在多个 List 里重复出现（引用 8B + List 开销）；这些结构每个槽 ×2 份。
- 优化方向：克隆后对只读容器 `TrimExcess()`（谨慎：运行期会扩容，可能反而更慢）；或对 `Tracker` 的“类型→列表”字典做 shared 索引（Type 键可共享，但 List 实例不能）；收益中等，风险低。
- 风险：低。

### B7. 事件/委托把已死对象钉在堆上
- 位置：`StateMarkUtils.cs:19`（`InternalSafeAdd(ReColor, ReColor)`，每次 save/load 遍历草莓）、`IgnoreSaveLoadComponent.cs:9-13`（静态 `List<(Entity,bool)>`，save 后到 load 前一直持有实体引用）、`GraphicResourcesHandler.cs:45`（`VirtualAssets` 静态列表）、`MuteAudioUtils.cs:20`（`RequireMuteAudios` 静态 List）、`DeepClonerUtils.cs:17-19`（`[ThreadStatic]` 栈/字典，目前只在 PostClone 内部用且会清空）
- 机制：这些静态集合持有 Entity/VirtualAsset/EventInstance 引用，直到下次 load/clear。
- 为什么占内存：`IgnoredEntities` 会持有被摘掉的实体（通常少量）；`VirtualAssets` 持有 VirtualAsset（本来就是全局资产，重复引用无害）；`RequireMuteAudios` 会累积 FMOD 实例。
- 优化方向：`RequireMuteAudios` 在 save 后定时/上限清理；`IgnoredEntities` 按槽位区分（现在多槽共用，理论上有跨槽串味风险，见 D9）。
- 风险：低。

### B8. 每次 save 的临时 GC 垃圾（造成 LOH/碎片）
- 位置：`Snapshot.cs:61`（8.3MB `Color[]`，LOH）；`SaveLoadAction.cs:302`（`pair.Value[memberName].DeepCloneShared()` 读档时再克隆一次）；`DeepClonerUtils.cs:101,239`（每对象 `lock`，无分配）；`:296-306`（DynData 每对象 `object[] parameters` 分配）；`StateManager.cs:400-405`（`UnloadLevel` 的 `Distinct()` + `List<Entity>`）；`:387`（`GCSettings.LargeObjectHeapCompactionMode = CompactOnce` + `GC.Collect()`）
- 为什么占内存：上述是“瞬时峰值 + 碎片”，会推高进程私有内存并触发更多 GC。
- 优化方向：Snapshot 用复用 buffer / `ArrayPool<Color>`；DynData 分支用 `FastInvoker` 的 `params object[]` 复用一个线程静态数组；`Distinct()` 用 HashSet 手写（避免 LINQ 迭代器）；`GcCollect` 只在必要时做 LOH compact。
- 风险：低。

---

## C. 性能热点排序（save/load 耗时）

### C1. DeepCloner 每对象开销（处理器 + 锁 + 类型判断）
- 位置：`DeepClonerUtils.cs:96-230`（Pre）、`:234-323`（Post）；调用点 `DeepClonerGenerator.cs:50-57`、`:87-94`
- 机制：每个被克隆的对象都要：类型缓存查找 → knownRef 查找 → `lock(sourceObj)` + 一串 `is` 判断 + `SaveLoadInterop.CustomDeepCloneObject`（foreach）→ 执行生成的克隆器 → 再来一次 `lock(sourceObj)` + HashSet/IDictionary 类型判断（`IsIDictionary` 里 `type.GetInterfaces()` 会分配）→ DynData 探测（CWT 查找 + 逐基类 HashSet 查找 + `object[]` 分配）→ `DynamicData._DataMap` 查找。
- 实测（同机、.NET 8 Release、14.8k 对象合成图）：无处理器 **1.5–1.7ms**；加上等价处理器形状 **4.0–5.9ms**（≈2.7–3.5×，每对象 +150–250ns）。
- 优化方向：
  1. `lock(sourceObj)` 两处：绝大多数对象不需要锁（锁是为了防止同对象被多线程同时处理）。DeepCloner 的克隆本身就是单线程遍历一个 state；**锁可以只在“已知会被并发访问”的类型上做**（或改成 `Monitor.TryEnter` + 单线程检测）。这是**最高性价比**的一项，预计省 20–30% 克隆时间。
  2. `PreCloneProcessor` 里按类型分派改 `Dictionary<Type,小整数枚举>` 一次缓存，避免一长串 `is`（虚拟调用 + 类型比较）。
  3. `PostCloneProcessor` 的 `IsHashSet`/`IsIDictionary` 类型谓词结果按 `Type` 缓存（`ConcurrentDictionary<Type,byte>`），避免每次 `GetInterfaces()`。
  4. DynData 探测：`NotExistDynData` 对“没有 DynData 的类型”已有 `IgnoreTypes` 快速路径；但对“有 DynData 的类型”每对象都 `object[]` + `InvokeMethod`。可缓存“该类型是否有任何非空 DynData 实例”（用计数维护），或对 Entity 用批量后处理。
- 风险：低（锁的语义要仔细，尤其 preclone 后台线程 + 主线程同时可能访问同一批对象——但两者访问的是不同图；见 D6）。

### C2. 首次克隆的 per-type codegen（0.19ms/类型）
- 位置：`DeepClonerGenerator.cs:22-58`、`:60-95`（`DeepClonerCache.TypeCache`）、`:259-274`（选择 generator）；`ClonerToExprGenerator.cs`（`CloneObjectTo` 永远用 Expr 生成器，`:276-315`）
- 实测：40 个不同闭合泛型类型冷克隆 7.36–7.63ms（≈0.19ms/类型，含首次 JIT）；热克隆 0.011–0.028ms。类型缓存**进程级常驻，不随 save/clear 清空**（`DeepClonerCache.cs:5-10`，只有测试用 `SwitchTo` 会清）。
- 机制/影响：一次会话内每个类型只付一次；但“首次 save”会在主线程付几百个类型 × 0.19ms ≈ 数十 ms；`load` 走 `TypeCacheDeepTo`（另一套缓存），所以**首次 load 也会付一遍**。换地图类型集合变化时继续付。
- 优化方向：在 `LoadState`/存档屏（空闲时）后台预热常用类型（Celeste/Monocle/SJ 常见类型）；或把 `GenerateCloner` 挪到线程池（需要锁保护 `DeepClonerCache`，已有 `lock(type)`）；至少把 `DeepCloneTo` 也切到 MSIL 生成器（见 C6）。
- 风险：低。

### C3. Scene.Begin 时全槽 re-preclone + 主线程 `Wait`
- 位置：`SaveSlotsManager.cs:139-152`（`HandleSwitchScene`：`orig(self)` 后 `WaitUntilThreadSafe()`，再对每个槽 `HandleSwitchScene` → `PreCloneSavedEntities()`）、`StateManager.cs:107-129`、`:538-549`；`SaveSlotsManager.cs:172-179`（`WaitUntilThreadSafe`）
- 机制：每进入一个新 `Level`（开章、死亡重载、进出图），对所有已保存槽重跑整图克隆，且**主线程要等它们全部完成**（`preCloneTask?.Wait()` 在循环里）。
- 影响：3 槽 × 整图克隆（每份 30–150ms 视房间）→ 开章/死亡后明显卡顿；8GB 旧机上后台克隆还会和主线程争 CPU。
- 优化方向：把 `Wait` 改成“不等待 + 分帧”；或只在 `Engine.Scene` 确实换了 Level 实例时对该槽 preclone（当前已是 Level 才做，但每个槽都做）；更彻底的是取消 preclone 常驻（B1 档 3）。

### C4. Snapshot 捕获（save 路径固定开销 + GPU 同步）
- 位置：`Snapshot.cs:54-94`
- 机制：在 `Level.BeforeRender` 里**再执行一次** `level.BeforeRender/Render/AfterRender`（重复一帧渲染，含所有 draw call），然后 `GraphicsDevice.GetBackBufferData`（**强制 GPU→CPU 同步**，是明显的管线 stall），再分配 `Color[w*h]`（1080p 8.3MB）并 `SetData` 上传到新纹理。
- 影响：save 的最坏单帧成本主要来自这里；弱 GPU/弱 CPU 上可达数十 ms。
- 优化方向：缩略图（B3）+ 复用 buffer；或延后到 save 冻结期间分帧渲染；或直接用上一帧的 backbuffer（避免重渲染）。
- 风险：低。

### C5. `UnloadLevel`（load 路径主成本之一）
- 位置：`StateManager.cs:399-432`
- 机制：`level.Tracker.GetEntities<Player>()` + `level.Entities` 拼 List → `Distinct()`（LINQ：HashSet + 闭包）→ 每个实体 `Removed(level)`（mod 侧可能有 Dispose/停音/解 hook，成本不可控）→ `TagLists/Tracker/Pooler.EntityRemoved` → 最后 `GetComponentsCopy<SoundSource>().ForEach(RemoveSelf)`。
- 影响：几百实体时，`Removed` 的 mod 逻辑是主要时间；SRT 侧还有 `Distinct()` 与多次遍历（TagLists 里每个 tag 一个 List 的 EntityRemoved 是 O(tags)）。
- 优化方向：`Distinct()` 手写 HashSet（省迭代器/闭包分配）；把 `TagLists.EntityRemoved` 合并到一次遍历；记录 per-类型 `Removed` 耗时便于定位慢 mod（见 E）。
- 风险：低。

### C6. save 用 Expr 生成器、preclone 用 MSIL 生成器（两套路径）
- 位置：`DeepClonerGenerator.cs:259-274`（`IsSafeVariant()` → Expr；否则 MSIL）+ `:276-315`（`CloneObjectTo` **总是** `ClonerToExprGenerator`）
- 实测：`ShallowObjectCloner.IsSafeVariant()` 在本机 .NET 8 = **False** → `DeepClone` 走 MSIL 生成器；而 `DeepCloneTo`（save/load 主路径）走 `Expression.Compile()` 生成的委托。
- 差异：MSIL 生成器对 `isGoodConstructor` 的类直接 `newobj` + 只复制复杂字段（`DeepClonerMsilGenerator.cs:57-152`）；Expr 生成器（`ClonerToExprGenerator.cs:63-104`）**逐个字段赋值**，并且对 `initonly` 字段用 `ForceSetField`（运行时反射改 `FieldAttributes` + `SetValue`，见 D2）——比 `stfld` 慢一个数量级。另外两套生成器的 readonly 处理不一致（D2/D3）。
- 优化方向：给 `CloneObjectTo` 也实现/复用 MSIL 路径（`CloneClassTo` 语义：目标对象已存在，只需“复杂字段克隆 + 赋值”，可以直接 Stfld 写 initonly，因为动态方法不校验）；至少要给 Expr 生成器的 `ForceSetField` 加快速路径（对 `initonly` 但在动态方法里可直写的场景用 `Expression.Assign` 替代——**注意实测普通 `Expression.Assign` 会抛 `ArgumentException: Expression must be writeable`**，所以需要“生成 IL 的 Stfld”而不是 Expr API）。
- 风险：中（要保证 readonly 语义；建议先加一轮“克隆正确性回归”：对 Level/Entity/Session/所有 mod 类型字段做 clone→compare）。

### C7. `OnLoadState`/`OnSaveState` 中 action 的重复克隆
- 位置：`SaveLoadAction.cs:265-312`（save 时 `DeepCloneShared()`，load 时**又** `DeepCloneShared()`）、`:595-621`、`:911-931`（`SupportModSessionAndSaveData` 读档时 `savedValues.DeepCloneShared()` 整表克隆）、`:800-801`（Input 读档整表克隆）
- 机制：读档时为了“不破坏存档内的原值”而再克隆一份；对 `SupportModSessionAndSaveData`（所有 mod 的 `_Session/_SaveData`）与 `Input`/`VirtualInput` 这是每槽每 load 一次的整表克隆。
- 影响：中等；`SupportSimpleStaticFields` 的静态值在 load 时还会 `DeepCloneShared`（`SaveLoadAction.cs:304`）。
- 优化方向：改成“模板不可变 + 目标对象 `DeepCloneTo`”；或对 `_Session/_SaveData` 直接 `DeepCloneToShared(module._Session)` 原地恢复（见 D8）。
- 风险：中（原地恢复会把外部持有的引用一起改好，其实是更正确的做法，但要处理字段被整体替换的语义）。

### C8. 冻结/解冻与 wipe
- 位置：`GameFreezeManager.cs:76-109`、`:116-150`；`StateManager.cs:601-632`；`StateManager.cs:634-647`
- 机制：`State != None` 期间 `level.Update` 完全不执行，只跑 Wipe/HiresSnow/Backdrop/Tooltip/NonFrozenMiniTextbox；解冻靠 `On.Monocle.Scene.BeforeUpdate` 里检查 `unfreezeInputs.Any(IsUnfreeze)`（每帧 9 个 VirtualInput 的 2 次轮询）+ `Hotkey.Pressed()`。
- 影响：本身开销小；但“冻结期间不做 Update”会和 `DesyncRiskAnalyzer`/`MuteAudioUtils` 等 hook 形成“状态机不再推进”的窗口，容易出现“卡在 Waiting”类 bug（见 D1/D10）。
- 优化方向：把 `FreezeAfterLoadStateType` 的默认值调成“Off”或在旧机上默认关；解冻检查降频（每 2 帧一次）。
- 风险：低。

---

## D. Bug / 风险清单

> 严重度：🔴高（崩溃/软锁）/ 🟠中（desync、功能失效）/ 🟡低（内存、边缘）

### D1. 🔴 save/load 没有异常保护；`State` 没有 finally 复位（崩溃 or 永久冻结）
- 位置：`StateManager.cs:157-240`（save，`:192` 设 `State=Saving`）、`:243-319`（load，`:270` 设 `State=Loading`）、`GameFreezeManager.cs:91-98`（`State != None` 就不 `orig(level)`，即 `level.Update` 被整体跳过）、`SaveSlotsManager.cs:64-73`（`CannotSaveLoad` → 所有入口被挡）
- 传播路径（已核对）：hotkey 派发在 `MInput.Update` 的 hook 里（`HotkeyConfigUi.cs:187-211`，`hotkey.GetHotkeyConfig().OnPressed?.Invoke(scene)`，**无 try/catch**）→ `SaveLoadAction` 的异常会直接逃出 `MInput.Update` → `Engine.Update` → 进程级未处理异常。
- 两类后果：
  1. **崩溃**（最常见）：异常逃出 → 游戏崩溃/回到 Everest 崩溃界面（丢当前 run）。能触发异常的点很多：mod `Removed()` 抛非 NRE（`StateManager.cs:425-427` 只 catch `NullReferenceException`）、`Areas_Safe[areaKey.ID]` KeyNotFound（`:456`）、`preCloneTask.Result` 的 `AggregateException`（后台克隆抛异常时，`:274` 的 `.Result` 会在主线程重抛）、`DesyncRiskAnalyzer`/`DynData` 反射失败等。
  2. **冻结但未复位**（不抛异常时）：`State` 只在 `DoScreenWipe` 回调（`:605-614`）、`OutOfFreeze`（`:616-632`）、`ClearStateImpl`（仅 `State==Waiting` 才解冻，`:324-327`）里复位。若 `State.Waiting` 时场景被替换、或 `WaitingEntity.Render` 未被调用（Level 不再 Render），`State` 会停在 `Saving`；若 load 的 wipe 回调丢失则停在 `Loading`——这时 `IsAllFree()` 恒 false，**hotkey 的 save/load/clear、TAS 的 clear 全部被 `CannotSaveLoad()` 挡住**，只能重启。
- 建议：
  - `SaveStateImpl`/`LoadStateImpl` 整体 `try/catch(Logger.LogDetailed)/finally { if (!success) ResetAfterFailure(level); }`；`ResetAfterFailure`：`State=None`、`OutOfFreeze`、`sharedDeepCloneState=null`、必要时 `ClearStateImpl`。
  - hotkey 派发处（`HotkeyConfigUi.cs:199-208`）包 try/catch + 日志，避免一个 mod 的坏状态把整局炸掉。
  - `UnloadLevel` 的 catch 放宽到 `Exception` 并记 `entity.GetType()`（现在 mod 抛非 NRE 会中断 load）。
  - `preCloneTask` 的异常要在启动时 `ContinueWith` 记录（否则 `.Result` 重抛时上下文已丢失）。

### D2. 🔴 `ForceSetField` 的反射写 readonly（正确性 + 性能 + 线程安全）
- 位置：`DeepClonerExprGenerator.cs:15-40`（实现）、`ClonerToExprGenerator.cs:80-89` 与 `:91-103`（调用）
- 机制：`ForceSetField` 每次调用都用反射把 `FieldInfo.m_fieldAttributes` 临时改成非 `InitOnly`，`field.SetValue(obj, value)`，再改回。**进程级全局可变状态**：另一个线程若同时生成/执行同类字段的克隆会读到被临时改写的 attributes；`lock(fieldInfo)` 只保护这一个字段，且 `m_fieldAttributes` 是 `FieldInfo` 上的字段。
- 风险：多线程（preclone 后台线程 + 主线程同时克隆不同图）时有竞态；若 `GetPrivateField("m_fieldAttributes")` 返回 null（`:19-25`）则**静默不赋值**，克隆对象字段保持默认值 → 隐蔽 desync。
- 建议：改用“生成的 IL 直接 `stfld`”（`DeepClonerMsilGenerator` 已经这么干，动态方法不校验 initonly），彻底删掉 `ForceSetField`；或在 `Load` 阶段一次性把需要写的 readonly `FieldInfo` 的快路径委托建好（每个字段一个 `Action<object,object>`，内部 Stfld），避免每对象反射。

### D3. 🟠 `DeepClonerExprGenerator` 仍存在“只读简单字段”崩溃隐患（与 WIP 修复不一致）
- 位置：`DeepClonerExprGenerator.cs:114-143`（`Expression.Assign` 到 `initonly` 简单字段，`:139-141`）；WIP 修复只加在 `ClonerToExprGenerator.cs:91-103`
- 实测：`Expression.Assign(Expression.Field(readonlyField), …)` 在 .NET 8 会抛 `ArgumentException: Expression must be writeable`（我们已用最小程序验证）。
- 现状：本机 `ShallowObjectCloner.IsSafeVariant() == False`（实测）→ `DeepClone` 走 MSIL 生成器，暂时踩不到；但一旦某些平台/沙箱下 dynamic MemberwiseClone 失败（`ShallowObjectCloner.cs:30-46` 的 fallback 逻辑），Expr 生成器会被启用 → 任何含 `readonly int/string/enum` 字段的类型首次克隆都会抛异常 → 触发 D1 软锁。
- 建议：把 WIP 的 readonly 处理也补到 `DeepClonerExprGenerator.cs`（或统一改用 MSIL 路径），并加单测覆盖 `readonly int/string/readonly 自定义 struct/readonly 复杂类型` 四种字段。

### D4. 🟠 `InitFields` 运算符优先级 bug（扫描范围与设计不符）
- 位置：`SaveLoadAction.cs:466-472`
```csharp
!type.IsGenericType && type.FullName != null
&& !type.FullName.StartsWith("Celeste.Mod.SpeedrunTool")
&& !type.IsSubclassOf(typeof(Oui))
&& type.IsSameOrSubclassOf(typeof(Entity))
|| type.IsSameOrSubclassOf(typeof(Component))
|| type.IsSameOrSubclassOf(typeof(Renderer));
```
- 实际语义：`(A && B && C && D && Entity) || Component || Renderer` → `Component`/`Renderer` 分支没有“排除 SpeedrunTool 自己 / 排除 Oui / FullName 非空”的过滤。
- 后果：SRT 自己的 `Renderer`/`Component` 子类（如 `ConfettiRenderer`）也会被扫进 `simpleStaticFields` → 存档会保存/恢复 SRT 自己的静态状态（潜在自我干扰）；`Oui` 系（不是 Component，影响小）。另外 `type.IsSubclassOf` 在泛型定义上调用会抛异常，靠外层 try/catch 吞掉。
- 建议：抽 `bool IsCandidate(Type t)` 方法，加上括号，并用 `for`/局部函数明确三段逻辑。

### D5. 🟠 存档槽切换 / action 增删导致 `AllSavedValues == null` 但 `IsSaved == true`（读档 NRE → 级联 D1 崩溃）
- 位置：`SaveLoadAction.cs:161-167`（`Remove` → `needInitializeDictionaryId = true`）、`:356-382`（`InitSlots` → `SaveSlotsManager.RequireReInit()`）、`SaveSlotsManager.cs:181-186`（把**所有槽**的 `AllSavedValues = null`）、`SaveLoadAction.cs:205-225`（`OnLoadState` 里 `dict[saveLoadAction.dictionaryId]`）
- 复现：存 2 个槽 → 某 mod 在运行时 `SaveLoadAction.SafeAdd/Remove`（或 `SaveLoadAction.Remove(Func)`）→ 再存档（`InitSlots` 触发 `RequireReInit`，只重建**当前槽**的字典）→ 切回槽 1 → load → `AllSavedValues == null` → `dict[...]` **NullReferenceException** → 从 hotkey 抛出 → 按 D1 是崩溃。
- 建议：`RequireReInit` 只影响“未保存/未初始化”的槽；对已保存槽延后到 `ClearStateImpl` 时再置空；`OnLoadState/OnSaveState` 里对 null 字典做兜底（补空字典）并记日志。

### D6. 🟠 后台 preclone 线程与主线程共享可变静态状态
- 位置：`StateManager.cs:541-547`（`Task.Run` 里调用 `DeepClone`，会进 `PreCloneProcessor`）、`DeepClonerUtils.cs:101/239`（`lock(sourceObj)`）、`:340-355`（`sharedDeepCloneState` 静态）、`SaveLoadAction.ClonedEventInstancesWhenPreClone`（`SaveLoadAction.cs:30`）
- 问题：preclone 在后台线程执行 SRT 的处理器：会写 `SaveLoadAction.ClonedEventInstancesWhenPreClone`（List，非线程安全）、`GraphicResourcesHandler.VirtualAssets`（List）、`DynDataUtils.Cache*`；同时主线程可能在做 load（`SetSharedDeepCloneState(preCloneTask.Result)`)——代码用 `WaitUntilThreadSafe()` 兜底，但它不是所有路径都调用（`LoadStateImpl` 直接 `.Result`；`GcCollect`、`SupportAudioMusic` 等在主线程操作同一批静态集合）。
- 风险：偶发 `List` 损坏/丢实例（导致读档后音量/资源异常）。
- 建议：给这些静态集合加锁或改成 `ConcurrentQueue` + 在 `Save/LoadStateImpl` 开头/结尾做一次“收割”；`sharedDeepCloneState` 改成 `ThreadStatic` 或显式 state 传递。

### D7. 🟡 `Audio` 的 snapshot / `EventInstance` 状态未被保存（局部音频状态丢失）；mod 自持 EventInstance 有句柄共享风险
- 位置：`SaveLoadAction.cs:933-978`（`SupportAudioMusic`）、`:972-975`（loadState 无条件 `Audio.ReleaseSnapshot(Level._PauseSnapshot)`）、`DeepClonerUtils.cs:148-160`（只有 `NeedManualClone` 的 EventInstance 才重建；否则结构体浅拷贝句柄）
- 已核对的事实（Mono.Cecil）：`Level.PauseSnapshot` / `DialogSnapshot` / `AssistSpeedSnapshot` 都是 **static** 字段（`Level._PauseSnapshot` 是 static 只读属性）→ **不在克隆图里**。所以：
  - 存档时若处于对话（`DialogSnapshot` 有效）或 AssistSpeed 状态，读档后这些 snapshot 句柄既不恢复也不重建 → 音频状态与存档点不一致（低风险，听感/音频 desync）。
  - loadState 只在 `!level.Paused && Level._PauseSnapshot != null` 时释放暂停快照，这是“读档后避免残留暂停音效”的兜底，但**不判断该句柄是否已经释放**；FMOD `EventInstance.release()` 失败只会返回 `RESULT`（Celeste `Audio.ReleaseSnapshot` 忽略返回值，实测 IL：只做 `stop`+`release`），不会崩，但会掩盖状态错误。
- 另一类风险（未逐 mod 验证）：mod 实体自己持有 `EventInstance` 字段时，若没有 `NeedManualClone`，克隆会把 native handle 浅拷贝给模板/克隆两份对象 → 两份对象共享同一 FMOD 实例，任一方 `release` 后另一方变悬垂句柄。当前 SRT 只对 `SoundSource.instance` 与 `CassetteBlockManager.sfx/snapshot` 做了 special-case。
- 建议：把“Level/实体上的 snapshot 类 EventInstance”统一纳入 manual-clone 清单（或读档时用 `Audio.CreateSnapshot` 重建）；对 mod 提供的 `EventInstance` 走 `SaveLoadInterop` 的自定义处理器。

### D7b. 🟡 存档期间 `Level.DialogSnapshot` 等 static 句柄会被“读档时的静态字段恢复”间接影响
- 位置：`SaveLoadAction.cs:685-710`（`SaveStaticMemberValues` 保存 `Engine`/`Glitch`/`Distort`/`ScreenWipe.WipeColor`/`Audio.currentCamera`/`DustStyles.Styles`）
- 说明：这些静态成员是“按名字列举”的白名单；新版本 Celeste/Everest 新增的静态状态不会被覆盖（例如 `Level.PauseSnapshot`、`Audio.CurrentMusicEventInstance` 之外的 snapshot 字段），因此升级游戏版本后可能出现新 desync。建议把这份白名单改成“读档后重新初始化”而不是“逐个保存”。
- 严重度：低（可维护性/版本兼容风险）。

### D8. 🟠 `SaveData.Instance` / `EverestModule._Session/_SaveData` 整体替换
- 位置：`StateManager.cs:281`（`SaveData.Instance = savedSaveData.DeepCloneShared()`）、`SaveLoadAction.cs:921-929`（`module._Session = dictionary["_Session"] as EverestModuleSession` 等）
- 问题：外部 mod 若缓存了 `SaveData.Instance` 或 `module._Session` 的引用（大量 mod 会，例如在 `Load()` 里存一个字段），读档后它们仍指向旧对象 → 读档后 mod 状态不同步（desync）；且 `SaveData.Instance` 是 Celeste 全局单例，替换会让 Celeste 内部已捕获的引用（如果有）失效。
- 建议：优先原地恢复（`savedSaveData.DeepCloneToShared(SaveData.Instance)`，注意 `Areas_Safe` 等 readonly 字段用 D2 的方案）；无法原地恢复时至少保留“同一实例”语义并记录风险。mod `_Session` 同理可 `DeepCloneToShared(module._Session)`，只对 null 时替换。

### D9. 🟡 多槽共享的静态状态（`IgnoredEntities`、`ClonedEventInstances*`、`VirtualAssets`、`ModSettings.GcAfter…`）
- 位置：`IgnoreSaveLoadComponent.cs:9`（静态 `IgnoredEntities`，注释说“天然支持多存档”但它是进程级）、`SaveLoadAction.cs:29-30`（`ClonedEventInstancesWhenSave` 按槽存，但 clear 路径可能清错槽）、`GraphicResourcesHandler.cs:45`
- 问题：`ClonedEventInstancesWhenSave` 用 `SaveSlotsManager.Slot` 取得（按当前槽），`ReleaseEventInstances` 的 `clearState` 在切槽/清槽时会释放“当前槽”的实例；`ClearAll` 循环里 `Slot` 被逐槽设置（`SaveSlotsManager.cs:120-137`），逻辑勉强正确，但任何“执行 action 时 Slot 不是预期的那个槽”的路径都会释放错对象（FMOD 实例被提前释放）。
- 建议：把 per-slot 状态全部收进 `SaveSlot`（已有字段），不要在 action 里通过全局 `Slot` 取。

### D10. 🟡 冻结期间 `Waiting` 状态依赖输入轮询，存在“卡在 Waiting”的路径
- 位置：`GameFreezeManager.cs:76-89`（只有 `BeforeUpdate` 且 `unfreezeInputs.Any(IsUnfreeze)` 才解冻）、`StateManager.cs:605-614`（`DoScreenWipe` 回调置 `Waiting`）
- 问题：若 `FreezeAfterLoadStateType == IgnoreHoldingKeys` 且玩家读档时按着某个键，`IsUnfreeze` 恒 false 直到松手（设计如此）；但若 `waitOneFrame` 的 `WaitingEntity.Render` 因为 `Level.Render` 未调用（例如切场景/读档发生在 `Render` 之前）而永不触发，则 `State` 停在 `Saving`/`Loading`，加上 D1 就软锁。另 `OutOfFreeze` 里 `savedLevel == null` 时直接 `State=None`，但 `freezeType` 为 Load 时 `LoadStateComplete` 不执行 → `sharedDeepCloneState` 不清理（`:486`）。
- 建议：在 `OutOfFreeze`/`DoScreenWipe` 增加超时兜底（N 秒后强制解冻）；`OutOfFreeze` 的 `savedLevel == null` 分支也清理 shared state。

### D11. 🟡 `Snapshot` 纹理泄漏（VRAM）
- 位置：`Snapshot.cs:29-32`（`ClearAll` 只 `Dictionary.Clear()`）、`:115-124`（同槽重复存档覆盖对象）、`:87`（捕获失败 dispose 了，正常路径 dispose 旧纹理）
- 建议：`ClearAll` 里逐个 `Dispose`；覆盖前 `snap.snapshotTex?.Dispose()` 或复用同一个 `Snapshot` 实例。

### D12. 🟡 `UpdateTimeAndDeaths` 的 `Areas_Safe[areaKey.ID]` 越界风险
- 位置：`StateManager.cs:456-457`、`:477-479`
- 风险：`SaveData.Areas_Safe` 是 `Dictionary<int, AreaStats>`；mod 图/新章节若该 ID 尚未在 SaveData 中创建，会 `KeyNotFoundException` → D1 软锁；`Modes[(int)areaKey.Mode]` 假定 3 元素数组。
- 建议：`TryGetValue` + 缺省创建。

### D13. 🟡 FrostHelper / 其它 SJ 依赖无专门支持
- 位置：`ThirdPartySupport/ThirdParty.cs:16-24`（支持清单）；`Grep "FrostHelper"` → 无任何命中
- 风险：FrostHelper 的实体（FrostTemple 系列、可交互方块等）状态若在静态字段/自定义容器里，SL 后不恢复 → desync。Grandmaster Heartside 大量使用 FrostHelper。
- 建议：用 `bench-env` 的 harness 在 SJ 里做“存档→读档→逐帧对比”找差异；至少为 FrostHelper 的静态状态/常见实体加 `CloneModTypeFields`。

### D14. 🟡 `SaveLoadInterop` 的 `returnSameObjectTypes` 对已生成克隆器无效
- 位置：`SaveLoadInterop.cs:14-16`、`DeepClonerSafeTypes.cs:33-43`（`KnownTypes` 永久缓存）、`DeepClonerCache`（类型缓存不清）
- 问题：mod 在第一次 SL 之后才 `AddReturnSameObjectType`，已生成的克隆器不会更新 → 依然克隆该类型（desync 或多余内存）。`KnownTypes` 也永久缓存。
- 建议：API 文档里要求“必须在第一次 SL 之前注册”；或注册时清 `DeepClonerCache` + `KnownTypes`（代价：重新 codegen）。

### D16. 🟠 第三阶段 desync 检测（DeepClone 期间的 LateCheck）实际上已失效
- 位置：`DesyncRiskAnalyzer.cs:187`（`Check` 的门槛）、`:129-140`（`GetAndRefreshIfDesyncCheck`）、`StateManager.cs:174-178`/`:202-206`（`shouldDesyncCheck` 同时门控 Before/AfterSaveState）
- 机制（git 对比）：旧版本 `Check()` 的条件是 `if (State != Saving || !IsMainThread) return;`（每次主线程 save 都做 late check）。`0e27bfb`（"refactor: slightly refactor DesyncRiskAnalyzer"）改成 `if (!(State == Saving && IsMainThread && !shouldDesyncCheckTemporarily)) return;`。
- 后果：
  - 普通地图：`shouldDesyncCheckTemporarily = !Consult(level) = true` → `!true == false` → **late check 直接 return，永远不做**。
  - Oracle 声明安全的地图（`SafeMaps`，如 `StrawberryJam2021/5-Grandmaster/maya`）：`shouldDesyncCheckTemporarily = false` → late check 会执行并可能写 `desyncReason`，但 `StateManager.cs:202` 的 `OnAfterSaveState` 被 `shouldDesyncCheck`（此时 false）挡住 → **检测结果永远不会被读取**（只在下次被 `OnBeforeSaveState` 的 `desyncReason = null` 覆盖）。
  - 也就是说“第 3 阶段：检测 DeepClone 时字段里是否含 Lua 对象”这条防线整体形同虚设；`EarlyCheckEntityTypes` 也不会因 late check 而增长。
- 建议：把条件改成 `&& shouldDesyncCheckTemporarily`（或直接去掉这个 negation，恢复“Saving + 主线程就检查”）；对 oracle-safe 地图要么不跑 late check、要么让 `OnAfterSaveState` 也执行（区分“拒绝存档”与“仅记录”）。加日志：第一轮 save 记录发现的 candidate 类型。
- 严重度：🟠（不会崩，但“危险地图拒绝存档/自动清档”的保护失效，会让 LuaCutscenes/BossesHelper 系地图在 SL 后 desync 而不自知）。
- 位置：`DeepClonerCache.cs:5-10`（Key = `Type`，永不清理）、`ReflectionExtensions.cs:42-48`、`DynDataUtils.cs:9`、`DeepClonerSafeTypes.KnownTypes`
- 问题：mod 热重载/禁用后，这些字典仍持有旧 `Type`/`MethodInfo` → mod 程序集无法卸载（Everest 热重载内存增长）。
- 建议：`OnAssetReload`/`BeforeReload` 时清 Type 相关缓存（注意 `DeepClonerCache.ClearCache()` 后需要重新 codegen，成本可接受）。

---

## E. 基准可插桩点建议（给 bench-env / Lead 的 profiling）

> 现状：所有 profiling 都在 `#if DEBUG` 里（`DebugTool/Config.cs`），Release 构建拿不到数字。建议把开关改成运行期设置/环境变量（`SRT_PROFILE=1`），或在 `SpeedrunToolSettings` 增加隐藏项。

### E1. StateManager 分段计时（最重要）
`StateManager.cs`：`SaveStateImpl`（`:157-240`）/`LoadStateImpl`（`:243-319`）加分段 stopwatch，输出一行 JSON/日志：
```csharp
var sw = Stopwatch.StartNew(); long t;
t = sw.ElapsedTicks;
SaveLoadAction.InitSlots();
Log("initSlots", t);
t = sw.ElapsedTicks; level.DeepCloneToShared(savedLevel = ...); Log("cloneLevel", t);
t = sw.ElapsedTicks; SaveLoadAction.OnSaveState(level); Log("saveActions", t);
t = sw.ElapsedTicks; PreCloneSavedEntities(); Log("precloneStart", t);   // 只记启动耗时
// load:
t = sw.ElapsedTicks; var st = preCloneTask?.Result; Log("waitPreclone", t);
t = sw.ElapsedTicks; UnloadLevel(level); Log("unloadLevel", t);
t = sw.ElapsedTicks; savedLevel.DeepCloneToShared(level); Log("cloneBack", t);
t = sw.ElapsedTicks; SaveLoadAction.OnLoadState(level); Log("loadActions", t);
```
同时记录 `GC.GetTotalAllocatedBytes(precise:true)` 与 `GC.GetGCMemoryInfo().HeapSizeBytes` 前后差值。

### E2. 每 action 计时（已存在，放开即可）
`SaveLoadAction.cs:183-225` 已有 `EachAction_Profilling` + >10ms 日志。建议：阈值改成可配（默认 2ms），并把 `ActionDescription` 在 Release 保留（现在 `#if DEBUG`，`AddDebugDescription` 绑定 `$if DEBUG`，Release 下 `ActionDescription` 不存在）。做法：把 `ActionDescription` 改成非 DEBUG 字段，注册时用 `CallerMemberName`/`StackTrace(skipFrames:false)` 只算一次。

### E3. DeepCloner 每类型统计（定位“谁在克隆”）
在 `DeepClonerGenerator.CloneClassInternal`（`:60-95`）与 `CloneStructInternal`（`:97-115`）加开关控制的统计：
```csharp
static readonly bool Profile = Environment.GetEnvironmentVariable("SRT_CLONE_STATS") == "1";
static readonly Dictionary<Type, (long count, long ticks)> Stats = new();
...
if (Profile) { var t0 = Stopwatch.GetTimestamp(); ... ; Record(type, t0); }
```
输出 Top-30：类型、实例数、总耗时、平均耗时。这一条能直接回答“B1/B5/B6 到底值多少”。

### E4. Pre/Post 处理器开销拆分
在 `DeepClonerUtils.PreCloneProcessor`/`PostCloneProcessor` 入口加 `[ThreadStatic]` 计数器（调用次数、lock 等待时间、DynData 命中次数），或在 `DeepCloner.InvokePreCloneProcessor`（`DeepCloner.cs:33-45`）里累加总耗时。目标：区分“锁/类型判断”与“实际克隆”的占比（我们的微基准估计是 2.7–3.5×，需要真实图验证）。

### E5. codegen 冷启动统计
`DeepClonerGenerator.GenerateCloner`（`:259-274`）与 `CloneObjectTo`（`:276-315`）：记录每次生成的 `type`、耗时（`Stopwatch.GetTimestamp()`）、是否命中缓存；汇总“冷启动总耗时/类型数”。预期 ≈0.19ms/类型（实测）。

### E6. Preclone 后台任务
`StateManager.cs:541-547`：包 `Stopwatch` + `GC.GetAllocatedBytesForCurrentThread()`，log 出 `entities` 数、耗时、分配；`SaveSlotsManager.HandleSwitchScene`（`:139-152`）里记录“每个槽 preclone 耗时 + 主线程等待耗时”。

### E7. Snapshot 捕获
`Snapshot.cs:54-94`：`BeforeRender`/`Render`/`AfterRender`/`GetBackBufferData`/`new Color[]`/`SetData` 分别计时 + `array.Length`；记录“捕获总耗时”和“纹理字节数”。预期瓶颈在 `GetBackBufferData`。

### E8. UnloadLevel
`StateManager.cs:399-432`：总耗时 + 实体数 + 每类型 `Removed` 耗时 Top-10（用 `entity.GetType()` 聚合）。目的：找出“哪个 mod 的 `Removed` 慢/抛异常”。

### E9. GC 行为
`StateManager.GcCollect`（`:357-396`）已 log `GC latency`；建议补：`GC.CollectionCount(0/1/2)`、`GC.GetGCMemoryInfo().FragmentedBytes`、`GC.GetTotalMemory(false)` 前后值；`GCLargeObjectHeapCompactionMode` 生效时间也要计。

### E10. 帧级卡顿（“带 savestate 时游戏不卡顿”的核心指标）
在 `SpeedrunToolModule`/`GameFreezeManager.OnAfterUpdate_EvenIfGameFreeze` 上挂一个“每帧 ms”环形缓冲（只在 `SRT_PROFILE` 时启用），存档状态下持续记录 `Engine.DeltaTime`/`Stopwatch`，输出 P50/P95/P99/最大值，以及“超过 16.7ms/33ms 的帧数”。这样才能回答“卡顿到底来自 save/load 本身，还是 preclone 后台线程 / Snapshot / GC”。

### E11. 内存快照对比
在 `SaveStateImpl` 前后、`LoadStateImpl` 前后各打一次：`GC.GetTotalMemory(forceFullCollection:false)`、`GC.GetGCMemoryInfo()`、`Process.PrivateMemorySize64`、`Process.WorkingSet64`；并在“存 0/1/2/3 槽”三点各打一次 → 直接量出 B1/B2/B3 的真实占比。

---

## F. 重构建议（简短清单）

1. **拆 `StateManager`（655 行，职责 5+）**：
   - `SaveStateCapture`（save 路径：抓取 + 生成模板）
   - `StateRestorer`（load 路径：unload + 重映射 + 恢复）
   - `PreClonePipeline`（后台克隆、取消、等待策略）
   - `FreezeController`（`FreezeGame/DoScreenWipe/OutOfFreeze/WaitingEntity/GameFreezeManager` 的 Waiting 部分）
   - `StateManager` 只保留公共状态机与 `Save/Load/Clear` 门面（保持 `StateManager.Instance` API 不变，外部 mod 依赖）
2. **拆 `SaveLoadAction`（1005 行）**：
   - `SaveLoadActionRegistry`（`SharedActions`、`dictionaryId`、`executeOrder`、`InitSlots`/`RequireReInit`）
   - `SaveLoadValueStore`（每槽 `AllSavedValues`、`OnSave/OnLoad/OnClear` 分发）
   - `BuiltinActions/`：`CoreStaticsActions`、`InputActions`、`AudioActions`、`ModuleActions`、`GraphicsActions`、`EventInstanceActions`、`SettingsActions`、`RandomActions`
   - 保持 `SaveLoadAction.SafeAdd/Remove` + `SaveLoadInterop` 的公开签名（是 mod 生态契约）
3. **`SaveSlotsManager` 的静态 `Slot` 全局状态**：把 action 执行上下文显式传给回调（或 `[ThreadStatic] currentSlot`），避免“清错槽”类问题（D9）。
4. **DeepCloner 单一生成路径**：`DeepClone` 用 MSIL、`DeepCloneTo` 用 Expr 的分裂导致 readonly 语义/性能不一致（D2/D3/C6）。建议：
   - 新增 `DeepClonerMsilGenerator.GenerateClonerToInternal`（目标对象已存在，复杂字段递归克隆 + `stfld`）
   - 删掉 `ForceSetField`，改为生成 IL
   - 统一 `DeepClone`/`DeepCloneTo` 都走 MSIL 生成器（`#if !NETCORE` 分支可保留 Expr 作为 fallback，但补上 readonly 修复）
5. **命名/依赖**：
   - `DeepClonerUtils` 名字太泛（实际是 SRT 的全局处理器 + shared state），拆成 `DeepCloneProcessors`（处理器）与 `DeepCloneStateScope`（shared state 生命周期）
   - `SaveLoadAction` 的 `Order_*` 常量 + `contextOrder` 隐式排序改成显式注册 API（`InternalSafeAdd(order: …)`），避免“加在最后”依赖注释（`SaveLoadAction.cs:346-349`）
   - `GraphicResourcesHandler` 里两个 `NotImplementedException` 的死代码（`:28-41`）删掉或转 issue
   - `ThirdParty` vs `ThirdPartySupport` 命名统一；每个 mod 支持文件统一 `TryRegister()` 模式（现在 `Support()` 里混着 throw/return）
6. **文档/契约**：给 `SaveLoadInterop` 补 XML 文档说明“必须在首次 SL 前注册”；给 `SaveLoadAction` 的执行顺序、线程模型（主线程 vs preclone 线程）写进 README。

---

## 附录 1：本次实测数据（可复现）

**环境**：Windows / .NET 8 Release / x64（与本机一致）。样本：合成“实体图”（400 实体 × 4 组件 + Sprite/Animation 字典 + collider 列表 + 渲染器数组 + 300 MTexture-like），14,788 个克隆对象。

| 场景 | 结果 |
|------|------|
| `DeepClone`（MSIL 生成器）无处理器 | 1.5–1.7 ms，分配 2.37 MB |
| `DeepClone` + 等价 SRT 处理器形状（2×lock、类型判断、CWT/DynData 探测） | 4.0–5.9 ms，分配 2.5–3.7 MB |
| `DeepCloneTo`（Expr 生成器，save/load 主路径） | 1.5–1.6 ms（冷启动含 JIT 7.4ms） |
| 冷 codegen 40 个不同闭合泛型类型 | 7.4 ms ≈ **0.19 ms/类型**（热克隆同图 0.011–0.028 ms） |
| `ShallowObjectCloner.IsSafeVariant()` | **False** → `DeepClone` 用 `DeepClonerMsilGenerator` |
| `Expression.Assign(initonly 字段)` | 抛 `ArgumentException: Expression must be writeable` |

> 注意：以上为隔离微基准（无 Celeste 运行时、无 mod），用于给“处理器开销/ codegen 开销”定级；真实房间的绝对耗时需 bench-env 在游戏内测。

## 附录 2：Celeste 类型字段/数组实测（Mono.Cecil dump，用于内存估算）

- `Celeste.Level` 实例字段：`Session`、`SolidsData`/`BgData`(`VirtualMap<char>`)、`Wipe`、`FormationBackdrop`、`SolidTiles`、`BgTiles`、`Background/Foreground`(`BackdropRenderer`)、`GameplayRenderer/HudRenderer/LightingRenderer/DisplacementRenderer/BloomRenderer`、`FgTilesLightMask`(`TileGrid`)、`Particles/ParticlesBG/ParticlesFG`、`HiresSnow`、`Camera`、`Pathfinder`、`DialogSnapshot/PauseSnapshot/AssistSpeedSnapshot`(EventInstance)、`LoadOverrides`(readonly CWT)、`EntityLoaders`(readonly Dictionary)、`_GCCollectLock` 等；继承 `Monocle.Scene`：`Entities`(EntityList)、`TagLists`、`RendererList`、`Tracker`、`actualDepthLookup`、`OnEndOfFrame`。
- `Celeste.LevelLoader.LoadingThread`：`ParticlesBG = new ParticleSystem(-8000, 400)`、`ParticlesFG = new ParticleSystem(8000, 400)`、`Particles = new ParticleSystem(-50000, 800)` → **1600 个 Particle 结构体/Level**（每个含 2 引用 + 约 56–72B）。
- `Celeste.LightingRenderer` 构造：`VertexPositionColor[11520]`(16B/个 ≈184KB) + `VertexPositionColorMaskTexture[384]` + `int[11520]`(46KB) + `Vector3[20]` + `VertexLight[64]` → **≈240KB 数组/实例**（Level 克隆 + preclone RendererList 各一份）。
- `Monocle.Tracker`：2×`Dictionary<Type,List<Type>>`、2×`HashSet<Type>`、`Dictionary<Type,List<Entity>>`、`Dictionary<Type,List<Component>>`。
- `Monocle.EntityList`：4×`List<Entity>` + 3×`HashSet<Entity>`；`Monocle.ComponentList`：3×List + 3×HashSet + lockMode。
- `Monocle.MTexture`：25 个字段（`VirtualTexture Texture`、`Rectangle ClipRect`、`string AtlasPath`、`Vector2 DrawOffset/Center`、`int Width/Height`、4×float UV、`MTexture Parent`、`Atlas _Atlas`、`ModAsset Metadata`、`_Orig*`、`List<ModAsset> _ModAssets`、`float _ScaleFix`）≈150B/个，**未被共享**。
- `Monocle.VirtualMap<T>`：`T[,][,] segments`（分块二维数组）；`VirtualMap<char>`/`VirtualMap<MTexture>` 被共享，`VirtualMap<bool>`（`Grid.Data`）**未**共享（`SolidTiles.Grid`、`FgTilesLightMask` 等会克隆）。
- `Engine.Pooler`：`Dictionary<Type,Queue<Entity>>`（静态字段被 `SaveLoadAction.cs:698` 存档/恢复 → 会克隆池中实体）。
