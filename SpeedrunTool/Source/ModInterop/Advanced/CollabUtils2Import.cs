using MonoMod.ModInterop;

namespace Celeste.Mod.SpeedrunTool.ModInterop;

internal static class CollabUtils2Import {

    public static bool IsCollabLobby(string sid) => ModImports.IsCollabLobby?.Invoke(sid) ?? false;

    [Initialize]
    private static void Initialize() {
        typeof(ModImports).ModInterop();
    }

    [ModImportName("CollabUtils2.LobbyHelper")]
    private static class ModImports {
        public static Func<string, bool> IsCollabLevelSet;

        public static Func<string, bool> IsCollabMap;

        public static Func<string, bool> IsCollabLobby;

        public static Func<string, bool> IsCollabGym;
    }
}