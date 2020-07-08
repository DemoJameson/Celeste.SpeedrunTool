using System;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.EntityActions {
    public class KeyRestoreAction : AbstractRestoreAction {
        public KeyRestoreAction() : base(typeof(Key)) { }
        public override void OnLoad() {
            On.Celeste.Key.ctor_Player_EntityID += KeyOnCtor_Player_EntityID;
        }

        public override void OnUnload() {
            On.Celeste.Key.ctor_Player_EntityID -= KeyOnCtor_Player_EntityID;
        }

        private static void KeyOnCtor_Player_EntityID(On.Celeste.Key.orig_ctor_Player_EntityID orig, Key self, Player player, EntityID id) {
            self.SetEntityId2(id);
            orig(self, player, id);
        }

        public override void AfterEntityCreateAndUpdate1Frame(Entity loadedEntity, Entity savedEntity) {
            Key loaded = (Key) loadedEntity;
            Key saved = (Key) savedEntity;
            
            // TODO Restore Tween and Alarm
            // RestoreTween(loaded, saved);
        }

        private void RestoreTween(Key loaded, Key saved) {
            Tween loadedTween = loaded.GetTween("tween");
            Tween savedTween = saved.GetTween("tween");
            if (savedTween == null) return;
            loadedTween?.RemoveSelf();

            // TODO 还原 Key Tween
            if (Math.Abs(savedTween.Duration - 1f) < 0.01f) {
                // Vector2 position = Position;
                // SimpleCurve curve = new SimpleCurve(position, target, (target + position) / 2f + new Vector2(0f, -48f));
                // tween = Tween.Create(Tween.TweenMode.Oneshot, Ease.CubeOut, 1f, start: true);
                // tween.OnUpdate = delegate(Tween t)
                // {
                //     key.Position = curve.GetPoint(t.Eased);
                //     key.sprite.Rate = 1f + t.Eased * 2f;
                // };
            } else if (savedTween.Duration - 0.3f < 0.01f) {
                Tween tween = Tween.Create(savedTween.Mode, Ease.CubeIn, savedTween.Duration, start: true);
                tween.OnUpdate = delegate(Tween t)
                {
                    loaded.GetSprite("sprite").Rotation = t.Eased * ((float)Math.PI / 2f);
                };
                tween.CopyFrom(savedTween);
                loaded.Add(tween);
                loaded.SetField("tween", tween);
            }
        }
    }
}