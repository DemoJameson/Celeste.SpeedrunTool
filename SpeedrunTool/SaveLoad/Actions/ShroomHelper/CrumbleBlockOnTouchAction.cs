using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions.ShroomHelper {
    public class CrumbleBlockOnTouchAction : AbstractEntityAction {
        private const string FullName = "Celeste.Mod.ShroomHelper.Entities.CrumbleBlockOnTouch";
        private const string FullName2 = "Celeste.Mod.AcidHelper.Entities.CrumbleWallOnTouch";
        private Dictionary<EntityID, Entity> savedBlocks = new Dictionary<EntityID, Entity>();

        public override void OnQuickSave(Level level) {
            savedBlocks = level.Entities.FindAll<Entity>()
                .Where(entity => entity.GetType().FullName == FullName || entity.GetType().FullName == FullName2)
                .GetDictionary();
        }

        private void SolidOnCtor(On.Celeste.Solid.orig_ctor orig, Solid self, Vector2 position, float width,
            float height, bool safe) {
            orig(self, position, width, height, safe);
            if (self.GetType().FullName != FullName && self.GetType().FullName != FullName2) {
                return;
            }

            EntityID entityId = self.CreateEntityId(position.ToString(), width.ToString(), height.ToString(), safe.ToString());
            if (entityId.IsDefault()) {
                return;
            }
            self.SetEntityId(entityId);

            if (IsLoadStart && !savedBlocks.ContainsKey(entityId)) {
                self.Add(new RemoveSelfComponent());
            }
        }

        public override void OnClear() {
            savedBlocks.Clear();
        }

        public override void OnLoad() {
            On.Celeste.Solid.ctor += SolidOnCtor;
        }

        public override void OnUnload() {
            On.Celeste.Solid.ctor -= SolidOnCtor;
        }
    }
}