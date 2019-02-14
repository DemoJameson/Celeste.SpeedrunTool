using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions
{
    public class BladeTrackSpinnerAction : AbstractEntityAction
    {
        private Dictionary<EntityID, BladeTrackSpinner> _savedBladeTrackSpinners = new Dictionary<EntityID, BladeTrackSpinner>();

        public override void OnQuickSave(Level level)
        {
            _savedBladeTrackSpinners = level.Tracker.GetDictionary<BladeTrackSpinner>();
        }

        private void RestoreBladeTrackSpinnerPosition(On.Celeste.BladeTrackSpinner.orig_ctor orig, BladeTrackSpinner self, EntityData data,
            Vector2 offset)
        {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && _savedBladeTrackSpinners.ContainsKey(entityId))
            {
                BladeTrackSpinner savedBladeTrackSpinner = _savedBladeTrackSpinners[entityId];
                
                PropertyInfo property = typeof(TrackSpinner).GetProperty("Percent", BindingFlags.Public | BindingFlags.Instance);
                property = property.DeclaringType.GetProperty(property.Name); 
                property.SetValue(self, savedBladeTrackSpinner.Percent);
                self.Up = savedBladeTrackSpinner.Up;
            }
        }

        public override void OnClear()
        {
            _savedBladeTrackSpinners.Clear();
        }

        public override void OnLoad()
        {
            On.Celeste.BladeTrackSpinner.ctor += RestoreBladeTrackSpinnerPosition;
        }

        public override void OnUnload()
        {
            On.Celeste.BladeTrackSpinner.ctor -= RestoreBladeTrackSpinnerPosition;
        }

        public override void OnInit()
        {
            typeof(BladeTrackSpinner).AddToTracker();
        }
    }
}