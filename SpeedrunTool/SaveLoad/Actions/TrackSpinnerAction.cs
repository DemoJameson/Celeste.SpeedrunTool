using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class TrackSpinnerAction : AbstractEntityAction {
        private Dictionary<EntityID, TrackSpinner> _savedTrackSpinners = new Dictionary<EntityID, TrackSpinner>();

        public override void OnQuickSave(Level level) {
            List<Entity> entities = level.Tracker.GetEntities<BladeTrackSpinner>();
            entities.AddRange(level.Tracker.GetEntities<DustTrackSpinner>());
            _savedTrackSpinners = entities.Cast<TrackSpinner>().ToDictionary(entity => entity.GetEntityId());
        }

        private void RestoreTrackSpinnerPosition(On.Celeste.TrackSpinner.orig_ctor orig, TrackSpinner self,
            EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && _savedTrackSpinners.ContainsKey(entityId)) {
                TrackSpinner savedTrackSpinner = _savedTrackSpinners[entityId];
                PropertyInfo property =
                    typeof(TrackSpinner).GetProperty("Percent", BindingFlags.Public | BindingFlags.Instance);
                property.SetValue(self, savedTrackSpinner.Percent);
                self.Up = savedTrackSpinner.Up;
                self.Moving = savedTrackSpinner.Moving;
                self.PauseTimer = savedTrackSpinner.PauseTimer;

                if (self is DustTrackSpinner) {
                    self.Add(new Coroutine(RestoreEyeDirection(self, savedTrackSpinner)));
                }
            }
        }

        private static IEnumerator RestoreEyeDirection(TrackSpinner self, TrackSpinner saved) {
            DustGraphic dustGraphic = self.GetPrivateField("dusty") as DustGraphic;
            DustGraphic savedDustGraphic = saved.GetPrivateField("dusty") as DustGraphic;
            dustGraphic.EyeDirection = savedDustGraphic.EyeDirection;
            yield break;
        }

        public override void OnClear() {
            _savedTrackSpinners.Clear();
        }

        public override void OnLoad() {
            On.Celeste.TrackSpinner.ctor += RestoreTrackSpinnerPosition;
        }

        public override void OnUnload() {
            On.Celeste.TrackSpinner.ctor -= RestoreTrackSpinnerPosition;
        }

        public override void OnInit() {
            typeof(BladeTrackSpinner).AddToTracker();
            typeof(DustTrackSpinner).AddToTracker();
        }
    }
}