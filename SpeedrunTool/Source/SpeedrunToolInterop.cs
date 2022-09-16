using Celeste.Mod.SpeedrunTool.SaveLoad;
using MonoMod.ModInterop;

namespace Celeste.Mod.SpeedrunTool;

public static class SpeedrunToolInterop {
    [Load]
    private static void Initialize() {
        typeof(SaveLoadExports).ModInterop();
    }

    // ReSharper disable once MemberCanBePrivate.Global
    [ModExportName("SpeedrunTool.SaveLoad")]
    public static class SaveLoadExports {
        public static void RegisterStaticTypes(Type t, params string[] memberNames) {
            SaveLoadAction.SafeAdd(
                (savedValues, _) => SaveLoadAction.SaveStaticMemberValues(savedValues, t, memberNames),
                (savedValues, _) => SaveLoadAction.LoadStaticMemberValues(savedValues));
        }
    }
}