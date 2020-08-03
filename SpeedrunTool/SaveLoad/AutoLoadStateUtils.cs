using Celeste.Mod.SpeedrunTool.Extensions;

namespace Celeste.Mod.SpeedrunTool.SaveLoad {
    internal static class AutoLoadStateUtils {
        private static StateManager Manager => StateManager.Instance;

        public static void OnHook() {
            On.Celeste.PlayerDeadBody.End += PlayerDeadBodyOnEnd;
        }

        public static void OnUnhook() {
            On.Celeste.PlayerDeadBody.End -= PlayerDeadBodyOnEnd;
        }

        private static void PlayerDeadBodyOnEnd(On.Celeste.PlayerDeadBody.orig_End orig, PlayerDeadBody self) {
            if (SpeedrunToolModule.Settings.Enabled
                && SpeedrunToolModule.Settings.AutoLoadAfterDeath
                && Manager.IsSaved
                && !(bool) self.GetField("finished")
            ) {
                self.RemoveSelf();
                self.SceneAs<Level>().OnEndOfFrame += () => Manager.LoadState();
            } else {
                orig(self);
            }
        }
    }
}