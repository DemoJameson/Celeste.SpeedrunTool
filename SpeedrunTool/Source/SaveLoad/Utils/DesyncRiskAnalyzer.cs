using Celeste.Mod.SpeedrunTool.Utils;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Utils;

internal static class DesyncRiskAnalyzer {

    private static bool Enabled => !ModSettings.SaveInLuaCutscene;

    // similar to Force.DeepCloner DeepClonerSafeTypes

    private static readonly ConcurrentDictionary<Type, bool> KnownTypes = new ConcurrentDictionary<Type, bool>();

    private static readonly HashSet<Type> DesyncEntityTypes = [];

    private static readonly Dictionary<Type, Func<Entity, bool>> SpecialHandlers = new(); // returns if the entity is actively working

    internal static bool CheckAll = true;

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
        Register(
            ModUtils.GetType("LuaCutscenes", "Celeste.Mod.LuaCutscenes.LuaCutsceneEntity"),
            false,
            e => e is CutsceneEntity cs && cs.Running
        );
        if (ModUtils.GetAssembly("LuaCutscenes")?.GetTypes() is { } list && list.IsNotNullOrEmpty()) {
            foreach (Type type in list) {
                KnownTypes.TryAdd(type, true);
            }
        }

        static void Register(Type type, bool sync, Func<Entity, bool> func) {
            if (type is null) {
                return;
            }
            KnownTypes.TryAdd(type, sync);
            if (!sync && type.IsSubclassOf(typeof(Entity))) {
                DesyncEntityTypes.Add(type);
                if (func is not null) {
                    SpecialHandlers[type] = func;
                }
            }
        }
    }

    private static Type desyncReason;

    public static void OnBeforeSaveState() {
        desyncReason = null;
    }

    public static void OnAfterSaveState() {
        CheckAll = false;
    }

    public static bool EarlyCheckDesyncRisk(Level level, out string reason) {
        if (!Enabled) {
            reason = "";
            return false;
        }

        foreach (Type type in DesyncEntityTypes) {
            if (level.Tracker.GetEntitiesTrackIfNeeded(type) is { } list && list.IsNotNullOrEmpty()) {
                bool has = false;
                if (SpecialHandlers.TryGetValue(type, out Func<Entity, bool> handler)) {
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
                    reason = $"Failed to Save: [{type}] may lead to desync!";
                    return true;
                }
            }
        }
        reason = "";
        return false;
    }

    public static bool LateCheckDesyncRisk(out string reason) {
        if (!Enabled) {
            reason = "";
            return false;
        }

        bool b = desyncReason is not null;
        reason = b ? $"Saved but Cleared: [{desyncReason}] may lead to desync!" : "";
        return b;
    }


    internal static void Check(Entity obj) {
        if (!Enabled) {
            return;
        }
        if (!CheckAll && desyncReason is not null) {
            return;
        }
        if (StateManager.Instance.State != State.Saving || !Thread.CurrentThread.IsMainThread()) {
            return;
        }
        if (obj.GetType() is not { } type || CanSyncType(type, null)) {
            return;
        }
        //if (obj is not Entity e) {
        //    desyncReason = type;
        //    return;
        //}
        DesyncEntityTypes.Add(type);
        if (SpecialHandlers.TryGetValue(type, out Func<Entity, bool> handler) && !handler(obj)) {
            return;
        }
        // otherwise, it involves lua and we don't know whether it's safe to savestate, so we refuse to savestate
        desyncReason = type;
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