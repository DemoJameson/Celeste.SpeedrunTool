using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.Base;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions {
    public class BurstRestoreAction : RestoreAction {
        public override void OnHook() {
            On.Celeste.DisplacementRenderer.AddBurst += DisplacementRendererOnAddBurst;
        }

        public override void OnUnhook() {
            On.Celeste.DisplacementRenderer.AddBurst -= DisplacementRendererOnAddBurst;
        }

        private DisplacementRenderer.Burst DisplacementRendererOnAddBurst(
            On.Celeste.DisplacementRenderer.orig_AddBurst orig, DisplacementRenderer self, Vector2 position,
            float duration, float radiusFrom, float radiusTo, float alpha, Ease.Easer alphaEaser,
            Ease.Easer radiusEaser) {
            DisplacementRenderer.Burst burst = orig(self, position, duration, radiusFrom, radiusTo, alpha, alphaEaser,
                radiusEaser);
            burst.SetStartPosition(position);
            burst.SetRadiusFrom(radiusFrom);
            burst.SetRadiusTo(radiusTo);
            burst.SetAlpha(alpha);
            return burst;
        }
    }

    public static class BurstExtensions {
        private const string BurstRadiusFromKey = "BurstRadiusFrom-Key";
        private const string BurstRadiusToKey = "BurstRadiusTo-Key";
        private const string BurstAlphaKey = "BurstAlpha-Key";
        private const string BurstStartPositionKey = "BurstStartPosition-Key";

        public static DisplacementRenderer.Burst Clone(this DisplacementRenderer.Burst burst) {
            return Engine.Scene.GetLevel()?.Displacement.AddBurst(burst.GetStartPosition(), burst.Duration,
                burst.GetExtendedFloat(BurstRadiusFromKey), burst.GetExtendedFloat(BurstRadiusToKey),
                burst.GetExtendedFloat(BurstAlphaKey),
                burst.AlphaEaser);
        }

        public static void SetRadiusFrom(this DisplacementRenderer.Burst burst, float radiusFrom) {
            burst.SetExtendedFloat(BurstRadiusFromKey, radiusFrom);
        }

        public static void SetRadiusTo(this DisplacementRenderer.Burst burst, float radiusTo) {
            burst.SetExtendedFloat(BurstRadiusToKey, radiusTo);
        }

        public static void SetAlpha(this DisplacementRenderer.Burst burst, float alpha) {
            burst.SetExtendedFloat(BurstAlphaKey, alpha);
        }

        public static Vector2 GetStartPosition(this DisplacementRenderer.Burst burst) {
            return burst.GetExtendedDataValue<Vector2>(BurstStartPositionKey);
        }

        public static void SetStartPosition(this DisplacementRenderer.Burst burst, Vector2 startPosition) {
            burst.SetExtendedDataValue(BurstStartPositionKey, startPosition);
        }
    }
}