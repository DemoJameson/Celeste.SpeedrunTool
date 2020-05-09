using System.Collections;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class LightningBreakerBoxAction : AbstractEntityAction {
        private const string DisableSetBreakValue = "DisableSetBreakValue";
        private LightningBreakerBox savedLightningBreakerBox;

        public override void OnQuickSave(Level level) {
            savedLightningBreakerBox = level.Entities.FindFirst<LightningBreakerBox>();
        }

        private void RestoreLightningBreakerBoxHealth(
            On.Celeste.LightningBreakerBox.orig_ctor_EntityData_Vector2 orig, LightningBreakerBox self,
            EntityData entityData, Vector2 levelOffset) {
            orig(self, entityData, levelOffset);

            if (IsLoadStart) {
                if (savedLightningBreakerBox != null) {
                    self.Position = savedLightningBreakerBox.Position;
                    self.CopyField(typeof(LightningBreakerBox), "health", savedLightningBreakerBox);
                    self.CopyField(typeof(LightningBreakerBox), "sink", savedLightningBreakerBox);
                    SineWave sine = self.Get<SineWave>();
                    SineWave savedSine = savedLightningBreakerBox.Get<SineWave>();
                    sine.Counter = savedSine.Counter;

                    Sprite sprite = (Sprite) self.GetField(typeof(LightningBreakerBox), "sprite");
                    int health = (int) savedLightningBreakerBox.GetField(typeof(LightningBreakerBox), "health");
                    if (health < 2) {
                        sprite.Play("open");
                    }
                    if (health == 0) {
                        self.Visible = false;
                        self.Collidable = false;
                        self.Add(new Coroutine(BreakBox(self)));
                    }
                }
                else {
                    self.Visible = false;
                    self.Collidable = false;
                    self.Add(new Coroutine(BreakBox(self)));
                }
            }
        }

        private static IEnumerator BreakBox(LightningBreakerBox self) {
            self.InvokeMethod(typeof(LightningBreakerBox), "Break");
            self.Add(new DisableSetBreakValueComponent());
            yield break;
        }

        // 去除撞毁电箱后整个画面闪白的效果
        private void LightningOnSetBreakValue(On.Celeste.Lightning.orig_SetBreakValue orig, Level level, float t) {
            if (level.Entities.FindFirst<LightningBreakerBox>() is LightningBreakerBox box &&
                box.GetExtendedBoolean(DisableSetBreakValue)) {
                return;
            }

            orig(level, t);
        }

        public override void OnClear() {
            savedLightningBreakerBox = null;
        }

        public override void OnLoad() {
            On.Celeste.LightningBreakerBox.ctor_EntityData_Vector2 += RestoreLightningBreakerBoxHealth;
            On.Celeste.Lightning.SetBreakValue += LightningOnSetBreakValue;
        }

        public override void OnUnload() {
            On.Celeste.LightningBreakerBox.ctor_EntityData_Vector2 -= RestoreLightningBreakerBoxHealth;
            On.Celeste.Lightning.SetBreakValue -= LightningOnSetBreakValue;
        }

        private class DisableSetBreakValueComponent : Monocle.Component {
            public DisableSetBreakValueComponent() : base(true, true) { }

            public override void Added(Entity entity) {
                base.Added(entity);
                entity.SetExtendedBoolean(DisableSetBreakValue, true);
            }

            public override void EntityRemoved(Scene scene) {
                base.EntityRemoved(scene);
                Entity?.SetExtendedBoolean(DisableSetBreakValue, false);
            }
        }
    }
}