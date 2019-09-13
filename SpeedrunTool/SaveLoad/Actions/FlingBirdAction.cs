using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class FlingBirdAction : AbstractEntityAction {
        private Dictionary<EntityID, FlingBird> savedFlingBirds = new Dictionary<EntityID, FlingBird>();
        private bool waitForUpdate;

        public override void OnQuickSave(Level level) {
            savedFlingBirds = level.Tracker.GetDictionary<FlingBird>();
        }

        private void RestoreFlingBirdPosition(On.Celeste.FlingBird.orig_ctor_EntityData_Vector2 orig,
            FlingBird self, EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart) {
                if (savedFlingBirds.ContainsKey(entityId)) {
                    waitForUpdate = true;
                }
                else {
                    self.Add(new RemoveSelfComponent());
                }
            }
        }

        public override void OnClear() {
            savedFlingBirds.Clear();
        }

        public override void OnLoad() {
            On.Celeste.FlingBird.ctor_EntityData_Vector2 += RestoreFlingBirdPosition;
            On.Celeste.FlingBird.Update += FlingBirdOnUpdate;
        }

        private void FlingBirdOnUpdate(On.Celeste.FlingBird.orig_Update orig, FlingBird self) {
            if (waitForUpdate) {
                waitForUpdate = false;
                EntityID entityId = self.GetEntityId();
                if (IsLoadStart && savedFlingBirds.ContainsKey(entityId)) {
                    FlingBird savedFlingBird = savedFlingBirds[entityId];
                    int segmentIndex = (int) savedFlingBird.GetPrivateField("segmentIndex");

                    if (segmentIndex > 0) {
                        bool atEnding = segmentIndex >= savedFlingBird.NodeSegments.Count;
                        Sprite sprite = (Sprite) self.GetPrivateField("sprite");
                        sprite.Scale = Vector2.One;
                        if (atEnding) {
                            self.Position = savedFlingBird.NodeSegments[segmentIndex - 1].Last();
                            sprite.Play("hoverStressed");
                            sprite.Scale.X = 1f;
                            // WaitForLightningClear
                            self.SetPrivateField("state", 3);
                        }
                        else {
                            self.Position = savedFlingBird.NodeSegments[segmentIndex].First();
                            sprite.Scale.X = -1f;
                            if (savedFlingBird.SegmentsWaiting[segmentIndex]) {
                                sprite.Play("hoverStressed");
                            }
                            else {
                                sprite.Play("hover");
                            }
                        }

                        self.CopyPrivateField("segmentIndex", savedFlingBird);
                    }
                }
            }

            orig(self);
        }

        public override void OnUnload() {
            On.Celeste.FlingBird.ctor_EntityData_Vector2 -= RestoreFlingBirdPosition;
            On.Celeste.FlingBird.Update -= FlingBirdOnUpdate;
        }

        public override void OnInit() {
            typeof(FlingBird).AddToTracker();
        }
    }
}