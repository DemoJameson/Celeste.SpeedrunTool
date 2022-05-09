using System.Collections.Generic;
using System.Reflection;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;

namespace Celeste.Mod.SpeedrunTool.Utils;

internal static class HookHelper {
    private static readonly List<IDetour> Hooks = new();

    [Unload]
    private static void Unload() {
        foreach (IDetour detour in Hooks) {
            detour.Dispose();
        }

        Hooks.Clear();
    }

    // ReSharper disable once InconsistentNaming
    public static void ILHook(this MethodBase from, Action<ILCursor, ILContext> manipulator) {
        if (from == null) {
            new ArgumentNullException(nameof(from)).LogDetailed("SpeedrunTool");
            return;
        }

        Hooks.Add(new ILHook(from, il => {
            ILCursor ilCursor = new(il);
            manipulator(ilCursor, il);
        }));
    }
}