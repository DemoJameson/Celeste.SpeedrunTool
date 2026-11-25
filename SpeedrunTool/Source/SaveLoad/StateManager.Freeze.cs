namespace Celeste.Mod.SpeedrunTool.SaveLoad;

/// <summary>
/// Freeze / wipe / unfreeze flow used by the normal (non-TAS) save and load.
/// </summary>
public sealed partial class StateManager {
    private void FreezeGame(FreezeType freeze) {
        freezeType = freeze;
    }

    private void DoScreenWipe(Level level) {
        level.DoScreenWipe(true, () => {
            // The player can move again as soon as the freeze ends, so nothing may still be cloning by
            // then (AGENTS.md §16: no background work after the operation). The pre-clone was started
            // before the wipe and the wipe is long enough that this join is free; it is also the last
            // point that is still inside the operation, so the cost never lands on the next input.
            WaitForPreClone();
            if (ModSettings.FreezeAfterLoadStateType != FreezeAfterLoadStateType.Off) {
                State = State.Waiting;
            }
            else {
                OutOfFreeze(level);
            }
        });
    }

    internal void OutOfFreeze(Level level) {
        if (freezeType == FreezeType.Save || savedLevel == null) {
            if (savedLevel != null) {
                RestoreLevelTime(level);
            }

            State = State.None;
        }
        else {
            LoadStateComplete(level);
        }

        // when freeze by SRT, the CycleGroupCounter still updates, so we restore it when out of freeze
        RestoreCycleGroupCounter();

        freezeType = FreezeType.None;
    }
}
