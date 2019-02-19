using System.Collections.Generic;
using System.Reflection;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class CrushBlockAction : AbstractEntityAction {
        private Dictionary<EntityID, CrushBlock> _savedCrushBlocks = new Dictionary<EntityID, CrushBlock>();

        public override void OnQuickSave(Level level) {
            _savedCrushBlocks = level.Tracker.GetDictionary<CrushBlock>();
        }

        private void RestoreCrushBlockState(On.Celeste.CrushBlock.orig_ctor_EntityData_Vector2 orig, CrushBlock self,
            EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart) {
                if (_savedCrushBlocks.ContainsKey(entityId)) {
                    CrushBlock savedCrushBlock = _savedCrushBlocks[entityId];
                    if (self.Position != savedCrushBlock.Position) {
                        self.Position = savedCrushBlock.Position;
                        self.CopyPrivateField("returnStack", savedCrushBlock);
                        self.Add(new RestoreCrushBlockStateComponent(savedCrushBlock));
                    }
                }
                else
                    self.Add(new RemoveSelfComponent());
            }
        }

        private static void CrushBlockOnAttack(On.Celeste.CrushBlock.orig_Attack orig, CrushBlock self,
            Vector2 direction) {
            orig(self, direction);
            self.SetExtendedDataValue("lastCrushDir", direction);
        }

        private static bool CrushBlockOnMoveHCheck(On.Celeste.CrushBlock.orig_MoveHCheck orig, CrushBlock self,
            float amount) {
            if (self.GetExtendedDataValue<bool>("IsReturning")) {
                self.SetExtendedDataValue("IsReturning", false);
                return true;
            }
            else {
                return orig(self, amount);
            }
        }

        private static bool CrushBlockOnMoveVCheck(On.Celeste.CrushBlock.orig_MoveVCheck orig, CrushBlock self,
            float amount) {
            if (self.GetExtendedDataValue<bool>("IsReturning")) {
                self.SetExtendedDataValue("IsReturning", false);
                return true;
            }

            return orig(self, amount);
        }

        public override void OnClear() {
            _savedCrushBlocks.Clear();
        }

        public override void OnLoad() {
            On.Celeste.CrushBlock.ctor_EntityData_Vector2 += RestoreCrushBlockState;
            On.Celeste.CrushBlock.Attack += CrushBlockOnAttack;
            On.Celeste.CrushBlock.MoveHCheck += CrushBlockOnMoveHCheck;
            On.Celeste.CrushBlock.MoveVCheck += CrushBlockOnMoveVCheck;
        }

        public override void OnUnload() {
            On.Celeste.CrushBlock.ctor_EntityData_Vector2 -= RestoreCrushBlockState;
            On.Celeste.CrushBlock.Attack -= CrushBlockOnAttack;
            On.Celeste.CrushBlock.MoveHCheck -= CrushBlockOnMoveHCheck;
            On.Celeste.CrushBlock.MoveVCheck -= CrushBlockOnMoveVCheck;
        }

        public override void OnInit() {
            typeof(CrushBlock).AddToTracker();
        }

        public override void OnUpdateEntitiesWhenFreeze(Level level) {
            level.UpdateEntities<CrushBlock>();
        }

        private class RestoreCrushBlockStateComponent : Monocle.Component {
            private readonly CrushBlock _savedCrushBlock;

            public RestoreCrushBlockStateComponent(CrushBlock savedCrushBlock) : base(true, false) {
                _savedCrushBlock = savedCrushBlock;
            }

            public override void Update() {
                object crushDir = _savedCrushBlock.GetPrivateField("crushDir");
                if (crushDir != null) {
                    MuteAudio("event:/game/06_reflection/crushblock_activate");

                    MethodInfo attackMethodInfo =
                        Entity.GetType().GetMethod("Attack", BindingFlags.Instance | BindingFlags.NonPublic);
                    if ((Vector2) crushDir != Vector2.Zero) {
                        attackMethodInfo?.Invoke(Entity, new[] {crushDir});
                    }
                    else {
                        MuteAudio("event:/game/06_reflection/crushblock_impact");
                        MuteSoundSource("event:/game/06_reflection/crushblock_move_loop");
                        object lastCrushDir = _savedCrushBlock.GetExtendedDataValue<Vector2>("lastCrushDir");
                        attackMethodInfo?.Invoke(Entity, new[] {lastCrushDir});
                        Entity.SetExtendedDataValue("IsReturning", true);
                    }

                    Entity.SetPrivateField("canActivate", true);
                }

                RemoveSelf();
            }
        }
    }
}