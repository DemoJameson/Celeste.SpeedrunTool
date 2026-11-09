# SJ · GM Heartside savestate 兼容性静态分析报告

> 只读分析，未修改任何源码。分析对象：`D:\Games\SteamLibrary\steamapps\common\Celeste\Mods` 下已安装的 SJ 及其 helper mod。
> 方法：Mono.Cecil 静态分析（字段/IL/调用图）+ 地图 `LevelData` bin 的字符串反查。工具与原始输出在临时目录（见文末“方法附录”，可随时复现）。
> 配套文档：`docs/audit.md`（SRT 自身性能/内存/bug 审计）、`docs/audit-instrumentation.md`（插桩方案）。

---

## 0. 结论摘要（给 Lead 的行动清单）

**已验证的“好消息”**
1. GM chapter 用到的 30 个 helper dll 里，**只有 5 个类型**采用“`Added` 注册 hook + `Removed` 注销 hook”的**实体级 hook** 模式（SRT 读档会丢 hook）。其中 2 个 SRT 已经手工处理（PandorasBox.TimeField、SpirialisHelper.TimeController），**真正未覆盖的只有 2 个**：
   - `YetAnotherHelper.Entities.SpikeJumpThruController`（`Awake` 注册 `Spikes.OnCollide`，`Removed` 注销）——**该类型在 chapter 地图里被使用**。
   - `CollabUtils2.Entities.CollabCrystalHeart`（`Added` 注册 `Level.RegisterAreaComplete/CompleteArea`，`Removed` 注销）。
   其余 mod（FrostHelper/CommunalHelper/MaxHelpingHand/VivHelper/CrystallineHelper/SJ2021 等）的 hook 全部是**模块生命周期**（`LoadIfNeeded`/`Load` 注册，`Unload` 只在 mod 卸载时调用；实测这些 `Unload` 在程序集内**没有调用者**）→ **SRT 读档不会移除它们，不需要重建**。这是本次分析最重要的结论，避免了对 FrostHelper 等做大量无用 workaround。
2. **FrostHelper 的“FrostTemple 系列”在本 chapter（含 Heartside）的地图数据里完全没有出现**（`grep -i temple` 只命中原版贴图/音效路径）。FrostHelper 在 Heartside 用到的是 20 个其它实体（见 §5），在 chapter 用到 35 个。
3. FrostHelper 的静态状态基本都被 SRT 的通用静态扫描覆盖（静态 `MTexture`/`MTexture[]`/`List<MTexture>`/bool 标志）；模块类的 static int（`FrostModule.CustomDreamDashState`）经 Cecil 读写点分析确认是 **Player 状态注册 ID**，不需要保存（§7.3）。**FrostHelper 唯一真实缺口是实体持有的裸 `EventInstance`**（`ToggleSwapBlock.moveSfx/returnSfx`），结论见 §6。
4. **多个 mod 自己接入了 SRT ModInterop**（`[ModImportName("SpeedrunTool.SaveLoad")]`）：`MaxHelpingHand`（`RegisterSaveLoadAction` 保存 `flagMapping`/`colorsMildOverride`；`RegisterStaticTypes` 保存 `StylegroundFadeController` 静态字段）、`FrostHelper`（`AddReturnSameObjectProcessor`：`ISavestatePersisted` 类型共享不克隆）、`CommunalHelper`/`FemtoHelper`/`EeveeHelper`（`RegisterStaticTypes`）。→ SRT 侧对这些 mod 的兼容有相当一部分是 mod 作者自己补的，**不要重复实现**。
5. `FrostHelper.ModIntegration.ISavestatePersisted` 的 16 个实现类型（`ColorRainbowChannel`/`CustomSpinnerSpriteSource`/`VirtualRenderTargetRef`/`SessionExpressions.CompiledCondition<>` 等）由 FrostHelper 在 `LoadContent` 里通过 `AddReturnSameObjectProcessor` 声明为**共享不克隆**；SRT 的 `ClearCaches()` 修复（D14）保证这个迟到注册能生效。

