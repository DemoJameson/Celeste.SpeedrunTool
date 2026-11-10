using Celeste.Mod.SpeedrunTool.SaveLoad;

namespace DesyncCheckerTests;

/// <summary>
/// Offline truth tables for the desync checker's game-free rules:
/// (1) the generic "Lua is executing" probe, (2) the built-in at-rest predicates,
/// (3) fail-closed handling of unavailable evidence, (4) the SpeedrunTool.Desync registration /
/// override semantics.
/// </summary>
internal static class Program {
    private static int failures;

    private static int Main() {
        ProbeTests();
        AtRestTests();
        FailClosedTests();
        RegistrationTests();

        if (failures == 0) {
            Console.WriteLine("DesyncCheckerTests: all checks passed.");
            return 0;
        }

        Console.WriteLine($"DesyncCheckerTests: {failures} check(s) FAILED.");
        return 1;
    }

    // ---------------------------------------------------------------------------------------------
    // 1. LuaUsageProbe decision: "Lua is executing"
    // ---------------------------------------------------------------------------------------------
    private static void ProbeTests() {
        // No evidence -> nothing blocks.
        ExpectProbe("no coroutine components", false);

        // A coroutine with no Lua drive at all is fine.
        ExpectProbe("coroutine without Lua drive", false, Snapshot("Player", cutscene: false, Leaf("SomeEnumerator", finished: true)));

        // Prepared/inactive snapshots with only finished leaves are at rest.
        ExpectProbe("finished Lua drive only", false, Snapshot("SomeModEntity", cutscene: false, Leaf("Celeste.Mod.LuaCoroutine", finished: true)));

        // A live Lua drive blocks.
        ExpectProbe("running LuaCoroutine", true, Snapshot("BossController", cutscene: false, Leaf("Celeste.Mod.LuaCoroutine", finished: false)));
        ExpectProbe("running mod subclass of LuaCoroutine", true,
            Snapshot("BossController", cutscene: false, Leaf("Celeste.Mod.BossesHelper...LuaProxyCoroutine", finished: false)));

        // "Save in Lua cutscene" only tolerates LuaCutscenes-owned execution.
        ExpectProbe("running Lua cutscene with SaveInLuaCutscene", false, saveInLuaCutscene: true,
            Snapshot("LuaCutsceneEntity", cutscene: true, Leaf("Celeste.Mod.LuaCoroutine", finished: false)));
        ExpectProbe("running Lua cutscene without SaveInLuaCutscene", true, saveInLuaCutscene: false,
            Snapshot("LuaCutsceneEntity", cutscene: true, Leaf("Celeste.Mod.LuaCoroutine", finished: false)));
        ExpectProbe("running non-cutscene Lua with SaveInLuaCutscene", true, saveInLuaCutscene: true,
            Snapshot("BossController", cutscene: false, Leaf("Celeste.Mod.LuaCoroutine", finished: false)));

        // A SpeedrunTool.Desync registration for the owner suppresses the probe for that owner only.
        ExpectProbe("declared-safe owner suppresses the probe", false, saveInLuaCutscene: false,
            Snapshot("MyModEntity", cutscene: false, declaredSafe: true, Leaf("Celeste.Mod.LuaCoroutine", finished: false)));
        ExpectProbe("declared-safe owner does not suppress other owners", true, saveInLuaCutscene: false,
            Snapshot("MyModEntity", cutscene: false, declaredSafe: true, Leaf("Celeste.Mod.LuaCoroutine", finished: false)),
            Snapshot("OtherModEntity", cutscene: false, Leaf("Celeste.Mod.LuaCoroutine", finished: false)));

        // Multiple drives in one stack: a finished one must not hide a running one.
        ExpectProbe("finished drive followed by running drive", true,
            Snapshot("MyModEntity", cutscene: false,
                Leaf("A", finished: true), Leaf("Celeste.Mod.LuaCoroutine", finished: false)));

        // Multiple snapshots: scan order does not matter.
        ExpectProbe("second snapshot blocks", true,
            Snapshot("Idle", cutscene: false, Leaf("Celeste.Mod.LuaCoroutine", finished: true)),
            Snapshot("Busy", cutscene: false, Leaf("Celeste.Mod.LuaCoroutine", finished: false)));
    }

