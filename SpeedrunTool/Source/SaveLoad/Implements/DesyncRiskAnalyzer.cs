using Celeste.Mod.SpeedrunTool.Utils;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;

public static class ExternalSafeOracle {
    // each oracle returns if it thinks it's safe
    // contact me if you need an API to add your oracles
    public static readonly List<Func<Level, bool>> Oracles = [];

    internal static bool Consult(Level level) {
        foreach (Func<Level, bool> provider in Oracles) {
            if (provider(level)) {
                return true;
            }
        }
        return false;
    }

    public static readonly HashSet<string> SafeMaps = ["StrawberryJam2021/5-Grandmaster/maya"];

    [Initialize]
    private static void Initialize() {
        Oracles.Add(static (level) => SafeMaps.Contains(level.Session.Area.SID));
    }
}


internal static class DesyncRiskAnalyzer {

    // SaveInLuaCutscene only bypasses the cutscene-specific early checks now; disabling the
    // whole checker is a separate, explicit advanced option.
    private static bool Enabled => !ModSettings.DisableDesyncCheck;

    private static readonly HashSet<Type> EarlyCheckEntityTypes = [];

    private static readonly Dictionary<Type, Func<Level, Type, bool>> EarlyCheckSpecialHandlers = [];

    // similar to Force.DeepCloner DeepClonerSafeTypes. Used for late checks

    private static readonly ConcurrentDictionary<Type, bool> KnownTypes = new();

    internal static bool CheckAll = true;

    private static bool shouldDesyncCheckTemporarily = false;

    private static Type desyncReason;

    // LateCheck 的诊断信息：命中时记录「从实体类型到 Lua 类型」的字段路径，
    // 方便用户从日志里定位是哪个 mod 的哪个字段。只在主线程的 Check 中使用。
    [ThreadStatic] private static List<string> canSyncTypePath;
    [ThreadStatic] private static string unsafeLuaPath;

    // 我们把检测分为三个时期:
    // 1. VeryVeryEarlyCheck: 这一步检测本功能是否开启, 以及通过地图元数据等决定是否需要检测. 我们也允许外部在这一步注入检测
    // 2. EarlyCheck/BeforeSaveState: 检测有嫌疑的那些实体是否在关卡中
    // 3. AfterSaveState: 检测 DeepClone 时是否有某些实体的某个字段是 Lua 对象

