using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class FireBallAction : AbstractEntityAction {
        private Dictionary<string, FireBall> _savedFireBalls = new Dictionary<string, FireBall>();

        public override void OnQuickSave(Level level) {
            _savedFireBalls = level.Tracker.GetCastEntities<FireBall>()
                .ToDictionary(ball => ball.GetExtendedDataValue<string>("nodesIndexKey"));
        }

        private void RestoreFireBallState(On.Celeste.FireBall.orig_ctor_Vector2Array_int_int_float_float orig,
            FireBall self,
            Vector2[] nodes, int amount, int index, float offset, float speedMult) {
            orig(self, nodes, amount, index, offset, speedMult);

            string nodesIndexKey = string.Join("", nodes.Select(vector2 => vector2.ToString())) + index;
            self.SetExtendedDataValue("nodesIndexKey", nodesIndexKey);

            if (IsLoadStart && _savedFireBalls.ContainsKey(nodesIndexKey)) {
                FireBall savedFireBall = _savedFireBalls[nodesIndexKey];
                self.CopyPrivateField("percent", savedFireBall);

                if ((bool) savedFireBall.GetPrivateField("iceMode")) {
                    self.Visible = self.Collidable = savedFireBall.Collidable;
                    self.Add(new Coroutine(RestoreBroken(self, savedFireBall)));
                }
            }
        }

        private static IEnumerator RestoreBroken(FireBall self, FireBall savedFireBall) {
            self.CopyPrivateField("broken", savedFireBall);
            yield break;
        }

        private void SpriteOnPlay(On.Monocle.Sprite.orig_Play orig, Sprite self, string id, bool restart,
            bool randomizeFrame) {
            orig(self, id, restart, randomizeFrame);

            if (id == "ice" && !restart && randomizeFrame && self.Entity is FireBall iceBall && iceBall.Collidable) {
                iceBall.Visible = true;
            }
        }


        public override void OnClear() {
            _savedFireBalls.Clear();
        }

        public override void OnLoad() {
            On.Celeste.FireBall.ctor_Vector2Array_int_int_float_float += RestoreFireBallState;
            On.Monocle.Sprite.Play += SpriteOnPlay;
        }

        public override void OnUnload() {
            On.Celeste.FireBall.ctor_Vector2Array_int_int_float_float -= RestoreFireBallState;
            On.Monocle.Sprite.Play -= SpriteOnPlay;
        }

        public override void OnInit() {
            typeof(FireBall).AddToTracker();
        }
    }
}