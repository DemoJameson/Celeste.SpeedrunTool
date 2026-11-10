# Desync checker（Lua desync 检测）重构说明

> 分支：`feat/desync-checker`（基于 main `dae2afb`）。本文档描述旧规则、新架构、`SpeedrunTool.Desync`
> 公开 API、行为矩阵、迁移说明、bench 验证计划与风险/回退。所有结论来自代码 + 离线构建/单测 +
> 对安装版 DLL 的 Cecil 复核；**本分支未启动游戏**，游戏内行为仍需 bench 验证（见 §7）。

---

## 1. 旧规则与问题（`dae2afb`）

旧 checker 位于 `Source/SaveLoad/Implements/DesyncRiskAnalyzer.cs`，三个阶段：

| 阶段 | 位置 | 行为 |
|---|---|---|
| VeryVeryEarlyCheck | `GetAndRefreshIfDesyncCheck()` | 关闭条件：`DisableDesyncCheck`、非 Level、或 `ExternalSafeOracle`（硬编码 `SafeMaps = ["StrawberryJam2021/5-Grandmaster/maya"]`）命中 |
| EarlyCheck | `OnBeforeSaveState()` | 拒绝「已知危险实体」：LuaCutscenes 整程序集白名单 + `LuaCutsceneEntity`/`LuaTalker` 特判；BossesHelper 整程序集白名单 + `BossController` **无条件**拒绝 |
| LateCheck | `Check(Entity)`（`DeepClonerUtils` 克隆时调用） | 按**声明字段类型**递归；命中 Lua 类型 → 存档后清档并提示 `Saved but Cleared` |

具体问题（详见 `lua-survey` §9）：

1. **误报（False Positive）**：`CrossoverCollab/1-Submissions/Knight` 等 4 个 Boss 房，只要存在
   `BossController` 就永远不能存档，即使 Boss 已死/空闲。
2. **整程序集 blanket**：LuaCutscenes/BossesHelper 全部类型 `KnownTypes[type]=true`，late check
   对这些类型完全失效；新增 Lua mod 必须手改 SRT 源码。
3. **漏报（False Negative）**：容器/数组/泛型实参（main 未合并 P1 前）、`Entity.Components`
   运行时类型、`object`/委托、静态字段/CWT 等。
4. **反射式 blanket 不可维护**：每来一个新 Lua mod 就要手写 early check；`ExternalSafeOracle`
   还是 `public List<Func<Level,bool>>` 的裸 C# API，**从未 ModInterop 导出**（无外部契约）。
5. `SafeMaps` 硬编码在源码里，地图作者无法声明。

已在本分支保留的历史修复：P1（容器/泛型展开）、P2（LuaTalker unskippable）、P3（late check
字段路径日志）、`DisableDesyncCheck` / `SaveInLuaCutscene` 拆分（v3.28.0）。

---

## 2. 新架构

