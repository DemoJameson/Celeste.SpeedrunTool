using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using System.Collections.Generic;
using System.Reflection;

namespace Celeste.Mod.SpeedrunTool.Utils;

internal static class HookHelper {
    private static readonly List<ILHook> Hooks = new();

    [Unload]
    private static void Unload() {
        foreach (ILHook detour in Hooks) {
            detour.Dispose();
        }

        Hooks.Clear();
    }

    // ReSharper disable once InconsistentNaming
    public static void ILHook(this MethodBase from, Action<ILCursor, ILContext> manipulator) {
        if (from == null) {
            Logger.LogDetailed(new ArgumentNullException(nameof(from)), "SpeedrunTool");
            return;
        }

        Hooks.Add(new ILHook(from, il => {
            ILCursor ilCursor = new(il);
            manipulator(ilCursor, il);
        }));
    }
}

internal static class DetourContextHelper {
    public static IDisposable Use(string ID = "SpeedrunTool", int? priority = null, IEnumerable<string>? Before = null, IEnumerable<string>? After = null) {
        return new DetourConfigContext(new DetourConfig(ID, priority, Before, After)).Use();
    }
}