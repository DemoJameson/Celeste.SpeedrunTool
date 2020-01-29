using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class MoveBlockAction : AbstractEntityAction {
        private Dictionary<EntityID, MoveBlock> movingBlocks = new Dictionary<EntityID, MoveBlock>();

        public override void OnQuickSave(Level level) {
            movingBlocks = level.Entities.GetDictionary<MoveBlock>();
        }


        private void RestoreMoveBlockStateOnCreate(On.Celeste.MoveBlock.orig_ctor_EntityData_Vector2 orig,
            MoveBlock self,
            EntityData data, Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (!IsLoadStart || !movingBlocks.ContainsKey(entityId)) {
                return;
            }

            MoveBlock savedMoveBlock = movingBlocks[entityId];

            int state = (int) savedMoveBlock.GetPrivateField("state");
            switch (state) {
                case 1:
                    // MovementState.Moving
                    self.Position = savedMoveBlock.Position;
                    self.Add(new Coroutine(TriggerBlock(self)));
                    break;
                case 2:
                    // MovementState.Breaking
                    self.Add(new FastForwardComponent<MoveBlock>(savedMoveBlock, OnFastForward));
                    break;
            }
        }

        private static void OnFastForward(MoveBlock entity, MoveBlock savedEntity) {
            AudioAction.MuteAudioPathVector2("event:/game/04_cliffside/arrowblock_activate");
            AudioAction.MuteAudioPathVector2("event:/game/04_cliffside/arrowblock_break");
            entity.Update();
            entity.OnStaticMoverTrigger(null);
            Rectangle bounds = entity.SceneAs<Level>().Bounds;
            entity.MoveTo(new Vector2(bounds.Left - 100f, bounds.Bottom - 100f));
            float breakTime = savedEntity.GetExtendedDataValue<float>(nameof(breakTime));
            int breakTimeFrames = Convert.ToInt32(breakTime / 0.017f);
            for (int i = 0; i < 12 + breakTimeFrames; i++) {
                entity.Update();
            }
        }

        private static IEnumerator TriggerBlock(MoveBlock self) {
            self.OnStaticMoverTrigger(null);
            yield break;
        }

        private static IEnumerator MoveBlockOnController(On.Celeste.MoveBlock.orig_Controller orig, MoveBlock self) {
            IEnumerator enumerator = orig(self);
            while (enumerator.MoveNext()) {
                object result = enumerator.Current;
                if (result is float restoreTime && Math.Abs(restoreTime - 2.2f) < 0.01) {
                    restoreTime += 0.016f;
                    float breakTime = 0f;
                    while (restoreTime > 0f) {
                        restoreTime -= Engine.DeltaTime;
                        breakTime += Engine.DeltaTime;
                        self.SetExtendedDataValue(nameof(breakTime), breakTime);
                        yield return null;
                    }
                    continue;
                }

                yield return result;
            }
        }

        public override void OnClear() {
            movingBlocks.Clear();
        }

        public override void OnLoad() {
            On.Celeste.MoveBlock.ctor_EntityData_Vector2 += RestoreMoveBlockStateOnCreate;
            On.Celeste.MoveBlock.Controller += MoveBlockOnController;
        }

        public override void OnUnload() {
            On.Celeste.MoveBlock.ctor_EntityData_Vector2 -= RestoreMoveBlockStateOnCreate;
            On.Celeste.MoveBlock.Controller -= MoveBlockOnController;
        }
    }
}