using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions
{
    // TODO: 7E 中与移动平台相连的时候，保存后无法关联移动平台
    public class DustRotateSpinnerAction : AbstractEntityAction
    {
        private Dictionary<EntityID, DustRotateSpinner> _savedDustRotateSpinners =
            new Dictionary<EntityID, DustRotateSpinner>();

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
                self.Add(new Coroutine(RestoreRotationPercent(self, savedDustRotateSpinner)));
            }
        }

        private IEnumerator RestoreRotationPercent(DustRotateSpinner self, DustRotateSpinner saved)
        {
            FieldInfo fieldInfo =
                typeof(RotateSpinner).GetField("rotationPercent", BindingFlags.NonPublic | BindingFlags.Instance);
            fieldInfo.SetValue(self, fieldInfo.GetValue(saved));
            yield break;
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