```
save 时（全部主线程；probe/gate 每次 save 只算一次，结果缓存到当前 save 结束）
┌─────────────────────────────────────────────────────────────────────────────┐
│ 0. VeryVeryEarlyCheck: DisableDesyncCheck / 非 Level / level predicate 命中  │
│    （默认 predicate 保留 maya safe map）→ 本 save 完全跳过 checker            │
├─────────────────────────────────────────────────────────────────────────────┤
│ 1. LuaUsageProbe（通用「Lua 正在执行」探测）                                  │
│    - 扫 level.Entities 的每个 Component；Monocle.Coroutine 的 private         │
│      enumerators 栈（反射，字段缓存）递归展开 Flattened（Monocle 用来扁平化  │
│      嵌套枚举器的 boxed struct）                                              │
│    - 栈里出现未完成的 LuaCoroutine / 其子类（如 BossesHelper                 │
│      LuaProxyCoroutine）→ 拒绝 [LuaExecuting]                                │
│    - SaveInLuaCutscene=true 时只豁免「LuaCutscenes 生态拥有者」的协程        │
├─────────────────────────────────────────────────────────────────────────────┤
│ 2. 内置生态 at-rest gate（实例级，替代整程序集 blanket）                      │
│    - LuaCutscenes / BossesHelper / FrostHelper 各自检查关卡里的**每个实例**  │
│      （LuaTalker 额外反射 onTalkRoutine 状态机，覆盖「无 Coroutine 组件」的 │
│       bench 盲区；见 §3.1）                                                 │
│    - 通过 → 该生态「具体持有 Lua 的实体类型」才允许 late check 快速放行      │
│    - 失败 → 拒绝；类型/字段缺失/读不出来 → fail-closed（见 §3/§6）           │
│    - SpeedrunTool.Desync 的注册可覆盖「被判定 unsafe 的具体实例」            │
├─────────────────────────────────────────────────────────────────────────────┤
│ 3. 动态 fail-closed 列表：上一次 late check 拒绝过的类型，本次提前拒绝        │
├─────────────────────────────────────────────────────────────────────────────┤
│ 4. LateCheck（克隆期间；调用点/CheckAll 语义不变）                            │
│    - 显式注册（AddSafeType / entity predicate）→ 直接放行该实例               │
│    - 生态 gate 已通过的类型 → 放行（仅本次 save；不是初始化期 blanket）       │
│    - 其余：声明字段类型图（含 P1 容器/数组/泛型展开）→ 不通过则记录 P3 路径   │
│    - 新增：Entity.Components 的**运行时类型**也走同一套类型图检查             │
│    - 命中 → 记录字段/组件路径、加入动态列表、存档后清档 + 提示               │
└─────────────────────────────────────────────────────────────────────────────┘
```

代码位置：

| 文件 | 作用 |
|---|---|
| `Source/SaveLoad/Implements/DesyncCheckLogic.cs` | **纯逻辑**真值表：executing 探测决策、4 个 at-rest 规则、override、fail-closed（无游戏依赖，单测链接） |
| `Source/SaveLoad/Implements/DesyncRegistration.cs` | **纯逻辑**注册表：safe type / entity predicate / level predicate + 异常降级（无游戏依赖，单测链接） |
| `Source/SaveLoad/Implements/LuaUsageProbe.cs` | 反射扫描 `Monocle.Coroutine.enumerators`（缓存字段）；收集证据交给纯逻辑 |
| `Source/SaveLoad/Implements/DesyncEcosystems.cs` | 3 个生态的解析 + 实例级 at-rest gate + 类型归属判定 |
| `Source/SaveLoad/Implements/DesyncRiskAnalyzer.cs` | 生命周期/三阶段编排；`StateManager`、`DeepClonerUtils` 调用点不变 |
| `Source/ModInterop/DesyncInterop.cs` | 公开 API `SpeedrunTool.Desync` + `ILuaStateProvider`（预留） |
| `tests/DesyncCheckerTests/` | 离线单测（`dotnet run`，无游戏依赖） |

### 2.1 为什么 late check 还能对生态类型快速放行？

旧实现是**无条件、初始化期**把整个程序集塞进 `KnownTypes`；新实现是**有条件、按 save**：

- 只有本次 save 的 gate 对「关卡内全部相关实例」验证为 at-rest，`DesyncEcosystems.IsGovernedType(type)`
  才返回 true；
- gate 失败时 save 在 early check 就已被拒绝，late check 根本不会跑；
- 未注册/未解析的类型永远不 governed → late check fail-closed。

因此「整程序集 blanket」被替换成「实例验证通过后的类型短路」，未知类型/组件仍然会被拒绝。

---

## 3. 内置生态 at-rest 规则

Cecil 核对对象（2025-10 副本，与 bench `Mods/Cache` 安装版一致）：`Celeste.dll`（Everest 1.6662）、
`LuaCutscenes 0.2.13`、`BossesHelper 1.2.2`、`FrostHelper 1.81.1`。

### 3.1 LuaCutscenes

