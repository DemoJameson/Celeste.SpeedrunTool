using Celeste.Mod.SpeedrunTool.Utils;

namespace Celeste.Mod.SpeedrunTool.Other;

public static class AllowPauseDuringDeath {
    private static readonly ReflectionExtensions.GetDelegate<bool> GetWasPaused = typeof(Level).GetFieldGetDelegate<bool>("wasPaused");

    [Load]
    private static void Load() {
        On.Celeste.Level.Update += LevelOnUpdate;
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

        if (level.Paused || level.PauseLock || level.SkippingCutscene || level.Transitioning || level.Wipe != null || GetWasPaused(level)) {
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
}