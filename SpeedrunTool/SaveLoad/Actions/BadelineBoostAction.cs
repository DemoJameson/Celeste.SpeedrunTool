using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class BadelineBoostAction2 : AbstractEntityAction {
        private Dictionary<EntityID, BadelineBoost> savedBadelineBoosts = new Dictionary<EntityID, BadelineBoost>();

        public override void OnQuickSave(Level level) {
            savedBadelineBoosts = level.Entities.GetDictionary<BadelineBoost>();
        }

        private void BadelineBoostOnCtor_EntityData_Vector2(On.Celeste.BadelineBoost.orig_ctor_EntityData_Vector2 orig,
            BadelineBoost self, EntityData data, Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);
        }

        private void BadelineBoostOnCtor_Vector2Array_bool_bool_bool_bool_bool(
            On.Celeste.BadelineBoost.orig_ctor_Vector2Array_bool_bool_bool_bool_bool orig, BadelineBoost self,
            Vector2[] nodes, bool lockCamera, bool canSkip, bool finalCh9Boost, bool finalCh9GoldenBoost,
            bool finalCh9Dialog) {
            EntityID entityId = self.GetEntityId();

            Level level = CelesteExtensions.GetLevel();

            if (level?.Session?.Level != null && entityId.IsDefault()) {
                entityId = self.CreateEntityId(string.Join("", nodes), lockCamera.ToString(), canSkip.ToString(),
                    finalCh9Boost.ToString(), finalCh9GoldenBoost.ToString(), finalCh9Dialog.ToString());
                self.SetEntityId(entityId);
            }

            orig(self, nodes, lockCamera, canSkip, finalCh9Boost, finalCh9GoldenBoost, finalCh9Dialog);

            if (!IsLoadStart) {
                return;
            }

            if (savedBadelineBoosts.ContainsKey(entityId)) {
                BadelineBoost saved = savedBadelineBoosts[entityId];

                self.Position = saved.Position;
                self.Visible = saved.Visible;
                self.Collidable = saved.Collidable;
                self.Depth = saved.Depth;

                self.CopySprite(saved, "sprite");
                self.CopyImage(saved, "stretch");
                self.CopyFields(saved,
                    "nodeIndex",
                    "travelling");

                self.Add(new Coroutine(SetHolding(self, saved)));

                RestoreTween(self, saved, nodes);
                RestoreAlarm(self, saved, nodes);
            } else {
                self.Add(new RemoveSelfComponent());
            }
        }

        private void RestoreAlarm(BadelineBoost self, BadelineBoost saved, Vector2[] nodes) {
            Alarm savedAlarm = saved.Get<Alarm>();
            if (savedAlarm == null) return;

            int nodeIndex = (int) saved.GetField("nodeIndex");

            Alarm alarm = Alarm.Create(Alarm.AlarmMode.Oneshot, () => {
                Player player = self.Scene.GetPlayer();
                if (player.Dashes < player.Inventory.Dashes) {
                    player.Dashes++;
                }

                BadelineDummy badeline = self.Scene.Entities.FindAll<BadelineDummy>()
                    .First(dummy => nodes[nodeIndex - 1] == dummy.GetStartPosition());
                if (badeline != null) {
                    self.Scene.Remove(badeline);
                    self.SceneAs<Level>().Displacement.AddBurst(badeline.Position, 0.25f, 8f, 32f, 0.5f);
                }
            }, 0.15f, start: true);
            alarm.SetProperty("TimeLeft", savedAlarm.TimeLeft);
            self.Add(alarm);
        }

        private static void RestoreTween(BadelineBoost self, BadelineBoost saved, Vector2[] nodes) {
            Tween savedTween = saved.Get<Tween>();
            if (savedTween == null) {
                return;
            }

            int nodeIndex = (int) saved.GetField("nodeIndex");
            Sprite sprite = self.GetSprite("sprite");
            Image stretch = self.GetImage("stretch");

            Vector2 from = nodes[nodeIndex - 1];
            Vector2 to = nodes[nodeIndex];
            float duration = Math.Min(3f, Vector2.Distance(from, to) / 320f);

            Tween tween = Tween.Create(Tween.TweenMode.Oneshot, Ease.SineInOut, duration, start: true);
            tween.OnUpdate = t => {
                self.Position = Vector2.Lerp(from, to, t.Eased);
                stretch.Scale.X = 1f + Calc.YoYo(t.Eased) * 2f;
                stretch.Scale.Y = 1f - Calc.YoYo(t.Eased) * 0.75f;
                if (t.Eased < 0.9f && self.Scene.OnInterval(0.03f)) {
                    TrailManager.Add(self, Player.TwoDashesHairColor, 0.5f, frozenUpdate: false,
                        useRawDeltaTime: false);
                    self.SceneAs<Level>().ParticlesFG
                        .Emit(BadelineBoost.P_Move, 1, self.Center, Vector2.One * 4f);
                }
            };
            tween.OnComplete = t => {
                if (self.X >= self.SceneAs<Level>().Bounds.Right) {
                    self.RemoveSelf();
                } else {
                    self.SetField("travelling", false);
                    stretch.Visible = false;
                    sprite.Visible = true;
                    self.Collidable = true;
                    Audio.Play("event:/char/badeline/booster_reappear", self.Position);
                }
            };
            tween.CopyFrom(savedTween);
            self.Add(tween);
        }

        private IEnumerator SetHolding(BadelineBoost self, BadelineBoost saved) {
            if (self.Scene.GetPlayer() is Player player && saved.GetField("holding") != null) {
                self.SetField("holding", player);
            }

            yield break;
        }

        public override void OnClear() {
            savedBadelineBoosts.Clear();
        }

        public override void OnLoad() {
            On.Celeste.BadelineBoost.ctor_EntityData_Vector2 += BadelineBoostOnCtor_EntityData_Vector2;
            On.Celeste.BadelineBoost.ctor_Vector2Array_bool_bool_bool_bool_bool +=
                BadelineBoostOnCtor_Vector2Array_bool_bool_bool_bool_bool;
        }

        public override void OnUnload() {
            On.Celeste.BadelineBoost.ctor_EntityData_Vector2 -= BadelineBoostOnCtor_EntityData_Vector2;
            On.Celeste.BadelineBoost.ctor_Vector2Array_bool_bool_bool_bool_bool -=
                BadelineBoostOnCtor_Vector2Array_bool_bool_bool_bool_bool;
        }
    }
}