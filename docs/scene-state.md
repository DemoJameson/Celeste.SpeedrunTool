# 跨场景（跨章节 / 章节选择界面）Savestate：现状、根因、设计与验证

状态: **已实现**（分支 `feat/scene-persistent-state`，基于 `6fa430a`）。
范围: 只改 SpeedrunTool；不改游戏本体、不部署、不启动游戏。
相关文档: `docs/audit.md`（D8：SaveData 整体替换、C3：Scene.Begin re-preclone）、`docs/review.md`、
`docs/sj-compat.md`。

---

## 1. 用户语义（已与用户确认）

1. 从**其它章节**或**章节选择界面（Overworld）**读档时，自动回到存档所在的章节并恢复，而不是只在同章节生效。
2. `AutoClearStateOnSceneSwitch` 默认值改为 **false**：状态跨场景保留；该设置保留为"旧行为/保守模式"，
   用户可手动开启"离开章节时清档"。EN/CN 设置文案与 README 已同步说明两种行为。
3. 失败必须安全：pending load 失败/超时（目标章节资源加载失败、SaveData 不匹配等）→ 拒绝读档 + popup +
   游戏保持可玩；**savestate 保留**，可重试。
4. TAS 模式**不允许**跨章节读档（`LoadStateTas` 的同步契约无法保证）：直接拒绝，state 保留。
5. 旧设置文件保留用户已存的 `true`（不做迁移）；新默认只影响新用户/未写过设置文件的用户。

---

## 2. 现状模型（改动前的代码，证据）

### 2.1 读档入口

- `StateManager.LoadStateImpl`（现 `SpeedrunTool/Source/SaveLoad/StateManager.cs:221`）：
  - 旧代码第一行要求 `Engine.Scene is Level`，否则 `"Cannot Load: Scene is not Level!"`；
  - 然后 `UnloadLevel(level)` + `savedLevel.DeepCloneToShared(level)` + `SaveData.Instance = savedSaveData.DeepCloneShared()`
    （旧 `:251-258`），**从不比较当前 `level.Session.Area` 与 `savedLevel.Session.Area`**。
- `SaveSlotsManager.CannotSaveLoad()`（旧 `SaveSlotsManager.cs:64-73`）同样要求 `Engine.Scene is Level`，
  因此 ModInterop/热键在 Overworld 都会被挡住。
- 读档入口有两个：Level 内热键（`MoreSaveSlotsUI/SaveLoadHotkeys.cs`，旧代码要求 `scene is Level`）与
  `SpeedrunTool.SaveLoad` ModInterop API（无场景限制，但会被上面的检查拒绝）。

### 2.2 Level 与 area 级资源从哪来

- 正常情况下，进入章节走 `LevelEnter.Go` → `LevelEnter`（postcard/vignette）→ `LevelLoader`：
  - `LevelLoader` 的 ctor/LoadingThread 负责加载 **map bin / MapData**（`session.MapData`，即
    `AreaData.Areas[id].Mode[mode].MapData`）、`GFX.FGAutotiler/BGAutotiler`、`AnimatedTilesBank`、
    `SpriteBank`、背景/前景 backdrops、`AreaData` 的 meta；
  - LoadingThread 里 `Level.Session = session; SaveData.Instance.StartSession(Level.Session);`
    （Celeste `LevelLoader.cs` 反编译约 :331）——`SaveData.StartSession` 会把每个 mod 的 `_Session`
    替换成新对象（`Celeste/SaveData.cs:666-685`），并更新 `LastArea_Safe/CurrentSession_Safe`；
  - `StartLevel()` 在下一个 Update 里调用 `Level.LoadLevel(...)` 创建实体，然后把 `Engine.Scene = Level`，
    `Level.Begin` 在同帧末由 `Engine.Update` 调用。
- **直读路径完全绕过这条 pipeline**：目标章节的 map data / tileset / audio / area meta 从未加载。

### 2.3 模板里的引用是什么形态

