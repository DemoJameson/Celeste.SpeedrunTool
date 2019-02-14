using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions
{
    public class FireBallAction : AbstractEntityAction
    {
        private Dictionary<string, FireBall> _savedFireBalls = new Dictionary<string, FireBall>();

        public override void OnQuickSave(Level level)
        {
            _savedFireBalls = level.Tracker.GetCastEntities<FireBall>().ToDictionary(ball => ball.GetExtendedDataValue<string>("nodesIndexKey"));
        }

        private void RestoreFireBallPosition(On.Celeste.FireBall.orig_ctor_Vector2Array_int_int_float_float orig, FireBall self,
            Vector2[] nodes, int amount, int index, float offset, float speedMult)
        {
            orig(self, nodes, amount, index, offset, speedMult);

            string nodesIndexKey = string.Join("", nodes.Select(vector2 => vector2.ToString())) + index;
            self.SetExtendedDataValue("nodesIndexKey", nodesIndexKey);

            if (IsLoadStart && _savedFireBalls.ContainsKey(nodesIndexKey))
            {
                self.CopyPrivateField("percent", _savedFireBalls[nodesIndexKey]);
            }
        }

        public override void OnClear()
        {
            _savedFireBalls.Clear();
        }

        public override void OnLoad()
        {
            On.Celeste.FireBall.ctor_Vector2Array_int_int_float_float += RestoreFireBallPosition;
        }

        public override void OnUnload()
        {
            On.Celeste.FireBall.ctor_Vector2Array_int_int_float_float -= RestoreFireBallPosition;
        }

        public override void OnInit()
        {
            typeof(FireBall).AddToTracker();
        }
    }
}