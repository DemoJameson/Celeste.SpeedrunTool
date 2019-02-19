using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class RotateSpinnerAction : AbstractEntityAction {
        private Dictionary<EntityID, RotateSpinner> _savedRotateSpinners =
            new Dictionary<EntityID, RotateSpinner>();

        public override void OnQuickSave(Level level) {
            List<Entity> entities = level.Tracker.GetEntities<BladeRotateSpinner>();
            entities.AddRange(level.Tracker.GetEntities<DustRotateSpinner>());
            _savedRotateSpinners = entities.Cast<RotateSpinner>().ToDictionary(entity => entity.GetEntityId());
        }

        private void RestoreRotateSpinnerState(On.Celeste.RotateSpinner.orig_ctor orig, RotateSpinner self,
            EntityData data,
            Vector2 offset) {
            orig(self, data, offset);
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);

            if (IsLoadStart && _savedRotateSpinners.ContainsKey(entityId)) {
                RotateSpinner saved = _savedRotateSpinners[entityId];
                FieldInfo centerFieldInfo =
                    typeof(RotateSpinner).GetField("center", BindingFlags.NonPublic | BindingFlags.Instance);
                centerFieldInfo?.SetValue(self, centerFieldInfo.GetValue(saved));
                self.Add(new Coroutine(RestoreRotationPercent(self, saved)));
            }
        }

        private static IEnumerator RestoreRotationPercent(RotateSpinner self, RotateSpinner saved) {
            FieldInfo fieldInfo =
                typeof(RotateSpinner).GetField("rotationPercent", BindingFlags.NonPublic | BindingFlags.Instance);
            fieldInfo?.SetValue(self, fieldInfo.GetValue(saved));
            yield break;
        }

        public override void OnClear() {
            _savedRotateSpinners.Clear();
        }

        public override void OnLoad() {
            On.Celeste.RotateSpinner.ctor += RestoreRotateSpinnerState;
        }

        public override void OnUnload() {
            On.Celeste.RotateSpinner.ctor -= RestoreRotateSpinnerState;
        }

        public override void OnInit() {
            typeof(BladeRotateSpinner).AddToTracker();
            typeof(DustRotateSpinner).AddToTracker();
        }
    }
}