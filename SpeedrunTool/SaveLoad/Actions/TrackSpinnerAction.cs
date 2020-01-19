using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class TrackSpinnerAction : AbstractEntityAction {
        private Dictionary<EntityID, TrackSpinner> savedTrackSpinners =
            new Dictionary<EntityID, TrackSpinner>();

        private static readonly PropertyInfo PercentPropertyInfo =
            typeof(TrackSpinner).GetProperty("Percent", BindingFlags.Public | BindingFlags.Instance);

        public override void OnQuickSave(Level level) {
            savedTrackSpinners = level.Entities.GetDictionary<TrackSpinner>();
        }

        private void RestoreTrackSpinnerPosition(On.Celeste.TrackSpinner.orig_ctor orig, TrackSpinner self,
            EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && savedTrackSpinners.ContainsKey(entityId)) {
                TrackSpinner savedTrackSpinner = savedTrackSpinners[entityId];

                PercentPropertyInfo.SetValue(self, savedTrackSpinner.Percent);
                self.Up = savedTrackSpinner.Up;
                self.Moving = savedTrackSpinner.Moving;
                self.PauseTimer = savedTrackSpinner.PauseTimer;

                if (self is DustTrackSpinner) {
                    self.Add(new Coroutine(RestoreEyeDirection(self, savedTrackSpinner)));
                }

                self.UpdatePosition();
            }
        }

        private static IEnumerator RestoreEyeDirection(TrackSpinner self, TrackSpinner saved) {
            DustGraphic dustGraphic = self.GetPrivateField("dusty") as DustGraphic;
            DustGraphic savedDustGraphic = saved.GetPrivateField("dusty") as DustGraphic;
            dustGraphic.EyeDirection = savedDustGraphic.EyeDirection;
            yield break;
        }

        public override void OnClear() {
            savedTrackSpinners.Clear();
        }

        public override void OnLoad() {
            On.Celeste.TrackSpinner.ctor += RestoreTrackSpinnerPosition;
        }

        public override void OnUnload() {
            On.Celeste.TrackSpinner.ctor -= RestoreTrackSpinnerPosition;
        }
    }
}