using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class StarJumpBlockAction : AbstractEntityAction {
        private Dictionary<EntityId2, StarJumpBlock> savedStarJumpBlocks = new Dictionary<EntityId2, StarJumpBlock>();

        public override void OnSaveSate(Level level) {
            savedStarJumpBlocks = level.Entities.FindAllToDict<StarJumpBlock>();
        }

        private void RestoreStarJumpBlockPosition(On.Celeste.StarJumpBlock.orig_ctor_EntityData_Vector2 orig,
            StarJumpBlock self, EntityData data,
            Vector2 offset) {
            EntityId2 entityId = data.ToEntityId2(self.GetType());
            self.SetEntityId2(entityId);
            orig(self, data, offset);

            if (!IsLoadStart) return;
            
            if (savedStarJumpBlocks.ContainsKey(entityId)) {
                var savedBlock = savedStarJumpBlocks[entityId];
                self.Position = savedBlock.Position;
                self.CopyFields(savedBlock, "sinks", "yLerp", "sinkTimer");
            }
        }

        public override void OnClear() {
            savedStarJumpBlocks.Clear();
        }

        public override void OnLoad() {
            On.Celeste.StarJumpBlock.ctor_EntityData_Vector2 += RestoreStarJumpBlockPosition;
        }

        public override void OnUnload() {
            On.Celeste.StarJumpBlock.ctor_EntityData_Vector2 -= RestoreStarJumpBlockPosition;
        }
    }
}