**需要 Lead 补代码的清单（按优先级）**
| # | 目标 | 类型 | 建议改动 / 现状 |
|---|------|------|----------|
| 1 | hook 重建 | `YetAnotherHelper.Entities.SpikeJumpThruController` | **已由 Lead 落地**（`YetAnotherHelperUtils.cs`）：loadState 里 `On.Celeste.Spikes.OnCollide -= onCollideHook; += onCollideHook;` 幂等重挂（`OnCollideHook` 是 private static，用 `Delegate.CreateDelegate` 取），并按房间内是否有该类型实体校正 `SpikeHooked`。**不要**调用 `Load()`/`Unload()`（那是模块生命周期的 `Level.LoadLevel` hook）。 |
| 2 | hook 重建 | `CollabUtils2.Entities.CollabCrystalHeart` | **已由 Lead 落地**（`CollabUtils2SavestateUtils.cs`）：loadState 先 `unhook()`（幂等，清掉 SRT 静态恢复的 `hooked=true` 与实际未挂钩的不一致）再按需 `hook()`。GM Heartside 未使用该实体（bin 里 0 命中），属通用兼容。 |
| 3 | ~~静态状态~~ | `MaxHelpingHand.Entities.MovingFlagTouchSwitch.flagMapping` | **无需改动**：MHM 自己通过 SRT ModInterop（`[ModImportName("SpeedrunTool.SaveLoad")]` + `RegisterSaveLoadAction`）保存/恢复 `flagMapping` 与 `BlackholeCustomColors.colorsMildOverride`（`MaxHelpingHand.Module.SpeedrunToolInterop.Initialize`，save lambda `<>c::<Initialize>b__2_0`）。**不要**再 `CloneModTypeFields`（会重复保存）。 |
| 4 | ~~模块静态状态~~ | `VivHelper.VivHelperModule` 的 static int 组 | **不要克隆**：Cecil 读写点显示 `BooState/PinkState/OrangeState/WindBoostState/CustomDashState/CustomBoostState` 只在 `Player_ctor` 里注册（Player 状态机 state ID），`type` 只在 `Load` 里 setup；克隆=同值、无收益。`createdCassetteManager`（per-level 派生 bool）先不动，bench 发现问题再说。 |
| 5 | ~~模块静态状态~~ | `FrostHelper.FrostModule.CustomDreamDashState` | **不要克隆**：只写于 `.cctor` / `PlayerOnOnRegisterStates`（Player 状态注册），读于 `CustomDreamBlock` 的 4 个 hook 里与 `player.StateMachine.State` 比较 → 注册 ID，不是 per-run 状态。 |
| 6 | 裸 `EventInstance` | 见 §6 表 | **结论：不做通用重建**（见 §6 末尾的风险/收益分析）；只作为已知风险记录；若 bench 定位到具体音频 bug，再对白名单类型（`FrostHelper.ToggleSwapBlock`、`SJ2021.WonkyCassetteBlockController`）做定向处理。 |
| 7 | 静态 Camera 引用 | `VivHelper.Entities.RefillCancelSpace.camera` | 低优先；load 后把它重新指向 `Engine.Scene.Camera`（或接受风险）。 |

---

## 1. GM Heartside 场景信息（给 bench-env）

- **地图 SID**：`StrawberryJam2021/5-Grandmaster/ZZ-HeartSide`
- **地图 bin**：`Mods/StrawberryJam2021/Maps/StrawberryJam2021/5-Grandmaster/ZZ-HeartSide.bin`（2.49 MB，含全部房间）
- **Lobby（章节菜单）SID**：`StrawberryJam2021/5-Grandmaster`（`Maps/.../0-Lobbies/5-Grandmaster.bin`）
- **chapter 内其它可选测试图**（同一套依赖，适合快速定位机制）：
  `5-Grandmaster/` 下：`CaptainCarpensir`、`Dav`、`DeathKontrol`、`Ecl1psed`、`Hydro`、`Linj`、`RedBatNick`、`Soloiini`、`Todd`、`alisticious`、`fishtank`、`math`、`maya`、`tcookiem`、`tobyaaa`、`tofu`、`xlibiza`、`xplosives`
- 注意：`SRT` 的 `ExternalSafeOracle.SafeMaps` 只包含 `StrawberryJam2021/5-Grandmaster/maya`（`DesyncRiskAnalyzer.cs:23`），**不包含 `ZZ-HeartSide`**。
- 房间名：位于 bin 内部 `Room.Name` 字段；本次没有解析（需读 Celeste `LevelData` 的私有反序列化逻辑）。若 bench-env 需要“房间名列表”，可以：
  - 用游戏自带 DebugMap 打开该图逐屏记录；或
  - 让我再补一个 `LevelData` 解析脚本（读 `Maps/.../*.bin` 的 Room 段）。
  Bench 场景最小化建议：优先 `ZZ-HeartSide` 的前 2–3 屏 + 任一含 `FrostHelper/ToggleSwapBlock`、`CommunalHelper/DreamMoveBlock`、`SJ2021/WonkyCassetteBlock*`、`YetAnotherHelper/SpikeJumpThruController` 的屏幕。

---

## 2. 依赖 mod 使用统计（证据：bin 内实体名字符串）

统计方式：从 `*.bin` 中按 `BinaryWriter` 的 7-bit 长度前缀提取字符串，筛出 `ModName/EntityName` 形式（排除 `.png/.ogg/...` 资源路径），按 mod 前缀计数。