    // ---------------------------------------------------------------------------------------------
    // 2. Built-in at-rest predicates
    // ---------------------------------------------------------------------------------------------
    private static void AtRestTests() {
        // LuaCutscenes.LuaCutsceneEntity
        ExpectVerdict("LuaCutsceneEntity finished + idle is at rest", AtRestVerdict.Safe,
            DesyncCheckLogic.LuaCutsceneEntity(finished: true, drivenByLevelCutscene: false, hasUnfinishedCoroutine: false));
        ExpectVerdict("LuaCutsceneEntity not finished is unsafe", AtRestVerdict.Unsafe,
            DesyncCheckLogic.LuaCutsceneEntity(finished: false, drivenByLevelCutscene: false, hasUnfinishedCoroutine: false));
        ExpectVerdict("LuaCutsceneEntity running in level cutscene is unsafe", AtRestVerdict.Unsafe,
            DesyncCheckLogic.LuaCutsceneEntity(finished: true, drivenByLevelCutscene: true, hasUnfinishedCoroutine: false));
        ExpectVerdict("LuaCutsceneEntity with running coroutine is unsafe", AtRestVerdict.Unsafe,
            DesyncCheckLogic.LuaCutsceneEntity(finished: true, drivenByLevelCutscene: false, hasUnfinishedCoroutine: true));

        // LuaCutscenes.LuaTalker (P2). Bench found a shape (SJ tofu [Atrium]) where the talk runs with
        // InCutscene == false and without a Monocle.Coroutine component, so onTalkRoutine must be
        // inspected directly: only null or a finished state machine (-1) is at rest.
        ExpectVerdict("LuaTalker with no talk routine is at rest", AtRestVerdict.Safe,
            DesyncCheckLogic.LuaTalker(drivenByLevelCutscene: false, hasUnfinishedCoroutine: false, TalkRoutineState.None));
        ExpectVerdict("LuaTalker with a finished talk routine is at rest", AtRestVerdict.Safe,
            DesyncCheckLogic.LuaTalker(drivenByLevelCutscene: false, hasUnfinishedCoroutine: false, TalkRoutineState.Finished));
        ExpectVerdict("LuaTalker with a pending/running talk routine is unsafe", AtRestVerdict.Unsafe,
            DesyncCheckLogic.LuaTalker(drivenByLevelCutscene: false, hasUnfinishedCoroutine: false, TalkRoutineState.Running));
        ExpectVerdict("LuaTalker with an uninspectable talk routine fails closed", AtRestVerdict.Unsafe,
            DesyncCheckLogic.LuaTalker(drivenByLevelCutscene: false, hasUnfinishedCoroutine: false, TalkRoutineState.Unknown));
        ExpectVerdict("LuaTalker with running coroutine is unsafe", AtRestVerdict.Unsafe,
            DesyncCheckLogic.LuaTalker(drivenByLevelCutscene: false, hasUnfinishedCoroutine: true, TalkRoutineState.None));
        ExpectVerdict("LuaTalker inside a skippable cutscene is unsafe", AtRestVerdict.Unsafe,
            DesyncCheckLogic.LuaTalker(drivenByLevelCutscene: true, hasUnfinishedCoroutine: false, TalkRoutineState.None));

        // BossesHelper.BossController
        ExpectVerdict("BossController idle is at rest", AtRestVerdict.Safe,
            DesyncCheckLogic.BossController(patternCoroutineFinished: true, patternCoroutineActive: false));
        ExpectVerdict("BossController running pattern is unsafe", AtRestVerdict.Unsafe,
            DesyncCheckLogic.BossController(patternCoroutineFinished: false, patternCoroutineActive: true));
        ExpectVerdict("BossController interrupted pattern is at rest", AtRestVerdict.Safe,
            DesyncCheckLogic.BossController(patternCoroutineFinished: false, patternCoroutineActive: false));
        // Defensive: Active=true with Finished=true (a paused/edge state) is still at rest because
        // actual Lua execution is covered by the probe.
        ExpectVerdict("BossController active but finished is at rest", AtRestVerdict.Safe,
            DesyncCheckLogic.BossController(patternCoroutineFinished: true, patternCoroutineActive: true));

        // FrostHelper.LuaBadelineBoss
        ExpectVerdict("FrostHelper boss with all coroutines finished is at rest", AtRestVerdict.Safe,
            DesyncCheckLogic.FrostLuaBoss(allCreatedCoroutinesFinished: true));
        ExpectVerdict("FrostHelper boss with a running coroutine is unsafe", AtRestVerdict.Unsafe,
            DesyncCheckLogic.FrostLuaBoss(allCreatedCoroutinesFinished: false));

        // Overrides from SpeedrunTool.Desync registrations.
        ExpectVerdict("entity override turns Unsafe into Safe", AtRestVerdict.Safe,
            DesyncCheckLogic.ApplyEntityOverride(AtRestVerdict.Unsafe, entityDeclaredSafe: true));
        ExpectVerdict("entity override keeps Unsafe without a declaration", AtRestVerdict.Unsafe,
            DesyncCheckLogic.ApplyEntityOverride(AtRestVerdict.Unsafe, entityDeclaredSafe: false));
        ExpectVerdict("entity override keeps Safe", AtRestVerdict.Safe,
            DesyncCheckLogic.ApplyEntityOverride(AtRestVerdict.Safe, entityDeclaredSafe: true));
    }