- `DeepClonerUtils.KnownTypeProcessor` 对 `VirtualMap<char>`、`VirtualMap<MTexture>`、`Atlas`、
  `GraphicsResource`、`VirtualAsset`（PreClone 返回原对象）等直接复用原对象
  （`DeepClonerUtils.cs:29-95`、`:219-238`）；`Scene`/`Level` 统一返回 `Engine.Scene`。
- 因此 savestate 模板里的 tile 数据 / tile 纹理 / `Level` 引用是**保存时的对象**。离开章节后这些
  MTexture/VirtualMap 可能已失效；`GraphicResourcesHandler` 只负责 reload
  `VirtualTexture`/`VirtualRenderTarget`（`GraphicResourcesHandler.cs:23-58`），救不了"目标 area 的
  map/kitset/audio 根本没加载"。
- `HandleSwitchScene`（`StateManager.Transitions.cs:57-79`）只做两件事：按 `AutoClearStateOnSceneSwitch`
  清档、以及 `Level` 实例变化时 `State = None; PreCloneSavedEntities()`；它不参与读档。

### 2.4 为什么表现是"资源有时候没正确加载"

跨章节直读得到的是**混合态**：实体/Session/渲染器来自旧章节模板，而 Level 的 `SolidsData/BgData`
（VirtualMap<char>，KnownType 共享）、tileset 纹理、背景、audio 总线、"当前 area 的 MapData"却来自
**当前场景**。具体后果包括：贴图/背景错章节或缺失、碰撞与画面不一致、走出当前房间后
`Session.LevelData` 取到错误 map data、`Level.NextLoadedPlayer`/`SaveData` 不一致、部分 mod 在
`Level.LoadLevel` 时读到错误的 `_Session`。这也解释了原注释 "todo: 资源有时候好像没有正确加载"。

### 2.5 设置为什么"看起来不存在"

`AutoClearStateOnSceneSwitch` 默认 `true`（旧 `SpeedrunToolSettings.cs:81`），且菜单项在
`#if DEBUG` 里（旧 `SpeedrunToolMenu.cs:122-126`）——Release 玩家无法关闭；于是"跨场景保留"在正式版
实际不可达。

---

## 3. 根因（按失败路径）

| 场景 | 旧行为 | 根因 |
| --- | --- | --- |
| A 章节 → A 章节（同章节换房间/重载/死亡） | 正常 | 直读不需要目标 area 资源，属于历史支持路径 |
| 章节 A 存 → 切到章节 B 读 | 混合态 / 资源错误 / crash | 直读把 A 模板克隆进 B 的 Level；B 的 map data、tileset、audio 都在，A 的没有 |
| Overworld 读 | 直接拒绝（"Scene is not Level!"） | `LoadStateImpl` / `CannotSaveLoad` 的场景检查 |
| 不同原版存档文件 | 直接拒绝或 SaveData 混乱 | 场景检查 + `SaveData.Instance` 只在 UnloadLevel 后替换，没有 `StartSession` 对齐 |
| CollabUtils2 in-game overworld helper 打开 | 状态能跨章节保留（旧默认 true 下唯一入口），但读档必坏 | 同"章节 A→B"，helper 打开只是让 `HandleSwitchScene` 不清档 |
| 切场景后立刻读档 | 取决于落在哪个 scene；非 Level 时拒绝 | 同上场景检查；没有"等目标 Level 就绪"的机制 |
| 读档中途失败 | 可能停在 Loading / 半毁 Level | 没有 pending 状态可回滚/超时兜底 |

补充：`Level`/`Session`/`SaveData` 的字段替换无法替代 pipeline——`UnloadLevel` 只触发实体 `Removed`，
不会重新加载 map bin；`DeepClonerUtils` 会把模板里的 `Level` 引用统一重定向到 `Engine.Scene`
（`DeepClonerUtils.cs:228-238`），所以"当前 Level 是哪个 area"决定了一切。

---

## 4. 设计：pending load

### 4.1 路由

`LoadStateImpl`（`StateManager.cs:221`）现在：

