namespace Celeste.Mod.SpeedrunTool.SaveLoad.Utils;

internal static class AutoClearStateOnScreenTrantition {
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
            bool b = SaveSlotsManager.ClearState();
            // 只清除当前槽位, 不清除所有

            if (b) {
                Message.PopupMessageUtils.Show("Auto Clear State on Screen Transition", null);
            }

        }
    }
}