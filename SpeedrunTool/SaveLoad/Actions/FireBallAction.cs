using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using On.Monocle;
using Coroutine = Monocle.Coroutine;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class FireBallAction : AbstractEntityAction {
        private Dictionary<string, FireBall> savedFireBalls = new Dictionary<string, FireBall>();

        public override void OnQuickSave(Level level) {
            savedFireBalls = level.Entities.FindAll<FireBall>()
                .ToDictionary(ball => ball.GetExtendedDataValue<string>("nodesIndexKey"));
        }

        private void RestoreFireBallState(On.Celeste.FireBall.orig_ctor_Vector2Array_int_int_float_float_bool orig,
            FireBall self,
            Vector2[] nodes, int amount, int index, float offset, float speedMult, bool notCoreMode) {
            orig(self, nodes, amount, index, offset, speedMult, notCoreMode);

            string nodesIndexKey = string.Join("", nodes.Select(vector2 => vector2.ToString())) + index;
            self.SetExtendedDataValue("nodesIndexKey", nodesIndexKey);

            if (IsLoadStart && savedFireBalls.ContainsKey(nodesIndexKey)) {
                FireBall savedFireBall = savedFireBalls[nodesIndexKey];
                self.CopyField(typeof(FireBall), "percent", savedFireBall);

                if ((bool) savedFireBall.GetField(typeof(FireBall), "iceMode")) {
                    self.Visible = self.Collidable = savedFireBall.Collidable;
                    self.Add(new Coroutine(RestoreBroken(self, savedFireBall)));
                }
            }
        }

        private static IEnumerator RestoreBroken(FireBall self, FireBall savedFireBall) {
            self.CopyField(typeof(FireBall), "broken", savedFireBall);
            yield break;
        }

        private static void SpriteOnPlay(Sprite.orig_Play orig, Monocle.Sprite self, string id, bool restart,
            bool randomizeFrame) {
            orig(self, id, restart, randomizeFrame);

            if (id == "ice" && !restart && randomizeFrame && self.Entity is FireBall iceBall && iceBall.Collidable) {
                iceBall.Visible = true;
            }
        }


        public override void OnClear() {
            savedFireBalls.Clear();
        }

        public override void OnLoad() {
            On.Celeste.FireBall.ctor_Vector2Array_int_int_float_float_bool += RestoreFireBallState;
            Sprite.Play += SpriteOnPlay;
        }

        public override void OnUnload() {
            On.Celeste.FireBall.ctor_Vector2Array_int_int_float_float_bool -= RestoreFireBallState;
            Sprite.Play -= SpriteOnPlay;
        }
    }
}