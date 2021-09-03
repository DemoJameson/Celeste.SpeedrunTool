using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using MonoMod.RuntimeDetour;
using On.Monocle;

namespace Celeste.Mod.SpeedrunTool.Other {
    public static class RespawnSpeedUtils {
        public static void Load() {
            using (new DetourContext {After = new List<string> {"*"}}) {
                Engine.Update += RespawnSpeed;
            }
        }

        public static void Unload() {
            Engine.Update -= RespawnSpeed;
        }

        private static void RespawnSpeed(Engine.orig_Update orig, Monocle.Engine self, GameTime time) {
            orig(self, time);

            if (!SpeedrunToolModule.Settings.Enabled || SpeedrunToolModule.Settings.RespawnSpeed == 1) {
                return;
            }

            if (Monocle.Engine.Scene is not Level level) {
                return;
            }

            if (level.Paused) {
                return;
            }

            Player player = level.GetPlayer();

            // 加速复活过程
            if (player == null || player.StateMachine.State == Player.StIntroRespawn) {
                for (int i = 1; i < SpeedrunToolModule.Settings.RespawnSpeed; i++) {
                    orig(self, time);
                }
            }
        }
    }
}