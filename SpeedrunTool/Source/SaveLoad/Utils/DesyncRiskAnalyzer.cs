using Celeste.Mod.SpeedrunTool.Utils;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Utils;

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

    private static bool Enabled => !ModSettings.SaveInLuaCutscene;

    private static readonly HashSet<Type> EarlyCheckTypes = [];

    private static readonly Dictionary<Type, Func<Entity, bool>> EarlyCheckSpecialHandlers = []; // returns if the entity is actively working

    // similar to Force.DeepCloner DeepClonerSafeTypes. Used for late checks

    private static readonly ConcurrentDictionary<Type, bool> KnownTypes = new();

    internal static bool CheckAll = true;

    private static bool shouldDesyncCheckTemporarily = false;

    private static Type desyncReason;

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
        }
        RegisterEarlyCheck(
            ModUtils.GetType("LuaCutscenes", "Celeste.Mod.LuaCutscenes.LuaCutsceneEntity"),
            e => e is CutsceneEntity cs && cs.Running
        );

        // TODO: support boss helper here

        static void RegisterEarlyCheck(Type type, Func<Entity, bool> func) {
            if (type is null || !type.IsSubclassOf(typeof(Entity))) {
                return;
            }
            KnownTypes.TryAdd(type, true); // it will be checked in EarlyCheck, so it's always safe in LateCheck
            EarlyCheckTypes.Add(type);
            if (func is not null) {
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
        foreach (Type type in EarlyCheckTypes) {
            if (level.Tracker.GetEntitiesTrackIfNeeded(type) is { } list && list.IsNotNullOrEmpty()) {
                bool has = false;
                if (EarlyCheckSpecialHandlers.TryGetValue(type, out Func<Entity, bool> handler)) {
                    foreach (Entity e in list) {
                        if (handler(e)) {
                            has = true;
                            break;
                        }
                    }
                }
                else {
                    has = true;
                }
                if (has) {
                    reason = $"Failed to Save: [{type.Name ?? "UnknownEntity"}] may lead to desync!";
                    return true;
                }
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
        if (!(StateManager.Instance.State == State.Saving && Thread.CurrentThread.IsMainThread() && !shouldDesyncCheckTemporarily)) {
            return;
        }
        if (obj.GetType() is not { } entityType || CanSyncType(entityType, null)) {
            return;
        }
        // it involves lua and we don't know whether it's safe to savestate, so we refuse to savestate
        EarlyCheckTypes.Add(entityType);
        // mark as safe coz it will be already checked in EarlyCheck
        KnownTypes[entityType] = true;
        desyncReason = entityType;
    }

    private static bool CanSyncType(Type type, HashSet<Type> processingTypes) {
        if (KnownTypes.TryGetValue(type, out bool isSafe)) {
            return isSafe;
        }

        processingTypes ??= [];
        processingTypes.Add(type);

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
            if (!CanSyncType(fieldType, processingTypes)) {
                KnownTypes.TryAdd(type, false);
                return false;
            }
        }

        KnownTypes.TryAdd(type, true);
        return true;
    }
}