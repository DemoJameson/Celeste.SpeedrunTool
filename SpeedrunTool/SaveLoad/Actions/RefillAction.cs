using System.Collections;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class RefillAction : AbstractEntityAction {
        private Dictionary<EntityID, Refill> savedRefills = new Dictionary<EntityID, Refill>();

        public override void OnQuickSave(Level level) {
            savedRefills = level.Tracker.GetDictionary<Refill>();
        }

        private void RefillOnCtorEntityDataVector2(
            On.Celeste.Refill.orig_ctor_EntityData_Vector2 orig, Refill self, EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart) {
                if (savedRefills.ContainsKey(entityId)) {
                    Refill savedRefill = savedRefills[entityId];
                    if (!savedRefill.Collidable) {
                        self.Collidable = false;
                        float respawnTimer = (float) savedRefill.GetPrivateField("respawnTimer");
                        self.SetPrivateField("respawnTimer", respawnTimer);
                        self.Add(new Coroutine(ConsumeRefill(self)));
                    }
                }
                else {
                    self.Add(new RemoveSelfComponent());
                }
            }
        }

        private static IEnumerator ConsumeRefill(Refill self) {
            (self.GetPrivateField("sprite") as Sprite).Visible = false;
            (self.GetPrivateField("flash") as Sprite).Visible = false;
            if (!(bool)self.GetPrivateField("oneUse")) {
                (self.GetPrivateField("outline") as Monocle.Image).Visible = true;
            }
            else {
                self.RemoveSelf();
            }
            self.Depth = 8999;
            yield return null;
        }

        public override void OnClear() {
            savedRefills.Clear();
        }

        public override void OnLoad() {
            On.Celeste.Refill.ctor_EntityData_Vector2 += RefillOnCtorEntityDataVector2;
        }


        public override void OnUnload() {
            On.Celeste.Refill.ctor_EntityData_Vector2 -= RefillOnCtorEntityDataVector2;
        }

        public override void OnInit() {
            typeof(Refill).AddToTracker();
        }
    }
}