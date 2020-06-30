using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using MonoMod.Cil;

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
				var savedZipMover = savedZipMovers[entityId];
				self.Position = savedZipMover.Position;
				self.CopyFields(typeof(ZipMover), savedZipMover, "percent");
				self.CopySprite(savedZipMover, "streetlight");
			}
		}

		private void BlockCoroutineStart(ILContext il) {
			il.SkipAddCoroutine<ZipMover>("Sequence", () => IsLoadStart);
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