    [Initialize]
    private static void Initialize() {
        desyncReason = null;

        // they are not in any EverestModule's assembly so ModUtils.GetAllTypes() don't include them
        KnownTypes.TryAdd(typeof(NLua.Lua), false);
        KnownTypes.TryAdd(typeof(KeraLua.Lua), false);
        foreach (Type type in typeof(NLua.LuaBase).Assembly.GetTypes()) {
            if (type.IsSubclassOf(typeof(NLua.LuaBase))) {
                KnownTypes.TryAdd(type, false);
            }
        }
        KnownTypes.TryAdd(typeof(LuaCoroutine), false);

        // almost everything relies on LuaCutsceneEntity so detecting the cutscene entity is enough
        if (ModUtils.GetAssembly("LuaCutscenes")?.GetTypes() is { } list && list.IsNotNullOrEmpty()) {
            foreach (Type type in list) {
                KnownTypes.TryAdd(type, true);
            }

            RegisterEarlyCheck(
                ModUtils.GetType("LuaCutscenes", "Celeste.Mod.LuaCutscenes.LuaCutsceneEntity"),
                (level, type) => {
                    if (ModSettings.SaveInLuaCutscene) {
                        return false;
                    }
                    if (level.InCutscene && level.onCutsceneSkip?.Target?.GetType() == type) {
                        return true;
                    }
                    if (level.Tracker.GetEntitiesTrackIfNeeded(type) is { } list && list.IsNotNullOrEmpty()) {
                        // 如果 cs 是 unskippable, 那么可能 Level 并不 InCutscene (而是在结束 Cutscene 的过程中)
                        foreach (Entity e in list) {
                            if (!e.GetFieldValue<bool>("Finished")) {
                                return true;
                            }
                        }
                    }
                    return false;
                }
            );

            // LuaTalker doesn't uses LuaCutsceneEntity
            // unskippable 的 talker 不会调用 Level.StartCutscene（见 LuaCutscenes 0.2.13 的
            // LuaTalker::onTalk），所以整个对话期间 level.InCutscene 都是 false：只看
            // InCutscene 会漏掉“对话进行中存档”。这里检查两层：
            //  1) 实体上有正在运行的 Coroutine（LuaCutsceneEntity/LuaTalker 用
            //     Add(new Coroutine(routine, true)) 驱动 Lua 协程；Finished == false 表示运行中）；
            //  2) onTalkRoutine 的状态机还没 finish。onTalkRoutine 在 onEnd 里不会被清空，
            //     所以必须看状态机状态（0/1/2/3 = 活动，-1 = 已结束），不能只看非 null。
            Type luaTalkerType = ModUtils.GetType("LuaCutscenes", "Celeste.Mod.LuaCutscenes.LuaTalker");
            FieldInfo onTalkRoutineField = luaTalkerType?.GetField("onTalkRoutine",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            RegisterEarlyCheck(
                luaTalkerType,
                (level, type) => {
                    if (ModSettings.SaveInLuaCutscene) {
                        return false;
                    }
                    if (level.InCutscene && level.onCutsceneSkip?.Target?.GetType() == type) {
                        return true;
                    }
                    if (level.Tracker.GetEntitiesTrackIfNeeded(type) is { } list && list.IsNotNullOrEmpty()) {
                        foreach (Entity e in list) {
                            if (e.Components is { } components) {
                                foreach (Component component in components) {
                                    if (component is Coroutine { Finished: false }) {
                                        return true;
                                    }
                                }
                            }
                            // onTalkRoutine 在 onEnd 里不会清空（LuaTalker::onEnd 只调
                            // onEndFunction + EndCutscene），对话结束后它仍是非 null。
                            if (onTalkRoutineField?.GetValue(e) is { } routine && IsLuaCoroutineRoutineActive(routine)) {
                                return true;
                            }
                        }
                    }
                    return false;
                }
            );
        }

        // almost everything relies on BossController
        if (ModUtils.GetAssembly("BossesHelper")?.GetTypes() is { } list2 && list2.IsNotNullOrEmpty()) {
            foreach (Type type in list2) {
                KnownTypes.TryAdd(type, true);
            }
            RegisterEarlyCheck(
                ModUtils.GetType("BossesHelper", "Celeste.Mod.BossesHelper.Code.Entities.BossController"),
                null
            );
        }

        static void RegisterEarlyCheck(Type type, Func<Level, Type, bool> func) {
            if (type is null) {
                return;
            }
            KnownTypes.TryAdd(type, true); // it will be checked in EarlyCheck, so it's always safe in LateCheck
            if (func is null) {
                if (!type.IsSubclassOf(typeof(Entity))) {
                    throw new Exception($"{nameof(DesyncRiskAnalyzer)}: {type} is not a subclass of Entity!");
                }
                EarlyCheckEntityTypes.Add(type);
                Tracker.AddTypeToTracker(type);
            }
            else {
                EarlyCheckSpecialHandlers[type] = func;
            }
        }
    }

    internal static bool GetAndRefreshIfDesyncCheck() {
        shouldDesyncCheckTemporarily = VeryVeryEarlyCheck();
        return shouldDesyncCheckTemporarily;

        // some maps are actually safe to save even though they have lua cutscene, in that case return false
        static bool VeryVeryEarlyCheck() {
            if (!Enabled || Engine.Scene is not Level level) {
                return false;
            }
            return !ExternalSafeOracle.Consult(level);
        }
    }

    internal static bool OnBeforeSaveState(Level level, out string popup) {
        if (EarlyCheckDesyncRisk(level, out string reason)) {
            popup = reason;
            return true;
        }
        popup = "";
        desyncReason = null;
        return false;
    }

    internal static bool OnAfterSaveState(out string popup) {
        CheckAll = false;
        bool b = desyncReason is not null;
        popup = b ? $"Saved but Cleared: [{desyncReason.Name ?? "UnknownEntity"}] may lead to desync!" : "";
        return b;
    }

    private static bool EarlyCheckDesyncRisk(Level level, out string reason) {
        foreach (KeyValuePair<Type, Func<Level, Type, bool>> pair in EarlyCheckSpecialHandlers) {
            Type type = pair.Key;
            if (pair.Value(level, type)) {
                reason = $"Failed to Save: [{type.Name}] may lead to desync!";
                return true;
            }
        }
        foreach (Type type in EarlyCheckEntityTypes) {
            if (level.Tracker.GetEntitiesTrackIfNeeded(type) is { } list && list.IsNotNullOrEmpty()) {
                reason = $"Failed to Save: [{type.Name ?? "UnknownEntity"}] may lead to desync!";
                return true;
            }
        }
        reason = "";
        return false;
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
        if (obj.GetType() is not { } entityType) {
            return;
        }
        canSyncTypePath?.Clear();
        canSyncTypePath ??= [];
        unsafeLuaPath = null;
        if (CanSyncType(entityType, null)) {
            return;
        }
        // it involves lua and we don't know whether it's safe to savestate, so we refuse to savestate
        Logger.Warn("SpeedrunTool/DesyncRiskAnalyzer",
            $"Entity [{entityType.FullName}] holds Lua state via [{unsafeLuaPath ?? "unknown"}]; the savestate will be cleared.");
        EarlyCheckEntityTypes.Add(entityType);
        // mark as safe coz it will be already checked in EarlyCheck
        KnownTypes[entityType] = true;
        desyncReason = entityType;
    }

    /// <summary>
    /// True when a LuaCutscenes <c>LuaCoroutineToIEnumerator</c> state machine is still alive.
    /// States: 0 = not started, 1 = waiting (float/int64 yield), 2 = yielded object,
    /// 3 = inner routine finished but the outer wrapper has not consumed it yet; -1 = finished.
    /// Falls back to Celeste.Mod.LuaCoroutine._Valid if the compiler-generated state field is
    /// missing (newer LuaCutscenes / different compiler), and conservatively returns true when
    /// no state can be inspected at all (do not silently allow a live Lua coroutine).
    /// </summary>
    private static bool IsLuaCoroutineRoutineActive(object routine) {
        Type routineType = routine.GetType();
        FieldInfo stateField = routineType.GetField("<>1__state",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (stateField is not null && stateField.FieldType == typeof(int)) {
            return (int)stateField.GetValue(routine) >= 0;
        }

        FieldInfo luaCoroutineField = routineType.GetField("routine",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        object luaCoroutine = luaCoroutineField?.GetValue(routine);
        FieldInfo validField = luaCoroutine?.GetType().GetField("_Valid",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (validField is not null && validField.FieldType == typeof(bool)) {
            return (bool)validField.GetValue(luaCoroutine);
        }

        return true;
    }

    private static bool CanSyncType(Type type, HashSet<Type> processingTypes) {
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