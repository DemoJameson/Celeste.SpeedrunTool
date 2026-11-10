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
│ 1. 内置生态 at-rest gate（实例级，替代整程序集 blanket；先于 probe 运行，     │
│    让生态拥有的问题保持旧弹窗 [BossController]/[LuaTalker]/...）              │
│    - LuaCutscenes gate **默认启用**（bench E1/E2/E3 已验证）：               │
│      LuaCutsceneEntity = Finished；LuaTalker = onTalkRoutine 状态机结束态；   │
│      LuaCutsceneTrigger 检查其引用的 cutsceneEntity                          │
│    - BossesHelper / FrostHelper：at-rest allow 逻辑已实现但**默认关闭**      │
│      （adapter 未移植前保持旧行为「有实例即拒绝」，见 §3.2/§3.3）             │
│    - 通过 → 该生态「具体持有 Lua 的实体类型」才允许 late check 快速放行      │
│    - 失败 → 拒绝；类型/字段缺失/读不出来 → fail-closed（见 §3/§6）           │
│    - SpeedrunTool.Desync 的注册可覆盖「被判定 unsafe 的具体实例」            │
├─────────────────────────────────────────────────────────────────────────────┤
│ 2. LuaUsageProbe（通用「Lua 正在执行」探测）                                  │
│    - 扫 level.Entities（及 Scene.HelperEntity）的每个 Component；            │
│      Monocle.Coroutine 的 private enumerators 栈（反射，字段缓存）递归展开   │
│      Flattened（Monocle 用来扁平化嵌套枚举器的 boxed struct）                │
│    - 栈里出现未完成的 LuaCoroutine / 其子类（如 BossesHelper                 │
│      LuaProxyCoroutine）→ 拒绝 [LuaExecuting]                                │
│    - SaveInLuaCutscene=true 时只豁免「LuaCutscenes 生态拥有者」的协程        │
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

> **bench 第一轮反馈（已修复，见 §3/§7）**：E2/E3 通过；E1 在 `SJ tofu [Atrium]` 出现过度拒绝
> （`LuaCutsceneTrigger holds a LuaCutsceneEntity that is not at rest (Finished=True,
> hasUnfinishedCoroutine=True)`）→ 现在 `Finished==true` 不再受残留 wrapper Coroutine 影响；
> E5b 被空闲 `LuaProxyCoroutine on CustomBossPuppet` 的 `[LuaExecuting]` 挡住 → BossesHelper /
> FrostHelper 的 at-rest allow 逻辑在 adapter 移植前保持关闭（旧版「有实例即拒绝」不回归）。

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
| `Celeste.Mod.LuaCutscenes.LuaCutsceneEntity` | `Finished == true` **且** 不是当前 skippable cutscene 的驱动者（`Finished` 由 `OnEnd` 置位，是「Lua 过场已结束」的权威信号；**残留 wrapper Coroutine 不再参与判定**，见 bench 修复） | `Finished` 字段缺失/读不到 → Unsafe |
| `Celeste.Mod.LuaCutscenes.LuaTalker` | ①不是当前 cutscene 驱动者；②`onTalkRoutine` 状态优先：`Finished`（`<>1__state == -1`）→ at rest（残留 Coroutine 不参与判定）；`Running`/`Unknown` → unsafe；③`onTalkRoutine == null` 时退回旧 P2 信号：存在未完成 `Coroutine` → unsafe | `onTalkRoutine` 字段缺失/读不到、状态机状态无法判定 → Unsafe |
| `Celeste.Mod.LuaCutscenes.LuaCutsceneTrigger` | 若字段 `cutsceneEntity` 非空，则**引用的实体**也按 `LuaCutsceneEntity` 规则检查（实体可能已从 `level.Entities` 移除，旧 blanket 会漏） | `cutsceneEntity` 字段缺失/读不到 → Unsafe |

「当前 cutscene 驱动者」= `level.InCutscene && level.onCutsceneSkip.Target` 的类型属于该 LuaCutscenes 实体类型
（与旧 early check 的判据一致）。

