using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.RoomTimer;
using Mono.Cecil.Cil;
using MonoMod.Cil;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;

internal static class StateMarkUtils {
    private const string SavedStateFlag = "SpeedrunTool_SavedSate";
    private static SpriteBank mySpriteBank;

    [Load]
    private static void Load() {
        On.Celeste.Strawberry.Added += StrawberryOnAdded;
        IL.Celeste.SpeedrunTimerDisplay.DrawTime += SetSaveStateColor;
        IL.Celeste.Level.Reload += LevelOnReload;
        SaveLoadAction.SafeAdd(ReColor, ReColor);
    }

    [Unload]
    private static void Unload() {
        On.Celeste.Strawberry.Added -= StrawberryOnAdded;
        IL.Celeste.SpeedrunTimerDisplay.DrawTime -= SetSaveStateColor;
        IL.Celeste.Level.Reload -= LevelOnReload;
    }

    [LoadContent]
    private static void LoadContent() {
        mySpriteBank = new SpriteBank(GFX.Game, "Graphics/SpeedrunToolSprites.xml");
    }

    private static void ReColor(Dictionary<Type, Dictionary<string, object>> savedValues, Level level) {
        if (StateManager.Instance.SavedByTas) {
            return;
        }

        // recolor golden berry
        foreach (Strawberry berry in level.Entities.FindAll<Strawberry>().Where(strawberry => strawberry.Golden)) {
            TryRecolorSprite(berry);
        }

        // recolor timer
        SetSavedStateFlag(level);
    }

    private static void StrawberryOnAdded(On.Celeste.Strawberry.orig_Added orig, Strawberry self, Scene scene) {
        orig(self, scene);
        if (self.Golden && !StateManager.Instance.SavedByTas && scene is Level level && GetSavedStateFlag(level)) {
            TryRecolorSprite(self);
        }
    }

    private static void TryRecolorSprite(Strawberry berry) {
        if (berry.sprite is not { } sprite) {
            return;
        }

        string spriteId = berry.GetType().FullName switch {
            "Celeste.Mod.CollabUtils2.Entities.SpeedBerry" => "speedrun_tool_speedberry",
            "Celeste.Mod.CollabUtils2.Entities.SilverBerry" => "speedrun_tool_silverberry",
            _ => "speedrun_tool_goldberry"
        };

        mySpriteBank.CreateOn(sprite, spriteId);
    }

    // Copy from https://github.com/rhelmot/CelesteRandomizer/blob/master/Randomizer/Patches/sessionLifecycle.cs#L144
    private static void SetSaveStateColor(ILContext il) {
        ILCursor cursor = new(il);
        if (!cursor.TryGotoNext(MoveType.After, instr => instr.MatchLdarg(3))) {
            return;
        }

        if (!cursor.TryGotoNext(MoveType.Before
                , instr => instr.MatchLdcI4(0)
                , instr => instr.OpCode == OpCodes.Stloc_S)) {
            return;
        }

        ILLabel afterInstr = cursor.MarkLabel();

        cursor.Index = 0;
        if (!cursor.TryGotoNext(MoveType.AfterLabel, instr => instr.MatchLdarg(3))) {
            return;
        }

        cursor.EmitDelegate<Func<bool>>(IsChangeTimerColor);

        ILLabel beforeInstr = cursor.DefineLabel();
        cursor.Emit(OpCodes.Brfalse, beforeInstr);

        cursor.Emit(OpCodes.Ldstr, "afdded");
        cursor.Emit(OpCodes.Call, typeof(Calc).GetMethod("HexToColor", new[] {typeof(string)}));
        cursor.Emit(OpCodes.Ldarg, 6);
        cursor.Emit(OpCodes.Call, typeof(Color).GetMethod("op_Multiply"));
        cursor.Emit(OpCodes.Stloc, 5);

        cursor.Emit(OpCodes.Ldstr, "8fc7db");
        cursor.Emit(OpCodes.Call, typeof(Calc).GetMethod("HexToColor", new[] {typeof(string)}));
        cursor.Emit(OpCodes.Ldarg, 6);
        cursor.Emit(OpCodes.Call, typeof(Color).GetMethod("op_Multiply"));
        cursor.Emit(OpCodes.Stloc, 6);

        cursor.Emit(OpCodes.Br, afterInstr);
        cursor.MarkLabel(beforeInstr);
    }

    private static bool IsChangeTimerColor() {
        return ModSettings.RoomTimerType == RoomTimerType.Off && Engine.Scene is Level {Completed: false} level && GetSavedStateFlag(level) && !StateManager.Instance.SavedByTas;
    }

    private static void LevelOnReload(ILContext il) {
        ILCursor ilCursor = new(il);
        if (ilCursor.TryGotoNext(ins => ins.OpCode == OpCodes.Ldarg_0,
                ins => ins.OpCode == OpCodes.Ldc_I4_0,
                ins => ins.MatchStfld<Level>("TimerStarted")
            )) {
            ilCursor.Emit(OpCodes.Ldarg_0).EmitDelegate<Action<Level>>(level => {
                if (!StateManager.Instance.IsSaved) {
                    RemoveSavedStateFlag(level);
                }
            });
        }
    }

    public static void SetSavedStateFlag(Level level) {
        level.Session.SetFlag(SavedStateFlag);
    }

    public static bool GetSavedStateFlag(Level level) {
        return level.Session.GetFlag(SavedStateFlag);
    }

    private static void RemoveSavedStateFlag(Level level) {
        level.Session.SetFlag(SavedStateFlag, false);
    }
}