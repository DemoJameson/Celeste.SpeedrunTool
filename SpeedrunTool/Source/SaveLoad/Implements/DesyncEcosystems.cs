using Celeste.Mod.SpeedrunTool.Utils;
using System.Collections;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;

/// <summary>
/// Built-in, instance-level ("at rest") checks for the three ecosystems that are known to host Lua
/// state: LuaCutscenes, BossesHelper and FrostHelper.
///
/// The old checker marked the whole LuaCutscenes/BossesHelper assemblies as safe and unconditionally
/// refused saves while a BossController existed. This replaces that blanket with:
/// <list type="bullet">
/// <item>a per-save, per-ecosystem gate that inspects the actual instances in the level;</item>
/// <item>lazy type governing: only an ecosystem whose gate passed may shortcut the late check for
/// the concrete Lua-holding entity types it owns;</item>
/// <item>fail-closed behaviour when a type/field cannot be resolved or read.</item>
/// </list>
///
/// Reflective field/member names were verified against the installed DLLs with Cecil; the inventory
/// and its provenance are written down in docs/desync-checker.md (maintenance: re-run the inventory
/// when one of the mods updates).
///
/// Policy status after bench: the LuaCutscenes instance-level gate is active (E1/E2/E3 verified),
/// while the BossesHelper / FrostHelper at-rest allow-list stays behind a disabled flag because the
/// lua-work E5/E5b adapters that clone/restore the mods' Lua state are not ported into this branch.
/// Until they are, a boss instance keeps refusing the save exactly like the old checker.
/// </summary>
internal static class DesyncEcosystems {
    private static readonly LuaCutscenesEcosystem luaCutscenes = new();
    private static readonly BossesHelperEcosystem bossesHelper = new();
    private static readonly FrostHelperEcosystem frostHelper = new();

    private static readonly EcosystemBase[] ecosystems = [luaCutscenes, bossesHelper, frostHelper];

    private static EcosystemGateResult[] lastResults;
    private static readonly Dictionary<Type, bool> governedTypeCache = new();

    internal static void Initialize() {
        lastResults = null;
        governedTypeCache.Clear();
        foreach (EcosystemBase ecosystem in ecosystems) {
            ecosystem.Resolve();
        }
    }

    /// <summary>Main thread, once per save. Evaluates all gates and caches the outcome for the late check.</summary>
    internal static IReadOnlyList<EcosystemGateResult> EvaluateAtRest(Level level) {
        governedTypeCache.Clear();
        EcosystemGateResult[] results = new EcosystemGateResult[ecosystems.Length];
        for (int i = 0; i < ecosystems.Length; i++) {
            try {
                results[i] = ecosystems[i].Evaluate(level) ?? ecosystems[i].UnavailableResult("unknown evaluation failure");
            }
            catch (Exception exception) {
                // Never let an ecosystem probe crash the save; treat it as uninspectable (fail-closed:
                // its types are not governed, so the late check refuses them if they hold Lua state).
                Logger.Warn("SpeedrunTool/DesyncRiskAnalyzer",
                    $"{ecosystems[i].ModName} at-rest evaluation failed: {exception}");
                results[i] = ecosystems[i].UnavailableResult(exception.Message);
            }
        }

        lastResults = results;
        return results;
    }

    /// <summary>Drops per-save state when the checker is disabled for this save.</summary>
    internal static void ResetPerSaveState() {
        lastResults = null;
        governedTypeCache.Clear();
    }

    /// <summary>
    /// True when this type belongs to an ecosystem whose at-rest gate passed for the current save, so
    /// its Lua-holding fields are accounted for by the instance-level check (and the late check may
    /// skip it). Unknown types are never governed -> the late check stays fail-closed.
    /// </summary>
    internal static bool IsGovernedType(Type type) {
        if (type is null) {
            return false;
        }

        if (governedTypeCache.TryGetValue(type, out bool cached)) {
            return cached;
        }

        bool governed = false;
        if (lastResults is not null) {
            foreach (EcosystemGateResult result in lastResults) {
                if (!result.IsGoverned) {
                    continue;
                }

                if (ecosystems[(int)result.Kind].Owns(type)) {
                    governed = true;
                    break;
                }
            }
        }

        governedTypeCache[type] = governed;
        return governed;
    }

