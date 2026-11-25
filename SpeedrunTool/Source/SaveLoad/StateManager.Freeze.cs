namespace Celeste.Mod.SpeedrunTool.SaveLoad;

/// <summary>
/// Freeze / wipe / unfreeze flow used by the normal (non-TAS) save and load.
/// </summary>
public sealed partial class StateManager {
    // 关闭冻结(Off)时，wipe 完成回调不能在同一帧把 State 置回 None：那一帧的 Level.Update 已经被
    // 跳过，但 Scene.AfterUpdate 里的计时器（房间计时等）会按 State == None 把这一帧算进去，
    // 结果房间计时比章节时间多一帧。把解冻推迟到下一帧的 Scene.BeforeUpdate，和 On 模式解冻走
    // 同一个位置，State 就只会在帧边界翻转。
    private bool pendingOffUnfreeze;

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
            if (ModSettings.FreezeAfterLoadStateType != FreezeAfterSaveLoadStateType.Off) {
                State = State.Waiting;
            }
            else {
                pendingOffUnfreeze = true;
            }
        });
    }

    /// <summary>取走并清除"关闭冻结模式下待解冻"标记；由 GameFreezeManager 在 Scene.BeforeUpdate 调用。</summary>
    internal bool TryConsumePendingOffUnfreeze() {
        if (!pendingOffUnfreeze) {
            return false;
        }

        pendingOffUnfreeze = false;
        return true;
    }

    internal void OutOfFreeze(Level level) {
        pendingOffUnfreeze = false;
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