| 类型 | 规则 | fail-closed |
|---|---|---|
| `Celeste.Mod.LuaCutscenes.LuaCutsceneEntity` | `Finished == true` **且** 无未完成 `Coroutine` **且** 不是当前 skippable cutscene 的驱动者 | `Finished` 字段缺失/读不到 → Unsafe |
| `Celeste.Mod.LuaCutscenes.LuaTalker` | ①无未完成 `Coroutine`（P2，覆盖 unskippable 分支）；②不是当前 cutscene 驱动者；③`onTalkRoutine` 为 null 或状态机 `<>1__state == -1`（已结束） | `onTalkRoutine` 字段缺失/读不到、状态机状态无法判定 → Unsafe |
| `Celeste.Mod.LuaCutscenes.LuaCutsceneTrigger` | 若字段 `cutsceneEntity` 非空，则**引用的实体**也按 `LuaCutsceneEntity` 规则检查（实体可能已从 `level.Entities` 移除，旧 blanket 会漏） | `cutsceneEntity` 字段缺失/读不到 → Unsafe |

「当前 cutscene 驱动者」= `level.InCutscene && level.onCutsceneSkip.Target` 的类型属于该 LuaCutscenes 实体类型
（与旧 early check 的判据一致）。

**LuaTalker 的 bench 盲区（SJ `tofu [Atrium]`）**：该路径对话进行中 `Level.InCutscene == false`、
`onTalkRoutine = <LuaCoroutineToIEnumerator>d__2`（非 null）、`cutsceneEnv` 已设置，但实体的
`Components` 里只有 `TalkComponent`、没有 `Monocle.Coroutine` 组件——旧 P2「只看 Coroutine 组件」
会漏检并放行存档。新 gate 直接反射 `LuaTalker.onTalkRoutine` 并读取状态机字段（Cecil 核对：
`LuaHelper+<LuaCoroutineToIEnumerator>d__2` 有 `<>1__state`、`<>2__current`、`routine: LuaCoroutine`）：

| `<>1__state` | 含义 | 判定 |
|---|---|---|
| 字段为 null（`onTalkRoutine == null`） | 从未启动 Lua 对话 | at rest |
| `-1` | `MoveNext` 已返回 false / 已 Dispose，Lua 协程结束 | at rest |
| `0` | 已创建但尚未第一次推进（Lua 线程仍在起点，之后会推进） | **unsafe** |
| `1/2/3` | 挂起在 yield（时间/值/结束前的 null） | **unsafe** |

若状态机类型/字段因 LuaCutscenes 版本变化而无法识别，回退到读 `routine: LuaCoroutine` 的
`_Valid`：`true`（上次 resume 仍 yield）→ unsafe；`false` 语义有歧义（未启动 vs 已结束）→
按 fail-closed 处理。仍然无法判定 → Unsafe（拒绝存档）。`SaveInLuaCutscene=true` 时该 gate 被
用户设置覆盖（与其它 LuaCutscenes 规则一致）。

`SaveInLuaCutscene=true` 时整个 LuaCutscenes gate 被用户设置覆盖（等价旧行为：过场中可以存档），
但**不再**关闭其它 mod 的 Lua 检查。

### 3.2 BossesHelper

| 类型 | 规则 | fail-closed |
|---|---|---|
| `...Code.Entities.BossController` | `PatternCoroutine` 不是 `{ Active: true, Finished: false }`（pattern 没在跑） | `PatternCoroutine` 字段缺失/为 null/读不到 → Unsafe |
| 全局 | 无活跃 `LuaProxyCoroutine`（由 §2 step 1 的 probe 覆盖；`LuaProxyCoroutine : Celeste.Mod.LuaCoroutine`） | probe 反射失败 → 拒绝 |

「gate 通过后短路 late check」的具体 Lua-holding 类型（Cecil 清单，`+` 表示嵌套类型）：
`BossController`、`BossPuppet`（含 `ContactBossPuppet` 等子类）、`GlobalSavePoint`、
`HealthSystemManager`、`HealthSystemManager+DamageController`、`BossEvent+CutsceneWrapper`。
子类通过 `IsSubclassOf` 覆盖。

### 3.3 FrostHelper

| 类型 | 规则 | fail-closed |
|---|---|---|
| `FrostHelper.Entities.LuaBadelineBoss` | `CreatedCoroutines` 全部 `Finished`（属性优先，回退 backing field） | 成员缺失/读不到/null 元素 → Unsafe |

只登记 `LuaBadelineBoss`（+ 子类），**不** blanket 整个 FrostHelper（FrostHelper 很大且绝大多数类型与 Lua 无关）。

---

## 4. 公开 API：`SpeedrunTool.Desync`

