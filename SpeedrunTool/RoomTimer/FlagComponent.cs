using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.RoomTimer {
    public class FlagComponent : Component {
        private readonly int number;
        private readonly bool flagStyle;
        public bool Activated;
        private readonly MTexture baseActive;
        private readonly MTexture baseEmpty;
        private readonly MTexture baseToggle;
        private readonly List<MTexture> numbersActive;
        private readonly List<MTexture> numbersEmpty;
        private readonly string numberString;

        public FlagComponent(int number, bool flagStyle) : base(false, true) {
            this.number = number;
            this.flagStyle = flagStyle;

            numberString = number.ToString("D2");
            baseEmpty = GFX.Game["scenery/summitcheckpoints/base00"];
            baseToggle = GFX.Game["scenery/summitcheckpoints/base01"];
            baseActive = GFX.Game["scenery/summitcheckpoints/base02"];
            numbersEmpty = GFX.Game.GetAtlasSubtextures("scenery/summitcheckpoints/numberbg");
            numbersActive = GFX.Game.GetAtlasSubtextures("scenery/summitcheckpoints/number");
        }

        public override void Render() {
            if (!flagStyle) {
                return;
            }
            
            List<MTexture> mTextureList = Activated ? numbersActive : numbersEmpty;
            MTexture mTexture = baseActive;
            if (!Activated) {
                mTexture = Scene.BetweenInterval(0.25f) ? baseEmpty : baseToggle;
            }

            mTexture.Draw(Entity.TopCenter - new Vector2(mTexture.Width / 2f + 1, mTexture.Height / 2f));
            mTextureList[numberString[0] - 48].DrawJustified(Entity.TopCenter + new Vector2(-1f, 1f), new Vector2(1f, 0.0f));
            mTextureList[numberString[1] - 48].DrawJustified(Entity.TopCenter + new Vector2(0.0f, 1f), new Vector2(0.0f, 0.0f));
        }

        public override void Update() {
            if (!(Scene is Level level) || Activated || !Entity.CollideCheck<Player>()) {
                return;
            }

            Activated = true;
            level.Displacement.AddBurst(Entity.TopCenter, 0.5f, 4f, 24f, 0.5f);
            level.Add(new ConfettiRenderer(Entity.TopCenter));
            Audio.Play("event:/game/07_summit/checkpoint_confetti", Entity.TopCenter);
        }
    }
}