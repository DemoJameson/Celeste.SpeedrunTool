using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions
{
    public class SpringAction : AbstractEntityAction
    {
        private Dictionary<EntityID, Spring> _springs = new Dictionary<EntityID, Spring>();

        public override void OnQuickSave(Level level)
        {
            _springs = level.Tracker.GetDictionary<Spring>();
        }

        private void SpringOnCtorEntityDataVector2Orientations(
            On.Celeste.Spring.orig_ctor_EntityData_Vector2_Orientations orig, Spring self, EntityData data,
            Vector2 offset, Spring.Orientations orientation)
        {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset, orientation);

            if (IsLoadStart)
            {
                if (_springs.ContainsKey(entityId))
                    self.Position = _springs[entityId].Position;
                else
                    self.Add(new RemoveSelfComponent());
            }
        }

        public override void OnClear()
        {
            _springs.Clear();
        }

        public override void OnLoad()
        {
            On.Celeste.Spring.ctor_EntityData_Vector2_Orientations += SpringOnCtorEntityDataVector2Orientations;
        }

        public override void OnUnload()
        {
            On.Celeste.Spring.ctor_EntityData_Vector2_Orientations -= SpringOnCtorEntityDataVector2Orientations;
        }

        public override void OnInit()
        {
            typeof(Spring).AddToTracker();
        }
    }
}