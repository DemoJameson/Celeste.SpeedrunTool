# DeepCloner `CloneObjectTo` MSIL 化方案（D2/D3 的落地设计）

> 只写方案，未改源码。目标：把 `DeepCloneTo`（save/load 主路径）从 `ClonerToExprGenerator`（Expression Trees）切到 `DeepClonerMsilGenerator` 风格的手写 IL，并彻底删除 `ForceSetField`（反射改写 `initonly` 字段）——同时修掉 D3（`DeepClonerExprGenerator` 的 readonly 简单字段会抛 `Expression must be writeable`）与 D2（反射写 readonly 的竞态/静默失败/性能问题）。
> 背景事实（已实测）：
> - `ShallowObjectCloner.IsSafeVariant() == false`（.NET 8）→ `DeepClone` 走 `DeepClonerMsilGenerator`；`CloneObjectTo` **无条件**走 `ClonerToExprGenerator`（`DeepClonerGenerator.cs:264-274`、`:276-315`）。
> - `Expression.Assign(readonlyField)` 在 .NET 8 抛 `ArgumentException: Expression must be writeable`（实测）。
> - 动态方法里 `stfld` 写 `initonly` 字段是可行的（`DeepClonerMsilGenerator.cs:124-153` 已经在这样做，且被现有 `DeepClone` 路径实际执行）。

---

## 1. 目标语义（必须与现状逐条对齐）

`DeepClonerGenerator.CloneObjectTo(object objFrom, object objTo, bool isDeep, DeepCloneState state)`（`:276-315`）的契约：

1. `objTo == null` → 返回 null；`objFrom == null` → `ArgumentNullException`；
2. `objFrom.GetType()` 必须能被 `objTo` 接收（`IsInstanceOfType`），否则 `InvalidOperationException`；
3. `objFrom is string` → `InvalidOperationException`（禁止克隆 string）；
4. 缓存：`TypeCacheDeepTo` / `TypeCacheShallowTo`（按 `Type` 缓存生成的委托）；
5. `state ??= new DeepCloneState()`；
6. 委托签名 `Func<object, object, DeepCloneState, object>`，语义：
   - **深克隆（isDeep）**：`state.AddKnownRef(from, to)`（root 绑定，防环）；然后逐字段：
     - 安全类型字段：`to.field = from.field`（值拷贝/引用共享）；
     - 复杂类型字段：`to.field = CloneClassInternal/CloneStructInternal(from.field, state)`（注意：**嵌套对象走 CloneClassInternal，会触发既有 Pre/PostCloneProcessor**；root 本身**不**触发 processor，与现状一致）；
   - **浅克隆（isShallow）**：不 `AddKnownRef`；所有字段直接引用赋值（包括复杂类型）。
7. 目标对象 `objTo` **已存在且已初始化**，不要 `MemberwiseClone`、不要 `newobj`；所有字段都必须显式写入（覆盖 target 旧值），否则会出现“上次残值”的隐蔽 bug；
8. 只支持引用类型 root（现状 `ClonerToExprGenerator.GenerateClonerInternal` 对 value type 抛 `InvalidOperationException`）；数组 root 现状是支持的（`GenerateProcessArrayMethod`），新实现要保留。

---

## 2. 新方法：`DeepClonerMsilGenerator.GenerateClonerToInternal`

### 2.1 入口

```csharp
// DeepCloner/Helpers/DeepClonerMsilGenerator.cs
internal static object GenerateClonerToInternal(Type realType, bool isDeepClone) {
    if (realType.IsValueType) {
        throw new InvalidOperationException("Operation is valid only for reference types");
    }

    Type funcType = typeof(Func<,,,>).MakeGenericType(typeof(object), typeof(object), typeof(DeepCloneState), typeof(object));
    DynamicMethod dt = new DynamicMethod(
        "DeepObjectClonerTo_" + realType.Name + "_" + Interlocked.Increment(ref _methodCounter),
        typeof(object), new[] { typeof(object), typeof(object), typeof(DeepCloneState) },
        TypeCreationHelper.GetModuleBuilder(), skipVisibility: true);
    dt.InitLocals = false;

    ILGenerator il = dt.GetILGenerator();
    if (realType.IsArray) {
        GenerateProcessArrayToMethod(il, realType, isDeepClone);   // 见 2.4
    } else {
        GenerateProcessToMethod(il, realType, isDeepClone);        // 见 2.2/2.3
    }
    return dt.CreateDelegate(funcType);
}
```

