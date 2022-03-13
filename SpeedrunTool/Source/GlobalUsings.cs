global using System;
global using Monocle;
global using Microsoft.Xna.Framework;
global using Celeste.Mod.SpeedrunTool.Extensions;
global using static Celeste.Mod.SpeedrunTool.GlobalVariables;

namespace Celeste.Mod.SpeedrunTool;

internal static class GlobalVariables {
    public static SpeedrunToolSettings ModSettings => SpeedrunToolSettings.Instance;
}