**GM Heartside（`ZZ-HeartSide.bin`，251 个唯一实体 ID）**
| mod | 次数 | | mod | 次数 |
|---|---|---|---|---|
| MaxHelpingHand | 39 | | FlaglinesAndSuch / FancyTileEntities / DJMapHelper | 各 4 |
| VivHelper | 33 | | XaphanHelper / VortexHelper / ShroomHelper / FemtoHelper / CollabUtils2 / BrokemiaHelper | 各 3 |
| SJ2021（SJ 本体） | 24 | | YetAnotherHelper / MoreDasheline / LunaticHelper / JungleHelper / CherryHelper / AdventureHelper | 各 2 |
| **FrostHelper** | **20** | | SorbetHelper / Sardine7 / FurryHelper / EeveeHelper / DisposableTheo / CrystallineHelper(`vitellary`) / SpirialisHelper(`spirialis`) / LuaCutscenes | 各 1 |
| CommunalHelper | 15 | | | |
| ContortHelper | 13 | | | |
| ExtendedVariantMode | 8 | | | |
| vitellary（CrystallineHelper） | 7 | | | |
| pandorasBox | 5 | | | |

**整个 GM chapter（18 张图，415 个唯一实体 ID，按出现次数）**
`MaxHelpingHand 299`、`VivHelper 147`、`everest 70`、`FrostHelper 69`、`SJ2021 68`、`CollabUtils2 52`、`ContortHelper 50`、`vitellary 39`、`pandorasBox 36`、`CommunalHelper 35`、`FancyTileEntities 30`、`ExtendedVariantMode 29`、`FlaglinesAndSuch 24`、`DJMapHelper 20`、`LunaticHelper 19`、`ShroomHelper 18`、`CherryHelper 17`、`HonlyHelper 15`、`VortexHelper / BrokemiaHelper 14`、`Sardine7 12`、`XaphanHelper 11`、`luaCutscenes / FemtoHelper / EeveeHelper 10`、`JungleHelper / AdventureHelper 7`、`YetAnotherHelper / SorbetHelper 5`、`DisposableTheo 4`、`MoreDasheline / MemorialHelper / CrystallineHelper 3`、`BounceHelper 3` …

**FrostHelper 在 Heartside 使用的 20 个实体**：
`BloomColorPulseTrigger, BlueBooster, CapDashOnGroundTrigger, CustomInvisibleBarrier, DecalContainer, EntityBatcher, EntityMover, EntityRainbowifyController, HackfixFlagLightSourceZone, LightOccluderEntity, OnFlagActivator, OnSpawnActivator, RainbowTilesetController, SpringFloor, SpringLeft, SpringRight, StaticBumper, StaticDoor, StylegroundMoveTrigger, ToggleSwapBlock`
（chapter 级共 35 个，另有 `CustomDreamBlock, Bubbler, ArbitraryShapeLightning, CustomFlutterBird, CustomRisingLava, CustomCrushBlock, ZoomManager, GrowBlock, ...`）

---

## 3. SRT 现有支持矩阵（对照 `SpeedrunTool/Source/SaveLoad/ThirdPartySupport/ThirdParty.cs`）

| mod | SRT 现状 | 与 GM HS 的关系 |
|---|---|---|
| FrostHelper | **零专门支持**（`ThirdParty.cs:16-24` 无此 mod；`grep FrostHelper` 在 Source 下 0 命中） | 20 实体/Heartside、69 次/chapter；见 §5 |
| CommunalHelper | 仅 `SeekerDash` 静态字段（已标 Obsolete，`ThirdParty.cs:80-86`） | 15 实体/Heartside |
| MaxHelpingHand | **零专门支持** | 39 实体/Heartside（使用最多的 helper） |
| VivHelper | 静态字段：`RefillCancel`/`SpeedPowerup`/`BooMushroom`/`Boosters`/`ExplodeLaunchModifier`/`Blockout`/`MoonHooks`/`HelperEntities`/`TeleportV2Hooks`（`ThirdParty.cs:108-122`） | 33 实体/Heartside；但模块静态 int 未覆盖 |
| CrystallineHelper | `VitModule.timeStopScaleTimer/timeStopType/noMoveScaleTimer`、`TriggerTrigger.collidedEntities`（`ThirdParty.cs:94-99`） | 7 实体/Heartside |
| PandorasBox | TimeField hook 重建 + MarioClearPipe 静态字段（`PandorasBoxUtils`） | 5 实体/Heartside |
| ExtendedVariantMode | 设置字段 + TriggerManager 克隆（`ExtendedVariantsUtils`） | 8 实体/Heartside |
| SpirialisHelper | TimeController hook 重建 + TimeZipMover ILHook 重建（`SpirialisHelperUtils`） | 1 实体 |
| CollabUtils2 | 仅 gym/lobby 判定 interop（`CollabUtils2Import`），**无 savestate 支持** | 6 实体/Heartside，含 MiniHeart（18 屏使用） |
| ContortHelper（50 次/chapter、13/Heartside） | **零支持** | 见 §4/§6 |
| YetAnotherHelper / CherryHelper / DJMapHelper / FlaglinesAndSuch / FancyTileEntities / LunaticHelper / ShroomHelper / VortexHelper / HonlyHelper / BrokemiaHelper / XaphanHelper / FemtoHelper / EeveeHelper / SorbetHelper / Sardine7 / FurryHelper / AdventureHelper / JungleHelper / outback / DisposableTheo / MemorialHelper / BounceHelper / MoreDasheline | **零专门支持** | 多数只有纯实例状态，SRT 克隆即可；但见 §4 的 hook 例外与 §6 的 EventInstance 列表 |