    // ---------------------------------------------------------------------------------------------
    // 3. Fail-closed: missing/unavailable evidence must refuse the save
    // ---------------------------------------------------------------------------------------------
    private static void FailClosedTests() {
        ExpectVerdict("LuaCutsceneEntity with missing Finished fails closed", AtRestVerdict.Unsafe,
            DesyncCheckLogic.LuaCutsceneEntity(finished: null, drivenByLevelCutscene: false, hasUnfinishedCoroutine: false));
        ExpectVerdict("BossController with missing PatternCoroutine fails closed", AtRestVerdict.Unsafe,
            DesyncCheckLogic.BossController(patternCoroutineFinished: null, patternCoroutineActive: false));
        ExpectVerdict("FrostHelper boss with missing CreatedCoroutines fails closed", AtRestVerdict.Unsafe,
            DesyncCheckLogic.FrostLuaBoss(allCreatedCoroutinesFinished: null));

        // A predicate that throws must not make the checker fail open.
        DesyncRegistration.Reset();
        Exception reported = null;
        DesyncRegistration.PredicateErrorHook = exception => reported = exception;
        DesyncRegistration.AddEntityPredicate(_ => throw new InvalidOperationException("boom"));
        Expect("throwing entity predicate is treated as not safe", false, DesyncRegistration.IsEntitySafe(new object()));
        Expect("throwing entity predicate is reported", true, reported is InvalidOperationException);

        DesyncRegistration.AddEntityPredicate(_ => true);
        Expect("a later predicate can still declare safe", true, DesyncRegistration.IsEntitySafe(new object()));

        DesyncRegistration.Reset();
        reported = null;
        DesyncRegistration.PredicateErrorHook = exception => reported = exception;
        DesyncRegistration.AddLevelPredicate(_ => throw new InvalidOperationException("boom"));
        Expect("throwing level predicate is treated as not safe", false, DesyncRegistration.IsLevelSafe("SomeMap/SID"));
        Expect("throwing level predicate is reported", true, reported is InvalidOperationException);
    }

