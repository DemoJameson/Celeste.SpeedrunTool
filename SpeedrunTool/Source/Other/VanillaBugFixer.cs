namespace Celeste.Mod.SpeedrunTool.Other;

public static class VanillaBugFixer {
    [Load]
    private static void Load() {
        On.Celeste.SlashFx.Burst += SlashFxOnBurst;
    }

    [Unload]
    private static void Unload() {
        On.Celeste.SlashFx.Burst -= SlashFxOnBurst;
    }

    // 修复：面向左边不按方向键冲刺，白色线条绘制异常
    private static SlashFx SlashFxOnBurst(On.Celeste.SlashFx.orig_Burst orig, Vector2 position, float direction) {
        if (ModSettings.Enabled && Math.Abs(direction + 3.141593) < 0.000001f) {
            direction = -direction;
        }

        return orig(position, direction);
    }
}