using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class KeyAction : AbstractEntityAction {
        private Dictionary<EntityID, Key> savedKeys = new Dictionary<EntityID, Key>();

        public override void OnQuickSave(Level level) {
            savedKeys = level.Entities.FindAll<Key>().ToDictionary(key => key.ID);
        }

        private void RestoreKeyPosition(On.Celeste.Key.orig_ctor_Player_EntityID orig,
            Key self, Player player,
            EntityID entityId) {
            orig(self, player, entityId);

            if (IsLoadStart && savedKeys.ContainsKey(entityId)) {
                var saved = savedKeys[entityId];
                self.CopyFrom(saved);
                self.CopySprite(saved, "sprite");
            }
        }

        public override void OnClear() {
            savedKeys.Clear();
        }

        public override void OnLoad() {
            On.Celeste.Key.ctor_Player_EntityID += RestoreKeyPosition;
        }

        public override void OnUnload() {
            On.Celeste.Key.ctor_Player_EntityID -= RestoreKeyPosition;
        }
    }
}