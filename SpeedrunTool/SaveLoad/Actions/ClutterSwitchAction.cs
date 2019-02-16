using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions
{
    public class ClutterSwitchAction : AbstractEntityAction
    {
        private Dictionary<EntityID, ClutterSwitch> _savedClutterSwitches = new Dictionary<EntityID, ClutterSwitch>();

        public override void OnQuickSave(Level level)
        {
            _savedClutterSwitches = level.Tracker.GetDictionary<ClutterSwitch>();
        }

        private void RestoreClutterSwitchPosition(On.Celeste.ClutterSwitch.orig_ctor_EntityData_Vector2 orig,
            ClutterSwitch self, EntityData data,
            Vector2 offset)
        {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && _savedClutterSwitches.ContainsKey(entityId))
            {
                ClutterSwitch savedClutterSwitch = _savedClutterSwitches[entityId];
                self.Position = savedClutterSwitch.Position;
            }
        }

        public override void OnClear()
        {
            _savedClutterSwitches.Clear();
        }

        public override void OnLoad()
        {
            On.Celeste.ClutterSwitch.ctor_EntityData_Vector2 += RestoreClutterSwitchPosition;
        }

        public override void OnUnload()
        {
            On.Celeste.ClutterSwitch.ctor_EntityData_Vector2 -= RestoreClutterSwitchPosition;
        }

        public override void OnInit()
        {
            typeof(ClutterSwitch).AddToTracker();
        }

        public override void OnUpdateEntitiesWhenFreeze(Level level)
        {
            level.UpdateEntities<ClutterSwitch>();
        }
    }
}