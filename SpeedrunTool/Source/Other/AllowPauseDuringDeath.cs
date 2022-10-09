using Celeste.Mod.SpeedrunTool.Utils;
using MonoMod.Cil;

namespace Celeste.Mod.SpeedrunTool.Other;

public static class AllowPauseDuringDeath {
    [Load]
    private static void Load() {
        On.Celeste.Level.Update += LevelOnUpdate;
        typeof(Level).GetMethodInfo("orig_Pause").ILHook(DisableRetryMenu);
    }

    [Unload]
    private static void Unload() {
        On.Celeste.Level.Update -= LevelOnUpdate;
    }

    private static void LevelOnUpdate(On.Celeste.Level.orig_Update orig, Level level) {
        orig(level);

        if (!ModSettings.Enabled || !ModSettings.AllowPauseDuringDeath) {
            return;
        }
        
        if (TasUtils.Running) {
            return;
        }

        if (level.CanPause) {
            return;
        }

        if (level.Paused || level.PauseLock || level.SkippingCutscene || level.Transitioning || level.wasPaused) {
            return;
        }

        if (level.Wipe != null && level.GetPlayer()?.StateMachine.State != Player.StIntroRespawn) {
            return;
        }

        if (Input.QuickRestart.Pressed) {
            Input.QuickRestart.ConsumeBuffer();
            level.Pause(0, minimal: false, quickReset: true);
        } else if (Input.Pause.Pressed || Input.ESC.Pressed) {
            Input.Pause.ConsumeBuffer();
            Input.ESC.ConsumeBuffer();
            level.Pause();
        }
    }

    private static void DisableRetryMenu(ILCursor ilCursor, ILContext ilContext) {
        if (ilCursor.TryGotoNext(MoveType.After, ins => ins.MatchLdfld<Level>("CanRetry"))) {
            ilCursor.EmitDelegate<Func<bool, bool>>(canRetry => {
                if (!ModSettings.Enabled || !ModSettings.AllowPauseDuringDeath || TasUtils.Running) {
                    return canRetry;
                }

                return !Engine.Scene.IsPlayerDead() && canRetry;
            });
        } 
    }
}