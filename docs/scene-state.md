# 跨场景（跨章节 / 章节选择界面）Savestate：现状、根因、设计与验证

状态: **已实现并部分验证**（分支 `feat/scene-persistent-state`，代码 commit `af3e8bd`，基于 `6fa430a`）。
自动化子集已由 bench 验证：**B1 同章节 / B2 A→B / B4 不同存档文件（只读）/ B6 切场景立刻读档 PASS，0 abort**；
B3 Overworld→A、B5 CollabUtils2 helper、B7 加载失败回滚仍需手动验证（清单见 §8.2）。
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
   **bench 实测确认**：bench 环境的 `Saves/Settings/SpeedrunTool.celeste` 里该值仍是旧的 `true`，因此切章节
   仍会清档；harness 覆盖为 `false` 后 B1/B2/B4/B6 全部正常。**升级用户想用"跨场景保留"需在
   State 设置里手动关闭该项**（或重置/删除该设置条目，让它使用新默认）；如果不关，行为与旧版一致（推荐的
   保守回退方式）。

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
| CollabUtils2 in-game overworld helper | legacy 下打开 helper 时会被误清档，跨章节读档必坏 | helper 是**包在 Level 里的 wrapped `Overworld`**（`SceneWrappingEntity`），打开时 `WrappedScene.Begin()` 会触发 `On.Monocle.Scene.Begin(Overworld)` → 旧 `HandleSwitchScene` 误判为"进入 Overworld"而清档（`IsOpen` 特判就是为此）；真正切章节仍会走目标章节的 `Level.Begin`（area 不同） |
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

## 8. 游戏内验证（bench）

> 由 bench 在唯一游戏实例上执行；需先把本分支构建的 SpeedrunTool.dll 部署到游戏 Mods（Lead 协调）。
> 建议开启 `log.txt`，过滤 `SpeedrunTool/PendingLoad` 与 `SpeedrunTool/LoadState`。
> 构建是 Release（无 profiling 代码）；需要分阶段耗时/内存时另建 `-p:SrtProfiling=true` + `srt-profile.flag`。

### 8.1 自动化子集结果（bench，2026-11-10，代码 `af3e8bd`）

- 环境：`scenefix` 构建的 SpeedrunTool.dll，harness 把 `AutoClearStateOnSceneSwitch` 覆盖为 `false`
  （用户设置文件里仍是旧值 `true`，见 §1.5）。
- 结果：

| # | 场景 | 结果 | 数据/备注 |
| --- | --- | --- | --- |
| B1 | 同章节回归 | **PASS** | 与改动前一致（直读路径，无 pending 日志） |
| B2 | 章节 A→B | **PASS** | `PendingLoad: ... is ready after **1452 ms**` |
| B3 | Overworld→A | 未自动化 | 手动清单见 §8.2 |
| B4 | 不同原版存档文件（只读） | **PASS** | `is ready after **1505 ms**`；只读验证，未触发游戏内保存 |
| B5 | CollabUtils2 helper | 未自动化 | 手动清单见 §8.2 |
| B6 | 切场景后立刻读档 | **PASS** | happy path：`is ready after **1495 ms**`；说明 `OnEndOfFrame` 延迟进入赢过了原 scene 的 `Engine.Scene` 赋值 |
| B7 | 加载失败回滚 | 未自动化 | 手动清单见 §8.2 |
| B8 | 性能/内存 | 部分 | 另一轮 `is ready after **1476 ms**`；整条 save→restored wall ≈ **3.5 s**（含 harness 的 1 s 观察 pause，实际 ≈2.5 s）；峰值内存未采，需要时用 SRT_PROFILE 或外部采样 |

- 解读：章节进入（`Entering` → `is ready`）稳定在 **约 1.45–1.51 s**，与设计预期"≈一次章节加载"一致；
  剩余 ≈1.0 s 是常规 restore（`UnloadLevel` + clone-back + actions + wipe）。同章节读档（B1）没有额外成本。
- **0 abort**：自动化只走了 happy path，watchdog/rollback 的失败分类没有被自动化覆盖；失败路径由
  §8.2 的 B7 手动验证 + `tests/SceneStateTests` 的真值表覆盖。
- 未发现与设计预期不符的数字或行为，**无需追加修复 commit**。

### 8.2 B3 / B5 / B7 手动验证清单