    internal static bool IsCutsceneOwnedType(Type type) {
        return type is not null && luaCutscenes.Owns(type);
    }

    /// <summary>
    /// A registered safe type/entity predicate (or the "Save in Lua cutscene" setting for LuaCutscenes)
    /// overrides an Unsafe/Unavailable gate: treat the ecosystem as verified for this save.
    /// </summary>
    internal static void AcceptOverride(DesyncEcosystemKind kind) {
        if (lastResults is null) {
            return;
        }

        foreach (EcosystemGateResult result in lastResults) {
            if (result.Kind != kind) {
                continue;
            }

            if (result.State is EcosystemGateState.Safe) {
                return;
            }

            result.State = EcosystemGateState.Safe;
            result.Detail = (result.Detail is null ? "" : result.Detail + "; ") + "overridden by SpeedrunTool.Desync registration";
            governedTypeCache.Clear();
            return;
        }
    }
}

internal enum DesyncEcosystemKind {
    LuaCutscenes = 0,
    BossesHelper = 1,
    FrostHelper = 2,
}

internal enum EcosystemGateState {
    NotInstalled = 0,
    Unavailable = 1,
    Safe = 2,
    Unsafe = 3,
}

/// <summary>Outcome of one ecosystem's at-rest gate for the current save.</summary>
internal sealed class EcosystemGateResult {
    internal DesyncEcosystemKind Kind;
    internal EcosystemGateState State;
    internal Entity Offender;
    internal string EntityTypeName;
    internal string Detail;

    internal bool IsGoverned => State is EcosystemGateState.Safe;
}

internal abstract class EcosystemBase {
    internal abstract DesyncEcosystemKind Kind { get; }
    internal abstract string ModName { get; }

    internal bool Installed { get; set; }
    internal bool Resolved { get; set; }

    /// <summary>Resolves the mod assembly and the concrete members used by the gate.</summary>
    internal abstract void Resolve();

    /// <summary>True when the late check should treat this type as accounted for by the gate.</summary>
    internal abstract bool Owns(Type type);

    /// <summary>Instance-level at-rest evaluation for the current level.</summary>
    internal abstract EcosystemGateResult Evaluate(Level level);

    internal EcosystemGateResult UnavailableResult(string detail) {
        return new EcosystemGateResult { Kind = Kind, State = EcosystemGateState.Unavailable, Detail = detail };
    }

    protected EcosystemGateResult NotInstalledResult() {
        return new EcosystemGateResult { Kind = Kind, State = EcosystemGateState.NotInstalled };
    }

    protected EcosystemGateResult SafeResult() {
        return new EcosystemGateResult { Kind = Kind, State = EcosystemGateState.Safe };
    }

    protected EcosystemGateResult UnsafeResult(Entity offender, string entityTypeName, string detail) {
        return new EcosystemGateResult {
            Kind = Kind,
            State = EcosystemGateState.Unsafe,
            Offender = offender,
            EntityTypeName = entityTypeName,
            Detail = detail,
        };
    }

    protected static bool HasUnfinishedCoroutine(Entity entity) {
        if (entity?.Components is not { } components) {
            return false;
        }

        foreach (Component component in components) {
            if (component is Coroutine { Finished: false }) {
                return true;
            }
        }

        return false;
    }

    /// <summary>First instance of <paramref name="type"/> that is not covered by a Desync registration.</summary>
    protected static Entity FirstInspectableEntityOfType(Level level, Type type) {
        if (level?.Entities is not { } entities) {
            return null;
        }

        foreach (Entity entity in entities) {
            if (entity is not null && type.IsInstanceOfType(entity) && !IsInstanceDeclaredSafe(entity)) {
                return entity;
            }
        }

        return null;
    }

    protected static bool TryReadField(FieldInfo field, object target, out object value) {
        value = null;
        if (field is null || target is null) {
            return false;
        }

        try {
            value = field.GetValue(target);
            return true;
        }
        catch (Exception exception) {
            Logger.Warn("SpeedrunTool/DesyncRiskAnalyzer", $"Failed to read {field.DeclaringType?.Name}.{field.Name}: {exception.Message}");
            return false;
        }
    }

