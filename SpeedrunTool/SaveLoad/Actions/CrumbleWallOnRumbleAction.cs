using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class CrumbleWallOnRumbleAction : AbstractEntityAction {
        private Dictionary<EntityID, CrumbleWallOnRumble> savedCrumbleWallOnRumbles = new Dictionary<EntityID, CrumbleWallOnRumble>();

        public override void OnQuickSave(Level level) {
            savedCrumbleWallOnRumbles = level.Tracker.GetDictionary<CrumbleWallOnRumble>();
        }

        private void RestoreCrumbleWallOnRumblePosition(On.Celeste.CrumbleWallOnRumble.orig_ctor_EntityData_Vector2_EntityID orig,
            CrumbleWallOnRumble self, EntityData data,
            Vector2 offset, EntityID id) {
            EntityID entityId = id;
            self.SetEntityId(entityId);
            orig(self, data, offset, id);

            if (IsLoadStart && !savedCrumbleWallOnRumbles.ContainsKey(entityId)) {
                self.Add(new RemoveSelfComponent());
            }
        }

        public override void OnClear() {
            savedCrumbleWallOnRumbles.Clear();
        }

        public override void OnLoad() {
            On.Celeste.CrumbleWallOnRumble.ctor_EntityData_Vector2_EntityID += RestoreCrumbleWallOnRumblePosition;
        }

        public override void OnUnload() {
            On.Celeste.CrumbleWallOnRumble.ctor_EntityData_Vector2_EntityID -= RestoreCrumbleWallOnRumblePosition;
        }

        public override void OnUpdateEntitiesWhenFreeze(Level level) {
            level.UpdateEntities<CrumbleWallOnRumble>();
        }
    }
}