1. `IsAllowLoad`（`IsSaved`、值字典完整、busy/Waiting、暂停）；
2. TAS state 校验；
3. `LoadStateRouteDecider.Decide(hasLiveLevel, IsSameTargetArea(...))`（`PendingLoadWatchdog.cs`）：
   - **Direct**：当前 `Engine.Scene` 是 Level 且 `Session.Area` 与模板一致（`ID+Mode`，并校验 SID 防
     mod 列表变化后 id 复用）→ 走历史同步直读 `LoadStateCore(..., mergeTimeAndDeaths: true)`，
     行为与旧版一致；
   - **Pending**：其余情况（别的章节 / Overworld / 正在 LevelLoader 等）→ `BeginPendingLoad`。
4. Pending 还额外拒绝：TAS（见 §1.4）、`SaveData.Instance == null`、`LevelEnter.ErrorMessage != null`
   （上一章节加载失败，再进只会弹 error postcard）、目标 `AreaData`/mode 不可用（mod 被禁用、SID 变化）。

### 4.2 Pending 流程（`StateManager.PendingLoad.cs`）

`BeginPendingLoad`（`StateManager.PendingLoad.cs:127`）：

1. `WaitForPreCloneTask()`（后台 preclone 正在读模板，不能并发改模板）+ `ClearSharedDeepCloneState()`；
2. 记录回滚快照：`SaveData.Instance`、`LastArea_Safe`、`CurrentSession_Safe`、每个 mod 的
   `_Session/_SaveData` 引用（`CaptureModuleStates`）；
3. **先合并时间/死亡数**：`MergeTimeAndDeaths`（`StateManager.Restore.cs:35`）读取**仍在运行的当前场景**
   （Level/SaveData/`CurrentSession_Safe`），语义与原 `UpdateTimeAndDeaths` 一致（默认"不还原时间/死亡数"，
   读档不倒退；跨存档文件时跳过合并，避免把 B 文件统计写进 A 文件）；
4. `SaveData.Instance = savedSaveData.DeepCloneShared()`；
5. `TargetSession = savedLevel.Session.DeepClone()`（保留存档时的 room/checkpoint/inventory/flags）；
6. `State = Loading`；
7. 在**当前场景的 `OnEndOfFrame`**（即 `Scene.AfterUpdate`，位于 Monocle 帧末切场景之前）调用
   `LevelEnter.Go(TargetSession, fromSaveData: true)`：走标准章节进入流程（Everest 事件、
   `LevelEnter`→`LevelLoader`），`fromSaveData: true` 跳过 area postcard / B-Side title / intro vignette。
   延迟到帧末是为了让这次切场景**赢过启动读档的那个场景自己的 `Engine.Scene` 赋值**
   （LevelLoader 启 Level、OverworldLoader 进 Overworld、LevelEnter 进 LevelLoader 都发生在 Update 里），
   否则"切场景后立刻读档"会被覆盖；进入时顺带清掉 `Engine.FreezeTimer` 避免 AfterUpdate 被冻结帧跳过；
8. 立即返回 `true` + `"Load from [slot]"`。

`Level.Begin` 钩子（`OnLevelBeginFinishPendingLoad`，`StateManager.PendingLoad.cs:241`）在其 **post-orig** 阶段执行
（此时 `SaveSlotsManager.HandleSwitchScene` 已经因为 Level 实例变化重跑并 `Wait()` 了 preclone，
`Engine.Scene` 已经是新 Level）：

- 校验新 Level 的 `Session.Area` 与模板一致；
- `pendingLoad = null`，执行抽出的 `LoadStateCore(level, tas: false, mergeTimeAndDeaths: false)`：
  完全复用原恢复流程——`SaveLoadAction.OnBeforeLoadState`、`SetSharedDeepCloneState(preClone)`、
  `UnloadLevel`、`Tracker.Refresh`、`savedLevel.DeepCloneToShared(level)`、`SaveData.Instance = ...`、
  恢复 transition routine / audio / cassette / `SaveLoadAction.OnLoadState`、`PreCloneSavedEntities`、
  非 TAS 的 `RestoreLevelTime` + `FreezeGame(Load)` + `DoScreenWipe`；异常走现有
  `RecoverFromFailedLoad`（半毁 Level 会在下一帧 `Reload()`）。