**bench 反馈修复 1（E1 过度拒绝）**：第一轮 bench 在 SJ `tofu [Atrium]` 对话结束后仍然拒绝，
日志为 `LuaCutsceneTrigger holds a LuaCutsceneEntity that is not at rest (Finished=True,
hasUnfinishedCoroutine=True)`。结论：`Finished==true` 时实体已完成 `OnEnd`，残留的 wrapper
`Monocle.Coroutine` 只是组件生命周期问题，不代表 Lua 还在执行。因此 `LuaCutsceneEntity` 现在只
按 `Finished` / 「当前 cutscene 驱动者」判定；`LuaTalker` 同理以 `onTalkRoutine` 状态优先，
`Finished` 时不因残留 Coroutine 误拒。目标行为：**tofu Atrium 对话结束后 F7 成功；Soloiini
对话中仍拒绝**（后者由 `onTalkRoutine` state 0/1/2/3 或 LuaCutsceneEntity `Finished==false` 命中）。

**LuaTalker 的 bench 盲区（SJ `tofu [Atrium]`）**：该路径对话进行中 `Level.InCutscene == false`、
`onTalkRoutine = <LuaCoroutineToIEnumerator>d__2`（非 null）、`cutsceneEnv` 已设置，但实体的
`Components` 里只有 `TalkComponent`、没有 `Monocle.Coroutine` 组件——旧 P2「只看 Coroutine 组件」
会漏检并放行存档。新 gate 直接反射 `LuaTalker.onTalkRoutine` 并读取状态机字段（Cecil 核对：
`LuaHelper+<LuaCoroutineToIEnumerator>d__2` 有 `<>1__state`、`<>2__current`、`routine: LuaCoroutine`）：

| `<>1__state` | 含义 | 判定 |
|---|---|---|
| 字段为 null（`onTalkRoutine == null`） | 从未启动 Lua 对话 | at rest（若存在未完成 Coroutine → unsafe，旧 P2） |
| `-1` | `MoveNext` 已返回 false / 已 Dispose，Lua 协程结束 | at rest |
| `0` | 已创建但尚未第一次推进（Lua 线程仍在起点，之后会推进） | **unsafe** |
| `1/2/3` | 挂起在 yield（时间/值/结束前的 null） | **unsafe** |

若状态机类型/字段因 LuaCutscenes 版本变化而无法识别，回退到读 `routine: LuaCoroutine` 的
`_Valid`：`true`（上次 resume 仍 yield）→ unsafe；`false` 语义有歧义（未启动 vs 已结束）→
按 fail-closed 处理。仍然无法判定 → Unsafe（拒绝存档）。

`SaveInLuaCutscene=true` 时整个 LuaCutscenes gate 被用户设置覆盖（等价旧行为：过场中可以存档），
但**不再**关闭其它 mod 的 Lua 检查。

### 3.2 BossesHelper（adapter 未移植，保持旧行为：有实例即拒绝）

第一轮 bench 的 E5b：`BossController` 空闲时，其 `CustomBossPuppet` 上仍有空闲的
`LuaProxyCoroutine`，被 §2 step 2 的 probe 判为 `[LuaExecuting]`。**at-rest 放行需要 lua-work
的 E5b adapter（主线程克隆/恢复 Boss Lua 状态），本分支未移植**，因此不能靠「pattern 没在跑」
放行。现在的策略：

| 场景 | 行为 |
|---|---|
| 关卡里存在 `BossController`（未被 Desync API 声明安全） | **拒绝**（`Failed to Save: [BossController] may lead to desync!`，与旧版一致） |
| 关卡里没有 `BossController`（例如只有 HealthSystemManager 的血条房） | 生态 gate 视为 Safe，其类型不做 late check（与旧「整程序集白名单」等价） |

at-rest 判定代码（`PatternCoroutine` 非 `{Active:true, Finished:false}`）保留在
`DesyncCheckLogic.BossController` + `BossesHelperEcosystem` 的 `AllowAtRestBossesWithAdapter`
分支后面，**flag 默认 false**；移植并 bench 验证 adapter 后把 flag 打开即可。具体 Lua-holding
类型清单（Cecil）：`BossController`、`BossPuppet`（含 `ContactBossPuppet` 等子类）、
`GlobalSavePoint`、`HealthSystemManager`、`HealthSystemManager+DamageController`、
`BossEvent+CutsceneWrapper`。

### 3.3 FrostHelper（adapter 未移植，保持旧行为：有实例即拒绝）

