using System;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class CrumblePlatformAction : AbstractEntityAction {
        private Dictionary<EntityID, CrumblePlatform> savedCrumblePlatforms =
            new Dictionary<EntityID, CrumblePlatform>();

        private ILHook addedHook;


        public override void OnQuickSave(Level level) {
            savedCrumblePlatforms = level.Entities.FindAll<CrumblePlatform>()
                .Where(platform => !platform.Collidable).ToDictionary(platform => platform.GetEntityId());
        }

        private void RestoreCrumblePlatformPosition(On.Celeste.CrumblePlatform.orig_ctor_EntityData_Vector2 orig,
            CrumblePlatform self, EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);
        }

        private void CrumblePlatformOnAdded(On.Celeste.CrumblePlatform.orig_Added orig, CrumblePlatform self, Scene scene) {
            orig(self, scene);

            EntityID entityId = self.GetEntityId();
            if (IsLoadStart && savedCrumblePlatforms.ContainsKey(entityId)) {
                CrumblePlatform savedCrumblePlatform = savedCrumblePlatforms[entityId];
                self.Collidable = savedCrumblePlatform.Collidable;
                (self.GetField("images") as List<Image>)?.ForEach(image => image.Visible = savedCrumblePlatform.Collidable);
                (self.GetField("outline") as List<Image>)?.ForEach(image => image.Color = Color.White * (savedCrumblePlatform.Collidable ? 0 : 1));
            } 
        }

        private static void BlockCoroutineStart(ILContext il) {
            ILCursor c = new ILCursor(il);
            for (int i = 0; i < 6; i++)
                c.GotoNext(inst =>
                    inst.MatchCall(typeof(Entity).GetMethod("Add", new[] {typeof(Monocle.Component)})));
            Instruction skipCoroutine = c.Next.Next;
            c.GotoPrev(i => i.MatchCall(typeof(Entity), "get_Width"));
            c.GotoNext();
            c.GotoNext();
            c.EmitDelegate<Func<bool>>(() => IsLoadStart);
            c.Emit(OpCodes.Brtrue, skipCoroutine);
        }

        public override void OnClear() {
            savedCrumblePlatforms.Clear();
        }

        public override void OnLoad() {
            On.Celeste.CrumblePlatform.ctor_EntityData_Vector2 += RestoreCrumblePlatformPosition;
            On.Celeste.CrumblePlatform.Added += CrumblePlatformOnAdded;
            addedHook = new ILHook(typeof(CrumblePlatform).GetMethod("orig_Added"), BlockCoroutineStart);
        }

        public override void OnUnload() {
            On.Celeste.CrumblePlatform.ctor_EntityData_Vector2 -= RestoreCrumblePlatformPosition;
            On.Celeste.CrumblePlatform.Added -= CrumblePlatformOnAdded;
            addedHook.Dispose();
        }
    }
}