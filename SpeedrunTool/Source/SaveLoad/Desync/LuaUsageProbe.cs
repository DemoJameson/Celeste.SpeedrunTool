using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;

/// <summary>
/// Generic "is Lua currently executing in this level?" probe.
///
/// It scans every live <see cref="Coroutine"/> component and recursively inspects the private
/// <c>Monocle.Coroutine.enumerators</c> stack (reflection, cached). A live stack may contain:
/// <list type="bullet">
/// <item>a <c>Celeste.Mod.LuaCoroutine</c> or a mod subclass (e.g. BossesHelper's
/// <c>LuaProxyCoroutine</c>) directly; and/or</item>
/// <item>a boxed <c>Celeste.Mod.SwapImmediatelyExtension+Flattened</c> struct that Monocle pushes to
/// flatten nested enumerators (its private <c>enums</c> stack holds the real enumerators).</item>
/// </list>
/// A Lua drive found in a live stack is by definition not finished, so it means Lua VM state will
/// advance past the snapshot point: saving then would share an already-advanced Lua state on load.
///
/// The probe also catches <c>LuaHelper.LuaCoroutineToIEnumerator</c> style wrappers when the Lua
/// coroutine shows up in the flattened stack; wrappers that hide the coroutine in a state-machine
/// field only (e.g. LuaCutscenes' <c>onBeginWrapper</c>) are covered by the LuaCutscenes at-rest gate
/// in <see cref="DesyncEcosystems"/>, not by this generic probe.
///
/// Decision logic lives in <see cref="DesyncCheckLogic.TryFindBlockingLuaExecution"/> so it can be
/// unit tested offline. This file only collects evidence.
/// </summary>
internal static class LuaUsageProbe {
    private const int MaxNestingDepth = 8;
    private const BindingFlags InstanceAnyVisibility = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static FieldInfo coroutineEnumeratorsField;
    private static Type flattenedType;
    private static FieldInfo flattenedEnumeratorsField;
    private static bool initialized;
    private static bool reflectionUnavailable;

    internal static void Initialize() {
        initialized = true;
        reflectionUnavailable = false;
        coroutineEnumeratorsField = null;
        flattenedType = null;
        flattenedEnumeratorsField = null;

        try {
            coroutineEnumeratorsField = typeof(Coroutine).GetField("enumerators", InstanceAnyVisibility);
            // Resolve by name instead of a compile reference: Flattened is a nested compiler helper of
            // Celeste.dll and we only need it by reflection.
            flattenedType = typeof(Coroutine).Assembly.GetType("Celeste.Mod.SwapImmediatelyExtension+Flattened");
            flattenedEnumeratorsField = flattenedType?.GetField("enums", InstanceAnyVisibility);
        }
        catch (Exception exception) {
            Logger.Warn("SpeedrunTool/DesyncRiskAnalyzer", $"LuaUsageProbe reflection setup failed: {exception.Message}");
        }

        if (coroutineEnumeratorsField is null || flattenedType is null || flattenedEnumeratorsField is null) {
            reflectionUnavailable = true;
            Logger.Error("SpeedrunTool/DesyncRiskAnalyzer",
                "LuaUsageProbe cannot inspect Monocle.Coroutine internals (Celeste version changed?); "
                + "the desync check will fail closed; the save will be refused.");
        }
    }