同 BossesHelper 的理由：E5 的 `LuaBadelineBoss` adapter 未移植，at-rest 存档会共享（可能已被
修改的）Lua 表。`AllowAtRestFrostBossWithAdapter` **flag 默认 false**：

| 场景 | 行为 |
|---|---|
| 关卡里存在 `LuaBadelineBoss`（未被 Desync API 声明安全） | **拒绝**（`Failed to Save: [LuaBadelineBoss] may lead to desync!`；旧版是 late check 的 `Saved but Cleared`，同样是拒绝） |
| 关卡里没有 `LuaBadelineBoss` | gate Safe，不做 late check |

at-rest 逻辑（`CreatedCoroutines` 全部 `Finished`，属性优先、回退 backing field）保留在同一
flag 后面，等 adapter 移植后开启。只登记 `LuaBadelineBoss`（+ 子类），**不** blanket 整个
FrostHelper。

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

| 场景 | 旧（`dae2afb`） | 新（本分支，bench 第一轮后） | 备注 |
|---|---|---|---|
| 普通地图，无 Lua 活动 | 允许 | 允许 | — |
| Lua cutscene 进行中（skippable/unskippable） | 拒绝（LuaCutsceneEntity/LuaTalker early check） | 拒绝（gate：`Finished==false` / `onTalkRoutine` 运行态；`[LuaCutsceneEntity]`/`[LuaTalker]`） | `SaveInLuaCutscene=true` 时两者都放行 |
| Lua talker 对话中，`InCutscene=false`、无 `Coroutine` 组件（SJ `tofu [Atrium]`） | **放行（漏检）** | 拒绝（gate：`onTalkRoutine` state 0/1/2/3 → `[LuaTalker]`） | 本次补充修复的 P2 盲区；待 bench 复测 |
| Lua cutscene **已结束**、实体仍在关卡、且带残留 wrapper Coroutine（`Finished==true`） | 允许（整程序集白名单） | 允许（gate：`Finished` 权威；残留 Coroutine 不再误拒） | bench E1 过度拒绝的修复；待 bench 复测 tofu Atrium 对话结束后可存 |
| `LuaCutsceneTrigger` 存在（正常地图） | 允许（blanket） | 允许（引用的 cutsceneEntity `Finished==true`/为空） | 若引用一个未完成的 cutscene entity → 拒绝 |
| CrossoverCollab `Knight` Boss 房，Boss 房间空闲/已死 | 永远拒绝 | **仍拒绝**（旧行为保持；at-rest allow 逻辑在 adapter 移植前关闭） | E5b 需 lua-work adapter + 游戏内验证后才可开启（§3.2/§7） |
| 同上，Boss pattern 正在执行 | 拒绝 | 拒绝（`[BossController]` 或 `[LuaExecuting]`） | — |
| FrostHelper `LuaBadelineBoss` 在房间（即使 CreatedCoroutines 全 finished） | 永远拒绝（late check 直接命中 Lua 字段） | **仍拒绝**（旧行为保持；E5 adapter 未移植） | 待 adapter + bench（§3.3） |
| 任意 mod 的 Lua 协程正在执行（非 LuaCutscenes 生态） | 视类型图；多半放行 | 拒绝 `[LuaExecuting]` | 新增 fail-closed；纯装饰性 Lua 协程可能误报（§8，可用 API/`DisableDesyncCheck` 规避） |
| 未知 mod 实体，字段含 `List<LuaTable>` / `LuaTable[]` / `Func<LuaTable>` | 拒绝（P1） | 拒绝（P1 保留） | — |
| 未知 mod 实体的 **Component** 持有 Lua（字段声明为 `Component`/`List<Component>`） | 放行（盲区） | 拒绝（组件运行时类型检查） | 离线扫描：当前已装 mod 无此类 Component，预期 0 新增误报 |
| 任意 Lua 字段实体（未知类型），空闲 | 拒绝（`Saved but Cleared`） | 拒绝（`Saved but Cleared` + 字段路径 + API 提示；下次提前拒绝） | 保留 fail-closed；mod 可用 `AddSafeType`/predicate 解除 |
| `StrawberryJam2021/5-Grandmaster/maya` | 跳过 checker | 跳过 checker（默认 level predicate） | 行为保持 |
| `DisableDesyncCheck=true` | 全部关闭 | 全部关闭 | 行为保持 |
| `SaveInLuaCutscene=true` + 非过场 Lua | 全部关闭（旧实现是 `Enabled=false`） | 仅豁免 LuaCutscenes 生态；其它 Lua 仍检查 | 有意的语义收窄（v3.28.0 拆分目标） |
| 未安装 LuaCutscenes/BossesHelper/FrostHelper | — | 对应 gate = NotInstalled，无开销 | — |

