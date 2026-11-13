using Celeste.Mod.SpeedrunTool.ModInterop;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;

/// <summary>
/// Lua desync checker ("checker") entry points used by the save pipeline.
///
/// The checker has three stages (the StateManager / DeepCloner call sites are unchanged):
/// <list type="number">
/// <item><b>VeryVeryEarlyCheck</b> (<see cref="GetAndRefreshIfDesyncCheck"/>): is the checker enabled
/// for this save at all? A level predicate registered through <c>SpeedrunTool.Desync</c> (plus the
/// historical Strawberry Jam "maya" safe map) can opt the level out entirely.</item>
/// <item><b>EarlyCheck</b> (<see cref="OnBeforeSaveState"/>, main thread, before cloning):
/// <list type="bullet">
/// <item>the generic <see cref="LuaUsageProbe"/> refuses the save when Lua is currently executing
/// (unless <c>SaveInLuaCutscene</c> covers a LuaCutscenes-driven coroutine);</item>
/// <item>the built-in <see cref="DesyncEcosystems"/> gates (LuaCutscenes / BossesHelper / FrostHelper)
/// verify that every instance in the level is at rest;</item>
/// <item>entity types that a previous save's late check had to refuse are refused up front
/// (dynamic fail-closed list).</item>
/// </list></item>
/// <item><b>LateCheck</b> (<see cref="Check"/>, main thread, during the save clone; result read by
/// <see cref="OnAfterSaveState"/>): fail-closed type-graph walk for entities that no gate covers.
/// It keeps the P1 container/array/generic expansion, the P3 field-path log, and additionally walks
/// <see cref="Entity.Components"/> by their <i>runtime</i> types. A hit clears the state and shows
/// "Saved but Cleared".</item>
/// </list>
/// </summary>
internal static class DesyncRiskAnalyzer {
    private const string LogTag = "SpeedrunTool/DesyncRiskAnalyzer";

    // One master switch for every desync check. The property keeps its old serialized name,
    // SaveInLuaCutscene, so existing settings files retain their choice.
    private static bool Enabled => !ModSettings.AllowUnsafeLuaSavestates;

    // Fail-closed late-check hits. Instances of these types are refused in the next save's early check.
    private static readonly HashSet<Type> EarlyCheckEntityTypes = [];

    // Late-check type decisions (similar to Force.DeepCloner DeepClonerSafeTypes). Seeded with the
    // Lua wrapper types; populated lazily while walking entity/component type graphs.
    private static readonly ConcurrentDictionary<Type, bool> KnownTypes = new();

    internal static bool CheckAll = true;

    private static bool shouldDesyncCheckTemporarily;
    private static Type desyncReason;
    private static string desyncDetail;

    // LateCheck 的诊断信息（P3）：命中时记录「从实体类型到 Lua 类型」的字段路径，
    // 方便用户从日志里定位是哪个 mod 的哪个字段。只在主线程的 Check 中使用。
    [ThreadStatic] private static List<string> canSyncTypePath;
    [ThreadStatic] private static string unsafeLuaPath;

    [Initialize]
    private static void Initialize() {
        desyncReason = null;
        desyncDetail = null;
        EarlyCheckEntityTypes.Clear();
        ResetKnownTypes();

        DesyncInterop.InstallDefaults();
        DesyncRegistration.SafeTypeRegisteredHook = OnSafeTypeRegistered;
        DesyncEcosystems.Initialize();
        LuaUsageProbe.Initialize();
    }

    private static void ResetKnownTypes() {
        KnownTypes.Clear();

        // They are not in any EverestModule's assembly so ModUtils.GetAllTypes() does not include them.
        KnownTypes.TryAdd(typeof(NLua.Lua), false);
        KnownTypes.TryAdd(typeof(KeraLua.Lua), false);
        foreach (Type type in typeof(NLua.LuaBase).Assembly.GetTypes()) {
            if (type.IsSubclassOf(typeof(NLua.LuaBase))) {
                KnownTypes.TryAdd(type, false);
            }
        }
        KnownTypes.TryAdd(typeof(LuaCoroutine), false);
    }

