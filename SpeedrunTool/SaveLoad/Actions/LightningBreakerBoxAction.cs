using System.Collections;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class LightningBreakerBoxAction : AbstractEntityAction {
        private LightningBreakerBox savedLightningBreakerBox;

        public override void OnQuickSave(Level level) {
            savedLightningBreakerBox = level.Tracker.GetEntity<LightningBreakerBox>();
        }

        private void RestoreLightningBreakerBoxHealth(
            On.Celeste.LightningBreakerBox.orig_ctor_EntityData_Vector2 orig, LightningBreakerBox self,
            EntityData entityData, Vector2 levelOffset) {
            orig(self, entityData, levelOffset);

            if (IsLoadStart) {
                if (savedLightningBreakerBox != null) {
                    self.Position = savedLightningBreakerBox.Position;
                    self.CopyPrivateField("health", savedLightningBreakerBox);
                    self.CopyPrivateField("sink", savedLightningBreakerBox);

                    Sprite sprite = (Sprite) self.GetPrivateField("sprite");
                    int health = (int) savedLightningBreakerBox.GetPrivateField("health");
                    if (health < 2) {
                        sprite.Play("open");
                    }
                    if (health == 0) {
                        self.Visible = false;
                        self.Collidable = false;
                        self.Add(new Coroutine(BreakBox(self)));
                    }
//                    
//                    SineWave sineWave = (SineWave) self.GetPrivateField("sine");
//                    SineWave savedSineWave = (SineWave) savedLightningBreakerBox.GetPrivateField("sine");
//                    sineWave.Counter = savedSineWave.Counter;
                }
                else {
                    self.Visible = false;
                    self.Collidable = false;
                    self.Add(new Coroutine(BreakBox(self)));
                }
            }
        }

        private static IEnumerator BreakBox(LightningBreakerBox self) {
            self.InvokePrivateMethod("Break");
            yield break;
        }

        public override void OnClear() {
            savedLightningBreakerBox = null;
        }

        public override void OnLoad() {
            On.Celeste.LightningBreakerBox.ctor_EntityData_Vector2 += RestoreLightningBreakerBoxHealth;
        }

        public override void OnUnload() {
            On.Celeste.LightningBreakerBox.ctor_EntityData_Vector2 -= RestoreLightningBreakerBoxHealth;
        }

        public override void OnInit() {
            typeof(LightningBreakerBox).AddToTracker();
        }
    }
}