## 6. Fail-closed 与错误安全

任何「无法证明安全」的路径都拒绝存档，且不抛异常到游戏（checker 内部 try/catch + 日志）：

| 情况 | 行为 |
|---|---|
| `Monocle.Coroutine.enumerators` / `SwapImmediatelyExtension+Flattened` 反射失败（Celeste 更新改名） | probe 直接拒绝并 `Logger.Error`，提示可用 `DisableDesyncCheck` 临时绕过 |
| 生态程序集装了但实体类型解析失败 | gate = Unavailable（不 governed）→ 相关类型走 late check，Lua 字段会被拒绝 |
| 生态实体类型在、但关键字段成员缺失（`Finished`/`cutsceneEntity`；`PatternCoroutine`/`CreatedCoroutines` 仅在未来 adapter flag 启用时需要） | 若关卡里存在实例 → 直接拒绝（fail-closed）；否则 Unavailable |
| 字段/属性反射读取抛异常 | 拒绝（fail-closed） |
| `AddSafeEntityPredicate` / level predicate 抛异常 | 捕获 + 日志 + 视为 false（不会 fail open） |
| late check 命中未知 Lua 类型 | 清档 + `Saved but Cleared`；类型加入动态列表，下次 save 提前拒绝 |
| 任何 checker 内部异常 | 日志 + 保守处理（能定位到实例就拒绝；不能则让 late check fail-closed） |

---

## 7. Bench 验证计划（不启动游戏 = 本分支未执行）

前置：构建 `dotnet build -c Release -p:CelestePrefix="D:\Games\SteamLibrary\steamapps\common\Celeste"`；
把 `SpeedrunTool/bin/Release/net8.0/SpeedrunTool.dll` 覆盖到 **bench 实例**的
`Mods/SpeedrunTool/SpeedrunTool.dll`（先备份旧 DLL 便于 A/B；切换后重启游戏）。默认键位 F7 存 / F8 读 / F4 清。

> **第一轮 bench 结果（`e840e4c`）**：E2/E3 通过；E1 在 `tofu [Atrium]` 对话结束后过度拒绝
> （`Finished=True, hasUnfinishedCoroutine=True`）→ 已修复（§3.1）；E5b 被空闲
> `LuaProxyCoroutine on CustomBossPuppet` 的 `[LuaExecuting]` 挡住 → 已改为 adapter 移植前
> **保持旧行为拒绝**（§3.2）。本轮复测范围：E1（tofu 对话结束后成功、Soloiini 对话中拒绝）、
> E2/E3 不回归；E5b 预期仍拒绝（与旧版一致，不再要求放行）。

### E1：LuaCutscenes 过场（旧 P2 回归 + gate 验证，5–10 分钟）

1. SJ `5-Grandmaster` 中含 `luaTalker` 的房间（`Linj.bin` / `Soloiini.bin` / `tofu.bin`）。
2. 对话进行中（skippable 或 unskippable 均可）按 **F7**。
   - 期望：`Failed to Save: [LuaTalker] may lead to desync!`（skippable 由 `drivenByLevelCutscene`，unskippable 由未完成 `Coroutine` 触发）。
   - **重点回归（P2 盲区）**：SJ `tofu [Atrium]` 的对话中 `InCutscene=false` 且实体上没有
     `Monocle.Coroutine` 组件；期望因 `onTalkRoutine` 状态机仍在运行（state 0/1/2/3）而拒绝。
     日志应出现 `talkRoutine=Running`（或 `Unknown`，fail-closed）。
   - 日志：`SpeedrunTool/DesyncRiskAnalyzer: Desync risk: [LuaTalker] ...`。
3. 等对话结束（实体仍可能留在房间、可能带残留 wrapper Coroutine）再按 F7。
   - 期望：**存档成功**（`LuaCutsceneEntity` 以 `Finished` 为准、`LuaTalker` 以 `onTalkRoutine`
     `<>1__state == -1` 为准，残留 Coroutine 不再误拒）。这是第一轮 bench 的过度拒绝修复点。
   - Soloiini 对话中应按 F7 拒绝；对话结束后应成功。
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

