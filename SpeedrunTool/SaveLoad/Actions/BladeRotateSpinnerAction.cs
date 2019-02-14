using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions
{
    public class BladeRotateSpinnerAction : AbstractEntityAction
    {
        private Dictionary<EntityID, BladeRotateSpinner> _savedBladeRotateSpinners = new Dictionary<EntityID, BladeRotateSpinner>();

        public override void OnQuickSave(Level level)
        {
            _savedBladeRotateSpinners = level.Tracker.GetDictionary<BladeRotateSpinner>();
        }

        private void RestoreBladeRotateSpinnerPosition(On.Celeste.BladeRotateSpinner.orig_ctor orig, BladeRotateSpinner self,
            EntityData data,
            Vector2 offset)
        {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && _savedBladeRotateSpinners.ContainsKey(entityId))
            {
                BladeRotateSpinner savedBladeRotateSpinner = _savedBladeRotateSpinners[entityId];
                FieldInfo property = typeof(RotateSpinner).GetField("rotationPercent", BindingFlags.NonPublic | BindingFlags.Instance);
                property.SetValue(self, property.GetValue(savedBladeRotateSpinner));               
            }
        }

        public override void OnClear()
        {
            _savedBladeRotateSpinners.Clear();
        }

        public override void OnLoad()
        {
            On.Celeste.BladeRotateSpinner.ctor += RestoreBladeRotateSpinnerPosition;
        }

        public override void OnUnload()
        {
            On.Celeste.BladeRotateSpinner.ctor -= RestoreBladeRotateSpinnerPosition;
        }

        public override void OnInit()
        {
            typeof(BladeRotateSpinner).AddToTracker();
        }
    }
}