    /// <summary>
    /// A mod registered an entire type as safe through <c>SpeedrunTool.Desync.AddSafeType</c>. Drop it
    /// from the dynamic fail-closed list so the next save does not refuse it up front. The late check
    /// consults the registration directly (it takes precedence over cached negative decisions), so no
    /// "safe" entry is written into <see cref="KnownTypes"/>: that keeps unregistration honest.
    /// </summary>
    internal static void OnSafeTypeRegistered(Type type) {
        EarlyCheckEntityTypes.RemoveWhere(known => known == type || known.IsSubclassOf(type));
        Logger.Info(LogTag, $"[{type.FullName}] was declared safe through SpeedrunTool.Desync.AddSafeType.");
    }

    internal static bool GetAndRefreshIfDesyncCheck() {
        shouldDesyncCheckTemporarily = VeryVeryEarlyCheck();
        if (!shouldDesyncCheckTemporarily) {
            DesyncEcosystems.ResetPerSaveState();
        }
        return shouldDesyncCheckTemporarily;

        // some maps are actually safe to save even though they have lua cutscene, in that case return false
        static bool VeryVeryEarlyCheck() {
            if (!Enabled || Engine.Scene is not Level level) {
                return false;
            }

            if (DesyncRegistration.IsLevelSafe(level)) {
                Logger.Info(LogTag, $"Level [{level.Session.Area.SID}] is declared safe; skipping the desync check for this save.");
                return false;
            }

            return true;
        }
    }

    internal static bool OnBeforeSaveState(Level level, out string popup) {
        if (EarlyCheckDesyncRisk(level, out string reason)) {
            popup = reason;
            return true;
        }

        popup = "";
        desyncReason = null;
        desyncDetail = null;
        return false;
    }

    internal static bool OnAfterSaveState(out string popup) {
        CheckAll = false;
        bool unsafeState = desyncReason is not null;
        if (!unsafeState) {
            popup = "";
            return false;
        }

        Logger.Warn(LogTag,
            $"Saved but cleared: [{desyncReason.FullName}] may lead to desync (via {desyncDetail ?? "unknown"}). "
            + "If this state is known safe, the mod can register SpeedrunTool.Desync.AddSafeType/AddSafeEntityPredicate.");
        popup = $"Saved but Cleared: [{desyncReason.Name ?? "UnknownEntity"}] may lead to desync!";
        return true;
    }

    private static bool EarlyCheckDesyncRisk(Level level, out string reason) {
        reason = "";
        if (level is null) {
            return false;
        }

        // ---- 1. Built-in ecosystem at-rest gates -------------------------------------------------
        // Run before the generic probe so ecosystem-owned problems keep their old, specific popup
        // ([BossController] / [LuaTalker] / ...) instead of being reported as generic [LuaExecuting].
        foreach (EcosystemGateResult gate in DesyncEcosystems.EvaluateAtRest(level)) {
            switch (gate.State) {
                case EcosystemGateState.NotInstalled:
                case EcosystemGateState.Safe:
                    continue;

                case EcosystemGateState.Unavailable:
                    if (gate.Kind == DesyncEcosystemKind.LuaCutscenes && ModSettings.SaveInLuaCutscene) {
                        DesyncEcosystems.AcceptOverride(gate.Kind);
                        Logger.Info(LogTag, $"LuaCutscenes state cannot be inspected but SaveInLuaCutscene is enabled: {gate.Detail}");
                        continue;
                    }

                    // Fail closed in the late check: the ecosystem types are not governed.
                    Logger.Warn(LogTag,
                        $"[{NameOf(gate.Kind)}] at-rest state cannot be inspected ({gate.Detail}); "
                        + "falling back to the fail-closed late check for its types.");
                    continue;

                default: // EcosystemGateState.Unsafe
                    if (gate.Kind == DesyncEcosystemKind.LuaCutscenes && ModSettings.SaveInLuaCutscene) {
                        DesyncEcosystems.AcceptOverride(gate.Kind);
                        Logger.Info(LogTag,
                            $"[{gate.EntityTypeName}] is not at rest but SaveInLuaCutscene is enabled; allowed by the user setting. {gate.Detail}");
                        continue;
                    }

                    desyncDetail = gate.Detail;
                    Logger.Warn(LogTag, $"Desync risk: [{gate.EntityTypeName ?? "unknown"}] {gate.Detail}");
                    reason = $"Failed to Save: [{gate.EntityTypeName ?? "UnknownEntity"}] may lead to desync!";
                    return true;
            }
        }

        // ---- 2. Generic "is Lua executing right now?" probe -------------------------------------
        // Catches Lua execution that no ecosystem gate covers (including unknown mods). Runs after
        // the gates so their instances keep the more specific popup.
        if (LuaUsageProbe.TryFindExecutingLua(level, ModSettings.SaveInLuaCutscene, out string executingDetail)) {
            desyncDetail = executingDetail;
            reason = "Failed to Save: [LuaExecuting] may lead to desync!";
            return true;
        }

        // ---- 3. Dynamic fail-closed list from previous late checks --------------------------------
        foreach (Type type in EarlyCheckEntityTypes.ToArray()) {
            if (level.Tracker.GetEntitiesTrackIfNeeded(type) is not { Count: > 0 } entities) {
                continue;
            }

            bool anyUnsafe = false;
            foreach (Entity entity in entities) {
                if (entity is null) {
                    continue;
                }

                if (DesyncRegistration.IsTypeSafe(type) || DesyncRegistration.IsEntitySafe(entity)) {
                    continue;
                }

                anyUnsafe = true;
                break;
            }

            if (!anyUnsafe) {
                continue;
            }

            desyncDetail = $"the late check refused [{type.FullName}] in an earlier save";
            reason = $"Failed to Save: [{type.Name}] may lead to desync!";
            return true;
        }

        return false;
    }