> `TypeCreationHelper.GetModuleBuilder()` 现为懒初始化、非线程安全（`DeepCloner/Helpers/TypeCreationHelper.cs:8`），建议顺手加 `lock`（与“每次 save 首次生成”并发无关，但便宜）。

### 2.2 IL 骨架（类）

```csharp
private static void GenerateProcessToMethod(ILGenerator il, Type type, bool isDeepClone) {
    // 参数：arg0 = from(object), arg1 = to(object), arg2 = state
    LocalBuilder fromLocal = il.DeclareLocal(type);
    LocalBuilder toLocal   = il.DeclareLocal(type);

    // fromLocal = (T)arg0;  toLocal = (T)arg1;
    il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, type); il.Emit(OpCodes.Stloc, fromLocal);
    il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Castclass, type); il.Emit(OpCodes.Stloc, toLocal);

    if (isDeepClone) {
        // state.AddKnownRef(arg0, toLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, toLocal);
        il.Emit(OpCodes.Call, typeof(DeepCloneState).GetMethod(nameof(DeepCloneState.AddKnownRef)));
    }

    foreach (FieldInfo fieldInfo in GetAllFields(type)) {     // 与现有实现一致的基类链遍历
        if (DeepClonerSafeTypes.CanReturnSameObject(fieldInfo.FieldType) || !isDeepClone) {
            // to.field = from.field;
            il.Emit(OpCodes.Ldloc, toLocal);
            il.Emit(OpCodes.Ldloc, fromLocal);
            il.Emit(OpCodes.Ldfld, fieldInfo);
            il.Emit(OpCodes.Stfld, fieldInfo);                // 对 initonly 也可写（动态方法不校验）
        } else {
            MethodInfo cloneMethod = fieldInfo.FieldType.IsValueType
                ? typeof(DeepClonerGenerator).GetMethod("CloneStructInternal", NonPublic | Static).MakeGenericMethod(fieldInfo.FieldType)
                : typeof(DeepClonerGenerator).GetMethod("CloneClassInternal", NonPublic | Static);

            // to.field = (<T>)CloneClassInternal/CloneStructInternal(from.field, state);
            il.Emit(OpCodes.Ldloc, toLocal);
            il.Emit(OpCodes.Ldloc, fromLocal);
            il.Emit(OpCodes.Ldfld, fieldInfo);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, cloneMethod);
            if (!fieldInfo.FieldType.IsValueType) {
                il.Emit(OpCodes.Castclass, fieldInfo.FieldType);  // 等价于 Expr 的 Expression.Convert
            }
            il.Emit(OpCodes.Stfld, fieldInfo);
        }
    }

    il.Emit(OpCodes.Ldloc, toLocal);
    il.Emit(OpCodes.Ret);
}
```

要点：
- **不要**为了“省一次字段拷贝”而加 `MemberwiseClone`：`CloneObjectTo` 的 target 是既有对象，`MemberwiseClone` 没有意义；而且如果某些字段被跳过（如 `isGoodConstructor` 优化），会留下 target 的旧值 → 语义错误。
- 字段遍历顺序与名称无关（Expr 版也是按类型声明顺序），但建议按 `DeclaredOnly` 逐层 + 保持稳定顺序，方便调试对比。
- `ContextBoundObject` 的 break 处理要保留（与 `ClonerToExprGenerator.cs:47-61` 一致）。
- `System.Tuple`/`ValueTuple` 的“先构造成员再赋值”优化在 CloneTo 场景**不需要/不适用**（target 已存在），跳过；但要确认 tuple 类型仍走普通字段路径（与 Expr 版一致：`ClonerToExprGenerator` 没有 tuple 特判）。
- 泛型/`ref struct`/指针等异常类型：沿用现有 `ClonerToExprGenerator` 的失败方式（抛出并由调用方处理）。