新 API，**无历史兼容负担**（旧 `ExternalSafeOracle` 从未 ModInterop 导出，现已删除）。风格对齐
`SpeedrunTool.SaveLoad` / `SpeedrunTool.Bench`：静态 class + `[ModExportName]`。

### 4.1 导入方式

```csharp
using Celeste.Mod.SpeedrunTool.ModInterop; // 只有 RegisterLuaStateProvider 需要引用 ILuaStateProvider

[ModImportName("SpeedrunTool.Desync")]
public static class Desync {
    public static extern void AddSafeType(Type type);
    public static extern bool RemoveSafeType(Type type);
    public static extern void AddSafeEntityPredicate(Func<Entity, bool> predicate);
    public static extern bool RemoveSafeEntityPredicate(Func<Entity, bool> predicate);
    public static extern void AddSafeLevelPredicate(Func<Level, bool> predicate);
    public static extern bool RemoveSafeLevelPredicate(Func<Level, bool> predicate);
}
```

### 4.2 方法语义

| 方法 | 语义 | 生效位置 |
|---|---|---|
| `AddSafeType(Type)` | 该类型（含子类）视为安全；即使字段里有 `LuaTable`/`LuaFunction`/`LuaCoroutine` 也放行。同时**抑制**「该类型实体拥有的、正在执行的 Lua 协程」这条 probe 证据（mod 自己负责其 Lua 状态） | late check（类型图短路）、probe（owner 类型豁免） |
| `AddSafeEntityPredicate(Func<Entity,bool>)` | 实例级 at-rest 判定：返回 true = 这个实例当前安全。返回 false = **没有意见**（不是"不安全"），fail-closed 仍由其它规则负责。异常会被捕获、记日志、按 false 处理 | probe（owner 实例豁免）、late check（该实例直接放行）、生态 gate（覆盖被判 unsafe 的具体实例 = §3 规则可被 mod 覆盖/补充） |
| `AddSafeLevelPredicate(Func<Level,bool>)` | 返回 true = 本 save 完全跳过 checker（旧 `ExternalSafeOracle` 的能力，慎用） | VeryVeryEarlyCheck |
| `Remove*` | 注销，用于 mod unload；找不到返回 false | — |
| `RegisterLuaStateProvider(ILuaStateProvider)` | **预留**：接口 `string Name { get; }` + `bool IsAtRest(Entity)`。当前版本只把 `IsAtRest` 当作 entity predicate 使用；save/restore 回调契约未实现（等真实 mod 需求）。返回 provider 作为注销 handle | 同 `AddSafeEntityPredicate` |

约定：

- 注册应在主线程（mod `Load`/`Initialize`）完成；predicate 在 save 期间的主线程被调用
  （probe 会按协程/实体调用，请保持廉价、无副作用）。
- `AddSafeType` 对**基类**注册会覆盖子类；`RemoveSafeType` 只移除精确注册的类型。
- 注册**不会**关闭全局 probe（除被声明类型自己的实体外），也不会影响其它 mod 的判定。
- 默认注册：历史硬编码 safe map `StrawberryJam2021/5-Grandmaster/maya` 变为默认 level predicate
  （`DesyncInterop.DefaultSafeMaps`）。
- 日志 tag 保持 `SpeedrunTool/DesyncRiskAnalyzer`；API 注册日志用 `SpeedrunTool/Desync`。

### 4.3 `ILuaStateProvider`（预留）

```csharp
public interface ILuaStateProvider {
    string Name { get; }
    bool IsAtRest(Entity entity);
}
```

`IsAtRest` 返回 true 的实例按「已知安全」放行；`false`/抛异常 = 没有意见。后续版本计划增加
`Save/Load` 回调（主线程、per-slot），届时 provider 可在 at-rest 之外自己深克隆 Lua 状态；
当前没有实现，因此**注册 provider 不会让原本 fail-closed 的 Lua 字段自动可克隆**（mod 仍需
自行保证状态可恢复，例如用 `SaveLoadAction`）。

---

## 5. 行为矩阵（旧 vs 新）