    private static string NameOf(DesyncEcosystemKind kind) {
        return kind switch {
            DesyncEcosystemKind.LuaCutscenes => "LuaCutscenes",
            DesyncEcosystemKind.BossesHelper => "BossesHelper",
            DesyncEcosystemKind.FrostHelper => "FrostHelper",
            _ => kind.ToString(),
        };
    }

    internal static void Check(Entity obj) {
        if (!Enabled) {
            return;
        }

        // 在每个 Level 第一次 SL 时, CheckAll = true, 尽可能多地执行检查, 方便后续在 EarlyCheck 阶段就终止.
        if (desyncReason is not null && !CheckAll) {
            return;
        }

        // skipDesyncCheckTemporarily 只有此时才有意义 (现在这个检查是在 DeepClone 中做的, 所以没被其他地方跳过)
        if (!(StateManager.Instance.State == State.Saving && Thread.CurrentThread.IsMainThread() && shouldDesyncCheckTemporarily)) {
            return;
        }

        if (obj is null) {
            return;
        }

        Type entityType = obj.GetType();

        // Instance-level validation already happened in the ecosystem gate -> the whole type is
        // accounted for during this save (no blanket: this only applies after the gate passed).
        if (DesyncEcosystems.IsGovernedType(entityType)) {
            return;
        }

        // An explicit registration takes full responsibility for this instance (and its subtree).
        // Deliberately no KnownTypes write here: if the mod unregisters the type again, the next save
        // must go back to the fail-closed type walk instead of trusting a stale "safe" cache entry.
        if (DesyncRegistration.IsTypeSafe(entityType) || DesyncRegistration.IsEntitySafe(obj)) {
            return;
        }

        canSyncTypePath?.Clear();
        canSyncTypePath ??= [];
        unsafeLuaPath = null;

        bool typeSafe = CanSyncType(entityType, null);
        string detail = typeSafe ? null : (unsafeLuaPath ?? "unknown");

        if (typeSafe && !CanSyncComponents(obj, out Type unsafeComponentType, out string componentDetail)) {
            // 组件盲区：字段声明为 List<Component>/Component 时，声明类型里看不到组件内部的 Lua。
            detail = componentDetail;
            typeSafe = false;
            KnownTypes[unsafeComponentType] = false;
        }

        if (typeSafe) {
            return;
        }

        // It involves lua and we don't know whether it's safe to savestate, so refuse to savestate
        // (the state is cleared by OnAfterSaveState and the type is refused up front next time).
        Logger.Warn(LogTag,
            $"Entity [{entityType.FullName}] holds Lua state via [{detail ?? "unknown"}]; the savestate will be cleared. "
            + "If this state is known safe, the mod can register SpeedrunTool.Desync.AddSafeType/AddSafeEntityPredicate.");
        EarlyCheckEntityTypes.Add(entityType);
        // mark as safe coz it will be already checked in EarlyCheck
        KnownTypes[entityType] = true;
        desyncReason = entityType;
        desyncDetail = detail;
    }

