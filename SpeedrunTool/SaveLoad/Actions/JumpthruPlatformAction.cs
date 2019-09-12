using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class JumpthruPlatformAction : AbstractEntityAction {
        private Dictionary<EntityID, JumpthruPlatform> savedJumpthruPlatforms = new Dictionary<EntityID, JumpthruPlatform>();

        public override void OnQuickSave(Level level) {
            savedJumpthruPlatforms = level.Tracker.GetDictionary<JumpthruPlatform>();
        }

        private void RestoreJumpthruPlatformPosition(On.Celeste.JumpthruPlatform.orig_ctor_EntityData_Vector2 orig,
            JumpthruPlatform self, EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
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

        public override void OnInit() {
            typeof(JumpthruPlatform).AddToTracker();
        }
    }
}