using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.RoomTimer {
    public class FlagComponent : Component {
//        private static readonly PropertyInfo WidthPropertyInfo = typeof(MTexture).GetProperty("Width");
        private readonly bool flagStyle;
        private readonly MTexture baseActive;
        private readonly MTexture baseEmpty;
        private readonly MTexture baseToggle;
        private readonly List<MTexture> numbersActive;
        private readonly List<MTexture> numbersEmpty;
        private readonly string numberString;
        private Vector2 offset;

        public FlagComponent(string numberString, bool flagStyle) : base(false, true) {
            this.flagStyle = flagStyle;
            this.numberString = numberString;
            
            baseEmpty = GFX.Game["scenery/speedrun_tool_summitcheckpoints/base00"];
            baseToggle = GFX.Game["scenery/speedrun_tool_summitcheckpoints/base01"];
            baseActive = GFX.Game["scenery/speedrun_tool_summitcheckpoints/base02"];
            numbersEmpty = GFX.Game.GetAtlasSubtextures("scenery/speedrun_tool_summitcheckpoints/numberbg");
            numbersActive = GFX.Game.GetAtlasSubtextures("scenery/speedrun_tool_summitcheckpoints/number");
        }

        public override void Added(Entity entity) {
            base.Added(entity);
            offset = entity.TopCenter + Vector2.UnitY;
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

            int width = mTexture.Width;
//            WidthPropertyInfo.SetValue(mTexture, width + 1);
            mTexture.Draw(offset - new Vector2(mTexture.Width / 2f - 1, mTexture.Height / 2f));
//            WidthPropertyInfo.SetValue(mTexture, width);


            mTextureList[numberString[0] - '0']
                .DrawJustified(offset + Vector2.UnitX + new Vector2(-1f, 1f), new Vector2(1f, 0.0f));

            int charCode = numberString[1];
            if (charCode >= 'A') {
                charCode -= 7;
            }

            mTextureList[charCode - '0']
                .DrawJustified(offset + Vector2.UnitX + new Vector2(0.0f, 1f), new Vector2(0.0f, 0.0f));
        }
    }
}