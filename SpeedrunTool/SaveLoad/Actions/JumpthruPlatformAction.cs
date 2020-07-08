using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.SaveLoad.Components;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class JumpthruPlatformAction : AbstractEntityAction {
        private Dictionary<EntityId2, JumpthruPlatform> savedJumpthruPlatforms = new Dictionary<EntityId2, JumpthruPlatform>();

        public override void OnSaveSate(Level level) {
            savedJumpthruPlatforms = level.Entities.FindAllToDict<JumpthruPlatform>();
        }

        private void RestoreJumpthruPlatformPosition(On.Celeste.JumpthruPlatform.orig_ctor_EntityData_Vector2 orig,
            JumpthruPlatform self, EntityData data,
            Vector2 offset) {
            EntityId2 entityId = data.ToEntityId2(self.GetType());
            self.SetEntityId2(entityId);
            orig(self, data, offset);

            if (IsLoadStart && savedJumpthruPlatforms.ContainsKey(entityId)) {
                JumpthruPlatform savedJumpthruPlatform = savedJumpthruPlatforms[entityId];
                self.Add(new RestorePositionComponent(self, savedJumpthruPlatform));
            }
        }

        public override void OnClear() {
            savedJumpthruPlatforms.Clear();
        }

        public override void OnLoad() {
            On.Celeste.JumpthruPlatform.ctor_EntityData_Vector2 += RestoreJumpthruPlatformPosition;
        }

        public override void OnUnload() {
            On.Celeste.JumpthruPlatform.ctor_EntityData_Vector2 -= RestoreJumpthruPlatformPosition;
        }
    }
}