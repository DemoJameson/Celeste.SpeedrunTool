using System.Collections;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.Other {
    [Tracked]
    public class Tooltip : Entity {
        private const int Padding = 25;
        private readonly Vector2 scale = Vector2.One * 0.9f;
        private readonly string message;
        private float alpha;
        private float unEasedAlpha;

        private Tooltip(string message) {
            this.message = message;
            Vector2 messageSize = ActiveFont.Measure(message);
            Position = new(Padding, Engine.Height - messageSize.Y - Padding / 2f);
            Tag = TagsExt.SubHUD | Tags.Global | Tags.FrozenUpdate | Tags.TransitionUpdate;
            Add(new Coroutine(Show()));
        }

        public override void Awake(Scene scene) {
            scene.Tracker.GetEntities<Tooltip>().ForEach(entity => {
                if (entity != this) {
                    entity.RemoveSelf();
                }
            });
        }

        private IEnumerator Show() {
            while (alpha < 1f) {
                unEasedAlpha = Calc.Approach(unEasedAlpha, 1f, Engine.RawDeltaTime * 4f);
                alpha = Ease.SineOut(unEasedAlpha);
                yield return null;
            }

            yield return Dismiss();
        }

        private IEnumerator Dismiss() {
            yield return 1f;
            while (alpha > 0f) {
                unEasedAlpha = Calc.Approach(unEasedAlpha, 0f, Engine.RawDeltaTime * 4f);
                alpha = Ease.SineIn(unEasedAlpha);
                yield return null;
            }

            RemoveSelf();
        }

        public override void Render() {
            base.Render();
            ActiveFont.DrawOutline(message, Position, Vector2.Zero, scale, Color.White * alpha, 2,
                Color.Black * alpha);
        }

        public static void Show(Level level, string message) {
            level.Add(new Tooltip(message));
        }
    }
}