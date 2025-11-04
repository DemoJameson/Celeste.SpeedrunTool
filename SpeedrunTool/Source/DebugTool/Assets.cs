#if DEBUG

namespace Celeste.Mod.SpeedrunTool.DebugTool;
internal static class Assets {

    private const string Tag = $"SpeedrunTool/{nameof(Assets)}";

    public static int MTextureCount = 0;


    [Load]
    private static void Load() {
        if (!Log_Assets) {
            return;
        }
        On.Monocle.VirtualTexture.Load += VirtualTexture_Load;
        On.Monocle.VirtualTexture.Unload += VirtualTexture_Unload;
        SaveLoad.SaveLoadAction.InternalSafeAdd(
            saveState: (_, _) => {
                $"MTexture Count: {MTextureCount}".Log();
            },
            loadState: (_, _) => {
                $"MTexture Count: {MTextureCount}".Log();
            },
            clearState: () => {
                $"MTexture Count: {MTextureCount}".Log();
            }
        );
    }

    [Unload]
    private static void Unload() {
        if (!Log_Assets) {
            return;
        }
        On.Monocle.VirtualTexture.Load -= VirtualTexture_Load;
        On.Monocle.VirtualTexture.Unload -= VirtualTexture_Unload;
    }

    private static void VirtualTexture_Unload(On.Monocle.VirtualTexture.orig_Unload orig, VirtualTexture self) {
        $"Unload #{MTextureCount}".Log();
        MTextureCount--;
        orig(self);
    }

    private static bool VirtualTexture_Load(On.Monocle.VirtualTexture.orig_Load orig, VirtualTexture self, bool wait, Func<Microsoft.Xna.Framework.Graphics.Texture2D> load) {
        MTextureCount++;
        $"Load #{MTextureCount}".Log();
        return orig(self, wait, load);
    }

    private static void Log(this object obj) {
        Logger.Warn(Tag, obj.ToString());
    }
}
#endif