### 2.3 为什么可以安全地 `stfld` 到 `initonly`

- `DynamicMethod(..., skipVisibility: true)` 生成的代码在 CLR 中不经过可验证性检查；`stfld` 到 `initonly` 字段在 **DynamicMethod/Reflection.Emit** 下是允许的（现有 `DeepClonerMsilGenerator` 已对 `Collider.Size` 等 `readonly` 字段执行该操作，且 mod 主路径工作正常）。
- 不再需要 `ForceSetField`：删除 `DeepClonerExprGenerator.ForceSetField`（`:18-40`）与 `_readonlyFields` 缓存（`:9`）、删除 `ClonerToExprGenerator` 里的两处 `ForceSetField` 调用（`:80-99`）。
- **验证手段**：回归测试里加一个 `readonly` 字段批量校验（见 §4），并在 `DeepClone`/`DeepCloneTo` 两条路径都跑一遍。

### 2.4 数组 root（保留现状语义）

`ClonerToExprGenerator.GenerateProcessArrayMethod`（`:122-167`）的行为：`Math.Min(srcLen, dstLen)` 个元素写入**已存在**的 target 数组，深/浅两种；多维数组与 `CloneAbstractArrayInternal` 也支持。
新实现 `GenerateProcessArrayToMethod` 直接复用 `ClonerToExprGenerator` 里现有的 `ShallowClone1DimArraySafeInternal/Clone1DimArraySafeInternal/...`（可把这些 `internal static` 方法整体搬到一个新的 `DeepClonerArrayToHelper`，或直接留在改名后的 `ClonerToMsilGenerator`），IL 只负责调用它们——这样数组语义零改动。

---

## 3. `DeepClonerGenerator.CloneObjectTo` 的改造

```csharp
public static object CloneObjectTo(object objFrom, object objTo, bool isDeep, DeepCloneState state = null) {
    if (objTo == null) return null;
    if (objFrom == null) throw new ArgumentNullException("objFrom", ...);
    Type type = objFrom.GetType();
    if (!type.IsInstanceOfType(objTo)) throw new InvalidOperationException(...);
    if (objFrom is string) throw new InvalidOperationException("It is forbidden to clone strings");

    // 单一路径：MSIL 生成器（不再分 Expr/MSIL）
    if (!DeepClonerCache.TypeCacheDeepTo.TryGetValue(type, out object deepTo))
        lock (type) DeepClonerCache.TypeCacheDeepTo.TryAdd(type, deepTo = DeepClonerMsilGenerator.GenerateClonerToInternal(type, true));
    if (!DeepClonerCache.TypeCacheShallowTo.TryGetValue(type, out object shallowTo))
        lock (type) DeepClonerCache.TypeCacheShallowTo.TryAdd(type, shallowTo = DeepClonerMsilGenerator.GenerateClonerToInternal(type, false));

    var cloner = (Func<object, object, DeepCloneState, object>)(isDeep ? deepTo : shallowTo);
    if (cloner == null) return objTo;

    state ??= new DeepCloneState();
    return cloner(objFrom, objTo, state);
}
```
- 缓存语义不变（`TypeCacheDeepTo`/`TypeCacheShallowTo` 继续用，key 为 `Type`）。
- 保留 `cloner == null` 分支（虽然 MSIL 生成器目前不返回 null，保持与 upstream 兼容）。
- `GenerateCloner(Type, bool asObject)`（`:259-274`）里的 `#if !NETCORE` / `#if NETCORE` 分裂建议删除：`DynamicMethod`/`AssemblyBuilder` 在 .NET Core / .NET 8 都可用（本机的 `DeepClone` 已经走 MSIL 分支），统一走 MSIL 可减少一条未测试路径。

