using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Components;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class RefillAction : AbstractEntityAction {
        private Dictionary<EntityId2, Refill> savedRefills = new Dictionary<EntityId2, Refill>();

        public override void OnSaveSate(Level level) {
            savedRefills = level.Entities.FindAllToDict<Refill>();
        }

        private void RefillOnCtorEntityDataVector2(
            On.Celeste.Refill.orig_ctor_EntityData_Vector2 orig, Refill self, EntityData data,
            Vector2 offset) {
            EntityId2 entityId = data.ToEntityId2(self.GetType());
            self.SetEntityId2(entityId);
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