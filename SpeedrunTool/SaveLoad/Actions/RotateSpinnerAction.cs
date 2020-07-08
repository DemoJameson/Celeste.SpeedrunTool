using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class RotateSpinnerAction : AbstractEntityAction {
        private Dictionary<EntityId2, RotateSpinner> savedRotateSpinners =
            new Dictionary<EntityId2, RotateSpinner>();

        private static readonly FieldInfo RotationPercentFieldInfo =
            typeof(RotateSpinner).GetField("rotationPercent", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo CenterFieldInfo =
            typeof(RotateSpinner).GetField("center", BindingFlags.NonPublic | BindingFlags.Instance);

        public override void OnSaveSate(Level level) {
            savedRotateSpinners = level.Entities.FindAllToDict<RotateSpinner>();
        }

        private void RestoreRotateSpinnerState(On.Celeste.RotateSpinner.orig_ctor orig, RotateSpinner self,
            EntityData data,
            Vector2 offset) {
            orig(self, data, offset);
            EntityId2 entityId = data.ToEntityId2(self.GetType());
            self.SetEntityId2(entityId);

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
    }
}