### 3.1 迁移与回滚

- 增加环境变量开关（与 profiling 一致的做法）：`SRT_DC_LEGACY_EXPR=1` 时走旧 `ClonerToExprGenerator`，默认走 MSIL。方便 bench-env 做 A/B（对比正确性/耗时）。
- A/B 通过后删除 `ClonerToExprGenerator.cs`、`DeepClonerExprGenerator.cs`、`DeepClonerMsilHelper.cs` 中的死逻辑（`ShallowObjectCloner.IsSafeVariant` 相关分支若无其他用途也可删）。
- `DeepCloner/UPSTREAM.md` 里补一段“本地修改：CloneTo 改为 MSIL 生成器，移除 ForceSetField”。

---

## 4. 回归测试清单（必须覆盖）

### 4.1 克隆正确性（单元级，建议放 `SpeedrunTool/Source/DebugTool/DeepClonerTests.cs`，`#if DEBUG` + 控制台命令触发，或在外部 test 项目里跑）
对每个测试类型构造实例 `a` → 用 `RuntimeHelpers.GetUninitializedObject(t)` 或普通 new 得到目标 `b` → `a.DeepCloneTo(b)` → 逐字段比较：
1. `readonly int` / `readonly string` / `readonly enum` / `readonly Guid` / `readonly DateTime`（**D3 的直接回归**）；
2. `readonly Vector2`（自定义 struct）/ `readonly` 自定义含引用字段的 struct；
3. `readonly List<X>`、`readonly Dictionary<K,V>`、`readonly X[]`（复杂/容器）；
4. `private readonly` 字段在**基类**中；`protected internal` 字段；`static readonly` 字段必须**不被**写入；
5. 泛型类 `Wrapper<T>`（T = int / 自定义类）；嵌套私有类；
6. 有自引用/环（`readonly Node Next = this`）→ 验证 `AddKnownRef` 后不无限递归；
7. `readonly` 字段为 `null`（源 null、目标非 null）；
8. 无参构造器/有参构造器/`ContextBoundObject` 子类（应跳过其字段）；
9. **浅克隆**：`ShallowCloneTo` 后引用字段应 `ReferenceEquals`，且不产生 knownRef 绑定；
10. 数组 root：长度相同/不同（Min 语义）、多维数组、非零下界数组（`Array.CreateInstance(type, lengths, lowerBounds)`）；
11. 目标对象与源对象是**同一实例**（`a.DeepCloneTo(a)`）：现状允许吗？→ 加测试确认（Expr 版可以；MSIL 版同样可以，注意 AddKnownRef(from,to) 是同一对象）。

### 4.2 字段覆盖断言（抓“静默丢字段”）
写一个通用比较器：反射获取类型及其基类所有实例字段，对每个字段 `src.GetValue` / `dst.GetValue` 做比较：
- 安全类型：`Equals` / `ReferenceEquals`（值类型用 `Equals`）；
- 复杂类型：若 `CanReturnSameObject` 则 `ReferenceEquals`；否则只检查**非 null 且类型一致**（递归比较可选，注意环）；
- 对 `EventInstance`/`ILHook`/`GraphicsResource` 等 SRT 特殊类型用 ReferenceEquals 规则。
该断言能直接抓住 `ForceSetField` 静默失败那类问题（历史 bug 模式），建议对 `Level`/`Session`/`SaveData` 的三个真实对象也跑一次（用游戏内 hotkey 生成测试数据）。

### 4.3 SRT 级回归（游戏内）
- save → 修改状态 → load → save（第二个槽）→ load（第一个槽）→ load（第二个槽）交叉；
- 重点地图：GM Heartside 前几屏 + `docs/sj-compat.md` §8 的机制清单；
- 逐字段/逐实体的对比可以用“DEBUG 里的 `SaveLoadAction.LogSavedValues`/`DeepClonerUtils` 日志”辅助。

