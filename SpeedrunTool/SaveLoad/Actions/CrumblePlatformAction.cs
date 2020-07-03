using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
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
                CrumblePlatform saved = savedCrumblePlatforms[entityId];
                self.CopyFrom(saved);
                
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