    protected static bool? ReadBool(FieldInfo field, object target) {
        if (!TryReadField(field, target, out object value)) {
            return null;
        }

        return value is bool flag ? flag : null;
    }

    /// <summary>
    /// A SpeedrunTool.Desync registration (safe type, entity predicate or Lua state provider) takes
    /// full responsibility for this instance: the built-in at-rest rule is skipped for it. Other
    /// instances of the same ecosystem are still checked.
    /// </summary>
    protected static bool IsInstanceDeclaredSafe(Entity entity) {
        return entity is not null
               && (DesyncRegistration.IsTypeSafe(entity.GetType()) || DesyncRegistration.IsEntitySafe(entity));
    }
}

internal sealed class LuaCutscenesEcosystem : EcosystemBase {
    internal override DesyncEcosystemKind Kind => DesyncEcosystemKind.LuaCutscenes;
    internal override string ModName => "LuaCutscenes";

    private Type entityType;
    private Type talkerType;
    private Type triggerType;
    private FieldInfo finishedField;
    private FieldInfo triggerEntityField;
    private FieldInfo talkerOnTalkRoutineField;
    private FieldInfo luaCoroutineValidField;
    private Type[] ownedTypes = [];

    internal override void Resolve() {
        entityType = null;
        talkerType = null;
        triggerType = null;
        finishedField = null;
        triggerEntityField = null;
        talkerOnTalkRoutineField = null;
        luaCoroutineValidField = null;
        ownedTypes = [];
        Installed = false;
        Resolved = false;

        if (ModUtils.GetAssembly(ModName) is null) {
            return;
        }

        Installed = true;
        entityType = ModUtils.GetType(ModName, "Celeste.Mod.LuaCutscenes.LuaCutsceneEntity");
        talkerType = ModUtils.GetType(ModName, "Celeste.Mod.LuaCutscenes.LuaTalker");
        triggerType = ModUtils.GetType(ModName, "Celeste.Mod.LuaCutscenes.LuaCutsceneTrigger");

        if (entityType is null) {
            Logger.Warn("SpeedrunTool/DesyncRiskAnalyzer",
                "LuaCutscenes is installed but LuaCutsceneEntity could not be resolved; its Lua state cannot be inspected (fail-closed).");
            return;
        }

        finishedField = entityType.GetFieldInfo("Finished");
        triggerEntityField = triggerType?.GetFieldInfo("cutsceneEntity");
        // Bench (SJ tofu [Atrium]): a LuaTalker can run its Lua routine while InCutscene == false and
        // without a Monocle.Coroutine component, so the coroutine scan misses it. Inspect the
        // onTalkRoutine state machine directly instead (see GetTalkRoutineState).
        talkerOnTalkRoutineField = talkerType?.GetFieldInfo("onTalkRoutine");
        luaCoroutineValidField = typeof(LuaCoroutine).GetFieldInfo("_Valid");
        if (finishedField is null) {
            Logger.Warn("SpeedrunTool/DesyncRiskAnalyzer",
                "LuaCutscenes.LuaCutsceneEntity.Finished is missing; instances will fail closed.");
        }
        if (triggerType is not null && triggerEntityField is null) {
            Logger.Warn("SpeedrunTool/DesyncRiskAnalyzer",
                "LuaCutscenes.LuaCutsceneTrigger.cutsceneEntity is missing; instances will fail closed.");
        }
        if (talkerType is not null && talkerOnTalkRoutineField is null) {
            Logger.Warn("SpeedrunTool/DesyncRiskAnalyzer",
                "LuaCutscenes.LuaTalker.onTalkRoutine is missing; talker instances will fail closed.");
        }

        ownedTypes = new[] { entityType, talkerType, triggerType }.Where(type => type is not null).ToArray();
        Resolved = true;
    }

    internal override bool Owns(Type type) {
        foreach (Type ownedType in ownedTypes) {
            if (type == ownedType || type.IsSubclassOf(ownedType)) {
                return true;
            }
        }

        return false;
    }

