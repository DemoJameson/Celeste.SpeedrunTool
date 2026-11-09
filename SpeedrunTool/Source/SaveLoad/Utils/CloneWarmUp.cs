#nullable enable annotations

using Celeste.Mod.SpeedrunTool.Utils;
using Force.DeepCloner;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Utils;

/// <summary>
/// Hides the one-time cost of the first save/load of a session behind the level loading screen.
///
/// The profiling showed that the first save of a session pays a large one-off cost that later
/// saves do not have: on the dev machine ~216 ms for the first <c>DeepCloneTo</c> of the Level in
/// an entry room (and ~283 ms in the 3451-entity Grandmaster Heartside second room), ~142 ms for
/// the first execution of the save actions (and ~127 ms there) and ~62 ms for the first load
/// actions. The reason is JIT + <c>Expression.Compile</c> of the generated cloners and of the
/// save-state lambdas; the steady state is only ~13 ms per save in a small room.
///
/// So after the action table is initialised on the first <c>Level.Begin</c>:
/// <list type="bullet">
/// <item>on the main thread, run a throw-away "save" of every registered action into temporary
/// dictionaries (and clone <c>SaveData</c> once). The results are discarded, but all save-state
/// lambdas and the cloner delegates they use are JITed and cached while the loading screen is
/// still up. The game is frozen/loading at this point and the actions are read-only snapshots,
/// so this is safe; the shared clone state is cleared afterwards.</item>
/// <item>on a background thread, force the JIT of the big <c>DeepCloneTo</c> delegates (the Level
/// and ExtendedVariantMode's TriggerManager) by running them on two uninitialised dummy objects.
/// They only read/write the dummy objects, so this is safe off the main thread and keeps the
/// (large) JIT cost off the load path.</item>
/// </list>
///
/// Everything is best effort: any exception is swallowed, because the normal lazy path still
/// works. Disable with the <c>SetCloneWarmUpEnabled</c> ModInterop API for A/B benchmarks.
/// </summary>
internal static class CloneWarmUp {
    private static bool started;

    internal static bool Enabled { get; set; } = true;

    internal static void WarmUp(Level level) {
        if (!Enabled || started) {
            return;
        }

        started = true;

        Stopwatch stopwatch = Stopwatch.StartNew();
        DeepClonerUtils.WarmingUp = true;
        try {
            WarmUpSaveActions(level);
            WarmUpCreateState(level);
        }
        catch (Exception exception) {
            Logger.Warn("SpeedrunTool/CloneWarmUp", $"Warm-up failed: {exception.GetType().Name}: {exception.Message}");
        }
        finally {
            DeepClonerUtils.WarmingUp = false;
            DeepClonerUtils.ClearSharedDeepCloneState();
        }

        Logger.Info("SpeedrunTool/CloneWarmUp",
            $"Warm-up took {stopwatch.ElapsedMilliseconds}ms (throw-away state, discarded).");
    }

    /// <summary>Runs every save-state callback once so their lambdas and cloners get JITed.</summary>
    private static void WarmUpSaveActions(Level level) {
        Stopwatch stopwatch = Stopwatch.StartNew();
        string firstFailure = null;
        int failures = 0;
        try {
            SaveLoadAction.WarmUpSaveState(level, out failures, out firstFailure);
        }
        catch (Exception exception) {
            failures++;
            Logger.Warn("SpeedrunTool/CloneWarmUp", $"Save-action warm-up aborted: {exception.GetType().Name}: {exception.Message}");
        }

        Logger.Info("SpeedrunTool/CloneWarmUp",
            $"Save-action warm-up: {stopwatch.ElapsedMilliseconds}ms, {failures} callback(s) failed"
            + (firstFailure is null ? "." : $", first: {firstFailure}"));
    }

    /// <summary>
    /// Does one throw-away clone of the live level into a dummy template, which is what the first
    /// save does: this JITs every cloner delegate that the real level graph actually reaches.
    /// </summary>
    private static void WarmUpCreateState(Level level) {
        Stopwatch stopwatch = Stopwatch.StartNew();
        try {
            Level throwAway = (Level)RuntimeHelpers.GetUninitializedObject(typeof(Level));
            level.DeepCloneToShared(throwAway);

            try {
                SaveData.Instance?.DeepCloneShared();
            }
            catch {
                // best effort
            }
        }
        catch (Exception exception) {
            Logger.Warn("SpeedrunTool/CloneWarmUp", $"Level warm-up aborted: {exception}");
        }

        Logger.Info("SpeedrunTool/CloneWarmUp", $"Level warm-up: {stopwatch.ElapsedMilliseconds}ms.");
    }
}
