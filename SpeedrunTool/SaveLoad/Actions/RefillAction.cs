using System.Collections;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class RefillAction : AbstractEntityAction {
        private Dictionary<EntityID, Refill> savedRefills = new Dictionary<EntityID, Refill>();

        public override void OnQuickSave(Level level) {
            savedRefills = level.Entities.GetDictionary<Refill>();
        }

        private void RefillOnCtorEntityDataVector2(
            On.Celeste.Refill.orig_ctor_EntityData_Vector2 orig, Refill self, EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart) {
                if (savedRefills.ContainsKey(entityId)) {
                    Refill savedRefill = savedRefills[entityId];
                    if (!savedRefill.Collidable) {
                        self.Collidable = false;
                        float respawnTimer = (float) savedRefill.GetField(typeof(Refill), "respawnTimer");
                        self.SetField(typeof(Refill), "respawnTimer", respawnTimer);
                        ConsumeRefill(self);
                    }
                }
                else {
                    self.Add(new RemoveSelfComponent());
                }
            }
        }

        private static void ConsumeRefill(Refill self) {
            (self.GetField(typeof(Refill), "sprite") as Sprite).Visible = false;
            (self.GetField(typeof(Refill), "flash") as Sprite).Visible = false;
            if (!(bool)self.GetField(typeof(Refill), "oneUse")) {
                (self.GetField(typeof(Refill), "outline") as Image).Visible = true;
            }
            self.Depth = 8999;
			// Refill.RefillRoutine takes care of the rest
            return;
        }

        public override void OnClear() {
            savedRefills.Clear();
        }

        public override void OnLoad() {
            On.Celeste.Refill.ctor_EntityData_Vector2 += RefillOnCtorEntityDataVector2;
        }


        public override void OnUnload() {
            On.Celeste.Refill.ctor_EntityData_Vector2 -= RefillOnCtorEntityDataVector2;
        }
    }
}