using Celeste.Mod.SpeedrunTool.Message;
using Celeste.Mod.SpeedrunTool.ModInterop;
using Celeste.Mod.SpeedrunTool.MoreSaveSlotsUI;
using Celeste.Mod.SpeedrunTool.Other;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using System.Collections.Generic;
using System.Linq;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;
internal static class GameFreezeManager {

    private static State State => StateManager.Instance.State;

    private static readonly Dictionary<VirtualInput, bool> LastChecks = [];

    private static List<VirtualInput> unfreezeInputs = [];

    // different with Everest's event, which won't work when game freeze
    internal static event Action<Level> OnAfterUpdate_EvenIfGameFreeze = null;

    private static void Input_OnInitialize() {
        // 每次重置游戏键位后, 这些 VirtualInput 都是新的对象, 因此必须重新获取
        unfreezeInputs = [Input.Dash, Input.Jump, Input.Grab, Input.MoveX, Input.MoveY, Input.Dash, Input.Aim, Input.Pause, Input.CrouchDash];
        LastChecks.Clear();
    }

    [Load]
    private static void Load() {
        IL.Monocle.Engine.Update += IL_Engine_Update;
        On.Celeste.Level.Update += MakeGameFreezeAfterSaveLoad;
        On.Monocle.Scene.BeforeUpdate += UnfreezeTheGame;
        SaveLoadAction.InternalSafeAdd(
            (_, _) => UpdateLastChecks(),
            (_, _) => UpdateLastChecks(),
            () => LastChecks.Clear()
        );
        Everest.Events.Input.OnInitialize += Input_OnInitialize;
    }

    [Unload]
    private static void Unload() {
        IL.Monocle.Engine.Update -= IL_Engine_Update;
        On.Celeste.Level.Update -= MakeGameFreezeAfterSaveLoad;
        On.Monocle.Scene.BeforeUpdate -= UnfreezeTheGame;
        Everest.Events.Input.OnInitialize -= Input_OnInitialize;
    }


    #region Freeze_AfterSaveLoad
    private static void UpdateLastChecks() {
        if (ModSettings.FreezeAfterLoadStateType != FreezeAfterLoadStateType.IgnoreHoldingKeys) {
            return;
        }

        foreach (VirtualInput virtualInput in unfreezeInputs) {
            LastChecks[virtualInput] = virtualInput.IsCheck();
        }
    }

    private static bool IsUnfreeze(VirtualInput input) {
        if (input.IsPressed()) {
            return true;
        }

        if (!input.IsCheck()) {
            return false;
        }

        bool lastCheck = ModSettings.FreezeAfterLoadStateType == FreezeAfterLoadStateType.IgnoreHoldingKeys &&
                         LastChecks.TryGetValue(input, out bool value) && value;
        return !lastCheck;
    }

    private static void UnfreezeTheGame(On.Monocle.Scene.orig_BeforeUpdate orig, Scene self) {
        if (ModSettings.Enabled && self is Level level && State == State.Waiting) {
            if (unfreezeInputs.Any(IsUnfreeze) || Hotkey.CheckDeathStatistics.Pressed()) {
                LastChecks.Clear();
                StateManager.Instance.OutOfFreeze(level);
            }

            if (State == State.Waiting) {
                UpdateLastChecks();
            }
        }

        orig(self);
    }

    private static void MakeGameFreezeAfterSaveLoad(On.Celeste.Level.orig_Update orig, Level level) {
        if (State != State.None) {
            UpdateBackdropWhenWaiting(level);
        }
        else {
            orig(level);
        }

        OnAfterUpdate_EvenIfGameFreeze?.Invoke(level);

        static void UpdateBackdropWhenWaiting(Level level) {
            level.Wipe?.Update(level);
            level.HiresSnow?.Update(level);
            level.Foreground.Update(level);
            level.Background.Update(level);
            level.Tracker.GetEntity<Tooltip>()?.Update();
            level.Tracker.GetEntity<NonFrozenMiniTextbox>()?.Update();
        }
    }

    #endregion


    #region Freeze_SnapshotUI

    private static void IL_Engine_Update(ILContext il) {
        ILCursor cursor = new(il);
        if (cursor.TryGotoNext(MoveType.After, ins => ins.MatchCall(typeof(MInput), nameof(MInput.Update)))) {
            // Prevent further execution
            ILLabel label = cursor.DefineLabel();
            cursor.EmitDelegate(UI_IsOpen);
            cursor.Emit(OpCodes.Brfalse, label);
            cursor.Emit(OpCodes.Ret);
            cursor.MarkLabel(label);
        }
    }


    private static bool UI_IsOpen() {
        if (Engine.Scene is not Level level) {
            return false;
        }
        if (TasUtils.Running) {
            SnapshotUI.Close();
            return false;
        }
        if (!SnapshotUI.OnScreen) {
            return false;
        }
        SnapshotUI.Update();
        UpdateMessageWhenWaiting(level);

        return true;

        static void UpdateMessageWhenWaiting(Level level) {
            level.Entities.UpdateLists();
            level.Tracker.GetEntity<Tooltip>()?.Update();
            // we forced PopupMessageUtils to use Tooltip, so no need to update NonFrozenMiniTextbox
        }
    }
    #endregion
}