    /// <summary>
    /// Checks the runtime types of the entity's components (the clone graph walks <c>Components</c>,
    /// so a mod component that holds Lua state would otherwise be invisible to the declared-type walk).
    /// </summary>
    private static bool CanSyncComponents(Entity entity, out Type unsafeComponentType, out string detail) {
        unsafeComponentType = null;
        detail = null;
        if (entity.Components is not { } components) {
            return true;
        }

        foreach (Component component in components) {
            if (component is null) {
                continue;
            }

            Type componentType = component.GetType();
            if (DesyncEcosystems.IsGovernedType(componentType) || DesyncRegistration.IsTypeSafe(componentType)) {
                continue;
            }

            canSyncTypePath?.Clear();
            unsafeLuaPath = null;
            if (CanSyncType(componentType, null)) {
                continue;
            }

            unsafeComponentType = componentType;
            detail = $"Components[{componentType.FullName}] -> {unsafeLuaPath ?? "unknown"}";
            return false;
        }

        return true;
    }

    private static bool CanSyncType(Type type, HashSet<Type> processingTypes) {
        // Registrations and passing ecosystem gates take precedence over cached negative decisions.
        if (DesyncRegistration.IsTypeSafe(type) || DesyncEcosystems.IsGovernedType(type)) {
            return true;
        }

        if (KnownTypes.TryGetValue(type, out bool isSafe)) {
            if (!isSafe) {
                unsafeLuaPath ??= FormatFieldPath(type);
            }
            return isSafe;
        }

        // 泛型参数/指针/byref 没有可检查的实例字段；不缓存结果
        if (type.IsGenericParameter || type.IsPointer || type.IsByRef) {
            return true;
        }

        processingTypes ??= [];
        processingTypes.Add(type);

        // 数组和泛型容器不会把元素类型暴露成字段：`LuaTable[]` / `List<LuaFunction>` /
        // `Dictionary<string, LuaTable>` 的实例字段只有数组/内部结构（数组类型没有实例字段），
        // 所以只看 GetFields 会把它们判成安全。这里显式展开元素类型与泛型实参。
        // 代价是保守误报：只在泛型实参里“提及” Lua 类型、但并不保存 Lua 状态的类型
        // （例如 Func<LuaTable>）也会被判为不安全；对 desync 检测而言宁可拒绝存档。
        if (type.IsArray) {
            PushFieldPath($"{type.Name}[element]");
            bool arraySafe = CanSyncType(type.GetElementType(), processingTypes);
            PopFieldPath();
            KnownTypes.TryAdd(type, arraySafe);
            return arraySafe;
        }

        if (type.IsGenericType) {
            foreach (Type argument in type.GetGenericArguments()) {
                if (argument.IsGenericParameter || processingTypes.Contains(argument)) {
                    continue;
                }
                PushFieldPath($"{type.Name}[{argument.Name}]");
                bool argumentSafe = CanSyncType(argument, processingTypes);
                PopFieldPath();
                if (!argumentSafe) {
                    KnownTypes.TryAdd(type, false);
                    return false;
                }
            }
        }

        List<FieldInfo> fi = [];
        Type tp = type;
        do {
            fi.AddRange(tp.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public));
            tp = tp.BaseType;
        } while (tp != null);

        foreach (FieldInfo fieldInfo in fi) {
            Type fieldType = fieldInfo.FieldType;
            if (processingTypes.Contains(fieldType)) {
                continue;
            }
            PushFieldPath($"{fieldInfo.DeclaringType?.Name}.{fieldInfo.Name}");
            bool fieldSafe = CanSyncType(fieldType, processingTypes);
            PopFieldPath();
            if (!fieldSafe) {
                KnownTypes.TryAdd(type, false);
                return false;
            }
        }

        KnownTypes.TryAdd(type, true);
        return true;
    }

    // LateCheck 只在主线程调用，路径栈用 [ThreadStatic] 防止意外跨线程复用。
    private static void PushFieldPath(string segment) {
        canSyncTypePath ??= [];
        canSyncTypePath.Add(segment);
    }

    private static void PopFieldPath() {
        if (canSyncTypePath is { Count: > 0 } path) {
            path.RemoveAt(path.Count - 1);
        }
    }

    private static string FormatFieldPath(Type leafType) {
        string leaf = leafType.FullName ?? leafType.Name;
        return canSyncTypePath is { Count: > 0 } path
            ? string.Join(" -> ", path) + " -> " + leaf
            : leaf;
    }
}
