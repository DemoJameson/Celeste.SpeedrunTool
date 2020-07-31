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
            if (SpeedrunToolModule.Settings.Enabled && Manager.IsSaved &&
                !wipeIn && scene is Level level &&
                onComplete != null && (onComplete == level.Reload || scene.Entities.FindFirst<PlayerDeadBody>()?.HasGolden == true)) {
                Action origComplete = onComplete;
                onComplete = () => {
                    // 避免死亡时读档后重复读档
                    if(level.Entities.FindFirst<Player>() != null) return;
                    origComplete();
                };
                if (SpeedrunToolModule.Settings.AutoLoadAfterDeath) {
                    onComplete = () => {
                        // 避免死亡时读档后重复读档
                        if(level.Entities.FindFirst<Player>() != null) return;
                        if (Manager.IsSaved) {
                            Manager.LoadState();
                        } else {
                            origComplete();
                        }
                    };
                }
            }

            orig(self, scene, wipeIn, onComplete);
        }
    }
}