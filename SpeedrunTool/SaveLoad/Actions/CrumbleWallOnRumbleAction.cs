using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.SaveLoad.Components;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class CrumbleWallOnRumbleAction : AbstractEntityAction {
        private Dictionary<EntityId2, CrumbleWallOnRumble> savedCrumbleWallOnRumbles = new Dictionary<EntityId2, CrumbleWallOnRumble>();

        public override void OnSaveSate(Level level) {
            savedCrumbleWallOnRumbles = level.Entities.FindAllToDict<CrumbleWallOnRumble>();
        }

        private void RestoreCrumbleWallOnRumblePosition(On.Celeste.CrumbleWallOnRumble.orig_ctor_EntityData_Vector2_EntityID orig,
            CrumbleWallOnRumble self, EntityData data,
            Vector2 offset, EntityID id) {
            EntityId2 entityId2 = id.ToEntityId2(self.GetType());
            self.SetEntityId2(entityId2);
            orig(self, data, offset, id);

            if (IsLoadStart && !savedCrumbleWallOnRumbles.ContainsKey(entityId2)) {
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
    }
}