using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.DeathStatistics {
    public class DeathMarker : Entity {
        private const string Id = "youdied";
        public DeathMarker(Vector2 position) : base(position) {
            Sprite sprite = new Sprite(GFX.Game, $"objects/speedrun_tool_deathmarker/{Id}");
            sprite.AddLoop(Id, "", 1f);
            sprite.Play(Id);
            sprite.CenterOrigin();
            sprite.RenderPosition -= Vector2.UnitY * 8;
            sprite.Color = Color.White * 0.5f;
            Add(sprite);
            Depth = -999999999;
        }
    }
}