using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Components;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class BadelineDummyAction : AbstractEntityAction {
        
        private Dictionary<EntityId2, BadelineDummy> savedBadelineDummys =
            new Dictionary<EntityId2, BadelineDummy>();

        public override void OnQuickSave(Level level) {
            savedBadelineDummys = level.Entities.FindAllToDict<BadelineDummy>();
        }

        private void RestoreBadelineDummyState(On.Celeste.BadelineDummy.orig_ctor orig,
            BadelineDummy self, Vector2 position) {
            self.SetStartPosition(position);
            EntityId2 entityId2 = self.CreateEntityId2(position.ToString());
            self.SetEntityId2(entityId2);
            orig(self, position);

            if (!IsLoadStart) return;
            
            if (savedBadelineDummys.ContainsKey(entityId2)) {
                BadelineDummy saved = savedBadelineDummys[entityId2];
                self.CopyEntity(saved);
                self.Hair.CopyPlayerHairAndSprite(saved.Hair);
            } else {
                self.Add(new RemoveSelfComponent());
            }
        }

        public override void OnQuickLoadStart(Level level, Player player, Player savedPlayer) {
            var addedDummy = level.Entities.FindAllToDict<BadelineDummy>();

            foreach (var pair in savedBadelineDummys) {
                if (addedDummy.ContainsKey(pair.Key)) {
                    continue;
                }

                var dummy = new BadelineDummy(pair.Value.GetStartPosition()) {
                    Position = pair.Value.Position
                };
                level.Add(dummy);
            }
        }

        public override void OnClear() {
            savedBadelineDummys.Clear();
        }

        public override void OnLoad() {
            On.Celeste.BadelineDummy.ctor += RestoreBadelineDummyState;
        }

        public override void OnUnload() {
            On.Celeste.BadelineDummy.ctor -= RestoreBadelineDummyState;
        }
    }

    public static class BadelineDummyExtension {
        private const string StartPosition = "StartPosition";
 
        public static Vector2 GetStartPosition(this BadelineDummy badelineDummy) {
            return badelineDummy.GetExtendedDataValue<Vector2>(StartPosition);
        }
        
        public static void SetStartPosition(this BadelineDummy badelineDummy, Vector2 startPosition) {
            badelineDummy.SetExtendedDataValue(StartPosition, startPosition);
        }
    }
}