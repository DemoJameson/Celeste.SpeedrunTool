using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using On.Celeste.Pico8;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions
{
    public class BadelineBoostAction : AbstractEntityAction
    {
        private Dictionary<EntityID, Vector2[]> _savedNodes = new Dictionary<EntityID, Vector2[]>();

        public override void OnQuickSave(Level level)
        {
            _savedNodes = level.Tracker.GetCastEntities<BadelineBoost>().ToDictionary(boost => boost.GetEntityId(),
                boost =>
                {
                    int nodeIndex = (int) boost.GetPrivateField("nodeIndex");
                    Vector2[] nodes = boost.GetPrivateField("nodes") as Vector2[];
                    Vector2[] result = nodes?.Skip(nodeIndex).ToArray();
                    return result ?? new Vector2[] { };
                });
        }

        private static void AttachEntityId(On.Celeste.BadelineBoost.orig_ctor_EntityData_Vector2 orig,
            BadelineBoost self, EntityData data, Vector2 offset)
        {
            self.SetEntityId(data);
            orig(self, data, offset);
        }

        private void RestoreBadelineBoostState(On.Celeste.BadelineBoost.orig_ctor_Vector2Array_bool orig,
            BadelineBoost self, Vector2[] nodes, bool lockCamera)
        {
            EntityID entityId = self.GetEntityId();
            if (IsLoadStart)
            {
                if (_savedNodes.ContainsKey(entityId))
                {
                    Vector2[] savedNodes = _savedNodes[entityId];
                    if (savedNodes.Length == 0)
                        orig(self, nodes.Skip(nodes.Length - 1).ToArray(), false);
                    else
                        orig(self, savedNodes, savedNodes.Length != 1);
                }
                else
                {
                    orig(self, nodes.Skip(nodes.Length - 1).ToArray(), false);
                }
            }
            else
            {
                orig(self, nodes, lockCamera);
            }
        }

        public override void OnClear()
        {
            _savedNodes.Clear();
        }

        public override void OnLoad()
        {
            On.Celeste.BadelineBoost.ctor_EntityData_Vector2 += AttachEntityId;
            On.Celeste.BadelineBoost.ctor_Vector2Array_bool += RestoreBadelineBoostState;
        }

        public override void OnUnload()
        {
            On.Celeste.BadelineBoost.ctor_EntityData_Vector2 -= AttachEntityId;
            On.Celeste.BadelineBoost.ctor_Vector2Array_bool -= RestoreBadelineBoostState;
        }

        public override void OnInit()
        {
            typeof(BadelineBoost).AddToTracker();
        }
    }
}