    internal override EcosystemGateResult Evaluate(Level level) {
        if (!Installed) {
            return NotInstalledResult();
        }

        if (!Resolved || entityType is null) {
            return UnavailableResult("LuaCutscenes is installed but its entity types could not be resolved");
        }

        if (finishedField is null) {
            return FirstInspectableEntityOfType(level, entityType) is { } uninspectable
                ? UnsafeResult(uninspectable, uninspectable.GetType().Name,
                    "LuaCutscenes.LuaCutsceneEntity.Finished is missing; cannot prove the cutscene is at rest (fail-closed)")
                : UnavailableResult("LuaCutscenes.LuaCutsceneEntity.Finished is missing");
        }

        // Same level-level signal the old early check used: a skippable cutscene currently driven by
        // a LuaCutscenes entity.
        Type skipTargetType = level.InCutscene ? level.onCutsceneSkip?.Target?.GetType() : null;
        bool drivenByEntity = skipTargetType is not null && entityType.IsAssignableFrom(skipTargetType);
        bool drivenByTalker = skipTargetType is not null && talkerType is not null && talkerType.IsAssignableFrom(skipTargetType);

        bool drivingInstanceDeclaredSafe = false;
        foreach (Entity entity in level.Entities) {
            if (entity is null) {
                continue;
            }

            Type runtimeType = entity.GetType();
            if (entityType.IsAssignableFrom(runtimeType)) {
                // A SpeedrunTool.Desync registration takes full responsibility for this instance;
                // other instances are still checked.
                if (IsInstanceDeclaredSafe(entity)) {
                    if (drivenByEntity) {
                        drivingInstanceDeclaredSafe = true;
                    }
                    continue;
                }

                bool? finished = ReadBool(finishedField, entity);
                // Bench E1: only Finished decides - a finished entity must not be refused because a
                // wrapper Coroutine is still around (OnEnd already ran, Lua no longer executes).
                if (!DesyncCheckLogic.LuaCutsceneEntity(finished, drivenByEntity).IsSafe()) {
                    return UnsafeResult(entity, runtimeType.Name,
                        $"LuaCutsceneEntity is not at rest (Finished={Describe(finished)}, drivenByLevelCutscene={drivenByEntity})");
                }
            }
            else if (talkerType is not null && talkerType.IsAssignableFrom(runtimeType)) {
                if (IsInstanceDeclaredSafe(entity)) {
                    if (drivenByTalker) {
                        drivingInstanceDeclaredSafe = true;
                    }
                    continue;
                }

                bool hasUnfinishedCoroutine = HasUnfinishedCoroutine(entity);
                TalkRoutineState talkRoutineState = GetTalkRoutineState(entity);
                if (!DesyncCheckLogic.LuaTalker(drivenByTalker, hasUnfinishedCoroutine, talkRoutineState).IsSafe()) {
                    return UnsafeResult(entity, runtimeType.Name,
                        $"LuaTalker is not at rest (drivenByLevelCutscene={drivenByTalker}, "
                        + $"hasUnfinishedCoroutine={hasUnfinishedCoroutine}, talkRoutine={talkRoutineState})");
                }
            }
            else if (triggerType is not null && triggerType.IsAssignableFrom(runtimeType)) {
                if (IsInstanceDeclaredSafe(entity)) {
                    continue;
                }

                if (triggerEntityField is null) {
                    return UnsafeResult(entity, runtimeType.Name,
                        "LuaCutsceneTrigger.cutsceneEntity is missing; cannot prove the referenced cutscene is at rest (fail-closed)");
                }

                if (!TryReadField(triggerEntityField, entity, out object referencedValue)) {
                    return UnsafeResult(entity, runtimeType.Name,
                        "LuaCutsceneTrigger.cutsceneEntity could not be read (fail-closed)");
                }

                if (referencedValue is not Entity referenced) {
                    continue;
                }

                if (IsInstanceDeclaredSafe(referenced)) {
                    continue;
                }

                // The trigger may keep a reference to an entity that is no longer in level.Entities;
                // validate it as well instead of trusting the trigger blindly. Bench E1: a finished
                // entity with a lingering wrapper Coroutine must not be refused (see LuaCutsceneEntity).
                bool? finished = ReadBool(finishedField, referenced);
                if (!DesyncCheckLogic.LuaCutsceneEntity(finished, drivenByEntity).IsSafe()) {
                    return UnsafeResult(referenced, referenced.GetType().Name,
                        $"LuaCutsceneTrigger holds a LuaCutsceneEntity that is not at rest (Finished={Describe(finished)})");
                }
            }
        }

        // The level is inside a Lua cutscene whose driving instance is no longer in level.Entities
        // (or the driver was not covered by a registration): refuse, like the old early check did.
        if ((drivenByEntity || drivenByTalker) && !drivingInstanceDeclaredSafe) {
            Type drivingType = drivenByEntity ? entityType : talkerType;
            return UnsafeResult(null, drivingType.Name,
                $"level.InCutscene is driven by [{drivingType.Name}]; the Lua cutscene is still running");
        }

        return SafeResult();
    }

