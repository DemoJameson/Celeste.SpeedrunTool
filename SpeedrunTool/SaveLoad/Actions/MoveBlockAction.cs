using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.Cil;
using Mono.Cecil.Cil;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class MoveBlockAction : AbstractEntityAction {
        private const string BreakTimeFrames = "breakTimeFrames";
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

            int state = (int) savedMoveBlock.GetField(typeof(MoveBlock), "state");
			self.SetField("state", state);
            if (state == 1 || state == 2 && savedMoveBlock.GetExtendedDataValue<int>(BreakTimeFrames) == 0) {
                // MovementState.Moving or MovementState.Breaking but stopped, not disappeared.
                self.Position = savedMoveBlock.Position;
				self.CopyFields(savedMoveBlock, "triggered", "speed", "angle", "targetSpeed", "targetAngle");
            } else if (state == 2) {
                // MovementState.Breaking
                self.Add(new FastForwardComponent<MoveBlock>(savedMoveBlock, OnFastForward));
            }
        }

        private static void OnFastForward(MoveBlock entity, MoveBlock savedEntity) {
            AudioAction.MuteAudioPathVector2("event:/game/04_cliffside/arrowblock_activate");
            AudioAction.MuteAudioPathVector2("event:/game/04_cliffside/arrowblock_break");
            entity.Update();
            entity.OnStaticMoverTrigger(null);
            Rectangle bounds = entity.SceneAs<Level>().Bounds;
            entity.MoveTo(new Vector2(bounds.Left - 100f, bounds.Bottom - 100f));
            int breakTimeFrames = savedEntity.GetExtendedDataValue<int>(BreakTimeFrames);
            for (int i = 0; i < 12 + breakTimeFrames; i++) {
                entity.Update();
            }
        }

        public override void OnClear() {
            movingBlocks.Clear();
        }

        public override void OnLoad() {
            On.Celeste.MoveBlock.ctor_EntityData_Vector2 += RestoreMoveBlockStateOnCreate;
			IL.Celeste.MoveBlock.ctor_Vector2_int_int_Directions_bool_bool += BlockCoroutineStart;
        }

		private void BlockCoroutineStart(ILContext il) {
			ILCursor c = new ILCursor(il);
			c.GotoNext((i) => i.MatchCallvirt(typeof(MoveBlock), "UpdateColors"));
			Instruction skipCoroutine = c.Prev;
			for (int i = 0; i < 2; i++)
				c.GotoPrev((inst) => inst.MatchCall(typeof(Entity).GetMethod("Add", new Type[] { typeof(Monocle.Component) })));
			c.GotoNext();
			c.EmitDelegate<Func<bool>>(() => IsLoadStart);
			c.Emit(OpCodes.Brtrue, skipCoroutine);
		}

		public override void OnUnload() {
            On.Celeste.MoveBlock.ctor_EntityData_Vector2 -= RestoreMoveBlockStateOnCreate;
        }
    }
}