using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class LightningAction : AbstractEntityAction {
        private Dictionary<EntityID, Lightning> savedLightnings = new Dictionary<EntityID, Lightning>();

        public override void OnQuickSave(Level level) {
            savedLightnings = level.Tracker.GetDictionary<Lightning>();
        }

        private void RestoreLightningState(
            On.Celeste.Lightning.orig_ctor_EntityData_Vector2 orig, Lightning self, EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart) {
                if (savedLightnings.ContainsKey(entityId)) {
                    Lightning saved = savedLightnings[entityId];
                    self.Collidable = saved.Collidable;
                    self.Visible = saved.Visible;
                }
                else {
                    self.Add(new RemoveSelfComponent());
                }
            }
        }

        private void LightningOnUpdate(On.Celeste.Lightning.orig_Update orig, Lightning self) {
            if (IsLoadStart) {
                EntityID entityId = self.GetEntityId();
                if (savedLightnings.ContainsKey(entityId)) {
                    Lightning saved = savedLightnings[entityId];

                    LightningRenderer lightningRenderer = self.Scene.Tracker.GetEntity<LightningRenderer>();
                    while (self.Position != saved.Position) {
                        lightningRenderer?.Update();
                        orig(self);
                    }
                }
            }

            orig(self);
        }

        public override void OnClear() {
            savedLightnings.Clear();
        }

        public override void OnLoad() {
            On.Celeste.Lightning.ctor_EntityData_Vector2 += RestoreLightningState;
            On.Celeste.Lightning.Update += LightningOnUpdate;
        }

        public override void OnUnload() {
            On.Celeste.Lightning.ctor_EntityData_Vector2 -= RestoreLightningState;
            On.Celeste.Lightning.Update -= LightningOnUpdate;
        }

        public override void OnInit() {
            typeof(Lightning).AddToTracker();
        }
    }
}