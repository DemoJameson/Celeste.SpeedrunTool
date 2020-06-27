using System;
using System.Collections;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.Cil;
using Mono.Cecil.Cil;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class ZipMoverAction : AbstractEntityAction {
        private Dictionary<EntityID, ZipMover> savedZipMovers = new Dictionary<EntityID, ZipMover>();

        public override void OnQuickSave(Level level) {
            savedZipMovers = level.Entities.GetDictionary<ZipMover>();
        }

        public override void OnClear() {
            savedZipMovers.Clear();
        }

		private void RestoreZipMoverPosition(On.Celeste.ZipMover.orig_ctor_EntityData_Vector2 orig, ZipMover self, EntityData data, Vector2 offset) {
			EntityID entityId = data.ToEntityId();
			self.SetEntityId(entityId);
			orig.Invoke(self, data, offset);
			if (IsLoadStart && savedZipMovers.ContainsKey(entityId)) {
				self.Position = savedZipMovers[entityId].Position;
				self.CopyField("percent", savedZipMovers[entityId]);
			}
		}

		private void BlockCoroutineStart(ILContext il) {
			ILCursor c = new ILCursor(il);
			c.GotoNext((i) => i.MatchCall(typeof(Entity).GetMethod("Add", new Type[] { typeof(Monocle.Component) })));
			Instruction skipCoroutine = c.Next.Next;
			c.GotoPrev((i) => i.MatchStfld(typeof(ZipMover), "theme"));
			c.GotoNext();
			c.EmitDelegate<Func<bool>>(() => IsLoadStart);
			c.Emit(OpCodes.Brtrue, skipCoroutine);
		}

		public override void OnLoad() {
			On.Celeste.ZipMover.ctor_EntityData_Vector2 += RestoreZipMoverPosition;
			IL.Celeste.ZipMover.ctor_Vector2_int_int_Vector2_Themes += BlockCoroutineStart;
		}

		public override void OnUnload() {
			On.Celeste.ZipMover.ctor_EntityData_Vector2 -= RestoreZipMoverPosition;
			IL.Celeste.ZipMover.ctor_Vector2_int_int_Vector2_Themes -= BlockCoroutineStart;
		}
    }
}