    /// <summary>
    /// Introspects <c>LuaTalker.onTalkRoutine</c> without trusting the coroutine-component scan.
    /// The field holds a compiler-generated iterator
    /// (<c>LuaHelper+&lt;LuaCoroutineToIEnumerator&gt;d__2</c>): <c>&lt;&gt;1__state</c> is 0 before the
    /// first step, 1/2/3 while suspended, -1 after it completes. Only -1 (or a null field) proves the
    /// Lua routine no longer advances; everything unreadable is fail-closed (<see cref="TalkRoutineState.Unknown"/>).
    /// Fallback for a different wrapper type: inspect a field named <c>routine</c> that holds a
    /// <see cref="LuaCoroutine"/>; <c>_Valid</c> == true means it is mid-execution.
    /// </summary>
    private TalkRoutineState GetTalkRoutineState(Entity talker) {
        if (talkerOnTalkRoutineField is null) {
            return TalkRoutineState.Unknown; // can't inspect -> fail closed
        }

        if (!TryReadField(talkerOnTalkRoutineField, talker, out object routine)) {
            return TalkRoutineState.Unknown;
        }

        if (routine is null) {
            return TalkRoutineState.None;
        }

        Type routineType = routine.GetType();
        FieldInfo stateField = routineType.GetFieldInfo("<>1__state");
        if (stateField is not null && TryReadField(stateField, routine, out object stateValue) && stateValue is int state) {
            return state == -1 ? TalkRoutineState.Finished : TalkRoutineState.Running;
        }

        FieldInfo luaCoroutineField = routineType.GetFieldInfo("routine");
        if (luaCoroutineField is not null
            && TryReadField(luaCoroutineField, routine, out object luaValue)
            && luaValue is LuaCoroutine luaCoroutine
            && luaCoroutineValidField is not null
            && TryReadField(luaCoroutineValidField, luaCoroutine, out object validValue)
            && validValue is bool valid) {
            // _Valid == true: the last resume yielded -> still running. false is ambiguous
            // (not started vs finished), so only the true case is conclusive here.
            return valid ? TalkRoutineState.Running : TalkRoutineState.Unknown;
        }

        return TalkRoutineState.Unknown;
    }

    private static string Describe(bool? value) {
        return value is null ? "missing" : value.Value.ToString();
    }
}

internal sealed class BossesHelperEcosystem : EcosystemBase {
    internal override DesyncEcosystemKind Kind => DesyncEcosystemKind.BossesHelper;
    internal override string ModName => "BossesHelper";

    // The lua-work E5b adapter (main-thread cloning of the boss Lua state) is NOT ported into this
    // branch. Bench showed that the idle PatternCoroutine check alone is not enough: an idle
    // LuaProxyCoroutine on CustomBossPuppet still triggers the [LuaExecuting] probe, so "at-rest boss"
    // saves are not safe without the adapter. Keep the old fail-closed behaviour (a BossController in
    // the level refuses the save) until the adapter is ported and bench-verified; flip this flag then.
    private static readonly bool AllowAtRestBossesWithAdapter = false;

    private Type bossControllerType;
    private FieldInfo patternCoroutineField;
    private Type[] ownedTypes = [];

