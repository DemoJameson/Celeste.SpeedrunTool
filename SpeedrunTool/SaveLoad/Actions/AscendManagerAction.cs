using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;
using MonoMod.Cil;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class AscendManagerAction : AbstractEntityAction {
        private Dictionary<EntityID, AscendManager> savedAscendManagers = new Dictionary<EntityID, AscendManager>();

        public override void OnQuickSave(Level level) {
            savedAscendManagers = level.Entities.GetDictionary<AscendManager>();
        }

        private void AscendManagerOnCtor(On.Celeste.AscendManager.orig_ctor orig, AscendManager self, EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (!IsLoadStart) return;

            if (savedAscendManagers.ContainsKey(entityId)) {
                AscendManager saved = savedAscendManagers[entityId];
                self.Depth = saved.Depth;
                self.CopyFields(saved, "fade", "scroll");
            } else {
                self.Add(new RemoveSelfComponent());
            }
        }

        private void AscendManagerOnAdded(ILContext il) {
            il.SkipAddCoroutine<AscendManager>("Routine", () => IsLoadStart);
        }

        public override void OnClear() {
            savedAscendManagers.Clear();
        }

        public override void OnLoad() {
            On.Celeste.AscendManager.ctor += AscendManagerOnCtor;
            IL.Celeste.AscendManager.Added += AscendManagerOnAdded;
        }

        public override void OnUnload() {
            On.Celeste.AscendManager.ctor -= AscendManagerOnCtor;
            IL.Celeste.AscendManager.Added -= AscendManagerOnAdded;
        }
    }
}