---

## 4. 实体级 hook 模式（读档会丢 hook 的类型）

**分析方法**：对每个 mod dll 用 Cecil 构建“方法 → 自身调用/`On.*`/`IL.*` 的 `add_`/`remove_` 事件”的调用图，对 `Added`/`Removed`/`Awake`/`Update`/`.ctor`/`LoadIfNeeded`/`LoadHooksIfNeeded` 做可达性分析，判断 hook 的注册/注销分别由谁触发。
**方法自检**：用已知案例 `PandorasBox.TimeField` 验证 → 工具正确输出 `add_from=[Added,Update] remove_from=[Removed]`，且 SRT 确实为它写了 `PandorasBoxUtils` hook 重建代码；对照 `PandorasBox.ColoredWater` 输出 `add_from/remove_from=[]`（静态 `Load/Unload`），SRT 也确实没有为它写代码。

分析覆盖 30 个 dll（含 chapter 使用的全部 helper）。结果：

| mod | 含 hook 的类型数 | 其中“实体级注册/注销”类型 |
|---|---|---|
| FrostHelper | 55 | 0 |
| CommunalHelper | 51 | 0 |
| MaxHelpingHand | 66 | 0 |
| VivHelper | 27 | 0 |
| CrystallineHelper(vitmod) | 25 | 0 |
| SJ2021 | 44 | 0 |
| ExtendedVariantMode | 127 | 0 |
| XaphanHelper | 74 | 0 |
| FemtoHelper | 30 | 0 |
| JungleHelper | 14 | 0 |
| BrokemiaHelper | 13 | 0 |
| SorbetHelper | 13 | 0 |
| DJMapHelper | 15 | 0 |
| CollabUtils2 | 16 | **1**：`CollabCrystalHeart`（`Added` → `Removed`） |
| CherryHelper | 5 | **1**：`AnterogradeController`（`Removed` 注销；chapter 未用到该类型） |
| PandorasBox | 7 | **1**：`TimeField`（已由 SRT 处理） |
| SpirialisHelper | 8 | **1**：`TimeController`（已由 SRT 处理） |
| YetAnotherHelper | 4 | **1**：`SpikeJumpThruController`（`Awake` → `Removed`）——**chapter 使用中，未覆盖** |
| 其它（FlaglinesAndSuch/FancyTileEntities/VortexHelper/ShroomHelper/HonlyHelper/LunaticHelper/EeveeHelper/AdventureHelper/BGswitch/outback/DisposableTheo/MoreDasheline/MemorialHelper…） | 0–5 | 0 |

**结论**：
- 其余 4 个“实体级”类型中 3 个（TimeField/TimeController/CollabCrystalHeart）SRT 已处理或影响面小；**唯一确定需要新增的工作是 `YetAnotherHelper.SpikeJumpThruController`**（它在 chapter 的 `YetAnotherHelper/` 3 个实体之一，且 hook 是 `Spikes.OnCollide`——读档后跳到该 JumpThru 上会失去特殊碰撞逻辑）。
- FrostHelper/CommunalHelper/MaxHelpingHand/VivHelper/CrystallineHelper/SJ2021 的 hook 全部是**模块生命周期**：`Unload` 方法在这些程序集内**没有任何调用者**（FrostHelper/CommunalHelper/MaxHelpingHand/SJ2021：0；VivHelper→`VivHelperModule.Unload`；CrystallineHelper→`VitModule.Unload`，都只在 mod 卸载时跑）。因此 SRT 读档**不会**移除它们，`_hooksLoaded`/`HooksLoaded` 这类 static bool 被 SRT 保存/恢复为 true 也是自洽的 → **不需要为它们写 hook 重建**。
- 注意：SRT 的 `UnloadLevel` 会对每个实体调 `Removed()`；上述 mod 的 `Removed` 不注销 hook，所以没问题；但**如果以后遇到“Added 注册 + Removed 注销”的新 mod**，可以复用这套分析方法（工具命令见 §9）快速定位。

---

## 5. FrostHelper 专项

**使用面**：Heartside 20 个实体、chapter 69 次（见 §2）；**地图数据里没有任何 `FrostHelper/FrostTemple*` 实体**（`grep -i temple` 只命中原版贴图/音乐路径，如 `5-temple/...`、`event:/game/05_mirror_temple/...`）。若以后要测其它 FrostHelper 地图，需重新跑一次本分析。

