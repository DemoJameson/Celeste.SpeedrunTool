using System.Collections;
using System.Linq;
using Celeste.Mod.SpeedrunTool.SaveLoad;

namespace Celeste.Mod.SpeedrunTool.Message; 

[Tracked]
public class Tooltip : Entity {
    private const int Padding = 25;
    private readonly string message;
    private float alpha;
    private float unEasedAlpha;
    private readonly float duration;

    private Tooltip(string message, float duration = 1f) {
        this.message = message;
        this.duration = duration;
        Vector2 messageSize = ActiveFont.Measure(message);
        Position = new(Padding, Engine.Height - messageSize.Y - Padding / 2f);
        Tag = Tags.HUD | Tags.Global | Tags.FrozenUpdate | Tags.PauseUpdate| Tags.TransitionUpdate;
        Add(new Coroutine(Show()));
        Add(new IgnoreSaveLoadComponent());
    }

    private IEnumerator Show() {
        while (alpha < 1f) {
            unEasedAlpha = Calc.Approach(unEasedAlpha, 1f, Engine.RawDeltaTime * 5f);
            alpha = Ease.SineOut(unEasedAlpha);
            yield return null;
        }

        yield return Dismiss();
    }

    private IEnumerator Dismiss() {
        yield return duration;
        while (alpha > 0f) {
            unEasedAlpha = Calc.Approach(unEasedAlpha, 0f, Engine.RawDeltaTime * 5f);
            alpha = Ease.SineIn(unEasedAlpha);
            yield return null;
        }

        RemoveSelf();
    }

    public override void Render() {
        base.Render();
        ActiveFont.DrawOutline(message, Position, Vector2.Zero, Vector2.One, Color.White * alpha, 2,
            Color.Black * alpha * alpha * alpha);
    }

    public static void Show(string message, float duration = 1f) {
        if (Engine.Scene is {} scene) {
            if (!scene.Tracker.Entities.TryGetValue(typeof(Tooltip), out var tooltips)) {
                tooltips = scene.Entities.FindAll<Tooltip>().Cast<Entity>().ToList();
            }
            tooltips.ForEach(entity => entity.RemoveSelf());
            scene.Add(new Tooltip(message, duration));
        }
    }
}