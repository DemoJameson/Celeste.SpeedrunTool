using System.Collections.Generic;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;

// -------------------------------------------------------------------------------------------------
// Game-free decision logic for the desync checker.
//
// Everything in this file only uses BCL types so that tests/DesyncCheckerTests can link the file and
// run the truth tables offline (same convention as PendingLoadWatchdog.cs). The game-facing layers
// (LuaUsageProbe / DesyncEcosystems / DesyncRiskAnalyzer) collect the evidence and call these rules.
// -------------------------------------------------------------------------------------------------

/// <summary>Result of evaluating whether a piece of Lua-holding state is at rest.</summary>
internal enum AtRestVerdict {
    Safe = 0,
    Unsafe = 1,
}

/// <summary>
/// A Lua-driven enumerator found in one live <c>Monocle.Coroutine</c> enumerator stack.
/// A leaf is only <see cref="Finished"/> when completion is proven; a driver still on the stack is
/// by definition not finished (the stack pops completed enumerators).
/// </summary>
internal readonly struct LuaDriveLeaf {
    public LuaDriveLeaf(string typeName, bool finished) {
        TypeName = typeName;
        Finished = finished;
    }

    /// <summary>Runtime type name of the Lua coroutine (<c>Celeste.Mod.LuaCoroutine</c> or a mod subclass).</summary>
    public string TypeName { get; }

    /// <summary>True only when this drive has completed and no longer runs Lua code.</summary>
    public bool Finished { get; }
}

/// <summary>
/// One live coroutine component plus the Lua drives found inside its enumerator stack.
/// Produced by <see cref="LuaUsageProbe"/> on the main thread once per save.
/// </summary>
internal readonly struct LuaDriveSnapshot {
    public LuaDriveSnapshot(string ownerTypeName, bool ownerIsCutsceneEcosystem, bool ownerDeclaredSafe,
        IReadOnlyList<LuaDriveLeaf> leaves) {
        OwnerTypeName = ownerTypeName;
        OwnerIsCutsceneEcosystem = ownerIsCutsceneEcosystem;
        OwnerDeclaredSafe = ownerDeclaredSafe;
        Leaves = leaves;
    }

    /// <summary>Runtime type name of the entity that owns the coroutine component.</summary>
    public string OwnerTypeName { get; }

    /// <summary>True when the owner belongs to the LuaCutscenes ecosystem (used by "Save in Lua cutscene").</summary>
    public bool OwnerIsCutsceneEcosystem { get; }

    /// <summary>True when <c>SpeedrunTool.Desync</c> registrations declare this owner safe.</summary>
    public bool OwnerDeclaredSafe { get; }

    /// <summary>Lua drives found in this coroutine's live enumerator stack (empty when not Lua-driven).</summary>
    public IReadOnlyList<LuaDriveLeaf> Leaves { get; }
}

/// <summary>
/// State of <c>LuaTalker.onTalkRoutine</c> (the <c>IEnumerator</c> created by
/// <c>LuaHelper.LuaCoroutineToIEnumerator</c>, runtime type
/// <c>LuaHelper+&lt;LuaCoroutineToIEnumerator&gt;d__2</c>). Bench found a LuaTalker path
/// (SJ <c>tofu [Atrium]</c>) that talks while <c>Level.InCutscene == false</c> and without a
/// <c>Monocle.Coroutine</c> component, so the coroutine-component scan alone misses it.
/// </summary>
internal enum TalkRoutineState {
    /// <summary><c>onTalkRoutine</c> is null: the talker never started a Lua routine.</summary>
    None = 0,

    /// <summary>The state machine reached <c>&lt;&gt;1__state == -1</c>: the routine completed.</summary>
    Finished = 1,

    /// <summary>The routine is pending/suspended (state 0, 1, 2 or 3): Lua will still advance.</summary>
    Running = 2,

    /// <summary>The routine state could not be proven finished -> fail closed.</summary>
    Unknown = 3,
}

/// <summary>
/// Pure truth tables for the checker. Keeping them here makes the fail-closed rules testable without
/// the game assembly: every "evidence unavailable" input must resolve to <see cref="AtRestVerdict.Unsafe"/>.
/// </summary>
internal static class DesyncCheckLogic {

    /// <summary>
    /// Generic "Lua is executing" decision. Returns true (and a human-readable blocker) when a live
    /// coroutine stack contains a Lua drive that is not proven finished.
    /// </summary>
    /// <param name="snapshots">Live coroutine components collected from the level.</param>
    /// <param name="saveInLuaCutscene">The user setting: cutscene-owned execution alone is tolerated then.</param>
    /// <param name="blocker">Description of the first blocking drive.</param>
    public static bool TryFindBlockingLuaExecution(IReadOnlyList<LuaDriveSnapshot> snapshots, bool saveInLuaCutscene,
        out string blocker) {
        blocker = null;
        if (snapshots is null) {
            return false;
        }

        foreach (LuaDriveSnapshot snapshot in snapshots) {
            // A registered safe entity/type takes full responsibility for its own Lua state.
            if (snapshot.OwnerDeclaredSafe) {
                continue;
            }

            if (!TryFindUnfinishedLeaf(snapshot.Leaves, out LuaDriveLeaf running)) {
                continue;
            }

            // "Save in Lua cutscene" only tolerates Lua driven by the LuaCutscenes ecosystem; other
            // mods' executing Lua still blocks the save (the old option used to disable everything).
            if (saveInLuaCutscene && snapshot.OwnerIsCutsceneEcosystem) {
                continue;
            }

            blocker = $"Lua is executing: [{running.TypeName}] on [{snapshot.OwnerTypeName ?? "unknown entity"}]";
            return true;
        }

        return false;
    }

