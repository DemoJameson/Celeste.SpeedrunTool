using Celeste.Mod.SpeedrunTool.SaveLoad;
using MonoMod.ModInterop;
using System.Collections.Generic;

namespace Celeste.Mod.SpeedrunTool;

public static class SpeedrunToolInterop {
    [Load]
    private static void Initialize() {
        typeof(SaveLoadExports).ModInterop();
    }

    [ModExportName("SpeedrunTool.SaveLoad")]
    public static class SaveLoadExports {
        public static void RegisterStaticTypes(Type t, params string[] memberNames) {
            SaveLoadAction.SafeAdd(
                (savedValues, _) => SaveLoadAction.SaveStaticMemberValues(savedValues, t, memberNames),
                (savedValues, _) => SaveLoadAction.LoadStaticMemberValues(savedValues));
        }
    }
}