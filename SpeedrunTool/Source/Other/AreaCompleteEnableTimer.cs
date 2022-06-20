namespace Celeste.Mod.SpeedrunTool.Other;

public static class AreaCompleteEnableTimer {
    private static bool shouldRestoreTimer;

    [Load]
    private static void Load() {
        On.Celeste.Level.RegisterAreaComplete += EnableTimer;
        On.Celeste.Celeste.OnSceneTransition += RestoreTimer;
    }

    [Unload]
    private static void Unload() {
        On.Celeste.Level.RegisterAreaComplete -= EnableTimer;
        On.Celeste.Celeste.OnSceneTransition -= RestoreTimer;
    }

    private static void EnableTimer(On.Celeste.Level.orig_RegisterAreaComplete orig, Level self) {
        orig(self);

        if (Settings.Instance.SpeedrunClock is SpeedrunType.Off && ModSettings.AreaCompleteEnableTimerType is not SpeedrunType.Off && !AreaData.Get(self.Session).Interlude_Safe) {
            Settings.Instance.SpeedrunClock = ModSettings.AreaCompleteEnableTimerType;
            shouldRestoreTimer = true;
        }
    }

    private static void RestoreTimer(On.Celeste.Celeste.orig_OnSceneTransition orig, Celeste self, Scene last, Scene next) {
        orig(self, last, next);

        if (shouldRestoreTimer && !(next is LevelExit or AreaComplete)) { 
            Settings.Instance.SpeedrunClock = SpeedrunType.Off;
            shouldRestoreTimer = false;
        }
    }
}