    internal override void Resolve() {
        bossControllerType = null;
        patternCoroutineField = null;
        ownedTypes = [];
        Installed = false;
        Resolved = false;

        if (ModUtils.GetAssembly(ModName) is null) {
            return;
        }

        Installed = true;
        bossControllerType = ModUtils.GetType(ModName, "Celeste.Mod.BossesHelper.Code.Entities.BossController");
        if (bossControllerType is null) {
            Logger.Warn("SpeedrunTool/DesyncRiskAnalyzer",
                "BossesHelper is installed but BossController could not be resolved; its Lua state cannot be inspected (fail-closed).");
            return;
        }

        patternCoroutineField = bossControllerType.GetFieldInfo("PatternCoroutine");
        if (patternCoroutineField is null) {
            Logger.Warn("SpeedrunTool/DesyncRiskAnalyzer",
                "BossesHelper.BossController.PatternCoroutine is missing; at-rest adapter support would fail closed.");
        }

        // Entity types that can hold Lua state directly or transitively (Cecil inventory in
        // docs/desync-checker.md). Subclasses are covered by Owns().
        string[] ownedTypeNames = [
            "Celeste.Mod.BossesHelper.Code.Entities.BossController",
            "Celeste.Mod.BossesHelper.Code.Entities.BossPuppet",
            "Celeste.Mod.BossesHelper.Code.Entities.GlobalSavePoint",
            "Celeste.Mod.BossesHelper.Code.Entities.HealthSystemManager",
            "Celeste.Mod.BossesHelper.Code.Entities.HealthSystemManager+DamageController",
            "Celeste.Mod.BossesHelper.Code.Helpers.BossEvent+CutsceneWrapper",
        ];
        ownedTypes = ownedTypeNames
            .Select(name => ModUtils.GetType(ModName, name))
            .Where(type => type is not null)
            .ToArray();
        if (ownedTypes.Length != ownedTypeNames.Length) {
            Logger.Warn("SpeedrunTool/DesyncRiskAnalyzer",
                $"BossesHelper: only {ownedTypes.Length}/{ownedTypeNames.Length} known Lua-holding types resolved; "
                + "missing ones stay fail-closed in the late check.");
        }

        Resolved = true;
    }

    internal override bool Owns(Type type) {
        foreach (Type ownedType in ownedTypes) {
            if (type == ownedType || type.IsSubclassOf(ownedType)) {
                return true;
            }
        }

        return false;
    }

    internal override EcosystemGateResult Evaluate(Level level) {
        if (!Installed) {
            return NotInstalledResult();
        }

        if (!Resolved || bossControllerType is null) {
            return UnavailableResult("BossesHelper is installed but BossController could not be resolved");
        }

        Entity boss = FirstInspectableEntityOfType(level, bossControllerType);
        if (boss is null) {
            // No BossController in the level: same as the old "blanket safe" behaviour for the rest of
            // the assembly (e.g. a standalone health-system room).
            return SafeResult();
        }

        if (!AllowAtRestBossesWithAdapter) {
            return UnsafeResult(boss, boss.GetType().Name,
                "BossesHelper BossController present; saving its Lua boss state needs the opt-in E5b adapter "
                + "(not ported yet), so the save is refused (old fail-closed behaviour)");
        }

        if (patternCoroutineField is null) {
            return UnsafeResult(boss, boss.GetType().Name,
                "BossesHelper.BossController.PatternCoroutine is missing; cannot prove the boss is idle (fail-closed)");
        }

        foreach (Entity entity in level.Entities) {
            if (entity is null || !bossControllerType.IsInstanceOfType(entity)) {
                continue;
            }

            if (IsInstanceDeclaredSafe(entity)) {
                continue;
            }

            if (!TryReadField(patternCoroutineField, entity, out object patternValue)) {
                return UnsafeResult(entity, entity.GetType().Name, "BossController.PatternCoroutine could not be read (fail-closed)");
            }

            if (patternValue is not Coroutine patternCoroutine) {
                return UnsafeResult(entity, entity.GetType().Name,
                    "BossController.PatternCoroutine is null; cannot prove the boss is idle (fail-closed)");
            }

            // Active LuaProxyCoroutine instances are covered globally by LuaUsageProbe; here we only
            // need to know whether the boss pattern itself is running.
            if (!DesyncCheckLogic.BossController(patternCoroutine.Finished, patternCoroutine.Active).IsSafe()) {
                return UnsafeResult(entity, entity.GetType().Name,
                    $"BossController pattern coroutine is running (Active={patternCoroutine.Active}, Finished={patternCoroutine.Finished})");
            }
        }

        return SafeResult();
    }
}

