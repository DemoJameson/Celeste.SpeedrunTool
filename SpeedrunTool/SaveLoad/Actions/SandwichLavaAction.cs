using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Components;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class SandwichLavaAction : AbstractEntityAction {
        private Dictionary<EntityId2, SandwichLava> savedSandwichLavas = new Dictionary<EntityId2, SandwichLava>();

        public override void OnSaveSate(Level level) {
            savedSandwichLavas = level.Entities.FindAllToDict<SandwichLava>();
        }

        private void RestoreSandwichLavaState(On.Celeste.SandwichLava.orig_ctor_EntityData_Vector2 orig,
            SandwichLava self, EntityData data,
            Vector2 offset) {
            EntityId2 entityId = data.ToEntityId2(self.GetType());
            self.SetEntityId2(entityId);
            orig(self, data, offset);

            if (IsLoadStart) {
                if (savedSandwichLavas.ContainsKey(entityId)) {
                    SandwichLava savedSandwichLava = savedSandwichLavas[entityId];
                    self.Collidable = savedSandwichLava.Collidable;
                    self.Waiting = savedSandwichLava.Waiting;
                    self.CopyFields(typeof(SandwichLava), savedSandwichLava, "leaving");
                    self.CopyFields(typeof(SandwichLava), savedSandwichLava, "delay");
                    self.Add(new RestorePositionComponent(self, savedSandwichLava));
                }
                else {
                    self.Add(new RemoveSelfComponent());
                }
            }
        }

        public override void OnClear() {
            savedSandwichLavas.Clear();
        }

        public override void OnLoad() {
            On.Celeste.SandwichLava.ctor_EntityData_Vector2 += RestoreSandwichLavaState;
        }

        public override void OnUnload() {
            On.Celeste.SandwichLava.ctor_EntityData_Vector2 -= RestoreSandwichLavaState;
        }
    }
}