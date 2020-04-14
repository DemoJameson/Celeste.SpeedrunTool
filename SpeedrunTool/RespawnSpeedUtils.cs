using Microsoft.Xna.Framework;
using On.Monocle;

namespace Celeste.Mod.SpeedrunTool {
    public class RespawnSpeedUtils {
        public static void Load() {
            Engine.Update += RespawnSpeed;
        }

        public static void Unload() {
            Engine.Update -= RespawnSpeed;
        } 
        
        private static void RespawnSpeed(Engine.orig_Update orig, Monocle.Engine self, GameTime time) {
            orig(self, time);

            if (!SpeedrunToolModule.Settings.Enabled || SpeedrunToolModule.Settings.RespawnSpeed == 1) {
                return;
            }

            if (!(Monocle.Engine.Scene is Level level)) {
                return;
            }

            Player player = level.Entities.FindFirst<Player>();

            // level 场景中 player == null 代表人物死亡
            if (player != null && player.StateMachine.State == Player.StIntroRespawn || player == null) {
                for (int i = 1; i < SpeedrunToolModule.Settings.RespawnSpeed; i++) {
                    orig(self, time);
                }
            }
        }
    }
}