    // ---------------------------------------------------------------------------------------------
    // 4. SpeedrunTool.Desync registration / override semantics
    // ---------------------------------------------------------------------------------------------
    private static void RegistrationTests() {
        DesyncRegistration.Reset();

        // Types (including subclasses of a registered base type).
        Expect("unknown type is not safe", false, DesyncRegistration.IsTypeSafe(typeof(string)));
        Expect("null type is not safe", false, DesyncRegistration.IsTypeSafe(null));
        Expect("AddSafeType accepts a type", true, DesyncRegistration.AddSafeType(typeof(string)));
        Expect("registered type is safe", true, DesyncRegistration.IsTypeSafe(typeof(string)));
        Expect("unrelated type stays unsafe", false, DesyncRegistration.IsTypeSafe(typeof(int)));
        Expect("duplicate AddSafeType is a no-op", false, DesyncRegistration.AddSafeType(typeof(string)));
        Expect("base type covers subclasses", true,
            DesyncRegistration.AddSafeType(typeof(Exception)) && DesyncRegistration.IsTypeSafe(typeof(InvalidOperationException)));
        Expect("base type does not cover unrelated types", false, DesyncRegistration.IsTypeSafe(typeof(int)));
        Expect("null AddSafeType is ignored", false, DesyncRegistration.AddSafeType(null));

        Type hooked = null;
        DesyncRegistration.SafeTypeRegisteredHook = type => hooked = type;
        DesyncRegistration.AddSafeType(typeof(Uri));
        Expect("safe type registration notifies the checker", typeof(Uri), hooked);

        Expect("RemoveSafeType removes the declaration", true,
            DesyncRegistration.RemoveSafeType(typeof(Uri)) && !DesyncRegistration.IsTypeSafe(typeof(Uri)));
        Expect("RemoveSafeType on unknown type is false", false, DesyncRegistration.RemoveSafeType(typeof(Uri)));

        // Entity predicates: allow-list semantics, false means "no opinion".
        Expect("no entity predicate means not declared safe", false, DesyncRegistration.IsEntitySafe("entity"));
        Func<object, bool> isAtRest = value => ReferenceEquals(value, "idle");
        Expect("AddEntityPredicate succeeds", true, DesyncRegistration.AddEntityPredicate(isAtRest));
        Expect("predicate returning true declares safe", true, DesyncRegistration.IsEntitySafe("idle"));
        Expect("predicate returning false is no opinion", false, DesyncRegistration.IsEntitySafe("busy"));
        Expect("null entity is never declared safe", false, DesyncRegistration.IsEntitySafe(null));
        Expect("RemoveEntityPredicate removes it", true,
            DesyncRegistration.RemoveEntityPredicate(isAtRest) && !DesyncRegistration.IsEntitySafe("idle"));

        // Level predicates: whole-checker opt-out.
        Func<object, bool> safeLevel = value => Equals(value, "StrawberryJam2021/5-Grandmaster/maya");
        Expect("AddLevelPredicate succeeds", true, DesyncRegistration.AddLevelPredicate(safeLevel));
        Expect("declared safe level is skipped", true, DesyncRegistration.IsLevelSafe("StrawberryJam2021/5-Grandmaster/maya"));
        Expect("other levels are checked", false, DesyncRegistration.IsLevelSafe("Other/Map"));
        Expect("null level is never declared safe", false, DesyncRegistration.IsLevelSafe(null));
        Expect("RemoveLevelPredicate removes it", true,
            DesyncRegistration.RemoveLevelPredicate(safeLevel) && !DesyncRegistration.IsLevelSafe("StrawberryJam2021/5-Grandmaster/maya"));

        // End-to-end override shape: an at-rest rule says Unsafe, a registration overrides that instance.
        DesyncRegistration.AddEntityPredicate(value => ReferenceEquals(value, "knownSafeCutsceneEntity"));
        AtRestVerdict verdict = DesyncCheckLogic.LuaCutsceneEntity(finished: false, drivenByLevelCutscene: false, hasUnfinishedCoroutine: false);
        verdict = DesyncCheckLogic.ApplyEntityOverride(verdict, DesyncRegistration.IsEntitySafe("knownSafeCutsceneEntity"));
        Expect("registered entity overrides an Unsafe at-rest verdict", AtRestVerdict.Safe, verdict);

        // Reset is a clean slate (used by tests; the game never unloads mods in practice).
        DesyncRegistration.Reset();
        Expect("Reset clears types", false, DesyncRegistration.IsTypeSafe(typeof(string)));
        Expect("Reset clears predicates", false, DesyncRegistration.IsEntitySafe("idle"));
        Expect("Reset clears level predicates", false, DesyncRegistration.IsLevelSafe("StrawberryJam2021/5-Grandmaster/maya"));
        Expect("Reset clears the producer counts", 0,
            DesyncRegistration.SafeTypeCount + DesyncRegistration.EntityPredicateCount + DesyncRegistration.LevelPredicateCount);
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------
    private static LuaDriveLeaf Leaf(string typeName, bool finished) {
        return new LuaDriveLeaf(typeName, finished);
    }

    private static LuaDriveSnapshot Snapshot(string ownerTypeName, bool cutscene, params LuaDriveLeaf[] leaves) {
        return new LuaDriveSnapshot(ownerTypeName, cutscene, ownerDeclaredSafe: false, leaves);
    }

    private static LuaDriveSnapshot Snapshot(string ownerTypeName, bool cutscene, bool declaredSafe, params LuaDriveLeaf[] leaves) {
        return new LuaDriveSnapshot(ownerTypeName, cutscene, declaredSafe, leaves);
    }

    private static void ExpectProbe(string name, bool expected, params LuaDriveSnapshot[] snapshots) {
        ExpectProbe(name, expected, saveInLuaCutscene: false, snapshots);
    }

    private static void ExpectProbe(string name, bool expected, bool saveInLuaCutscene, params LuaDriveSnapshot[] snapshots) {
        bool actual = DesyncCheckLogic.TryFindBlockingLuaExecution(snapshots, saveInLuaCutscene, out string blocker);
        Expect($"probe: {name}", expected, actual, failedActual: actual ? $"blocker='{blocker}'" : "no blocker");
    }

    private static void ExpectVerdict(string name, AtRestVerdict expected, AtRestVerdict actual) {
        Expect($"at-rest: {name}", expected, actual);
        Expect($"at-rest: {name} (IsSafe)", expected == AtRestVerdict.Safe, actual.IsSafe());
    }

    private static void Expect<T>(string name, T expected, T actual, string failedActual = null) {
        if (EqualityComparer<T>.Default.Equals(expected, actual)) {
            return;
        }

        failures++;
        Console.WriteLine($"FAIL: {name}: expected {expected}, got {actual}{(failedActual is null ? "" : $" ({failedActual})")}");
    }
}
