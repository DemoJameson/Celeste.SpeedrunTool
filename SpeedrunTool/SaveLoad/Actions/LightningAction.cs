using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class LightningAction : AbstractEntityAction {
        private Dictionary<EntityID, Lightning> savedLightning = new Dictionary<EntityID, Lightning>();

        public override void OnQuickSave(Level level) {
            savedLightning = level.Tracker.GetDictionary<Lightning>();
        }

        private void RestoreLightningState(
            On.Celeste.Lightning.orig_ctor_EntityData_Vector2 orig, Lightning self, EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart) {
                if (savedLightning.ContainsKey(entityId)) {
                    self.Position = savedLightning[entityId].Position;
                    self.Collidable = savedLightning[entityId].Collidable;
                    self.Visible = savedLightning[entityId].Visible;
                }
                else {
                    self.Add(new RemoveSelfComponent());
                }
            }
        }

        public override void OnClear() {
            savedLightning.Clear();
        }

        public override void OnLoad() {
            On.Celeste.Lightning.ctor_EntityData_Vector2 += RestoreLightningState;
        }

        public override void OnUnload() {
            On.Celeste.Lightning.ctor_EntityData_Vector2 -= RestoreLightningState;
        }

        public override void OnUpdateEntitiesWhenFreeze(Level level) {
            level.UpdateEntities<Lightning>();
        }

        public override void OnInit() {
            typeof(Lightning).AddToTracker();
        }
    }
}