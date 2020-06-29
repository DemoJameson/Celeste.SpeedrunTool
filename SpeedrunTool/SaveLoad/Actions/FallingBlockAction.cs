using System;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class FallingBlockAction : AbstractEntityAction {
        private Dictionary<EntityID, FallingBlock> fallingBlocks = new Dictionary<EntityID, FallingBlock>();
        private const string DisableShakeSfx = "DisableShakeSfx";
        private const string DisableImpactSfx = "DisableImpactSfx";
        private const string DisableLandParticles = "DisableLandParticles";

        public override void OnQuickSave(Level level) {
            fallingBlocks = level.Entities.GetDictionary<FallingBlock>();
        }

        public override void OnClear() {
            fallingBlocks.Clear();
        }

        private void OnFallingBlockOnCtorEntityDataVector2(On.Celeste.FallingBlock.orig_ctor_EntityData_Vector2 orig,
            FallingBlock self, EntityData data, Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);
            RestoreState(self, entityId);
        }

        private FallingBlock FallingBlockOnCreateFinalBossBlock(On.Celeste.FallingBlock.orig_CreateFinalBossBlock orig,
            EntityData data, Vector2 offset) {
            FallingBlock self = orig(data, offset);
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);

            RestoreState(self, entityId);

            return self;
        }

        private void RestoreState(FallingBlock self, EntityID entityId) {
            if (IsLoadStart) {
                if (fallingBlocks.ContainsKey(entityId)) {
                    FallingBlock savedFallingBlock = fallingBlocks[entityId];
                    self.Position = savedFallingBlock.Position;
                    self.FallDelay = savedFallingBlock.FallDelay;
                    self.Triggered = savedFallingBlock.Triggered;
                    self.SetProperty(typeof(FallingBlock), "HasStartedFalling", savedFallingBlock.HasStartedFalling);
                    
                    // remove duplicate coroutine
                    // Add(new Coroutine(Sequence()));
                    self.Remove(self.Get<Coroutine>());
                }
                else {
                    self.Add(new RemoveSelfComponent());
                }
            }
        }

        // dont know why not work
		private void BlockCoroutineStart(ILContext il) {
			ILCursor c = new ILCursor(il);
			for (int i = 0; i < 3; i++)
				c.GotoNext(inst => inst.MatchCall(typeof(Entity).GetMethod("Add", new[] { typeof(Monocle.Component) })));
			Instruction skipCoroutine = c.Next.Next;
			c.GotoPrev(i => i.MatchStfld(typeof(TileGrid), "Alpha"));
			c.GotoNext();
			c.EmitDelegate<Func<bool>>(() => true);
			c.Emit(OpCodes.Brtrue, skipCoroutine);
        }

		public override void OnLoad() {
            On.Celeste.FallingBlock.CreateFinalBossBlock += FallingBlockOnCreateFinalBossBlock;
            On.Celeste.FallingBlock.ctor_EntityData_Vector2 += OnFallingBlockOnCtorEntityDataVector2;
			// IL.Celeste.FallingBlock.ctor_Vector2_char_int_int_bool_bool_bool += BlockCoroutineStart;
        }

        public override void OnUnload() {
            On.Celeste.FallingBlock.CreateFinalBossBlock -= FallingBlockOnCreateFinalBossBlock;
            On.Celeste.FallingBlock.ctor_EntityData_Vector2 -= OnFallingBlockOnCtorEntityDataVector2;
			// IL.Celeste.FallingBlock.ctor_Vector2_char_int_int_bool_bool_bool -= BlockCoroutineStart;
        }
    }
}