| 场景 | 旧（`dae2afb`） | 新（本分支） | 备注 |
|---|---|---|---|
| 普通地图，无 Lua 活动 | 允许 | 允许 | — |
| Lua cutscene 进行中（skippable/unskippable） | 拒绝（LuaCutsceneEntity/LuaTalker early check） | 拒绝（gate：`Finished==false` 或未完成 Coroutine；`[LuaCutsceneEntity]`/`[LuaTalker]`） | `SaveInLuaCutscene=true` 时两者都放行 |
| Lua talker 对话中，`InCutscene=false`、无 `Coroutine` 组件（SJ `tofu [Atrium]`） | **放行（漏检）** | 拒绝（gate：`onTalkRoutine` 状态 0/1/2/3 → `[LuaTalker]`） | 本次补充修复的 P2 盲区；bench E1 验证 |
| Lua cutscene 已结束、实体还在关卡里 | 允许（整程序集白名单） | 允许（gate：finished + 无活跃协程 → 类型短路） | 读档后再触发 OnEnter/OnStay 的共享 env 风险与旧版相同，属未验证边界 |
| `LuaCutsceneTrigger` 存在（正常地图） | 允许（blanket） | 允许（引用的 cutsceneEntity 已 finished/为空） | 若引用一个未完成的 cutscene entity → 拒绝（比旧版更严） |
| CrossoverCollab `Knight` Boss 房，Boss 房间空闲/已死 | **永远拒绝** | **允许**（`PatternCoroutine` 非 running） | 本次重构的主要收益；需 bench 验证读档后 Boss 行为（§7 E5b、§8 风险） |
| 同上，Boss pattern 正在执行 | 拒绝 | 拒绝（`[BossController]`） | — |
| FrostHelper `LuaBadelineBoss` 在房间（所有 CreatedCoroutines finished） | **永远拒绝**（late check 直接命中 Lua 字段） | **允许** | 当前已装环境无可加载地图；需 bench/离线构造（§7） |
| 任意 mod 的 Lua 协程正在执行（非 LuaCutscenes 生态） | 视类型图；多半放行 | 拒绝 `[LuaExecuting]` | 新增 fail-closed；纯装饰性 Lua 协程可能误报（§8，可用 API/`DisableDesyncCheck` 规避） |
| 未知 mod 实体，字段含 `List<LuaTable>` / `LuaTable[]` / `Func<LuaTable>` | 拒绝（P1） | 拒绝（P1 保留） | — |
| 未知 mod 实体的 **Component** 持有 Lua（字段声明为 `Component`/`List<Component>`） | 放行（盲区） | 拒绝（组件运行时类型检查） | 离线扫描：当前已装 mod 无此类 Component，预期 0 新增误报 |
| 任意 Lua 字段实体（未知类型），空闲 | 拒绝（`Saved but Cleared`） | 拒绝（`Saved but Cleared` + 字段路径 + API 提示；下次提前拒绝） | 保留 fail-closed；mod 可用 `AddSafeType`/predicate 解除 |
| `StrawberryJam2021/5-Grandmaster/maya` | 跳过 checker | 跳过 checker（默认 level predicate） | 行为保持 |
| `DisableDesyncCheck=true` | 全部关闭 | 全部关闭 | 行为保持 |
| `SaveInLuaCutscene=true` + 非过场 Lua | 全部关闭（旧实现是 `Enabled=false`） | 仅豁免 LuaCutscenes 生态；其它 Lua 仍检查 | 有意的语义收窄（v3.28.0 拆分目标） |
| 未安装 LuaCutscenes/BossesHelper/FrostHelper | — | 对应 gate = NotInstalled，无开销 | — |

---

## 6. Fail-closed 与错误安全

任何「无法证明安全」的路径都拒绝存档，且不抛异常到游戏（checker 内部 try/catch + 日志）：

