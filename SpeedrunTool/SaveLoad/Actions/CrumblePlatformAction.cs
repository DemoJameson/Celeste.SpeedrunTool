using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class CrumblePlatformAction : AbstractEntityAction {
        private Dictionary<EntityID, CrumblePlatform> savedCrumblePlatforms =
            new Dictionary<EntityID, CrumblePlatform>();

        public override void OnQuickSave(Level level) {
            savedCrumblePlatforms = level.Tracker.GetCastEntities<CrumblePlatform>()
                .Where(platform => !platform.Collidable).ToDictionary(platform => platform.GetEntityId());
        }

        private void RestoreCrumblePlatformPosition(On.Celeste.CrumblePlatform.orig_ctor_EntityData_Vector2 orig,
            CrumblePlatform self, EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && savedCrumblePlatforms.ContainsKey(entityId)) self.SetExtendedDataValue("IsFade", true);
        }

        private static Player SolidOnGetPlayerOnTop(On.Celeste.Solid.orig_GetPlayerOnTop orig, Solid self) {
            if (self is CrumblePlatform && self.GetExtendedDataValue<bool>("IsFade")) {
                self.SetExtendedDataValue("IsFade", false);

                MuteAudio("event:/game/general/platform_disintegrate");
                return Engine.Scene.Tracker.GetEntity<Player>();
            }

            return orig(self);
        }

        public override void OnClear() {
            savedCrumblePlatforms.Clear();
        }

        public override void OnLoad() {
            On.Celeste.CrumblePlatform.ctor_EntityData_Vector2 += RestoreCrumblePlatformPosition;
            On.Celeste.Solid.GetPlayerOnTop += SolidOnGetPlayerOnTop;
        }

        public override void OnUnload() {
            On.Celeste.CrumblePlatform.ctor_EntityData_Vector2 -= RestoreCrumblePlatformPosition;
            On.Celeste.Solid.GetPlayerOnTop -= SolidOnGetPlayerOnTop;
        }

        public override void OnInit() {
            typeof(CrumblePlatform).AddToTracker();
        }

        public override void OnUpdateEntitiesWhenFreeze(Level level) {
            level.UpdateEntities<CrumblePlatform>();
        }
    }
}