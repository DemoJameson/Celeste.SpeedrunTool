using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class CrumblePlatformAction : AbstractEntityAction {
        private Dictionary<EntityId2, CrumblePlatform> savedCrumblePlatforms =
            new Dictionary<EntityId2, CrumblePlatform>();

        private ILHook addedHook;

        public override void OnSaveSate(Level level) {
            savedCrumblePlatforms = level.Entities.FindAll<CrumblePlatform>()
                .Where(platform => !platform.Collidable).ToDictionary(platform => platform.GetEntityId2());
        }

        private void RestoreCrumblePlatformPosition(On.Celeste.CrumblePlatform.orig_ctor_EntityData_Vector2 orig,
            CrumblePlatform self, EntityData data,
            Vector2 offset) {
            EntityId2 entityId = data.ToEntityId2(self.GetType());
            self.SetEntityId2(entityId);
            orig(self, data, offset);
        }

        private void CrumblePlatformOnAdded(On.Celeste.CrumblePlatform.orig_Added orig, CrumblePlatform self, Scene scene) {
            orig(self, scene);

            EntityId2 entityId = self.GetEntityId2();
            if (IsLoadStart && savedCrumblePlatforms.ContainsKey(entityId)) {
                CrumblePlatform saved = savedCrumblePlatforms[entityId];
                self.CopyEntity(saved);
                
                (self.GetField("images") as List<Image>)?.ForEach(image => image.Visible = saved.Collidable);
                self.CopyImageList(saved, "outline");
            } 
        }

        private static void BlockCoroutineStart(ILContext il) {
            il.SkipAddCoroutine<CrumblePlatform>("Sequence", ()=>IsLoadStart);
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