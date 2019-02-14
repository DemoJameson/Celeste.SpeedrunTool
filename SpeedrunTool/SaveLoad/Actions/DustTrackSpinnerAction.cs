using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions
{
    public class DustTrackSpinnerAction : AbstractEntityAction
    {
        private Dictionary<EntityID, DustTrackSpinner> _savedDustTrackSpinners = new Dictionary<EntityID, DustTrackSpinner>();

        public override void OnQuickSave(Level level)
        {
            _savedDustTrackSpinners = level.Tracker.GetDictionary<DustTrackSpinner>();
        }

        private void RestoreDustTrackSpinnerPosition(On.Celeste.DustTrackSpinner.orig_ctor orig, DustTrackSpinner self, EntityData data,
            Vector2 offset)
        {

            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && _savedDustTrackSpinners.ContainsKey(entityId))
            {
                DustTrackSpinner savedDustTrackSpinner = _savedDustTrackSpinners[entityId];
                
                PropertyInfo property = typeof(TrackSpinner).GetProperty("Percent", BindingFlags.Public | BindingFlags.Instance);
                property.SetValue(self, savedDustTrackSpinner.Percent);
                self.Up = savedDustTrackSpinner.Up;
            }
        }

        public override void OnClear()
        {
            _savedDustTrackSpinners.Clear();
        }

        public override void OnLoad()
        {
            On.Celeste.DustTrackSpinner.ctor += RestoreDustTrackSpinnerPosition;
        }

        public override void OnUnload()
        {
            On.Celeste.DustTrackSpinner.ctor -= RestoreDustTrackSpinnerPosition;
        }

        public override void OnInit()
        {
            typeof(DustTrackSpinner).AddToTracker();
        }
    }
}