using System.Collections;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class ExitBlockAction : AbstractEntityAction {
        private Dictionary<EntityId2, ExitBlock> savedExitBlocks = new Dictionary<EntityId2, ExitBlock>();

        public override void OnSaveSate(Level level) {
            savedExitBlocks = level.Entities.FindAllToDict<ExitBlock>();
        }

        private void PreventExitBlockLockPlayer(On.Celeste.ExitBlock.orig_ctor_EntityData_Vector2 orig,
            ExitBlock self, EntityData data,
            Vector2 offset) {
            EntityId2 entityId = data.ToEntityId2(self.GetType());
            self.SetEntityId2(entityId);
            orig(self, data, offset);

            if (IsLoadStart && savedExitBlocks.ContainsKey(entityId)) {
                ExitBlock savedExitBlock = savedExitBlocks[entityId];
                if (savedExitBlock.Collidable == false) {
                    self.Collidable = false;
                    self.Add(new Coroutine(SetState(self)));
                }
            }
        }

        private IEnumerator SetState(ExitBlock self) {
            (self.GetField(typeof(ExitBlock), "tiles") as TileGrid).Alpha = 0f;
            self.Get<EffectCutout>().Alpha = 0f;
            yield break;
        }

        private void OnExitBlockOnUpdate(On.Celeste.ExitBlock.orig_Update orig, ExitBlock self) {
            if (IsLoadStart) {
                return;
            }

            orig(self);
        }

        public override void OnClear() {
            savedExitBlocks.Clear();
        }

        public override void OnLoad() {
            On.Celeste.ExitBlock.ctor_EntityData_Vector2 += PreventExitBlockLockPlayer;
            On.Celeste.ExitBlock.Update += OnExitBlockOnUpdate;
        }

        public override void OnUnload() {
            On.Celeste.ExitBlock.ctor_EntityData_Vector2 -= PreventExitBlockLockPlayer;
            On.Celeste.ExitBlock.Update -= OnExitBlockOnUpdate;
        }
    }
}