using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using MonoMod.Cil;

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

            int state = (int) savedMoveBlock.GetField("state");
            self.SetField("state", state);
            self.Visible = savedMoveBlock.Visible;
            self.Collidable = savedMoveBlock.Collidable;
            self.Position = savedMoveBlock.Position;
            self.StopPlayerRunIntoAnimation = savedMoveBlock.StopPlayerRunIntoAnimation;
            self.CopyFields(typeof(MoveBlock), savedMoveBlock,
                "flash",
                "triggered",
                "targetSpeed",
                "speed",
                "angle",
                "targetAngle",
                "leftPressed",
                "rightPressed",
                "topPressed");
        }

        private static void BlockCoroutineStart(ILContext il) {
            il.SkipAddCoroutine<MoveBlock>("Controller", () => IsLoadStart);
        }

        public override void OnClear() {
            movingBlocks.Clear();
        }

        public override void OnLoad() {
            On.Celeste.MoveBlock.ctor_EntityData_Vector2 += RestoreMoveBlockStateOnCreate;
            IL.Celeste.MoveBlock.ctor_Vector2_int_int_Directions_bool_bool += BlockCoroutineStart;
        }

        public override void OnUnload() {
            On.Celeste.MoveBlock.ctor_EntityData_Vector2 -= RestoreMoveBlockStateOnCreate;
            IL.Celeste.MoveBlock.ctor_Vector2_int_int_Directions_bool_bool -= BlockCoroutineStart;
        }
    }
}