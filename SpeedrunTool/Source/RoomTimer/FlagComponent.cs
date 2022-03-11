using System.Collections.Generic;

namespace Celeste.Mod.SpeedrunTool.RoomTimer; 

public class FlagComponent : Component {
    private readonly bool flagStyle;
    private readonly MTexture baseActive;
    private readonly MTexture baseEmpty;
    private readonly MTexture baseToggle;
    private readonly List<MTexture> numbersActive;
    private readonly List<MTexture> numbersEmpty;
    private Vector2 offset;
    private Vector2 numberOffset;

    public FlagComponent(bool flagStyle) : base(false, true) {
        this.flagStyle = flagStyle;
            
        baseEmpty = GFX.Game["scenery/speedrun_tool_summitcheckpoints/base00"];
        baseToggle = GFX.Game["scenery/speedrun_tool_summitcheckpoints/base01"];
        baseActive = GFX.Game["scenery/speedrun_tool_summitcheckpoints/base02"];
        numbersEmpty = GFX.Game.GetAtlasSubtextures("scenery/speedrun_tool_summitcheckpoints/numberbg");
        numbersActive = GFX.Game.GetAtlasSubtextures("scenery/speedrun_tool_summitcheckpoints/number");
    }

    public override void Added(Entity entity) {
        base.Added(entity);
        offset = entity.TopCenter + Vector2.UnitY;
        numberOffset = offset + Vector2.UnitY;
    }

    public override void Render() {
        if (!flagStyle) {
            return;
        }

        bool activated = EntityAs<EndPoint>().Activated;
        List<MTexture> mTextureList = activated ? numbersActive : numbersEmpty;
        MTexture mTexture = baseActive;
        if (!activated) {
            mTexture = Scene.BetweenInterval(0.25f) ? baseEmpty : baseToggle;
        }

        // ReSharper disable once PossibleLossOfFraction
        mTexture.Draw(offset - new Vector2(mTexture.Width / 2 + 1, mTexture.Height / 2));

        mTextureList[0]
            .DrawJustified(numberOffset + new Vector2(-1f, 1f), new Vector2(1f, 0.0f));
        mTextureList[1]
            .DrawJustified(numberOffset + new Vector2(0f, 1f), new Vector2(0f, 0f));
    }
}