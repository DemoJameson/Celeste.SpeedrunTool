using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.SpeedrunTool.Other {
    public static class RespawnSpeedUtils {
        [Load]
        private static void Load() {
            using (new DetourContext {After = new List<string> {"*"}}) {
                On.Monocle.Engine.Update += RespawnSpeed;
            }
        }

        [Unload]
        private static void Unload() {
            On.Monocle.Engine.Update -= RespawnSpeed;
        }

        private static void RespawnSpeed(On.Monocle.Engine.orig_Update orig, Engine self, GameTime time) {
            orig(self, time);

            if (!SpeedrunToolModule.Settings.Enabled || SpeedrunToolModule.Settings.RespawnSpeed == 1) {
                return;
            }

            if (Engine.Scene is not Level level) {
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