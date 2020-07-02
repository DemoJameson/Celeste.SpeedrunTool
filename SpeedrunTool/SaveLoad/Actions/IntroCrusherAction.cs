using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class IntroCrusherAction : AbstractEntityAction {
        private Dictionary<EntityID, IntroCrusher> savedIntroCrushers = new Dictionary<EntityID, IntroCrusher>();

        public override void OnQuickSave(Level level) {
            savedIntroCrushers = level.Entities.GetDictionary<IntroCrusher>();
        }
        
        private ILHook addedHook;

        private void RestoreIntroCrusherPosition(On.Celeste.IntroCrusher.orig_ctor_EntityData_Vector2 orig,
            IntroCrusher self, EntityData data, Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && savedIntroCrushers.ContainsKey(entityId)) {
                IntroCrusher saved = savedIntroCrushers[entityId];
                self.Position = saved.Position;
                self.CopyFields(saved, "shake", "triggered");
                self.CopyTileGrid(saved, "tilegrid");
            }
        }

        private void IntroCrusherOnAdded(ILContext il) {
            il.SkipAddCoroutine<IntroCrusher>("Sequence", () => IsLoadStart);
        }

        public override void OnClear() {
            savedIntroCrushers.Clear();
        }

        public override void OnLoad() {
            On.Celeste.IntroCrusher.ctor_EntityData_Vector2 += RestoreIntroCrusherPosition;
            IL.Celeste.IntroCrusher.Added += IntroCrusherOnAdded;
            addedHook = new ILHook(typeof(IntroCrusher).GetMethod("orig_Added"), IntroCrusherOnAdded);
        }

        public override void OnUnload() {
            On.Celeste.IntroCrusher.ctor_EntityData_Vector2 -= RestoreIntroCrusherPosition;
            IL.Celeste.IntroCrusher.Added -= IntroCrusherOnAdded;
            addedHook.Dispose();
        }
    }
}