**hook**：55 个含 hook 的类型，全部 `add_from=[.ctor|Added|LoadIfNeeded|LoadHooksIfNeeded]`、`remove_from=[Unload]`，而 `Unload` 在程序集内无调用者 → 模块生命周期，读档不丢 hook。代表：
- `FrostHelper.CustomDreamBlock`：`LoadIfNeeded(+Player.OnCollideH/V,+Player.RefillDash)`，static `_hooksLoaded`；
- `FrostHelper.Bubbler`：`LoadHooksIfNeeded(+Player.NormalBegin)`，static `HooksLoaded`；
- `FrostHelper.EntityRainbowifyController`：`+Level.Render`；`FrostHelper.RainbowTilesetController`：`+TileGrid.RenderAt,+Debris.Init`。

**静态状态**（SRT 通用静态扫描是否覆盖）：
| 字段 | 类型 | SRT 覆盖 | 判断 |
|---|---|---|---|
| `_hooksLoaded` / `_loadedHooks` / `HooksLoaded` | static bool（在 Entity 类型上） | ✅（`InitFields` 会扫 Entity/Component/Renderer 类型的 static bool） | 与 hook 持久化一致，安全 |
| `DefaultSolidBlockTexture` 等 MTexture / `MTexture[]` / `List<MTexture>` | 静态贴图 | ✅（`InitFields` 的 extraTypes 含 `MTexture` 及其容器） | 只是多克隆几个 MTexture（见 audit B5），无正确性问题 |
| `EntityBatcher.temp/temp2` | `VirtualRenderTarget` | ✅（`VirtualAsset` 共享） | 安全 |
| `EntityRainbowifyController.levelRenderManipulator`、`CustomRisingLava._onStay/_onLeave`、各 `ILHook`/`Hook` 静态 | delegate / detour | ❌（不需要；hook 模块生命周期） | 安全（不保存反而对） |
| **`FrostModule.CustomDreamDashState`** | **static int（模块类）** | ❌（模块类型不在 `InitFields` 的 Entity/Component/Renderer 扫描里，`InitModuleFields` 只收 Level/Session/Entity/Vector2 字段） | **待验证**：若它是“状态注册 ID”则无害；若是 per-run 状态则需要保存 |
| 各种 `Dictionary<...>` Cache / `SessionExpressions` 注册表 | 静态缓存 | ❌（不需要） | 安全（重建缓存即可） |

**实例状态**：`FrostHelper.ToggleSwapBlock`（Heartside 使用）持有 `EventInstance moveSfx, returnSfx` → 见 §6。
**结论**：FrostHelper 在 GM Heartside 场景下**不需要新增 hook/静态字段 support**；只需处理 §6 的 `EventInstance`，并用 bench 验证 `CustomDreamDashState`。

---

## 6. 裸 `EventInstance` / `Coroutine` 实例字段（chapter 实际使用到的类型）

SRT 只对 `SoundSource.instance` 和 `CassetteBlockManager.sfx/snapshot` 做 manual clone（`DeepClonerUtils.cs:122-160`）。其它实体自己持有的 `EventInstance` 字段在克隆时是**结构体浅拷贝**（同一个 FMOD native handle）：
- 读档后 `UnloadLevel` 会跑旧实体的 `Removed()`；若它 `stop/release` 了该实例，模板里的句柄就悬垂 → 读档后 `.start()`/`.setParameterValue()` 无效或 FMOD 报错；
- 即使不释放，两份对象共享一个 FMOD 实例，音量/参数互相干扰。

**chapter 地图里使用到的、带裸 EventInstance 字段的实体**（Cecil 扫描结果；“bin 序”= 该实体名在 `ZZ-HeartSide.bin` 中首次出现的字符串序号，≈房间顺序，越小越靠前）
| 类型 | 字段 | 备注 |
|---|---|---|
| `FrostHelper.ToggleSwapBlock` | `moveSfx`, `returnSfx` | **在 Heartside，bin 序≈777（最早）**；建议优先修 |
| `Celeste.Mod.StrawberryJam2021.Entities.WonkyCassetteBlockController` | `sfx`, `snapshot` | **在 Heartside，bin 序≈893**；类似 CassetteBlockManager，但没被 SRT 的 special-case 命中 |
| `Celeste.Mod.StrawberryJam2021.Entities.CustomBadelineBoost` | `FinalBoostSfx` | 在 Heartside，bin 序≈2973（后段） |
| `Celeste.Mod.XaphanHelper.Entities.FlagSwapBlock` | `moveSfx`, `returnSfx` | 在 Heartside，bin 序≈2557（中段） |
| `Celeste.Mod.XaphanHelper.Entities.Liquid` | `riseSoundSource`（+ `Coroutine LiquidDamageRoutine`） | 在 Heartside，bin 序≈3223（后段） |
| `Celeste.Mod.CollabUtils2.Entities.MiniHeart` | `pauseMusicSnapshot`（+ `Coroutine smashRoutine`） | chapter 使用（非 Heartside） |
| `Celeste.Mod.StrawberryJam2021.Entities.FakeCassette` | `collectAudioEvent` | chapter 使用（非 Heartside） |
| `Celeste.Mod.CommunalHelper.Entities.Chain` | `sfx` | chapter 使用（非 Heartside） |
| （另：`CommunalHelper.CassetteSwapBlock/ConnectedSwapBlock/DreamSwapBlock`、`DJMapHelper.BadelineBoostTeleport`、`FemtoHelper.FuckingAwesomeEmulator`、`BrokemiaHelper.*`、`vitmod.*` 等同类字段在其它图使用） | | |