| 情况 | 行为 |
|---|---|
| `Monocle.Coroutine.enumerators` / `SwapImmediatelyExtension+Flattened` 反射失败（Celeste 更新改名） | probe 直接拒绝并 `Logger.Error`，提示可用 `DisableDesyncCheck` 临时绕过 |
| 生态程序集装了但实体类型解析失败 | gate = Unavailable（不 governed）→ 相关类型走 late check，Lua 字段会被拒绝 |
| 生态实体类型在、但关键字段成员缺失（`Finished`/`PatternCoroutine`/`CreatedCoroutines`/`cutsceneEntity`） | 若关卡里存在实例 → 直接拒绝（fail-closed）；否则 Unavailable |
| 字段/属性反射读取抛异常 | 拒绝（fail-closed） |
| `AddSafeEntityPredicate` / level predicate 抛异常 | 捕获 + 日志 + 视为 false（不会 fail open） |
| late check 命中未知 Lua 类型 | 清档 + `Saved but Cleared`；类型加入动态列表，下次 save 提前拒绝 |
| 任何 checker 内部异常 | 日志 + 保守处理（能定位到实例就拒绝；不能则让 late check fail-closed） |

---

## 7. Bench 验证计划（不启动游戏 = 本分支未执行）

前置：构建 `dotnet build -c Release -p:CelestePrefix="D:\Games\SteamLibrary\steamapps\common\Celeste"`；
把 `SpeedrunTool/bin/Release/net8.0/SpeedrunTool.dll` 覆盖到 **bench 实例**的
`Mods/SpeedrunTool/SpeedrunTool.dll`（先备份旧 DLL 便于 A/B；切换后重启游戏）。默认键位 F7 存 / F8 读 / F4 清。

### E1：LuaCutscenes 过场（旧 P2 回归 + gate 验证，5–10 分钟）

1. SJ `5-Grandmaster` 中含 `luaTalker` 的房间（`Linj.bin` / `Soloiini.bin` / `tofu.bin`）。
2. 对话进行中（skippable 或 unskippable 均可）按 **F7**。
   - 期望：`Failed to Save: [LuaTalker] may lead to desync!`（skippable 由 `drivenByLevelCutscene`，
     unskippable 由未完成 `Coroutine` 触发）。
   - **重点回归（新修复）**：SJ `tofu [Atrium]` 的对话中 `InCutscene=false` 且实体上没有
     `Monocle.Coroutine` 组件；期望因 `onTalkRoutine` 状态机仍在运行（state 0/1/2/3）而拒绝。
     日志应出现 `talkRoutine=Running`（或 `Unknown`，fail-closed）。
   - 日志：`SpeedrunTool/DesyncRiskAnalyzer: Desync risk: [LuaTalker] ...`。
3. 等对话结束（实体仍可能留在房间）再按 F7。
   - 期望：**存档成功**（gate at-rest）。
4. `SaveInLuaCutscene=true` 后重进对话按 F7：期望存档成功（用户显式承担风险）。

### E2：普通 SJ 房间无误报 + 新 probe 行为（5–10 分钟）

1. SJ `5-Grandmaster/ZZ-HeartSide`（bench 场景 `Insert Coin`）+ 前 2–3 个房间，每进一间按 F7。
   - 期望：与旧 DLL 相同（成功）；**不应**出现新的 `[LuaExecuting]` / `Saved but Cleared`。
2. 完成一个 Lua 过场后在同一房间再按 F7：期望成功。
3. 检查 `log.txt`：
   - `SpeedrunTool.Desync API ready (default safe maps: ...)`；
   - 无 `cannot inspect` / `could not be resolved` warning；
   - 不出现 `LuaCutscenes state cannot be inspected`。
4. 如出现 `Failed to Save: [X] may lead to desync!`：记录 X/房间/log，用旧 DLL 同房复测区分
   既有命中 vs 新 checker 新增。

### E3：`SaveInLuaCutscene` 语义收窄 A/B（10 分钟）

1. 有可观测副作用（spawn / flag / 音乐 / 相机）的 Lua 过场进行中：
   - 默认设置按 F7 → 期望拒绝（`[LuaCutsceneEntity]` 或 `[LuaTalker]`）；
   - 打开 `SaveInLuaCutscene` 后按 F7 → 期望成功；
   - 读档 F8 → 观察脚本行为（共享 Lua 协程的旧失效模式，作为对照素材，不要求修复）。
2. 关掉 `SaveInLuaCutscene`，进一个「非 LuaCutscenes mod 的 Lua 协程正在跑」的场景（若有）：
   期望仍拒绝 `[LuaExecuting]`（旧 DLL 会因该设置整体关闭 checker）。

### E5b：BossesHelper（CrossoverCollab `Knight`，20–30 分钟）——**行为变化最大，重点验证**

