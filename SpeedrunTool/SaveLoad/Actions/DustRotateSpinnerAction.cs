using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions
{
    public class DustRotateSpinnerAction : AbstractEntityAction
    {
        private Dictionary<EntityID, DustRotateSpinner> _savedDustRotateSpinners = new Dictionary<EntityID, DustRotateSpinner>();

        public override void OnQuickSave(Level level)
        {
            _savedDustRotateSpinners = level.Tracker.GetDictionary<DustRotateSpinner>();
        }

        private void RestoreDustRotateSpinnerState(On.Celeste.DustRotateSpinner.orig_ctor orig, DustRotateSpinner self,
            EntityData data,
            Vector2 offset)
        {
            orig(self, data, offset);
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);

            if (IsLoadStart && _savedDustRotateSpinners.ContainsKey(entityId))
            {
                DustRotateSpinner savedDustRotateSpinner = _savedDustRotateSpinners[entityId];
                FieldInfo property = typeof(RotateSpinner).GetField("rotationPercent", BindingFlags.NonPublic | BindingFlags.Instance);
                property.SetValue(self, property.GetValue(savedDustRotateSpinner));               
            }
        }

        public override void OnClear()
        {
            _savedDustRotateSpinners.Clear();
        }

        public override void OnLoad()
        {
            On.Celeste.DustRotateSpinner.ctor += RestoreDustRotateSpinnerState;
        }

        public override void OnUnload()
        {
            On.Celeste.DustRotateSpinner.ctor -= RestoreDustRotateSpinnerState;
        }

        public override void OnInit()
        {
            typeof(DustRotateSpinner).AddToTracker();
        }
    }
}