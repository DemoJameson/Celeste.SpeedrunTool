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

    // 存档/读档后的冻结语义(ModSettings.FreezeAfterLoadStateType，同一个设置同时控制存档和读档的冻结)：
    //
    //   Off                不冻结。
    //
    //   On                 操作后冻结；任意一个被监测的输入处于"按下/非零"状态就解冻，
    //                      包括在操作开始前就已经按住的输入。
    //
    //   IgnoreHoldingKeys  操作后冻结；只有"操作开始之后新产生的输入"才解冻：
    //                      · 操作开始时已经按下、并且自那之后一直按着的输入会被忽略；
    //                      · 松开这些输入本身不会解冻，只是解除忽略，之后再按下就算新输入；
    //                      · 同一个动作绑定了多个键时，按另一个键算新输入（此前的 cded6fc 修复）；
    //                      · 轴(MoveX/MoveY/Aim)从 0 变为非 0 算新输入；
    //                      · 死亡统计热键与以前一样，按下即解冻。
    //
    // HeldSinceSnapshot 记录"自快照以来一直按着"的输入。快照取的是**操作开始执行的那一刻**的真实
    // 设备输入：存档在 beforeSaveState（克隆之前），直接读档在 beforeLoadState（恢复 MInput、卸载
    // 关卡之前）。跨章节的 pending load 例外：它在目标章节的 Level.Begin 里才真正执行恢复，快照点
    // 是那一刻，而不是按下读档热键的瞬间。
    // 之所以不能在读档之后再取快照：SRT 的 SupportMInput 会在读档的 loadState 阶段把 MInput 恢复成
    // 存档瞬间的状态，之后取快照会把"读档时按着"和"存档时按着"搞混（存档不会恢复 MInput，
    // 所以存档快照没有这个问题）。
    private static readonly Dictionary<VirtualInput, bool> HeldSinceSnapshot = [];

    // 读档后还要跳过判定的帧数。只有读档需要：SupportMInput 会把 MInput 恢复成存档瞬间的状态。
    // 严格说只有"读档当帧"是必须跳过的——那一帧 IsCheck() 读到的是存档时的旧状态，此时刷新快照
    // 会把"读档时按着"的信息抹掉；下一帧 IsCheck() 已经真实，只是 PreviousState 还旧、会产生假
    // Pressed 边沿，而判定要等 wipe 结束(State.Waiting)才开始，本来也碰不到。多跳过一帧是为了
    // 防御时长很短的自定义 wipe；代价是这两帧内"松开再按下"不会被观察到，该输入会继续被视为
    // "自快照以来一直按着"，需要再松开一次。
    // 注意不能用 Engine.FrameCounter 来算这个窗口：SRT 的静态字段存档会连同 Engine.FrameCounter
    // 一起恢复，读档后帧号会往回跳，用帧号比较会让窗口持续十几秒。这里按 Scene.BeforeUpdate
    // 的调用次数数帧。
    private const int SettleFramesAfterLoad = 2;
    private static int settleFrames;

    // 是否正在跟踪 HeldSinceSnapshot：快照时置 true，解冻时置 false。
    private static bool trackingInputs;

    private static List<VirtualInput> unfreezeInputs = [];

    private static void RefreshUnfreezeInputs() {
        // 每次重置游戏键位后、以及 TAS 读档替换了 Input 对象后，这些 VirtualInput 都可能变成新对象。
        // TAS 读档不会触发 Input.OnInitialize，所以在每次抓快照前也重新取一遍。
        unfreezeInputs = [Input.Dash, Input.Jump, Input.Grab, Input.MoveX, Input.MoveY, Input.Dash, Input.Aim, Input.Pause, Input.CrouchDash];
    }

    private static void Input_OnInitialize() {
        // 每次重置游戏键位后, 这些 VirtualInput 都是新的对象, 因此必须重新获取。
        // 注意如果在冻结期间重置键位，快照会被清空，之后仍按着的输入会被当成"新输入"而立即解冻。
        RefreshUnfreezeInputs();
        HeldSinceSnapshot.Clear();
        trackingInputs = false;
        settleFrames = 0;
    }

    [Load]
    private static void Load() {
        IL.Monocle.Engine.Update += IL_Engine_Update;
        On.Celeste.Level.Update += MakeGameFreezeAfterSaveLoad;
        On.Monocle.Scene.BeforeUpdate += UnfreezeTheGame;
        Everest.Events.Input.OnInitialize += Input_OnInitialize;
        SaveLoadAction.InternalSafeAdd(
            // 存档/读档开始时各抓一份快照，记录"操作完成瞬间按着什么"。
            // 读档的快照必须在 beforeLoadState 阶段抓取：SupportMInput 的 loadState 会把 MInput
            // 恢复成存档瞬间的状态，在那个阶段之后抓到的就不是"读档时你按着什么"了。
            beforeSaveState: _ => CaptureFreezeSnapshot(mInputWillBeRestored: false),
            beforeLoadState: _ => CaptureFreezeSnapshot(mInputWillBeRestored: true),
            clearState: () => {
                HeldSinceSnapshot.Clear();
                trackingInputs = false;
                settleFrames = 0;
            }
        );

        Hotkey.ToggleFreezeMode.RegisterPressedAction(scene => {
            if (scene is Level { Paused: false }) {
                // On <-> IgnoreHoldingKeys; Off falls through to On
                ModSettings.FreezeAfterLoadStateType = ModSettings.FreezeAfterLoadStateType == FreezeAfterSaveLoadStateType.On
                    ? FreezeAfterSaveLoadStateType.IgnoreHoldingKeys
                    : FreezeAfterSaveLoadStateType.On;
                SpeedrunToolModule.Instance.SaveSettings();
                PopupMessageUtils.ShowOptionState(DialogIds.FreezeAfterSaveLoadState.DialogClean(),
                    ModSettings.FreezeAfterLoadStateType.DialogClean());
            }
        });
    }

    [Unload]
    private static void Unload() {
        IL.Monocle.Engine.Update -= IL_Engine_Update;
        On.Celeste.Level.Update -= MakeGameFreezeAfterSaveLoad;
        On.Monocle.Scene.BeforeUpdate -= UnfreezeTheGame;
        Everest.Events.Input.OnInitialize -= Input_OnInitialize;
    }



    #region Freeze_AfterSaveLoad

    /// <summary>
    /// 存档/读档开始时记录"操作执行瞬间按着什么"，作为"自快照以来一直按着"的基线。
    /// 快照总是记录，不依赖当前模式：模式可能在操作和真正冻结之间被切换（例如 wipe 期间按了
    /// ToggleFreezeMode 热键），只有 IgnoreHoldingKeys 会使用这份基线。
    /// </summary>
    /// <param name="mInputWillBeRestored">
    /// 本次操作是否会恢复 MInput（读档会，存档不会）。读档时 SupportMInput 会把 MInput 回滚成
    /// 存档瞬间的状态，所以还需要跳过随后的帧，见 <see cref="UnfreezeTheGame"/>。
    /// </param>
    private static void CaptureFreezeSnapshot(bool mInputWillBeRestored) {
        // TAS 读档会替换 Input.* 对象（SupportInput），这里重新取一遍，避免之后非 TAS 存读档用到
        // 已经脱离 MInput.VirtualInputs 的旧对象（那样轴的 Value/turned 不再更新）。
        RefreshUnfreezeInputs();
        HeldSinceSnapshot.Clear();
        trackingInputs = true;
        settleFrames = mInputWillBeRestored ? SettleFramesAfterLoad : 0;

        // 输入对象在非 TAS 读档时不会被替换（SupportInput 只把 Inverted 之类的字段拷回现存对象），
        // 所以这里缓存的 VirtualInput 引用是稳定的；TAS 读档会替换它们，但 TAS 读档不会冻结。
        foreach (VirtualInput virtualInput in unfreezeInputs) {
            HeldSinceSnapshot[virtualInput] = virtualInput.IsCheck();
        }
    }

    /// <summary>
    /// 每帧刷新"自快照以来一直按着"的状态：只要观察到某个输入松开过，它就不再算"一直按着"，
    /// 之后再按下时就算新输入。从存档/读档一直调用到解冻（包括操作后的过场），
    /// 所以过场里松开过的键不会在冻结开始时还被当成"操作时按着"。
    /// </summary>
    private static void RefreshHeldSinceSnapshot() {
        foreach (VirtualInput virtualInput in unfreezeInputs) {
            if (HeldSinceSnapshot.TryGetValue(virtualInput, out bool held) && held && !virtualInput.IsCheck()) {
                HeldSinceSnapshot[virtualInput] = false;
            }
        }
    }

    /// <summary>
    /// 某个被监测的输入是否应该结束冻结。
    /// <para>On：任何输入（按下或保持按下）都解冻。</para>
    /// <para>IgnoreHoldingKeys：快照以来一直按着的输入被忽略；其余情况下按下的输入、
    /// 以及操作后新按下的键（包括同一动作的另一个键）都解冻。</para>
    /// <para>轴的"从 0 变为非 0"由最后的 IsCheck() 判断；这里的 IsPressed() 对轴是
    /// VirtualIntegerAxis.turned / VirtualJoystick.hTurned|vTurned，即"反向/翻转"，
    /// 所以快照时按住一个方向、之后按住反方向同样会解冻。</para>
    /// </summary>
    private static bool IsUnfreeze(VirtualInput input) {
        // 按下边沿：覆盖"同一个动作绑定了多个键，按了另一个键"的情况（cded6fc）。
        // 注意：读档时按着、存档时没按的键，会因为 SupportMInput 回滚 MInput 而在读档后产生一次
        // 并不存在的 Pressed 边沿；这个假边沿由 UnfreezeTheGame 里的 mInputSettling 跳过。
        // 存档不会回滚 MInput，所以存档冻结没有这个假边沿。
        if (input.IsPressed()) {
            return true;
        }

        bool heldSinceSnapshot =
            ModSettings.FreezeAfterLoadStateType == FreezeAfterSaveLoadStateType.IgnoreHoldingKeys &&
            HeldSinceSnapshot.TryGetValue(input, out bool held) && held;
        if (heldSinceSnapshot) {
            return false;
        }

        return input.IsCheck();
    }

    private static void UnfreezeTheGame(On.Monocle.Scene.orig_BeforeUpdate orig, Scene self) {
        // 这里不能用 ModSettings.Enabled 门控：MakeGameFreezeAfterSaveLoad 只看 State != None 就跳过
        // Level.Update（不看 Enabled），所以冻结期间把 SRT 关掉的话，Level.Update 会一直停着，而 SRT
        // 热键也都失效（HotkeyManager 同样门控 Enabled），游戏就再也没法解冻。State == Waiting 本身
        // 已经限定作用域，这里始终处理。
        if (self is Level level) {
            // 关闭冻结(Off)模式的解冻推迟到了这里：wipe 完成的那一帧 Level.Update 已被跳过，
            // 若在那一帧把 State 置回 None，Scene.AfterUpdate 里的计时器会把这一帧算进去
            // （房间计时比章节时间多一帧）。放在 BeforeUpdate 里解冻，State 的翻转就发生在
            // Level.Update 之前，计时器和 Session.Time 对同一批帧计数。
            if (StateManager.Instance.TryConsumePendingOffUnfreeze()) {
                StateManager.Instance.OutOfFreeze(level);
            }

            // SupportMInput 在读档的 loadState 阶段把 MInput 的设备状态恢复成"存档瞬间"的样子，
            // 读档当帧的 IsCheck() 读到的是存档时的旧状态：此时刷新快照会把"读档时按着"的信息抹掉。
            // 所以读档后先跳过 SettleFramesAfterLoad 帧（按 BeforeUpdate 调用次数数，不能用会被存档
            // 恢复的 Engine.FrameCounter），见 SettleFramesAfterLoad 的注释。
            bool mInputSettling = settleFrames > 0;
            if (mInputSettling) {
                settleFrames--;
            }

            if (!mInputSettling) {
                // MInput.Disabled 为 true 时，被监测的输入都走 Check/Pressed、会被判成 false，只有死亡统计
                // 热键（走裸 KeyboardState）还能解冻。正常游戏在存档/读档瞬间 Disabled 一定是 false，但第三方
                // mod 可能把它带进存档；读档后它会被一起恢复，而冻结期间 Level.Update 被跳过、游戏没机会改回来。
                // 输入读不到时冻结本来就不可能被满足，直接结束，避免只剩一个热键可解。
                if (State == State.Waiting &&
                    (MInput.Disabled || unfreezeInputs.Any(IsUnfreeze) || Hotkey.CheckDeathStatistics.Pressed())) {
                    HeldSinceSnapshot.Clear();
                    trackingInputs = false;
                    StateManager.Instance.OutOfFreeze(level);
                }

                // 从存档/读档到解冻之间持续跟踪（包括操作后的过场）：松开过的键就不再是"一直按着"。
                // 读不到输入的帧（例如控制台打开时 MInput.Update 会清空键盘状态）也按"松开"处理：
                // 那是玩家主动操作，清掉基线可以接受，最坏只是让冻结提前结束，不会卡死。
                if (trackingInputs) {
                    RefreshHeldSinceSnapshot();
                }
            }

            // 冻结结束（或这次操作最终没有冻结）后不再跟踪，正常游玩时没必要每帧刷新。
            if (State == State.None) {
                trackingInputs = false;
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
