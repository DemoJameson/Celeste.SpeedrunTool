using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.DeathStatistics;
using Celeste.Mod.SpeedrunTool.RoomTimer;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Celeste.Mod.SpeedrunTool.Utils;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.SpeedrunTool.Other;

public static class RespawnRestartSpeed {
    private const string StopFastRestartFlag = nameof(StopFastRestartFlag);

    [Load]
    private static void Hook() {
        using (new DetourContext {After = new List<string> {"*"}}) {
            On.Monocle.Engine.Update += RespawnSpeed;
        }
    }

    [Load]
    private static void Load() {
        if (ModUtils.VanillaAssembly.GetType("Celeste.Level+<>c__DisplayClass150_0")?.GetMethodInfo("<GiveUp>b__0") is { } methodInfo) {
            methodInfo.ILHook(ModRestartMenu);
        } else if (ModUtils.VanillaAssembly.GetType("Celeste.Level+<>c__DisplayClass147_0")?.GetMethodInfo("<GiveUp>b__0") is { } methodInfo1312) {
            // 兼容 v1312
            methodInfo1312.ILHook(ModRestartMenu);
        }

        SaveLoadAction.SafeAdd(beforeSaveState: level => level.Session.SetFlag(StopFastRestartFlag));
    }

    [Unload]
    private static void Unload() {
        On.Monocle.Engine.Update -= RespawnSpeed;
    }

    private static void RespawnSpeed(On.Monocle.Engine.orig_Update orig, Engine self, GameTime time) {
        orig(self, time);

        if (!ModSettings.Enabled || ModSettings.RespawnSpeed == 1 && ModSettings.RestartChapterSpeed == 1 || TasUtils.Running) {
            return;
        }

        if (Engine.Scene is not Level level) {
            return;
        }

        if (level.Paused) {
            return;
        }

        Player player = level.GetPlayer();

        // 加速复活过程
        for (int i = 1; i < ModSettings.RespawnSpeed && (player == null || player.StateMachine.State == Player.StIntroRespawn); i++) {
            orig(self, time);
        }

        // 加速章节启动
        for (int i = 1; i < ModSettings.RestartChapterSpeed && RequireFastRestart(level, player); i++) {
            orig(self, time);
        }
    }

    private static bool RequireFastRestart(Level level, Player player) {
        if (level.Session.GetFlag(StopFastRestartFlag)) {
            return false;
        }

        bool result = !level.TimerStarted && level.Session.Area.ID != 8 && !level.SkippingCutscene &&
                      player?.StateMachine.State != Player.StIntroRespawn ||
                      level.TimerStarted && !level.InCutscene && level.Session.FirstLevel && player?.InControl != true;

        if (!result) {
            level.Session.SetFlag(StopFastRestartFlag);
        }

        return result;
    }

    // 移除重启章节前面的黑屏
    private static void ModRestartMenu(ILCursor ilCursor, ILContext il) {
        if (ilCursor.TryGotoNext(
                MoveType.After,
                ins => ins.OpCode == OpCodes.Ldfld && ins.Operand.ToString().EndsWith("::restartArea")
            )) {
            ilCursor.Emit(OpCodes.Dup).EmitDelegate<Action<bool>>((restartArea) => {
                if (restartArea && ModSettings.Enabled && ModSettings.SkipRestartChapterScreenWipe && Engine.Scene is Level level && !TasUtils.Running) {
                    level.OnEndOfFrame += () => {
                        Engine.Scene = new LevelLoader(level.Session.Restart());
                        RoomTimerManager.TryTurnOffRoomTimer(); 
                        DeathStatisticsManager.Clear();
                    };
                }
            });
        }
    }
}