### 4.4 性能回归
- 用 `docs/audit-instrumentation.md` 的 `SaveLoadProfiler`：对比 `cloneBack`（load 路径 `DeepCloneToShared`）在 Expr vs MSIL 下的耗时；预期 MSIL 在 `readonly` 多的图上**明显更快**（省掉每字段 `ForceSetField` 的反射 + 每类型 `Expression.Compile` 的 JIT 抖动）。
- 微基准可复用 `audit.md` 附录 1 的方法（把 DeepCloner 源码复制到临时项目 + 引用 `MonoMod.Utils.dll`/`Mono.Cecil.dll`）。

---

## 5. 风险与缓解

| 风险 | 说明 | 缓解 |
|---|---|---|
| `stfld` 写 readonly 在少数运行时/裁剪场景失败 | 理论上 DynamicMethod 不受验证，但 NativeAOT/裁剪可能不同 | 保留环境变量回退到旧 Expr 路径一个版本；`DeepClone` 主路径已有先例 |
| 目标对象字段残留 | 新实现必须写全部字段；漏写会保留 target 旧值 | §4.2 字段覆盖断言 |
| root 不触发 Pre/PostCloneProcessor | 保持现状（`CloneObjectTo` 不调 processor），但要注意：如果有人在 root 上依赖 processor（例如 Level 的 `VirtualAsset` 收集），会与旧行为不同 | 现状 `CloneObjectTo` 也不调，无需改；在注释里写明 |
| 数组 root 语义 | Min 长度、非零下界 | 直接复用旧 helper 方法，仅换 IL 外壳 |
| 缓存混用 | 旧缓存里可能已有 Expr 版委托（同进程热重载场景） | 切换时清 `DeepClonerCache.ClearCache()`；缓存 key 不变但 delegate 来源变了，清一次即可 |
| `TypeCreationHelper` 线程安全 | 懒初始化 | 加锁 |

---

## 6. 改动清单（按顺序）

1. `DeepCloner/Helpers/DeepClonerMsilGenerator.cs`：新增 `GenerateClonerToInternal`、`GenerateProcessToMethod`、`GenerateProcessArrayToMethod`（复用旧数组 helper）；`#if !NETCORE` 守卫保留（`DynamicMethod` 需要，但 .NET 8 也满足，实际可去掉）。
2. `DeepCloner/Helpers/DeepClonerGenerator.cs`：`CloneObjectTo` 切到新入口；`GenerateCloner` 统一走 MSIL；保留 `#if` 作为回退开关。
3. `DeepCloner/Helpers/ClonerToExprGenerator.cs`：删除（或整体替换为 `ClonerToMsilGenerator` 只保留数组 helper）。
4. `DeepCloner/Helpers/DeepClonerExprGenerator.cs`：删除；`ForceSetField` 一并删除。
5. `DeepCloner/Helpers/DeepClonerCache.cs`：无需改（继续用 `TypeCacheDeepTo/ShallowTo`）。
6. 新增 `SpeedrunTool/Source/DebugTool/DeepClonerTests.cs`（DEBUG-only，可从命令/热键触发）+ 字段覆盖断言；如果 Lead 不想把测试放进 mod，可放独立 test 项目。
7. `DeepCloner/UPSTREAM.md` + `docs/audit.md` 的 D2/D3 状态更新（修完标记 resolved）。

---

## 7. 预期收益

- **正确性**：消除 `ForceSetField` 的（a）跨线程竞态、（b）`m_fieldAttributes` 取不到时静默不赋值、（c）`Expression.Assign` 对 readonly 简单字段抛异常（D2/D3 一次解决）。
- **性能**：load 路径每个 `initonly` 字段不再做“取 FieldInfo → 读 attributes → 改 attributes → SetValue（boxing）→ 改回”，批量 readonly 字段（如 mod 实体）能省可观时间；同时省掉 `Expression.Compile()` 的生成/首次 JIT（per-type ≈0.19ms，见 audit 附录 1）。
- **可维护性**：DeepCloner 只剩一条生成路径（MSIL），readonly/数组/struct 语义只维护一处。