    /// <summary>
    /// Main thread, once per save (called from <c>DesyncRiskAnalyzer.OnBeforeSaveState</c>).
    /// Returns true when Lua is executing and the save must be refused.
    /// </summary>
    internal static bool TryFindExecutingLua(Level level, bool saveInLuaCutscene, out string detail) {
        detail = null;
        if (!initialized) {
            Initialize();
        }

        if (reflectionUnavailable) {
            // Fail closed: without the enumerator stack we cannot prove that no Lua coroutine is running.
            detail = "Cannot inspect Monocle.Coroutine.enumerators (reflection unavailable); refusing to save (fail-closed)";
            Logger.Warn("SpeedrunTool/DesyncRiskAnalyzer", detail);
            return true;
        }

        if (level is null) {
            return false;
        }

        List<LuaDriveSnapshot> snapshots = [];
        try {
            foreach (Entity entity in level.Entities) {
                ScanCoroutineComponents(entity, snapshots);
            }

            // Components added to the scene itself live on Scene.HelperEntity (not in Entities).
            if (level.HelperEntity is { } helperEntity) {
                ScanCoroutineComponents(helperEntity, snapshots);
            }
        }
        catch (Exception exception) {
            // A broken level graph must not fail open (and must not crash the save with an exception
            // from the checker): refuse the save and log.
            Logger.Warn("SpeedrunTool/DesyncRiskAnalyzer", $"LuaUsageProbe failed while scanning the level: {exception}");
            detail = $"LuaUsageProbe failed while scanning the level: {exception.Message}";
            return true;
        }

        if (!DesyncCheckLogic.TryFindBlockingLuaExecution(snapshots, saveInLuaCutscene, out string blocker)) {
            return false;
        }

        detail = $"{blocker}; the Lua VM state would advance past the savestate point";
        Logger.Warn("SpeedrunTool/DesyncRiskAnalyzer", detail);
        return true;
    }

    private static void ScanCoroutineComponents(Entity entity, List<LuaDriveSnapshot> snapshots) {
        if (entity?.Components is not { } components) {
            return;
        }

        Type ownerType = entity.GetType();
        bool ownerIsCutscene = DesyncEcosystems.IsCutsceneOwnedType(ownerType);
        bool ownerDeclaredSafe = false;
        bool ownerChecked = false;

        foreach (Component component in components) {
            if (component is null) {
                continue;
            }

            List<LuaDriveLeaf> leaves = CollectLuaDrives(component);
            if (leaves is null) {
                continue;
            }

            if (!ownerChecked) {
                ownerChecked = true;
                ownerDeclaredSafe = DesyncRegistration.IsTypeSafe(ownerType) || DesyncRegistration.IsEntitySafe(entity);
            }

            snapshots.Add(new LuaDriveSnapshot(ownerType.Name, ownerIsCutscene, ownerDeclaredSafe, leaves));
        }
    }

    /// <summary>Returns the Lua drives found in this component, or null when the component drives no Lua.</summary>
    private static List<LuaDriveLeaf> CollectLuaDrives(Component component) {
        // Celeste.Mod.LuaCoroutine is an IEnumerator, not a Component: Lua execution is always driven
        // by a Monocle.Coroutine component (directly, or through a state machine that yields it).
        if (component is not Coroutine coroutine) {
            return null;
        }

        // A finished Coroutine is not updated anymore: it cannot advance Lua. This also guards against
        // stale components left on detached entities (see lua-work docs/checker-lua-gates.md §1/§2.4).
        if (coroutine.Finished) {
            return null;
        }

        if (coroutineEnumeratorsField.GetValue(coroutine) is not Stack<IEnumerator> stack) {
            return null;
        }

        List<LuaDriveLeaf> leaves = [];
        WalkStack(stack, leaves, depth: 0, visited: new HashSet<object>(ReferenceEqualityComparer.Instance));
        return leaves.Count > 0 ? leaves : null;
    }

    private static void WalkStack(Stack<IEnumerator> stack, List<LuaDriveLeaf> leaves, int depth, HashSet<object> visited) {
        if (stack is null || depth > MaxNestingDepth) {
            return;
        }

        foreach (IEnumerator enumerator in stack) {
            WalkEnumerator(enumerator, leaves, depth, visited);
        }
    }

    private static void WalkEnumerator(IEnumerator enumerator, List<LuaDriveLeaf> leaves, int depth, HashSet<object> visited) {
        if (enumerator is null || !visited.Add(enumerator)) {
            return;
        }

        if (enumerator is LuaCoroutine luaCoroutine) {
            // Still in a live enumerator stack => not finished (finished enumerators are popped).
            leaves.Add(new LuaDriveLeaf(TypeName(luaCoroutine.GetType()), finished: false));
            return;
        }

        Type type = enumerator.GetType();
        if (flattenedType is not null && (type == flattenedType || flattenedType.IsAssignableFrom(type))) {
            if (flattenedEnumeratorsField?.GetValue(enumerator) is Stack<IEnumerator> inner) {
                WalkStack(inner, leaves, depth + 1, visited);
            }
        }
    }

    private static string TypeName(Type type) {
        return type.FullName ?? type.Name;
    }
}