- 失败时 popup 提示并保留 savestate；此时游戏已在目标章节里，用户再按一次读档会走 Direct 路径。

`Level.LoadLevel` pre-orig 钩子（`OnLevelLoadLevelRestoreModuleStateEarly`，`StateManager.PendingLoad.cs:312`）：
`LevelLoader` 的 `SaveData.StartSession` 会把所有 mod `_Session` 换成空对象；在目标章节创建实体**之前**
（`StartLevel`→`Level.LoadLevel`），先用存档值恢复 `_Session/_SaveData`
（`SaveLoadAction.TryRestoreSavedModuleStateEarly`，`SaveLoadAction.cs:358`），让 mod 的 load-time 代码
看到"保存时"的 session；随后完整的 `OnLoadState` 才是权威恢复。best effort，异常只记日志。

### 4.3 看门狗与回滚（失败必须安全）

`Scene.BeforeUpdate` 钩子（`WatchPendingLoad`，`StateManager.PendingLoad.cs:341`）每帧检查 pending：

- 场景切换是**延迟**到帧末的，请求后允许 `4` 帧 grace；
- 之后按 `PendingLoadWatchdog.Decide`（纯逻辑，可离线单测，见 §7）判定：
  - 还在起始场景 → `TransitionDidNotStart`（被别的 mod 覆盖了 `Engine.Scene`）；
  - `LevelEnter.ErrorMessage != null` → `LevelEnterError`；
  - 退回 Overworld/OverworldLoader → `ReturnedToChapterSelect`；
  - 已经过 `LevelLoader` 后又出现 LevelEnter → `LevelLoaderGaveUp`；
  - pending 还在但场景已是 Level → `LoadedLevelIsNotTheTarget`；
  - 30s 超时 → `TimedOut`。
- `AbortPendingLoad`（`StateManager.PendingLoad.cs:422`）：
  1. 回滚 `SaveData.Instance`/`LastArea_Safe`/`CurrentSession_Safe` 和所有 mod `_Session/_SaveData`
     引用（`RollbackPendingReferences`，`StateManager.PendingLoad.cs:506`）；
  2. `State=None`、`freezeType=None`、`Engine.FreezeTimer=0`；
  3. 清掉 `LevelEnter.ErrorMessage`（避免下次进章节弹旧错误 postcard）；
  4. 若游戏卡在过渡中间（重载 scene 不是目标 Level、且不是起始场景）→
     `Engine.Scene = new OverworldLoader(Overworld.StartMode.AreaQuit)`（与游戏自身的加载失败流程一致）；
     popup 延迟到正常场景再显示；
  5. **不清档**：`savedLevel`/`savedSaveData` 保留，可重试。
- `ClearState/ClearAll/资产热重载`（`ClearStateImpl`、`SaveSlotsManager.AfterAssetReload`）会先
  `AbortPendingLoadForClear` / `CancelPendingLoadForAssetReload`，避免 `savedLevel=null` 的 NRE 和
  "等到一个永远不会来的 Level"。

### 4.4 设置与 UI

- `AutoClearStateOnSceneSwitch` 默认 `false`（`SpeedrunToolSettings.cs:86`），注释说明为 legacy 保守模式；
- 菜单项从 `#if DEBUG` 中释放（`SpeedrunToolMenu.cs:123-126`），并加悬浮描述；
- 文案（`Dialog/English.txt`、`Dialog/Simplified Chinese.txt`）：
  - `...AUTO_CLEAR_STATE_ON_SCENE_SWITCH=... (Legacy) / ... (旧行为)`
  - 新增 `..._DESCRIPTION`：开启=离开章节时清档（旧行为）；关闭（默认）=状态跨场景保留，读档自动回到保存章节；
- `CannotSaveLoad()` 拆成 `CannotSave()`（必须 Level）与 `CannotLoad()`（有 SaveData 即可），
  `LoadState` 热键与 `LoadFromLastSlot` 热键放宽到 `Overworld`（保存仍限 Level）；
- README/README_CN 增加功能说明与"跨章节读档 ≈ 多一次章节加载"的性能说明。

