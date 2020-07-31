using System;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad {
    internal class AutoLoadStateUtils {
        private static StateManager Manager => StateManager.Instance;

        public static void OnHook() {
            On.Celeste.AreaData.DoScreenWipe += AutoLoadStateWhenDeath;
        }

        public static void OnUnhook() {
            On.Celeste.AreaData.DoScreenWipe -= AutoLoadStateWhenDeath;
        }

        // Everest 的 Bug，另外的 Mod Hook 了 PlayerDeadBody.End 方法后 Level.DoScreenWipe Hook 的方法 wipeIn 为 false 时就不触发了
        // 所以改成了 Hook AreaData.DoScreenWipe 方法
        private static void AutoLoadStateWhenDeath(On.Celeste.AreaData.orig_DoScreenWipe orig, AreaData self, Scene scene,
            bool wipeIn, Action onComplete) {
            if (SpeedrunToolModule.Settings.Enabled && SpeedrunToolModule.Settings.AutoLoadAfterDeath && Manager.IsSaved &&
                !wipeIn && scene is Level level &&
                onComplete != null && (onComplete == level.Reload || scene.Entities.FindFirst<PlayerDeadBody>()?.HasGolden == true)) {
                Action complete = onComplete;
                onComplete = () => {
                    // 死亡时快速读档，这时候候已经恢复完毕，无需再次读档
                    if (scene.Entities.FindFirst<Player>() != null) return;

                    if (Manager.IsSaved) {
                        Manager.LoadState();
                    } else {
                        complete();
                    }
                };
            }

            orig(self, scene, wipeIn, onComplete);
        }
    }
}