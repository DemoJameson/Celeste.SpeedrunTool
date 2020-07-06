using System.Collections;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class LightningBreakerBoxAction : AbstractEntityAction {
        private const string DisableSetBreakValue = "DisableSetBreakValue";

        private Dictionary<EntityId2, LightningBreakerBox> savedBreakerBoxes =
            new Dictionary<EntityId2, LightningBreakerBox>();

        public override void OnQuickSave(Level level) {
            savedBreakerBoxes = level.Entities.FindAllToDict<LightningBreakerBox>();
        }

        private void RestoreLightningBreakerBoxHealth(
            On.Celeste.LightningBreakerBox.orig_ctor_EntityData_Vector2 orig, LightningBreakerBox self,
            EntityData entityData, Vector2 levelOffset) {
            EntityId2 entityId = entityData.ToEntityId2(self.GetType());
            self.SetEntityId2(entityId);
            orig(self, entityData, levelOffset);

            if (!IsLoadStart) return;

            if (savedBreakerBoxes.ContainsKey(entityId)) {
                LightningBreakerBox saved = savedBreakerBoxes[entityId];
                self.Position = saved.Position;
                self.CopyFields(saved, "health", "sink", "shakeCounter", "smashParticles");
                self.CopySprite(saved, "sprite");

                SineWave sine = self.Get<SineWave>();
                SineWave savedSine = saved.Get<SineWave>();
                sine.Counter = savedSine.Counter;

                int health = (int) saved.GetField("health");

                if (health == 0) {
                    self.Visible = false;
                    self.Collidable = false;
                    self.Add(new Coroutine(BreakBox(self, false)));
                }
            }
            else {
                self.Visible = false;
                self.Collidable = false;
                self.Add(new Coroutine(BreakBox(self, true)));
            }
        }

        private static IEnumerator BreakBox(LightningBreakerBox self, bool disableSetBreakValue) {
            self.InvokeMethod(typeof(LightningBreakerBox), "Break");
            if (disableSetBreakValue) {
                self.Add(new DisableSetBreakValueComponent());
            }
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
            savedBreakerBoxes.Clear();
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