---

## 5. 实现清单（本分支）

| 文件 | 改动 |
| --- | --- |
| `Source/SaveLoad/StateManager.cs` | `LoadStateImpl` 改为路由；抽出 `LoadStateCore`；新增 pending 字段与 3 个钩子注册；`ClearStateImpl` 先取消 pending；`IsAllowLoad` 支持无 Level |
| `Source/SaveLoad/StateManager.PendingLoad.cs` | **新增**：pending 状态机、begin/finish/watchdog/abort/回滚、early module session 钩子、目标 area 校验 |
| `Source/SaveLoad/StateManager.Restore.cs` | `UpdateTimeAndDeaths` → `MergeTimeAndDeaths(session, saveData, level)`，支持"离开场景前合并"与跨存档文件跳过 |
| `Source/SaveLoad/StateManager.Transitions.cs` | 更新 v3.28 注释，指向 pending load |
| `Source/SaveLoad/PendingLoadWatchdog.cs` | **新增**：纯逻辑路由/看门狗判定（无游戏依赖，离线可测） |
| `Source/SaveLoad/SaveSlotsManager.cs` | `CannotSave`/`CannotLoad` 拆分；`RunWithSlot`；资产重载前取消 pending；TAS 入口改用对应检查 |
| `Source/SaveLoad/SaveLoadAction.cs` / `.BuiltinActions.cs` | 记录 module session action 引用；新增 `TryRestoreSavedModuleStateEarly` |
| `Source/SpeedrunToolSettings.cs` / `SpeedrunToolMenu.cs` / `DialogIds.cs` / `Dialog/*.txt` | 默认值、菜单解禁、描述文案 |
| `Source/MoreSaveSlotsUI/SaveLoadHotkeys.cs` / `SwitchAndSaveLoad.cs` | 读档热键允许 Overworld |
| `README.md` / `README_CN.md` | 功能/性能说明 |
| `tests/SceneStateTests/` | **新增**：离线单测（链接 `PendingLoadWatchdog.cs`，无游戏依赖） |

公开 API/ModInterop 签名未变。语义变化：
- 非 TAS 的 `LoadState` 跨章节时会在目标章节加载完成后才真正恢复（返回 `true` 表示"已接受并开始加载"，
  不是"本帧已恢复"）；同章节仍是同步完成。
- `LoadStateTas` 跨章节返回 `false`（state 保留），需要手动读档。

---

## 6. 兼容性、风险与回退

| 风险 | 说明 / 缓解 |
| --- | --- |
| `SaveData.Instance` 整体替换（既有 D8） | 跨存档文件读档会把模板 SaveData 装回去；外部缓存了旧 `SaveData.Instance`/`module._Session` 的 mod 仍可能 desync。pending 失败会回滚引用，成功则与同章节读档同风险 |
| 跨存档文件读档后的 `FileSlot` | 恢复后的 `SaveData.Instance.FileSlot` 是**存档文件侧**的文件号；之后游戏内保存/退出会写回该文件（这是"多存档可存不同原版存档文件"语义的一部分）。bench 只做只读验证，不触发保存 |
| 目标章节实体先加载再丢弃 | 跨章节读档 = 一次完整章节加载 + 常规 load；会触发章节进入音效/实体 `Added` 副作用。日志/计时可比对；暂不做"跳过 LoadLevel 实体创建"的优化（改动大、风险高） |
| mod load-time 行为变化 | 目标章节加载时先恢复存档的 mod `_Session/_SaveData`；若某 mod 仍依赖空 session 的进入语义，需 bench 重点观察（SJ/CollabUtils2） |
| TAS | 跨章节直接拒绝（同步契约）；不影响同章节 TAS |
| 资产热重载/清除与 pending 并发 | 会先取消 pending 并回滚；极端情况下由资产重载自身重置场景 |
| 旧设置文件 | 保留用户已写的 `true`（旧行为）；新用户默认 `false` |
| 回退方案 | 本特性集中在 `StateManager.PendingLoad.cs` + `LoadStateImpl` 的 3 行路由；`git revert` 单个 commit 即可回到直读行为，设置默认值与 UI 文案可独立回退 |