前置：
- 先把 State 设置里的 **Auto Clear State on Chapter Transition (Legacy)** 关掉（否则 B3/B5 的跨章节状态会被清）；
  若测试 legacy 行为（B5)则按该项单独说明。
- 只读验证：不要触发游戏内保存/退出保存（尤其 B4/B7 之后），避免把 Savestate 的 `FileSlot` 写回原版存档。
- 日志过滤：`grep -E "SpeedrunTool/(PendingLoad|LoadState)" log.txt`。

#### B3 Overworld → 章节 A（约 3–5 min）
1. 进入章节 A 的某个房间 → F7 存到 slot 1（日志 `SpeedrunTool/SaveState: Save to [Default Slot]`）。
2. 通过暂停菜单"回到章节选择/退出章节"回到 Overworld（看到章节选择界面即可，不必回标题）。
3. 在 Overworld 直接按 **F8**（读档热键已在 Overworld 放宽）。
4. 预期日志顺序：
   - `SpeedrunTool/PendingLoad: Entering '<A 的 SID> [<room>]' to load the state from [Default Slot]`
   - `SpeedrunTool/PendingLoad: '<A 的 SID> [<room>]' is ready after N ms, restoring the state from [Default Slot]`
   - 无 `Pending load watchdog` / `Aborting the pending load`。
5. 预期表现：自动进入 A 的存档房间并恢复；贴图/tileset/背景/音乐/cassette/实体正确；存档位置/朝向/时间与 F7 时一致。
6. （可选 ModInterop 变体）在 Overworld 调 `SpeedrunTool.SaveLoad.LoadState()`，预期同上。

#### B5 CollabUtils2 in-game overworld helper（约 5–7 min）

背景（已按 CollabUtils2 反编译确认）：helper 是包在 **当前 Level** 里的 `SceneWrappingEntity<Overworld>`
（`Engine.Scene` 仍是 Level）。打开 helper 时 `WrappedScene.Begin()` 会触发一次
`On.Monocle.Scene.Begin(Overworld)`；`HandleSwitchScene` 里 `InGameOverworldHelperIsOpen` 特判的作用就是
**避免 legacy 模式仅仅因为打开 helper 就误清档**。真正切换章节仍然会进目标章节的 `Level.Begin`
（area 不同 → legacy 语义下会清档）。

**B5a legacy 模式：打开 helper 不应误清档（约 3 min）**
1. 开启 `Auto Clear State on Chapter Transition (Legacy) = On`。
2. 进入章节 A → F7 存 slot 1（helper 打开时 SRT 会拒绝保存，所以必须先存）。
3. 在 LEVEL 内打开 CJ2 in-game overworld helper（**不要在里面选章节**）。
4. 预期：log 中没有因打开 helper 产生的 `SpeedrunTool/ClearState: Clear [Default Slot]`；
   `IsSaved` 仍为 true（关闭 helper 后按 F8 能读，而不是 "Not saved state yet"）。
5. 关闭 helper → 按 F8：同一章节走**直读**（没有 `PendingLoad` 日志，popup `Load from [Default Slot]`），
   恢复正确、helper 消失、无 crash。
6. 可选（新覆盖）：**保持 helper 打开**直接按 F8 → 也应恢复 A 并关闭 helper；若 crash/卡住请回传 log。

**B5b legacy 反例：helper 里真正切章节会清档（预期，约 2 min）**
1. 仍为 legacy=On，A 存 slot 1。
2. 打开 helper，在其中选另一个章节 B 并进入。
3. 预期：进入 B 的 `Level.Begin` 时清档（`SpeedrunTool/ClearState`），之后 F8 显示 "Not saved state yet"。
   这是 legacy 语义，不是 bug。

**B5c 新语义（默认 AutoClear=false，可选但推荐，约 2–3 min）**
1. 把 `Auto Clear State on Chapter Transition (Legacy)` 关掉。
2. A 存 slot 1 → 打开 helper → 在 helper 里进入章节 B。
3. 在 B 里按 F8：预期 pending 自动回 A 并恢复（日志 `Entering 'A'` → `is ready after N ms`，无 abort）。

