using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Components;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions.Deprecated {
    public class BadelineOldsiteAction : ComponentAction {
        private Dictionary<EntityId2, BadelineOldsite> savedBadelineOldsites =
            new Dictionary<EntityId2, BadelineOldsite>();

        private ILHook addedHook;

        public override void OnSaveSate(Level level) {
            savedBadelineOldsites = level.Entities.FindAllToDict<BadelineOldsite>();
        }

        private void RestoreBadelineOldsitePosition(On.Celeste.BadelineOldsite.orig_ctor_EntityData_Vector2_int orig,
            BadelineOldsite self, EntityData data,
            Vector2 offset, int index) {
            EntityId2 entityId = data.ToEntityId2(self.GetType());
            self.SetEntityId2(entityId);
            orig(self, data, offset, index);

            if (!IsLoadStart) return;

            if (savedBadelineOldsites.ContainsKey(entityId)) {
                var saved = savedBadelineOldsites[entityId];
                self.CopyEntity(saved);
                self.Hovering = saved.Hovering;
                self.Hair.CopyPlayerHairAndSprite(saved.Hair);
                self.CopyFields(saved, "following", "hoveringTimer");
            } else {
                self.Add(new RemoveSelfComponent());
            }
        }

        private void BadelineOldsiteOnUpdate(On.Celeste.BadelineOldsite.orig_Update orig, BadelineOldsite self) {
            if (IsLoadStart && self.Scene.GetPlayer() is Player player) {
                self.SetField("player", player);
            }
            orig(self);
        }

        private void BadelineOldsiteOnAdded(ILContext il) {
            il.SkipAddCoroutine<BadelineOldsite>("StartChasingRoutine", () => IsLoadStart);
        }

        private void BadelineOldsiteOnOrigAdded(ILContext il) {
            il.SkipAddCoroutine<BadelineOldsite>("StartChasingRoutine", () => IsLoadStart);
        }

        public override void OnClear() {
            savedBadelineOldsites.Clear();
        }

        public override void OnLoad() {
            On.Celeste.BadelineOldsite.ctor_EntityData_Vector2_int += RestoreBadelineOldsitePosition;
            IL.Celeste.BadelineOldsite.Added += BadelineOldsiteOnAdded;
            addedHook = new ILHook(typeof(BadelineOldsite).GetMethod("orig_Added"), BadelineOldsiteOnOrigAdded);
            On.Celeste.BadelineOldsite.Update += BadelineOldsiteOnUpdate;
        }

        public override void OnUnload() {
            On.Celeste.BadelineOldsite.ctor_EntityData_Vector2_int -= RestoreBadelineOldsitePosition;
            IL.Celeste.BadelineOldsite.Added -= BadelineOldsiteOnAdded;
            On.Celeste.BadelineOldsite.Update -= BadelineOldsiteOnUpdate;
            addedHook.Dispose();
        }
    }
}