### E5b：BossesHelper（CrossoverCollab `Knight`）——**本轮只验证不回归（预期仍拒绝）**

> **状态：需 opt-in adapter，暂不放行。** lua-work 的 E5b adapter（主线程克隆/恢复 Boss Lua
> 状态）未移植；bench 证明「pattern 空闲」不足以安全存档（`CustomBossPuppet` 上仍有空闲
> `LuaProxyCoroutine`）。当前实现：**关卡里存在 `BossController` 即拒绝**（与旧版一致）。

1. 进 `CrossoverCollab/1-Submissions/Knight` 的 Boss 房（`BET-02` 等，bench 已确认含 `BossController`）。
2. Boss pattern 正在执行时按 **F7** → 期望拒绝 `[BossController]`（或 `[LuaExecuting]`；本轮
   修复后 gate 先运行，通常应为 `[BossController]`）。
3. 等 pattern 结束 / Boss 死亡后按 F7 → **期望仍然拒绝**（旧行为，无回归）；记录弹窗/日志。
4. 后续（不在本轮）：移植 lua-work `BossesHelperLuaAdapter` + 把
   `BossesHelperEcosystem.AllowAtRestBossesWithAdapter` 打开，再按原 E5b 步骤（idle 存档 →
   F8 读档观察 Boss 行为）做游戏内验证。

### E5（FrostHelper `LuaBadelineBoss`）——**需 opt-in adapter，暂不放行**

当前已装 Mods 没有可直接加载的 `FrostHelper/LuaBoss` 地图（候选在 `TestMap/SSC3/...`，不在
`Maps/` 下）。当前实现：关卡里存在 `LuaBadelineBoss` 即拒绝（旧行为）；`AllowAtRestFrostBossWithAdapter`
flag 默认 false，等 E5 adapter 移植后开启并用测试房间验证。


---

## 8. 迁移说明

- **没有破坏任何已有 ModInterop 契约**：`SpeedrunTool.SaveLoad` / `SpeedrunTool.Bench` 不变；
  `ExternalSafeOracle` 从未导出、无第三方使用，直接删除。
- **设置项不变**：`DisableDesyncCheck`（关闭 checker）与 `SaveInLuaCutscene`（过场中允许存档）
  的语义保持 v3.28.0 拆分；但 `SaveInLuaCutscene` 不再关闭非过场 Lua 检查（见 §5）。
- **日志**：tag 未变（`SpeedrunTool/DesyncRiskAnalyzer`）；新增 `[LuaExecuting]` 弹窗与
  `SpeedrunTool/Desync` 注册日志。依赖日志 grep 的脚本注意新增行。
- **弹窗**：`Failed to Save: [<Type>] may lead to desync!` / `Saved but Cleared: [...]` 格式保持；
  新增 `Failed to Save: [LuaExecuting] may lead to desync!`。生态 gate 现在先于 probe 运行，
  所以 BossesHelper/LuaCutscenes 的门槛仍优先显示 `[BossController]` / `[LuaTalker]` 等旧弹窗。
- **BossesHelper/FrostHelper 行为不变**：at-rest 放行逻辑已实现但 **flag 默认关闭**（adapter 未
  移植），这两类 Boss 房仍按旧行为拒绝存档；相关 bench/自动化期望**不需要**更新。
- **地图/模组迁移**：以前需要改 SRT 源码加 early check 的场景，现在由 mod 通过
  `SpeedrunTool.Desync` 注册；地图级 safe map 用 `AddSafeLevelPredicate`（或直接让默认
  `DefaultSafeMaps` 增加，但那是 SRT 内部行为，建议走 API）。

---

## 9. 风险与回退