**Heartside 前几屏（bin 序 600–1200）里的其它“读档需要验证”的类型**：`MaxHelpingHand/MovingFlagTouchSwitch`（640，静态 flagMapping，已由 MHM 自接 SRT）、`YetAnotherHelper/SpikeJumpThruController`（769，hook 需重建）、`VivHelper/RefillCancelSpace`（977，静态 Camera 引用）、`CommunalHelper/DreamMoveBlock`（1159，`Coroutine controller`）。

**结论（C 项决定）：不做通用 `EventInstance` 重建；只作为已知风险记录。**

理由（风险 > 收益）：
1. **旧句柄的 release 生命周期不可控**：模组实体在 `Removed()` 里可能 `stop/release` 自己的 EventInstance；SRT 读档时旧实体已被 `UnloadLevel` 移除，句柄可能已失效。通用重建需要在 **save 时**就把 `Audio.GetEventName(instance)` + `getTimelinePosition()` + 参数 + 音量/3D 属性都记下来（`GetEventName` 对已失效/空路径返回 null，信息会丢），否则重建出来的实例可能与原状态不一致（静音、循环点错、参数丢失）。
2. **与现有 manual-clone 路径冲突/重复**：`SoundSource.instance` 与 `CassetteBlockManager.sfx/snapshot` 已经由 `DeepClonerUtils.PreCloneProcessor` + `EventInstanceUtils.Clone` 处理；再做一个“全字段通用重建”会对同一对象产生两套实例（或时序竞争：克隆期 vs loadState 期）。
3. **新建实例必须由 SRT 负责释放**：每个槽每次 load 都会新建实例；若 `clearState` 路径漏释放（多槽切换、失败回滚），就是 FMOD native 泄漏（比托管内存更危险，无法被 GC 回收）。
4. **收益面很窄**：GM Heartside 前几屏只有 2 个类型（`FrostHelper.ToggleSwapBlock`、`SJ2021.WonkyCassetteBlockController`）命中；即使做白名单也只需 2 个 `SaveLoadAction`，而通用机制的维护/回归成本高得多。
5. **验证成本**：音频问题在 bench 里很难自动判定（听感/循环点/FMOD 日志），回归风险高。

**如果 bench-env 定位到具体音频 bug**：再按白名单定向做（不改通用路径）：
- `FrostHelper.ToggleSwapBlock.moveSfx/returnSfx`、`SJ2021.WonkyCassetteBlockController.sfx/snapshot`；
- 做法：save 时记录 `(eventName, timeline, 参数快照)`，load 时 `Audio.CreateInstance` 重建并赋回字段；**只在该字段当前句柄 invalid 时重建**；把新实例记入槽的“待释放”列表，`clearState` 统一 `Audio.ReleaseSnapshot`。
- `Coroutine` 字段（`DreamMoveBlock.controller` 等）单独验证“读档后是否重复启动”，不在此机制内。

---

## 7. 建议新增的代码（供 Lead 直接贴）

> 以下 `file:line` 为建议插入位置（基于当前 HEAD）。

### 7.1 YetAnotherHelper hook 重建（新文件 `ThirdPartySupport/YetAnotherHelperUtils.cs`）——已按 Cecil 实证修正

> **注意**：`Load()`/`Unload()` 管的是 **模块生命周期** 的 `On.Celeste.Level.LoadLevel` hook，**不要**在 loadState 里调用它们。需要重建的是 `Awake` 里挂的 `On.Celeste.Spikes.OnCollide`（静态回调 `OnCollideHook`）。

```csharp
internal static class YetAnotherHelperUtils {
    private static Type spikeCtrlType;
    private static On.Celeste.Spikes.hook_OnCollide onCollideHook;

    [Initialize]
    private static void Initialize() {
        spikeCtrlType = ModUtils.GetType("YetAnotherHelper", "Celeste.Mod.YetAnotherHelper.Entities.SpikeJumpThruController");
        if (spikeCtrlType is null) return;
        Tracker.AddTypeToTracker(spikeCtrlType);   // 为了 GetEntitiesTrackIfNeeded
        onCollideHook = (On.Celeste.Spikes.hook_OnCollide)Delegate.CreateDelegate(
            typeof(On.Celeste.Spikes.hook_OnCollide), spikeCtrlType.GetMethodInfo("OnCollideHook"));
    }

    internal static void Support() {
        if (spikeCtrlType is null) return;
        SaveLoadAction.InternalSafeAdd(loadState: (_, level) => {
            bool any = level.Tracker.GetEntitiesTrackIfNeeded(spikeCtrlType) is { Count: > 0 };
            if (any) {
                On.Celeste.Spikes.OnCollide -= onCollideHook;   // 幂等（OnCollideHook 是静态方法，delegate 相等）
                On.Celeste.Spikes.OnCollide += onCollideHook;
                spikeCtrlType.SetFieldValue("SpikeHooked", true);   // 校正 SRT 静态恢复后的可能不一致
            }
            else if (spikeCtrlType.GetFieldValue<bool>("SpikeHooked")) {
                On.Celeste.Spikes.OnCollide -= onCollideHook;
                spikeCtrlType.SetFieldValue("SpikeHooked", false);
            }
        });
    }
}
```
并在 `ThirdParty.ComplexModsSupport()`（`ThirdParty.cs:16-24`）末尾加 `YetAnotherHelperUtils.Support();`（`Initialize` 由 SRT 的 `[Initialize]` 分发自动调用）。