1. 进 `CrossoverCollab/1-Submissions/Knight` 的 Boss 房（`BET-02` 等，bench 已确认含 `BossController`）。
2. Boss pattern 正在执行时按 **F7**：
   - 期望：`Failed to Save: [BossController] may lead to desync!`；
   - 日志：`BossController pattern coroutine is running (Active=True, Finished=False)`。
3. 等当前 pattern 结束 / Boss 死亡（`PatternCoroutine` 不再 running）按 F7：
   - 期望：**存档成功**（旧版永远拒绝）；
   - 记录 Boss 可观察状态（pattern/血量/位置/房间）。
4. 继续玩几秒（让 Boss 继续行动/Lua 表可能被修改），按 **F8** 读档：
   - 期望：无 crash/卡死；Boss 从存档点继续；
   - **重点观察**：Boss 行为/血量/攻击顺序是否与旧版「拒绝存档」路径不同，`log.txt` 是否有
     desync 迹象（Lua 报错、协程异常、Boss 卡住）。
   - 说明：本分支**没有**移植 lua-work 的 E5b Lua adapter；at-rest 存档依赖「save→load 之间
     Lua 表未被修改」这一假设。若 bench 发现读档后行为不一致：
     - 最小回退：把 `DesyncEcosystems.BossesHelperEcosystem.Evaluate` 改回「有实例即 Unsafe」
       （恢复旧行为，5 行内改动）；
     - 或后续移植 lua-work `BossesHelperLuaAdapter`（opt-in）后再开启 at-rest 放行。
5. A/B：`DisableDesyncCheck=true` 后重复 step 2–4，作为「完全关闭 checker」对照。

### E5（可选）：FrostHelper `LuaBadelineBoss`

当前已装 Mods 没有可直接加载的 `FrostHelper/LuaBoss` 地图（候选在 `TestMap/SSC3/...`，不在
`Maps/` 下）。若 bench 准备测试房间：
- Boss 有未完成 `CreatedCoroutines` → F7 拒绝 `[LuaBadelineBoss]`；
- 全部 finished → F7 成功；F8 读档观察 Lua boss 行为（同样依赖 at-rest 假设，风险同 E5b）。

---

## 8. 迁移说明

- **没有破坏任何已有 ModInterop 契约**：`SpeedrunTool.SaveLoad` / `SpeedrunTool.Bench` 不变；
  `ExternalSafeOracle` 从未导出、无第三方使用，直接删除。
- **设置项不变**：`DisableDesyncCheck`（关闭 checker）与 `SaveInLuaCutscene`（过场中允许存档）
  的语义保持 v3.28.0 拆分；但 `SaveInLuaCutscene` 不再关闭非过场 Lua 检查（见 §5）。
- **日志**：tag 未变（`SpeedrunTool/DesyncRiskAnalyzer`）；新增 `[LuaExecuting]` 弹窗与
  `SpeedrunTool/Desync` 注册日志。依赖日志 grep 的脚本注意新增行。
- **弹窗**：`Failed to Save: [<Type>] may lead to desync!` / `Saved but Cleared: [...]` 格式保持；
  新增 `Failed to Save: [LuaExecuting] may lead to desync!`。
- **bench/自动化**：「CC Knight Boss 房永远不能存档」「FrostHelper LuaBoss 房间永远不能存档」
  这两个旧行为被有意改变，相关期望需要更新。
- **地图/模组迁移**：以前需要改 SRT 源码加 early check 的场景，现在由 mod 通过
  `SpeedrunTool.Desync` 注册；地图级 safe map 用 `AddSafeLevelPredicate`（或直接让默认
  `DefaultSafeMaps` 增加，但那是 SRT 内部行为，建议走 API）。

---

## 9. 风险与回退