internal sealed class FrostHelperEcosystem : EcosystemBase {
    internal override DesyncEcosystemKind Kind => DesyncEcosystemKind.FrostHelper;
    internal override string ModName => "FrostHelper";

    // Same policy as BossesHelper: the E5 LuaBadelineBoss adapter is not ported, so an at-rest save
    // would share the (possibly mutated) Lua tables across load. Keep the old fail-closed behaviour
    // until the adapter is ported and bench-verified; flip this flag then.
    private static readonly bool AllowAtRestFrostBossWithAdapter = false;

    private Type luaBossType;
    private PropertyInfo createdCoroutinesProperty;
    private FieldInfo createdCoroutinesBackingField;

    internal override void Resolve() {
        luaBossType = null;
        createdCoroutinesProperty = null;
        createdCoroutinesBackingField = null;
        Installed = false;
        Resolved = false;

        if (ModUtils.GetAssembly(ModName) is null) {
            return;
        }

        Installed = true;
        luaBossType = ModUtils.GetType(ModName, "FrostHelper.Entities.LuaBadelineBoss");
        if (luaBossType is null) {
            Logger.Warn("SpeedrunTool/DesyncRiskAnalyzer",
                "FrostHelper is installed but LuaBadelineBoss could not be resolved; its Lua state cannot be inspected.");
            return;
        }

        createdCoroutinesProperty = luaBossType.GetPropertyInfo("CreatedCoroutines");
        createdCoroutinesBackingField = luaBossType.GetFieldInfo("<CreatedCoroutines>k__BackingField");
        if (createdCoroutinesProperty is null && createdCoroutinesBackingField is null) {
            Logger.Warn("SpeedrunTool/DesyncRiskAnalyzer",
                "FrostHelper.LuaBadelineBoss.CreatedCoroutines is missing; instances will fail closed.");
        }

        Resolved = true;
    }

    internal override bool Owns(Type type) {
        return luaBossType is not null && (type == luaBossType || type.IsSubclassOf(luaBossType));
    }

    internal override EcosystemGateResult Evaluate(Level level) {
        if (!Installed) {
            return NotInstalledResult();
        }

        if (!Resolved || luaBossType is null) {
            return UnavailableResult("FrostHelper is installed but LuaBadelineBoss could not be resolved");
        }

        Entity boss = FirstInspectableEntityOfType(level, luaBossType);
        if (boss is null) {
            return SafeResult();
        }

        if (!AllowAtRestFrostBossWithAdapter) {
            return UnsafeResult(boss, boss.GetType().Name,
                "FrostHelper LuaBadelineBoss present; saving its Lua state needs the opt-in E5 adapter "
                + "(not ported yet), so the save is refused (old fail-closed behaviour)");
        }

        foreach (Entity entity in level.Entities) {
            if (entity is null || !luaBossType.IsInstanceOfType(entity)) {
                continue;
            }

            if (IsInstanceDeclaredSafe(entity)) {
                continue;
            }

            bool? allFinished = ReadAllCreatedCoroutinesFinished(entity);
            if (!DesyncCheckLogic.FrostLuaBoss(allFinished).IsSafe()) {
                return UnsafeResult(entity, entity.GetType().Name,
                    allFinished is null
                        ? "LuaBadelineBoss.CreatedCoroutines is missing/unreadable (fail-closed)"
                        : "LuaBadelineBoss still has a running coroutine");
            }
        }

        return SafeResult();
    }

    private bool? ReadAllCreatedCoroutinesFinished(Entity entity) {
        object value;
        if (createdCoroutinesProperty is not null) {
            try {
                value = createdCoroutinesProperty.GetValue(entity);
            }
            catch (Exception exception) {
                Logger.Warn("SpeedrunTool/DesyncRiskAnalyzer", $"Failed to read LuaBadelineBoss.CreatedCoroutines: {exception.Message}");
                return null;
            }
        }
        else if (!TryReadField(createdCoroutinesBackingField, entity, out value)) {
            return null;
        }

        if (value is not IEnumerable coroutines) {
            return null;
        }

        foreach (object coroutine in coroutines) {
            // Only a finished Monocle.Coroutine proves the Lua stack is not running; anything else fails closed.
            if (coroutine is not Coroutine { Finished: true }) {
                return false;
            }
        }

        return true;
    }
}
