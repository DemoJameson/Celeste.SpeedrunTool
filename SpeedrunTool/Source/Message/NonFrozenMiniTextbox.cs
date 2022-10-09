using System.Reflection;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Celeste.Mod.SpeedrunTool.Utils;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;

namespace Celeste.Mod.SpeedrunTool.Message;

[Tracked]
public class NonFrozenMiniTextbox : MiniTextbox {
    private static readonly MethodInfo RoutineMethod = typeof(MiniTextbox).GetMethodInfo("Routine").GetStateMachineTarget();

    private NonFrozenMiniTextbox(string dialogId, string message = null) : base(dialogId) {
        AddTag(Tags.Global | Tags.HUD | Tags.FrozenUpdate | Tags.PauseUpdate | Tags.TransitionUpdate);
        Add(new IgnoreSaveLoadComponent());
        if (message != null) {
            text = FancyText.Parse($"{{portrait {(IsPlayAsBadeline() ? "BADELINE" : "MADELINE")} left normal}}{message}", 1544, 2);
        }
    }

    [Load]
    private static void Load() {
        IL.Celeste.MiniTextbox.Render += MiniTextboxOnRender;
        RoutineMethod.ILHook(QuicklyClose);
    }

    [Unload]
    private static void Unload() {
        IL.Celeste.MiniTextbox.Render -= MiniTextboxOnRender;
    }

    private static void QuicklyClose(ILCursor ilCursor, ILContext il) {
        if (ilCursor.TryGotoNext(MoveType.After, i => i.OpCode == OpCodes.Ldarg_0, i => i.MatchLdcR4(3))) {
            ilCursor.Emit(OpCodes.Ldarg_0);
            ilCursor.Emit(OpCodes.Ldfld, RoutineMethod.DeclaringType.GetField("<>4__this"));
            ilCursor.EmitDelegate<Func<float, MiniTextbox, float>>((waitTimer, textbox) => textbox is NonFrozenMiniTextbox ? 1f : waitTimer);
        }
    }

    private static void MiniTextboxOnRender(ILContext il) {
        ILCursor ilCursor = new(il);
        if (ilCursor.TryGotoNext(MoveType.After, i => i.MatchCallvirt<Level>("get_FrozenOrPaused"))) {
            ilCursor.Emit(OpCodes.Ldarg_0).EmitDelegate<Func<bool, MiniTextbox, bool>>(
                (frozenOrPaused, textbox) => textbox is not NonFrozenMiniTextbox && frozenOrPaused);
        }
    }

    public static void Show(string dialogId, string message) {
        if (Engine.Scene is { } scene) {
            scene.Entities.FindAll<NonFrozenMiniTextbox>().ForEach(textbox => textbox.RemoveSelf());
            scene.Add(new NonFrozenMiniTextbox(ChooseDialog(dialogId), message));
        }
    }

    private static bool IsPlayAsBadeline() {
        if (Engine.Scene.GetPlayer() is { } player) {
            return player.Sprite.Mode == PlayerSpriteMode.MadelineAsBadeline;
        } else {
            return SaveData.Instance.Assists.PlayAsBadeline;
        }
    }

    private static string ChooseDialog(string madelineDialog) {
        bool isBadeline = IsPlayAsBadeline();

        if (madelineDialog == null) {
            // 仅用于 NonFrozenMiniTextbox 父类构造函数中确定头像，具体显示内容以 message 为准
            return isBadeline ? DialogIds.ClearStateDialogBadeline : DialogIds.ClearStateDialog;
        } else {
            return isBadeline ? $"{madelineDialog}_BADELINE" : madelineDialog;
        }
    }
}