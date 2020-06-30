using System;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using MonoMod.Cil;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
	// TODO: desync in TAS after savestate.
    public class SwitchGateAction : AbstractEntityAction {
        private Dictionary<EntityID, SwitchGate> savedSwitchGates = new Dictionary<EntityID, SwitchGate>();

        public override void OnQuickSave(Level level) {
            savedSwitchGates = level.Entities.GetDictionary<SwitchGate>();
        }

        private void RestoreSwitchGatePosition(On.Celeste.SwitchGate.orig_ctor_EntityData_Vector2 orig, SwitchGate self,
            EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && savedSwitchGates.ContainsKey(entityId)) {
	            var savedSwitchGate = savedSwitchGates[entityId];
	            self.Position = savedSwitchGate.Position;
                self.CopySprite(savedSwitchGate, "icon");
                self.CopyFields(savedSwitchGate, "iconOffset");
            }
		}

		//this isn't actually blocking the coroutine
		//makes it duplicate it in a less broken way though i guess
		private void BlockCoroutineStart(ILContext il) {
			ILCursor c = new ILCursor(il);
			c.GotoNext(i => i.MatchRet());
			c.GotoNext(i => i.MatchRet());
			Instruction skipCoroutine = c.Next;
			c.GotoPrev(i => i.MatchRet());
			c.GotoNext();
			c.EmitDelegate<Func<bool>>(() => true);
			c.Emit(OpCodes.Brtrue, skipCoroutine);
		}

		public override void OnClear() {
            savedSwitchGates.Clear();
        }

        public override void OnLoad() {
            On.Celeste.SwitchGate.ctor_EntityData_Vector2 += RestoreSwitchGatePosition;
			IL.Celeste.SwitchGate.Awake += BlockCoroutineStart;
        }

		public override void OnUnload() {
            On.Celeste.SwitchGate.ctor_EntityData_Vector2 -= RestoreSwitchGatePosition;
			IL.Celeste.SwitchGate.Awake -= BlockCoroutineStart;
		}
    }
}