    private static bool TryFindUnfinishedLeaf(IReadOnlyList<LuaDriveLeaf> leaves, out LuaDriveLeaf leaf) {
        leaf = default;
        if (leaves is null) {
            return false;
        }

        foreach (LuaDriveLeaf candidate in leaves) {
            if (!candidate.Finished) {
                leaf = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>LuaCutscenes <c>LuaCutsceneEntity</c>: safe only when finished and no coroutine is running.</summary>
    /// <param name="finished"><c>Finished</c> field value; null means the field is missing (fail-closed).</param>
    /// <param name="drivenByLevelCutscene">The level's current skippable cutscene is driven by this type.</param>
    /// <param name="hasUnfinishedCoroutine">The entity still carries an unfinished <c>Monocle.Coroutine</c>.</param>
    public static AtRestVerdict LuaCutsceneEntity(bool? finished, bool drivenByLevelCutscene, bool hasUnfinishedCoroutine) {
        if (finished is null) {
            return AtRestVerdict.Unsafe; // field missing -> fail-closed
        }

        if (drivenByLevelCutscene) {
            return AtRestVerdict.Unsafe; // level cutscene still running
        }

        if (!finished.Value) {
            return AtRestVerdict.Unsafe;
        }

        if (hasUnfinishedCoroutine) {
            return AtRestVerdict.Unsafe;
        }

        return AtRestVerdict.Safe;
    }

    /// <summary>
    /// LuaCutscenes <c>LuaTalker</c> (P2). Safe only when the level is not inside a cutscene driven by
    /// the talker, no coroutine component is running, and <c>onTalkRoutine</c> is either absent or
    /// proven finished.
    /// </summary>
    /// <param name="drivenByLevelCutscene">The level's current skippable cutscene is driven by a LuaTalker.</param>
    /// <param name="hasUnfinishedCoroutine">The entity still carries an unfinished <c>Monocle.Coroutine</c>.</param>
    /// <param name="talkRoutineState">Introspected state of the <c>onTalkRoutine</c> field.</param>
    public static AtRestVerdict LuaTalker(bool drivenByLevelCutscene, bool hasUnfinishedCoroutine, TalkRoutineState talkRoutineState) {
        if (drivenByLevelCutscene) {
            return AtRestVerdict.Unsafe;
        }

        if (hasUnfinishedCoroutine) {
            return AtRestVerdict.Unsafe;
        }

        return talkRoutineState is TalkRoutineState.None or TalkRoutineState.Finished
            ? AtRestVerdict.Safe
            : AtRestVerdict.Unsafe; // Running or Unknown (fail-closed)
    }

    /// <summary>
    /// BossesHelper <c>BossController</c>: safe unless its <c>PatternCoroutine</c> is actively running.
    /// A missing <c>PatternCoroutine</c> field is fail-closed.
    /// </summary>
    /// <param name="patternCoroutineFinished"><c>PatternCoroutine.Finished</c>; null = field missing.</param>
    /// <param name="patternCoroutineActive"><c>PatternCoroutine.Active</c>.</param>
    public static AtRestVerdict BossController(bool? patternCoroutineFinished, bool patternCoroutineActive) {
        if (patternCoroutineFinished is null) {
            return AtRestVerdict.Unsafe; // field missing -> fail-closed
        }

        if (patternCoroutineActive && !patternCoroutineFinished.Value) {
            return AtRestVerdict.Unsafe; // pattern (possibly Lua) currently executing
        }

        return AtRestVerdict.Safe;
    }

    /// <summary>
    /// FrostHelper <c>LuaBadelineBoss</c>: safe only when every entry of <c>CreatedCoroutines</c> is
    /// finished. A missing member is fail-closed.
    /// </summary>
    /// <param name="allCreatedCoroutinesFinished">null = <c>CreatedCoroutines</c> missing/unreadable.</param>
    public static AtRestVerdict FrostLuaBoss(bool? allCreatedCoroutinesFinished) {
        if (allCreatedCoroutinesFinished is null) {
            return AtRestVerdict.Unsafe; // member missing -> fail-closed
        }

        return allCreatedCoroutinesFinished.Value ? AtRestVerdict.Safe : AtRestVerdict.Unsafe;
    }

    /// <summary>
    /// An instance-level predicate registered through <c>SpeedrunTool.Desync</c> can override a verdict
    /// for that specific instance (e.g. a finished Lua cutscene the mod declares harmless).
    /// </summary>
    public static AtRestVerdict ApplyEntityOverride(AtRestVerdict verdict, bool entityDeclaredSafe) {
        return entityDeclaredSafe ? AtRestVerdict.Safe : verdict;
    }

    public static bool IsSafe(this AtRestVerdict verdict) {
        return verdict == AtRestVerdict.Safe;
    }
}