---

## 7. 离线验证（已完成）

- `dotnet build -c Release -p:CelestePrefix=...`：**0 error**（仅 3 个既有 CS0618 警告）。
- `dotnet run --project tests/SceneStateTests -c Release`（或 `cd tests/SceneStateTests && dotnet run -c Release`）：
  **15 项全过**（路由 + 看门狗真值表）。
- 代码审查要点：`RunWithSlot` 保证 pending 钩子里 `Slot/SlotName` 正确；所有 `LoadStateCore` 失败路径
  都经过 `RecoverFromFailedLoad`；看门狗每种失败都有 popup + state 保留。

---

## 8. 游戏内验证（bench 计划）

> 由 bench 在唯一游戏实例上执行；需先把本分支构建的 SpeedrunTool.dll 部署到游戏 Mods（Lead 协调）。
> 建议开启 `log.txt` 并记录 `SpeedrunTool/PendingLoad` 日志行。

| # | 场景 | 步骤 | 预期 | 成本（估） |
| --- | --- | --- | --- | --- |
| B1 | 同章节回归 | A 图存 F7 → 走到别的房间 / 死亡 / PageUp → F8 | 与改动前一致：回到存档房间，贴图/音频正常，无 crash | 3–5 min |
| B2 | 章节 A→B | A 图存 → 回章节选择进 B 图 → F8 | 自动进 A 并恢复；`pending` 日志有 "Entering 'A'" 与 "is ready, restoring"；贴图/背景/音乐/实体正常 | 5–8 min（含一次章节加载） |
| B3 | Overworld→A | A 图存 → 回章节选择（默认 AutoClear=false）→ 在 Overworld 按 F8 | 同上；若用 ModInterop 则调用 `SpeedrunTool.SaveLoad.LoadState()` | 5–8 min |
| B4 | 不同原版存档文件 | 文件 1 的 A 图存 → 退到标题 → 进文件 2 的 B 图 → F8 | 进入 A（文件 1 的 SaveData/时间/死亡数）；不 crash；再退出到标题不损坏文件 2 的存档（注意只检查项，不做存档写入） | 6–10 min |
| B5 | CollabUtils2 helper | 开启 legacy `AutoClear=True` → 打开 CJ2 in-game overworld helper 跨章节 → F8 | helper 打开时状态仍保留，F8 自动进存档章节并恢复；关闭 helper 后正常 | 5–8 min |
| B6 | 切场景立刻读档 | A→B 刚进 B（Level.Begin 后立即）按 F8；以及 Overworld 加载过程中用 ModInterop 调 `LoadState()` | 前者 pending 进 A 成功；后者若被其它 scene 覆盖则 popup 拒绝 + 保持可玩 + state 保留（看门狗 abort），重试成功 | 5–8 min |
| B7 | 加载失败回滚 | 临时禁用存档章节的 mod（或换一张已删除的 mod 图存档）再 F8 | popup "Cannot Load: ... not loaded by any mod" 或看门狗 abort；游戏回到章节选择/可玩，state 仍保留；log 有原因 | 3–5 min |
| B8 | 跨章节读档的性能/内存 | B2 全流程计时与 GC/内存日志 | 记录"章节加载 + 恢复"总耗时、峰值内存；与同章节读档对比，作为文档数据 | 3–5 min |

合计约 35–55 分钟（含地图加载）。B2–B6 需要目标章节资源真实存在；建议用 SJ 图（Grandmaster
Heartside）+ CollabUtils2 场景各跑一次。

---

## 9. 已知未做 / 后续

- 跨章节读档的"跳实体创建"优化（当前会先完整加载目标章节实体再丢弃）——需要 IL hook
  `Level.LoadLevel`，风险高，暂不做；bench 数据可决定是否值得。
- 跨存档文件读档的时间/死亡数合并策略：当前跨文件跳过合并（保留模板值）。
- 若 bench 发现某些 mod 在目标章节加载期需要更完整的 session 语义，可扩展 early restore 到更多
  action（目前只恢复 `_Session/_SaveData`）。