### 7.2 CollabUtils2 hook 重建（同上文件）——GM Heartside 实际未用到该实体，低优先

> `hook()` / `unhook()` 是 **`private static void`（无参数）**，全局生效；而且 `hooked` 是 static bool（SRT 会保存/恢复它）→ 读档后可能 `hooked==true` 但事件已摘。做法：先 `unhook()`（幂等）再按实体存在与否决定 `hook()`。

```csharp
if (ModUtils.GetType("CollabUtils2", "Celeste.Mod.CollabUtils2.Entities.CollabCrystalHeart") is { } type) {
    Tracker.AddTypeToTracker(type);
    SaveLoadAction.InternalSafeAdd(loadState: (_, level) => {
        type.InvokeMethod("unhook");   // 幂等；修正 SRT 静态恢复的 hooked=true 与实际未挂钩的不一致
        if (level.Tracker.GetEntitiesTrackIfNeeded(type) is { Count: > 0 }) {
            type.InvokeMethod("hook"); // 重新注册 On.Celeste.Level.RegisterAreaComplete + IL.Celeste.Level.CompleteArea
        }
    });
}
```

### 7.3 静态状态补充 —— 经复核后**无需新增**
- `MaxHelpingHand.Entities.MovingFlagTouchSwitch.flagMapping`：**MHM 已通过 SRT ModInterop 自行保存/恢复**（见 §7.4），不要再 `CloneModTypeFields`（会重复保存同一份状态）。
- `VivHelper.VivHelperModule` / `FrostHelper.FrostModule` 的 static int 组：Cecil 读写点证明它们是 **Player 状态注册 ID**（只在 `Player_ctor` / `PlayerOnOnRegisterStates` 写，读用于与 `StateMachine.State` 比较），克隆没有收益 → 不处理。
- `VivHelper.VivHelperModule.createdCassetteManager`（bool，per-level 派生标志）：先 bench 验证；目前不改。

### 7.4 各 mod 自己的 SRT 接入（避免重复劳动）
以下 mod 已实现 `[ModImportName("SpeedrunTool.SaveLoad")]` 并主动注册 SRT 回调：
- **MaxHelpingHand**：`Module.SpeedrunToolInterop` → `RegisterSaveLoadAction`（保存 `MovingFlagTouchSwitch.flagMapping` + `BlackholeCustomColors.colorsMildOverride`，load 时 `stsfld` 回静态字段）+ `RegisterStaticTypes(typeof(StylegroundFadeController), StylegroundFadeController.StaticFieldsToClone)`。
- **FrostHelper**：`ModIntegration.SpeedrunToolIntegration.AddReturnSameObjectProcessor`（`LoadContent` 里注册：`ISavestatePersisted` 类型共享不克隆，实现类型见 §5）；`AddCustomDeepCloneProcessor` 字段存在但程序集内没有读取点。
- **CommunalHelper / FemtoHelper / EeveeHelper**：`RegisterStaticTypes`（各自 dll 里含 `SpeedrunTool.SaveLoad` import）。
- 相关风险（与 audit D14 同源）：SRT 的 `returnSameObjectTypes/Predicates` 结果缓存在 `DeepClonerSafeTypes.KnownTypes`；若 mod 在 cloner 已生成后才注册 predicate，则不会生效。**本分支的结论**：`KnownTypes` 绝不能清（它预置了 `System.RuntimeType` 等框架类型，清空后重新判定会让游戏进图时 native 崩溃，已用 bisect 变体实测）；因此改为**文档约束**：`Add/RemoveReturnSameObject*` 必须在首次 save/load 前注册。FrostHelper 在 `LoadContent` 注册，满足约束。

### 7.5 若要“通用 hook 重建”而不是逐个 mod 写
可行性评估：hook 注册方法都是“幂等 + 只依赖静态表”，可以在 load 后对 `level.Entities` 里每个实体类型检查：
- 类型上有 static bool 字段名匹配 `*hooksLoaded*`/`*loaded*`（区分大小写不敏感）且为 true；
- 类型上有方法（`Load`/`LoadIfNeeded`/`LoadHooksIfNeeded`/`Hook`）在 IL 里含 `On.*`/`IL.*` 的 `add_`；
- 且这些方法的 `remove_` 对应物只在 `Unload`（模块生命周期）里 → 不需要重建（跳过）；
- 若 `Removed` 可达 `remove_`（实体级） → 调用注册方法重建。
风险：反射调用 mod 私有方法可能触发非预期副作用（多次注册、异常）。建议**只做白名单**（7.1/7.2），通用方案留作研究。

