using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class RotateSpinnerAction : AbstractEntityAction {
        private readonly Dictionary<EntityID, RotateSpinner> savedRotateSpinners =
            new Dictionary<EntityID, RotateSpinner>();

        private static readonly FieldInfo RotationPercentFieldInfo =
            typeof(RotateSpinner).GetField("rotationPercent", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo CenterFieldInfo =
            typeof(RotateSpinner).GetField("center", BindingFlags.NonPublic | BindingFlags.Instance);

        public override void OnQuickSave(Level level) {
            List<Entity> entities = level.Tracker.GetEntities<BladeRotateSpinner>();
            entities.AddRange(level.Tracker.GetEntities<DustRotateSpinner>());
            entities.AddRange(level.Tracker.GetEntities<StarRotateSpinner>());
            savedRotateSpinners.AddRange(entities.Cast<RotateSpinner>());
        }

        private void RestoreRotateSpinnerState(On.Celeste.RotateSpinner.orig_ctor orig, RotateSpinner self,
            EntityData data,
            Vector2 offset) {
            orig(self, data, offset);
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);

            if (IsLoadStart && savedRotateSpinners.ContainsKey(entityId)) {
                RotateSpinner saved = savedRotateSpinners[entityId];
                CenterFieldInfo.SetValue(self, CenterFieldInfo.GetValue(saved));
                self.Add(new Coroutine(RestoreRotationPercent(self, saved)));
            }
        }

        private static IEnumerator RestoreRotationPercent(RotateSpinner self, RotateSpinner saved) {
            RotationPercentFieldInfo.SetValue(self, RotationPercentFieldInfo.GetValue(saved));
            yield break;
        }

        public override void OnClear() {
            savedRotateSpinners.Clear();
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
            typeof(StarRotateSpinner).AddToTracker();
        }
    }
}