using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class BadelineBoostAction : AbstractEntityAction {
        private Dictionary<EntityID, Vector2[]> savedNodes = new Dictionary<EntityID, Vector2[]>();

        public override void OnQuickSave(Level level) {
            savedNodes = level.Entities.FindAll<BadelineBoost>().ToDictionary(boost => boost.GetEntityId(),
                boost => {
                    int nodeIndex = (int) boost.GetField(typeof(BadelineBoost), "nodeIndex");
                    Vector2[] nodes = boost.GetField(typeof(BadelineBoost), "nodes") as Vector2[];
                    Vector2[] result = nodes?.Skip(nodeIndex).ToArray();
                    return result ?? new Vector2[] { };
                });
        }

        private static void AttachEntityId(On.Celeste.BadelineBoost.orig_ctor_EntityData_Vector2 orig,
            BadelineBoost self, EntityData data, Vector2 offset) {
            self.SetEntityId(data);
            orig(self, data, offset);
        }

        private void RestoreBadelineBoostState(On.Celeste.BadelineBoost.orig_ctor_Vector2Array_bool_bool_bool_bool_bool orig,
            BadelineBoost self, Vector2[] nodes, bool lockCamera, bool canSkip, bool finalCh9Boost, bool finalCh9GoldenBoost, bool finalCh9Dialog) {
            EntityID entityId = self.GetEntityId();
            
            Level level = null;
            if (Engine.Scene is Level) {
                level = (Level) Engine.Scene;
            } else if (Engine.Scene is LevelLoader levelLoader) {
                level = levelLoader.Level;
            }
            
            if (entityId.Equals(default(EntityID))) {
                entityId = new EntityID(level?.Session.Level, nodes.GetHashCode());
                self.SetEntityId(entityId);
            }

            if (IsLoadStart) {
                if (savedNodes.ContainsKey(entityId)) {
                    Vector2[] savedNodes = this.savedNodes[entityId];
                    if (savedNodes.Length == 0) {
                        orig(self, nodes.Skip(nodes.Length - 1).ToArray(), false, canSkip, finalCh9Boost, finalCh9GoldenBoost, finalCh9Dialog);
                    }
                    else {
                        orig(self, savedNodes, savedNodes.Length != 1, canSkip, finalCh9Boost, finalCh9GoldenBoost, finalCh9Dialog);
                    }
                }
                else {
                    orig(self, nodes.Skip(nodes.Length - 1).ToArray(), false, canSkip, finalCh9Boost, finalCh9GoldenBoost, finalCh9Dialog);
                }
                if (level != null && !level.Bounds.Contains( (int) self.Position.X, (int) self.Position.Y)) {
                    self.Add(new RemoveSelfComponent());
                }
                
            }
            else {
                orig(self, nodes, lockCamera, canSkip, finalCh9Boost, finalCh9GoldenBoost, finalCh9Dialog);
            }
        }

        private static void FixMultipleTriggers(On.Celeste.BadelineBoost.orig_OnPlayer orig, BadelineBoost self,
            Player player) {
            if (player.SceneAs<Level>().Frozen) {
                return;
            }

            orig(self, player);
        }

        public override void OnClear() {
            savedNodes.Clear();
        }

        public override void OnLoad() {
            On.Celeste.BadelineBoost.ctor_EntityData_Vector2 += AttachEntityId;
            On.Celeste.BadelineBoost.ctor_Vector2Array_bool_bool_bool_bool_bool += RestoreBadelineBoostState;
            On.Celeste.BadelineBoost.OnPlayer += FixMultipleTriggers;
        }

        public override void OnUnload() {
            On.Celeste.BadelineBoost.ctor_EntityData_Vector2 -= AttachEntityId;
            On.Celeste.BadelineBoost.ctor_Vector2Array_bool_bool_bool_bool_bool -= RestoreBadelineBoostState;
            On.Celeste.BadelineBoost.OnPlayer -= FixMultipleTriggers;
        }
    }
}