---

## 8. 验证计划（交给 bench-env）

1. **基础机制回归**：按实体类型逐个测（推荐顺序，覆盖 Heartside 主机制）：
   - `CommunalHelper/DreamMoveBlock`、`ConnectedDreamBlock`、`ConnectedMoveBlock`、`DreamRefill`（dream 系）
   - `FrostHelper/ToggleSwapBlock`、`BlueBooster`、`SpringFloor`（音频 + 物理）
   - `SJ2021/WonkyCassetteBlock(Controller)`、`FakeCassette`、`CustomBadelineBoost`（cassette/音频）
   - `CollabUtils2/MiniHeart`（收集过程 + 音频）
   - `YetAnotherHelper/SpikeJumpThruController`（hook 重建验证：读档后贴着该 JumpThru 跳跃/撞刺）
   - `MaxHelpingHand/MovingFlagTouchSwitch`（flagMapping：存档→切 flag→读档→再用 switch）
   - `VivHelper/RefillCancelSpace`、`RefillWall`（静态 Camera/着色）
   - `ContortHelper/MadelineSpotlightModifierTrigger`、`MomentumModifierTrigger`（无 hook，纯实例状态）
2. **每项协议**：进屏 → 存档 A → 触发机制/改变状态 → 读档 A → 对比（画面/声音/行为）；然后再存档 B、读档 A（多槽交叉）、读档 B。
3. **卡顿/内存指标**：用 `docs/audit-instrumentation.md` 的插桩；重点看 `preclone` 后台任务、`snapshot`、`GC` 与每帧 P99。
4. **hook 验证技巧**：读档后触发该 hook 才会暴露（例如 `SpikeJumpThruController` 的 `Spikes.OnCollide`：读档后主动撞刺）；日志里应有 FMOD/Celeste 异常。
5. **音频验证**：读档后立刻听/看 FMOD 日志（`Celeste/Logs/log.txt` 中 `FMOD` 错误码），并在事件结束后重新触发该音频。

---

## 9. 方法附录（可复现）

工具（临时目录 `/tmp/sjtool`，源码 `Program.cs`，Mono.Cecil 引用游戏目录的 `Mono.Cecil.dll`）：
```bash
# 1) 从 map bin 提取 7-bit 长度前缀字符串（实体 ID/房间名字符串）
sjtool strings <map.bin>
# 2) 扫描 dll：静态字段分类（SRT-saved / NOT-SAVED）+ hook add/remove + 实例 EventInstance/Coroutine
sjtool analyze <ModsDir> <mod.dll> [typeNameSubstring]
# 3) hook 注册/注销调用图（Added/Removed/Awake/ctor/Update/LoadIfNeeded 可达性）
sjtool hookmap <ModsDir> <mod.dll>
# 4) 某类型完整字段+方法调用（定位 hook 装配链）
sjtool chain <ModsDir> <mod.dll> <TypeFullNameSuffix>
# 5) 谁调用了某方法（确认 Unload 是否只在 mod 卸载时调用）
sjtool callers <ModsDir> <mod.dll> <methodNamePrefix> [declaringTypePrefix]
```
分类器说明：`SRT-saved` 判断按 `SaveLoadAction.InitFields`（`SaveLoadAction.cs:462-521`）的规则近似实现；SRT 的 `IsSimple` 还包含 `Vector2/Vector3/Color/VertexPositionColor/TimeSpan` 等，因此工具报 `NOT-SAVED(dict-of-other)` 的 `Dictionary<int,Color>`/`List<float>` 实际上**会被 SRT 保存**；真实缺口以本报告 §5/§7 的**人工复核结论**为准。
原始输出：`/tmp/sjtool/out/<Mod>.txt`（静态/实例/hook）、`<Mod>.hooks2.txt`（hook 调用图）、`<Mod>.cnames.txt`（chapter 使用到的该类 mod 实体名）。若需要长期留存，我可以把工具源码整理到 `docs/tools/`（需 Lead 批准写文件）。

**工具统计口径**：分析覆盖 30 个 mod dll（Frost/Communal/MHM/Viv/Vit/SJ/Pandora/CollabUtils2/Cherry/DJMap/Femto/Shroom/Vortex/Flaglines/FancyTile/Lunatic/Eevee/Sorbet/Brokemia/Honly/Jungle/Xaphan/Adventure/DisposableTheo/ExtVar/Spirialis/Outback/YetAnother/BGswitch…），`HK` 类型总数 ~600+；其中实体级 hook 仅 5 个（§4）。
