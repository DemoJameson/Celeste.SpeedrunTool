namespace Celeste.Mod.SpeedrunTool.SaveLoad;

internal static class AutoClearState {
    [Load]
    private static void Load() {
        On.Celeste.Player.OnTransition += PlayerOnOnTransition;
    }

    [Unload]
    private static void Unload() {
        On.Celeste.Player.OnTransition -= PlayerOnOnTransition;
    }

    private static void PlayerOnOnTransition(On.Celeste.Player.orig_OnTransition orig, Player self) {
        orig(self);
        if (ModSettings.Enabled
            && ModSettings.AutoClearStateOnScreenTransition
            && StateManager.Instance.IsSaved
            && !StateManager.Instance.SavedByTas
            && self.Scene is Level
           ) {
            StateManager.Instance.ClearStateAndShowMessage();
        }
    }
}