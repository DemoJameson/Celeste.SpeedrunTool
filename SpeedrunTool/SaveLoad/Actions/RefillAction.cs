using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;

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
                        self.CopySprite(savedRefill, "sprite");
                        self.CopySprite(savedRefill, "flash");
                        self.CopyImage(savedRefill, "outline");
                        self.CopyFields(savedRefill, "respawnTimer");
                        self.Depth = savedRefill.Depth;
                    }
                }
                else {
                    self.Add(new RemoveSelfComponent());
                }
            }
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