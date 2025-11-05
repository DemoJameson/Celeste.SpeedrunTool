using System.Collections.Generic;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Utils;
internal static class GraphicResourcesHandler {

    private static readonly List<VirtualAsset> VirtualAssets = new(); // added and cleared when loading, so no need to be individual

    private static readonly HashSet<Atlas> SavedAtlas = new();

    private static readonly HashSet<Atlas> ShouldBeDisposedAtlas = new();
    // todo: each save slot should have a different HoldingAtlas

    [Load]
    private static void Load() {
        On.Monocle.Atlas.Dispose += Atlas_Dispose;
    }

    [Unload]
    private static void Unload() {
        On.Monocle.Atlas.Dispose -= Atlas_Dispose;
    }


    private static void Atlas_Dispose(On.Monocle.Atlas.orig_Dispose orig, Atlas self) {
        if (SavedAtlas.Contains(self)) {
            ShouldBeDisposedAtlas.Add(self);
        }
        else {
            orig(self);
        }
    }

    public static void Add(Atlas atlas) {
        SavedAtlas.Add(atlas);
    }

    public static void Add(VirtualAsset asset) {
        VirtualAssets.Add(asset);
    }

    public static void AssetsClear() {
        VirtualAssets.Clear();
    }

    public static void DelayedDispose() {
        SavedAtlas.Clear();
        foreach (Atlas atlas in ShouldBeDisposedAtlas) {
            atlas.Dispose();
        }
        ShouldBeDisposedAtlas.Clear();
    }

    internal static void ReloadVirtualAssets() {
        SaveLoadAction.InternalSafeAdd(
            loadState: (_, _) => {
                List<VirtualAsset> list = new List<VirtualAsset>(VirtualAssets);
                // if load too frequently and switching between different slots, then collection might be modified? idk
                // so we avoid the crash in this way

                foreach (VirtualAsset virtualAsset in list) {
                    switch (virtualAsset) {
                        case VirtualTexture { IsDisposed: true } virtualTexture:
                            // Fix: 全屏切换然后读档煤球红边消失
                            if (!virtualTexture.Name.StartsWith("dust-noise-")) {
                                virtualTexture.Reload();
                            }

                            break;
                        case VirtualRenderTarget { IsDisposed: true } virtualRenderTarget:
                            virtualRenderTarget.Reload();
                            break;
                    }
                }

                AssetsClear();
            },
            clearState: () => {
                AssetsClear();
                DelayedDispose();
            },
            preCloneEntities: () => AssetsClear()
        );
    }
}
