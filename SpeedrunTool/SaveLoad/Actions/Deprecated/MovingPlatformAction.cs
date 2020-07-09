using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions.Deprecated {
    public class MovingPlatformAction : ComponentAction {
        private Dictionary<EntityId2, MovingPlatform> savedMovingPlatforms = new Dictionary<EntityId2, MovingPlatform>();

        public override void OnSaveSate(Level level) {
            savedMovingPlatforms = level.Entities.FindAllToDict<MovingPlatform>();
        }

        private void ResotreMovingPlatformPosition(On.Celeste.MovingPlatform.orig_ctor_EntityData_Vector2 orig,
            MovingPlatform self, EntityData data,
            Vector2 offset) {
            EntityId2 entityId = data.ToEntityId2(self.GetType());
            self.SetEntityId2(entityId);
            orig(self, data, offset);

            if (IsLoadStart && savedMovingPlatforms.ContainsKey(entityId)) {
                MovingPlatform savedMovingPlatform = savedMovingPlatforms[entityId];
                self.Position = savedMovingPlatform.Position;
                Tween tween = self.Get<Tween>();
                Tween savedTween = savedMovingPlatform.Get<Tween>();
                tween.TryCopyFrom(savedTween);
            }
        }

        public override void OnClear() {
            savedMovingPlatforms.Clear();
        }


        public override void OnLoad() {
            On.Celeste.MovingPlatform.ctor_EntityData_Vector2 += ResotreMovingPlatformPosition;
        }

        public override void OnUnload() {
            On.Celeste.MovingPlatform.ctor_EntityData_Vector2 -= ResotreMovingPlatformPosition;
        }
    }
}