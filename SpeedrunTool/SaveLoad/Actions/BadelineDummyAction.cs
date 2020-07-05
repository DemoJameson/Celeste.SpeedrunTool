using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class BadelineDummyAction : AbstractEntityAction {
        
        private Dictionary<EntityID, BadelineDummy> savedBadelineDummys =
            new Dictionary<EntityID, BadelineDummy>();

        public override void OnQuickSave(Level level) {
            savedBadelineDummys = level.Entities.GetDictionary<BadelineDummy>();
        }

        private void RestoreBadelineDummyState(On.Celeste.BadelineDummy.orig_ctor orig,
            BadelineDummy self, Vector2 position) {
            self.SetStartPosition(position);
            EntityID entityId = self.CreateEntityId(position.ToString());
            self.SetEntityId(entityId);
            orig(self, position);

            if (!IsLoadStart) return;
            
            if (savedBadelineDummys.ContainsKey(entityId)) {
                BadelineDummy saved = savedBadelineDummys[entityId];
                self.CopyEntity(saved);
                self.Hair.CopyPlayerHairAndSprite(saved.Hair);
            } else {
                self.Add(new RemoveSelfComponent());
            }
        }

        public override void OnQuickLoadStart(Level level, Player player, Player savedPlayer) {
            var addedDummy = level.Entities.GetDictionary<BadelineDummy>();

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