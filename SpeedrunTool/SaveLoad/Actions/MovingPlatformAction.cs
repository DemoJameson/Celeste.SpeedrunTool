using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions
{
    public class MovingPlatformAction : AbstractEntityAction
    {
        private Dictionary<EntityID, MovingPlatform> _savedMovingPlatforms = new Dictionary<EntityID, MovingPlatform>();

        public override void OnQuickSave(Level level)
        {
            _savedMovingPlatforms = level.Tracker.GetDictionary<MovingPlatform>();
        }

        private void ResotreMovingPlatformPosition(On.Celeste.MovingPlatform.orig_ctor_EntityData_Vector2 orig,
            MovingPlatform self, EntityData data,
            Vector2 offset)
        {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && _savedMovingPlatforms.ContainsKey(entityId))
            {
                MovingPlatform savedMovingPlatform = _savedMovingPlatforms[entityId];
                self.Position = savedMovingPlatform.Position;
                Tween tween = self.Get<Tween>();
                Tween savedTween = savedMovingPlatform.Get<Tween>();
                tween.CopyFrom(savedTween);
            }
        }

        public override void OnClear()
        {
            _savedMovingPlatforms.Clear();
        }


        public override void OnLoad()
        {
            On.Celeste.MovingPlatform.ctor_EntityData_Vector2 += ResotreMovingPlatformPosition;
        }

        public override void OnUnload()
        {
            On.Celeste.MovingPlatform.ctor_EntityData_Vector2 -= ResotreMovingPlatformPosition;
        }

        public override void OnInit()
        {
            typeof(MovingPlatform).AddToTracker();
        }
    }
}