#### B7 加载失败回滚（约 5–8 min，两段）
B7a 目标章节不可用（安全、无需改文件，**在共享 bench 实例上也做**）：
1. 在章节 A（来自某个可单独禁用的 mod，或任意 mod 图）F7 存 slot 1。
2. 退出游戏；在 Mods 里把提供该地图的 mod 移走/改名（或用 Everest 的 mod 选择禁用）。
3. 启动游戏进入任意章节（不必是 A）→ F8。
4. 预期（不进入过渡，被前置校验拒绝）：
   - popup：`Cannot Load: the chapter of the savestate is not loaded by any mod!`（或 SID/mode 变化的对应文案）；
   - 日志：`SpeedrunTool/PendingLoad: Cannot Load: ...`；
   - 游戏保持可玩、**savestate 仍保留**（再按 F8 仍是同样拒绝，而不是 "Not saved state yet"）。
5. 把 mod 放回/启用后重进游戏，再按 F8 → 应成功（B2 流程）。

B7b 进入过渡后失败（**只在 disposable 游戏副本上做**，会改地图文件）：
1. 在副本里保留 mod 注册但让目标章节加载失败（例如改名/替换该 mod 的 map bin）。
2. 存有 A 的 savestate → 从别的章节 F8。
3. 预期（watchdog 回滚路径）：
   - 日志：`Pending load watchdog: LevelEnterError (the game failed to load the saved chapter)` 和/或
     `ReturnedToChapterSelect`、随后 `Aborting the pending load from [<slot>]: <reason>`；
   - 回滚后 `SaveData.Instance`/模块 `_Session/_SaveData` 恢复成读档前的引用；游戏回到章节选择/可玩，不 crash/不软锁；
   - savestate 保留：恢复地图文件后重进，F8 应成功（B2 流程）。
4. 失败判定：卡在 loading/黑屏不返回、crash、或 savestate 丢失、或游戏进入目标章节但半恢复（贴图/实体异常）→ 回传 log.txt 定位。

### 8.3 完整矩阵（参考，含已自动化项）

| # | 场景 | 步骤 | 预期 | 状态 |
| --- | --- | --- | --- | --- |
| B1 | 同章节回归 | A 图存 F7 → 换房间/死亡/PageUp → F8 | 与改动前一致，无 pending 日志 | PASS（自动化） |
| B2 | 章节 A→B | A 存 → B 图 → F8 | 自动进 A 恢复；`Entering`→`is ready` | PASS（1452 ms） |
| B3 | Overworld→A | A 存 → 回章节选择 → F8 | 同上 | 手动（§8.2） |
| B4 | 不同原版存档文件 | 文件 1 存 A → 文件 2 的 B → F8 | 进 A 用文件 1 SaveData；只读 | PASS（1505 ms，只读） |
| B5 | CJ2 helper | legacy On：打开 helper 不误清档，关闭后 F8 直读；默认 false：helper 进 B 后 F8 回 A | 见 §8.2 的 B5a/B5b/B5c | 手动（§8.2） |
| B6 | 切场景立即读档 | A→B 刚进 B 后立即 F8 | pending 进 A | PASS（1495 ms） |
| B7 | 加载失败回滚 | 禁用目标 mod / 破坏目标 map → F8 | 合法拒绝或 watchdog abort；可玩、state 保留 | 手动（§8.2） |
| B8 | 性能/内存 | B2 全流程计时/采样 | 章节加载 + 恢复耗时/峰值内存 | 部分：1476 ms entry，wall≈2.5 s(+1 s pause)，内存未采 |

## 9. 已知未做 / 后续

- 跨章节读档的"跳实体创建"优化（当前会先完整加载目标章节实体再丢弃）——需要 IL hook
  `Level.LoadLevel`，风险高，暂不做；bench 数据可决定是否值得。
- 跨存档文件读档的时间/死亡数合并策略：当前跨文件跳过合并（保留模板值）。
- 若 bench 发现某些 mod 在目标章节加载期需要更完整的 session 语义，可扩展 early restore 到更多
  action（目前只恢复 `_Session/_SaveData`）。
- **验证状态**：自动化 bench 覆盖 B1/B2/B4/B6（PASS，0 abort）；B3/B5/B7 待手动执行 §8.2；
  B8 只采到耗时（entry ≈1.45–1.51 s，wall ≈2.5 s + 1 s 观察 pause），峰值内存未采（可另建
  `-p:SrtProfiling=true` 构建用 `SRT_PROFILE` 行或外部采样）。目前没有发现需要追加修复 commit 的问题。
- 旧设置文件里的 `AutoClearStateOnSceneSwitch=true` 会让升级用户默认仍是旧行为（bench 实测）；
  README/本节已说明手动关闭方式，未做自动迁移（避免静默改变老用户行为）。