| 风险 | 说明 | 缓解 / 回退 |
|---|---|---|
| probe 误报（纯装饰性 Lua 协程） | 任何非 LuaCutscenes 生态的活跃 `LuaCoroutine` 都会被拒绝，即使脚本只做视觉效果 | 用户可用 `AddSafeType`/`AddSafeEntityPredicate`/`AddSafeLevelPredicate`，或 `DisableDesyncCheck`；最坏情况 revert commit 2（见下） |
| Celeste 内部结构变化 | probe 依赖 `Monocle.Coroutine.enumerators` 与 `SwapImmediatelyExtension+Flattened.enums`（反射，字段名已 Cecil 核对） | fail-closed 拒绝 + `Logger.Error` 明确提示；更新后改反射名即可（集中在 `LuaUsageProbe`） |
| BossesHelper at-rest 放行 | 没有 adapter 时 Lua 表/函数在 save→load 之间是共享的；若期间被修改，读档可能行为不一致（旧版用「永远拒绝」规避） | **E5b 是上线门槛**；失败则把 BossesHelper gate 改回「有实例即 Unsafe」或移植 lua-work E5b adapter |
| FrostHelper at-rest 放行 | 同上；当前无已装地图可验证 | 保持 fail-closed 也可一行改回；有真实地图后再开 |
| 生态类型/字段清单漂移 | 升级 LuaCutscenes/BossesHelper/FrostHelper 后字段改名 → 实例在 → 直接拒绝（不会半放行） | fail-closed + 日志；用 Cecil 重新核对清单（§3 的字段名即维护清单；`onTalkRoutine`/`<>1__state` 已 Cecil 核对） |
| 组件运行时类型检查新增命中 | 某些 mod 的 Component 可能持有 Lua（离线扫描当前环境为 0），会从「漏报」变「`Saved but Cleared`」 | 预期行为（fail-closed）；mod 可用 `AddSafeType(componentType)` 解除 |
| 性能 | probe 每个 save 扫全部 entity/components + 反射栈；gate 扫全部 entity；类型检查有 per-save/全局缓存 | 估算 O(entities×components) 每 save 一次，通常 < 几 ms 量级；如 bench 发现卡顿，可把 probe 改为「仅扫描有 Coroutine 组件的实体」（已是）或懒加载 |
| `SaveInLuaCutscene` 用户预期 | 旧文档/用户可能以为该开关能关闭全部 Lua 检查 | 文档 + 设置描述已说明「只允许过场中存档」；保持 v3.28.0 拆分 |

### 回退粒度

- **完全回退**：`git revert <commit2>`（analyzer 重写）。新文件（commit 1）不挂在现有路径上，
  单独的 revert 不会破坏构建；再 revert commit 1 即回到 `dae2afb`。
- **只回退某个生态 at-rest 放行**：把对应 `Evaluate()` 改成「发现实例即 Unsafe」；
  `AddSafeEntityPredicate` 仍可覆盖（变成显式 opt-in）。
- **只回退 probe**：`EarlyCheckDesyncRisk` step 1 跳过 `LuaUsageProbe`（其余 gate/late check 保留）。

---

## 10. 离线验证记录（本分支）

```
dotnet build SpeedrunTool/SpeedrunTool.csproj -c Release \
  -p:CelestePrefix="D:\Games\SteamLibrary\steamapps\common\Celeste"
→ 0 error（仅 3 个既有 CS0618 warning）

cd tests/DesyncCheckerTests && dotnet run -c Release
→ DesyncCheckerTests: all checks passed.
```

覆盖：probe 真值表（finished/unfinished/cutscene owner/SaveInLuaCutscene/owner declared safe/多栈）、
4 个 at-rest 规则（含 `LuaTalker` 的 `onTalkRoutine` 四态：None/Finished/Running/Unknown）、
缺字段 fail-closed、predicate 抛异常 fail-closed、safe type 子类覆盖、
entity/level predicate 注册与移除、override 组合、Reset。产物保留在
`SpeedrunTool/bin/Release/net8.0/SpeedrunTool.dll` 供 bench 使用。

---

## 11. 相关文档

- `D:\DSH\Celeste\lua-survey\docs\lua-usage-survey.md` §9–§11（checker 现状/方案对比/分阶段建议）
- `D:\DSH\Celeste\lua-work\docs\lua-desync.md` §1–§3（Lua 克隆边界）、§6（P1/P2/P3）、
  §15/§16（E5/E5b adapter，本分支未移植）、§8（E1–E3 原始 bench 步骤）
- `docs/scene-state.md`、`docs/audit.md`（SRT 既有验证/审计记录）
