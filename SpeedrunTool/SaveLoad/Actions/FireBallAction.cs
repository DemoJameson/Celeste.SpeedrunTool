using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Monocle;

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
                self.CopyFrom(savedFireBall);
                self.CopyFields(typeof(FireBall), savedFireBall, "percent", "broken");
                self.Add(new Coroutine(RestoreBroken(self, savedFireBall)));
            }
        }

        private static IEnumerator RestoreBroken(FireBall self, FireBall savedFireBall) {
            self.CopyFields(typeof(FireBall), savedFireBall, "iceMode", "speedMult");
            self.CopySprite(savedFireBall, "sprite");
            yield break;
        }

        public override void OnClear() {
            savedFireBalls.Clear();
        }

        public override void OnLoad() {
            On.Celeste.FireBall.ctor_Vector2Array_int_int_float_float_bool += RestoreFireBallState;
        }

        public override void OnUnload() {
            On.Celeste.FireBall.ctor_Vector2Array_int_int_float_float_bool -= RestoreFireBallState;
        }
    }
}