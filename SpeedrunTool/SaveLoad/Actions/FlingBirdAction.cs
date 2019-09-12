using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class FlingBirdAction : AbstractEntityAction {
        private Dictionary<EntityID, FlingBird> savedFlingBirds = new Dictionary<EntityID, FlingBird>();

        public override void OnQuickSave(Level level) {
            savedFlingBirds = level.Tracker.GetDictionary<FlingBird>();
        }

        private void RestoreFlingBirdPosition(On.Celeste.FlingBird.orig_ctor_EntityData_Vector2 orig,
            FlingBird self, EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && savedFlingBirds.ContainsKey(entityId)) {
                FlingBird savedFlingBird = savedFlingBirds[entityId];
                self.Position = savedFlingBird.Position;
            }
        }

        public override void OnClear() {
            savedFlingBirds.Clear();
        }

        public override void OnLoad() {
            On.Celeste.FlingBird.ctor_EntityData_Vector2 += RestoreFlingBirdPosition;
        }

        public override void OnUnload() {
            On.Celeste.FlingBird.ctor_EntityData_Vector2 -= RestoreFlingBirdPosition;
        }

        public override void OnInit() {
            typeof(FlingBird).AddToTracker();
        }
    }
}