| 风险 | 说明 | 缓解 / 回退 |
|---|---|---|
| probe 误报（纯装饰性 Lua 协程） | 任何非 LuaCutscenes 生态的活跃 `LuaCoroutine` 都会被拒绝，即使脚本只做视觉效果 | 用户可用 `AddSafeType`/`AddSafeEntityPredicate`/`AddSafeLevelPredicate`，或 `DisableDesyncCheck`；最坏情况 revert analyzer 提交 |
| Celeste 内部结构变化 | probe 依赖 `Monocle.Coroutine.enumerators` 与 `SwapImmediatelyExtension+Flattened.enums`（反射，字段名已 Cecil 核对） | fail-closed 拒绝 + `Logger.Error` 明确提示；更新后改反射名即可（集中在 `LuaUsageProbe`） |
| LuaCutscenes 字段漂移 | `Finished` / `onTalkRoutine` / `<>1__state` 改名 → 实例在 → 直接拒绝（不会半放行） | fail-closed + 日志；用 Cecil 重新核对清单（§3.1 即维护清单） |
| BossesHelper/FrostHelper 功能推迟 | at-rest 放行等 E5/E5b adapter；当前房间仍「有实例即拒绝」 | 与旧版一致（无回归）；adapter 移植后打开 `AllowAtRest*WithAdapter` flag 并 bench 验证 |
| 组件运行时类型检查新增命中 | 某些 mod 的 Component 可能持有 Lua（离线扫描当前环境为 0），会从「漏报」变「`Saved but Cleared`」 | 预期行为（fail-closed）；mod 可用 `AddSafeType(componentType)` 解除 |
| 性能 | probe 每个 save 扫全部 entity/components + 反射栈；gate 扫全部 entity；类型检查有 per-save/全局缓存 | 估算 O(entities×components) 每 save 一次，通常 < 几 ms 量级；如 bench 发现卡顿，可把 probe 改为「仅扫描有 Coroutine 组件的实体」（已是）或懒加载 |
| `SaveInLuaCutscene` 用户预期 | 旧文档/用户可能以为该开关能关闭全部 Lua 检查 | 文档 + 设置描述已说明「只允许过场中存档」；保持 v3.28.0 拆分 |

### 回退粒度

- **完全回退**：按 3 个提交倒序 revert（tests/docs → analyzer 重写 → probe/API/ecosystem 文件）。
  新文件不挂在旧 checker 路径上，单独 revert 不会破坏构建；全部 revert 即回到 `dae2afb`。
- **只回退某个生态行为**：生态内 `Evaluate()` 的 `AllowAtRest*WithAdapter` flag 是唯一开关；
  置 false 即「有实例即拒绝」（当前默认），置 true 才启用 at-rest 放行。
- **只回退 probe**：`EarlyCheckDesyncRisk` 跳过 `LuaUsageProbe` 分支（其余 gate/late check 保留）。
- **只回退 E1 修复**：`DesyncCheckLogic.LuaCutsceneEntity` / `LuaTalker` 的 `Finished`/状态机
  优先级是纯函数，改回带 coroutine 参数即可（单测会同步失败提示）。

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
at-rest 规则（`LuaCutsceneEntity` 只按 `Finished`；`LuaTalker` 的 `onTalkRoutine` 四态
None/Finished/Running/Unknown，含「Finished + 残留 Coroutine = Safe」的 bench 回归）、
`BossController`/`FrostLuaBoss` 规则（保留给未来 adapter）、缺字段 fail-closed、
predicate 抛异常 fail-closed、safe type 子类覆盖、entity/level predicate 注册与移除、
override 组合、Reset。

**游戏内 bench 记录**：
- 第一轮（`e840e4c`，DLL `d2eb9d96…`）：E2/E3 通过；E1 tofu Atrium 过度拒绝（已修）；E5b 被
  `[LuaExecuting]` 挡住（已按「adapter 前不放行」处理）。
- 第二轮（本提交）：待复测 E1/E2/E3；E5b 预期仍拒绝（与旧版一致）。

产物保留在 `SpeedrunTool/bin/Release/net8.0/SpeedrunTool.dll` 供 bench 使用。

---

## 11. 相关文档

- `D:\DSH\Celeste\lua-survey\docs\lua-usage-survey.md` §9–§11（checker 现状/方案对比/分阶段建议）
- `D:\DSH\Celeste\lua-work\docs\lua-desync.md` §1–§3（Lua 克隆边界）、§6（P1/P2/P3）、
  §15/§16（E5/E5b adapter，本分支未移植）、§8（E1–E3 原始 bench 步骤）
- `docs/scene-state.md`、`docs/audit.md`（SRT 既有验证/审计记录）
