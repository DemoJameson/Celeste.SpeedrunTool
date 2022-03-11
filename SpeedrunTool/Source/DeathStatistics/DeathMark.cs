using Celeste.Mod.SpeedrunTool.SaveLoad;

namespace Celeste.Mod.SpeedrunTool.DeathStatistics; 

public class DeathMark : Entity {
    private const string Id = "youdied";
    public DeathMark(Vector2 position) : base(position) {
        Sprite sprite = new(GFX.Game, $"objects/speedrun_tool_deathmark/{Id}");
        sprite.AddLoop(Id, "", 1f);
        sprite.Play(Id);
        sprite.CenterOrigin();
        sprite.RenderPosition -= Vector2.UnitY * 8;
        sprite.Color = Color.White * 0.5f;
        Add(sprite);
        Add(new ClearBeforeSaveComponent());
        Depth = Depths.FormationSequences;
    }
}