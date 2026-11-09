using System.Collections.Generic;
using System.IO;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;
internal static class GraphicResourcesHandler {

    public static void Add(VirtualAsset asset) {
        VirtualAssetsHandler.Add(asset);
    }

    internal static void AddSaveLoadAction() {
        VirtualAssetsHandler.ReloadVirtualAssets();
        AtlasHandler.AddAction();
    }

    private static class VirtualAssetsHandler {

        private static readonly HashSet<VirtualAsset> VirtualAssets = []; // added and cleared when loading, so no need to be individual

        internal static void Add(VirtualAsset asset) {
            VirtualAssets.Add(asset);
        }
        internal static void ReloadVirtualAssets() {
            SaveLoadAction.InternalSafeAdd(
                loadState: static (_, level) => {
                    List<VirtualAsset> list = [.. VirtualAssets];
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

                    VirtualAssets.Clear();

                    if (level.Tracker.GetEntity<TrailManager>() is { } trailManager) {
                        for (int i = 0; i < trailManager.buffers.Length; i++) {
                            if (trailManager.buffers[i] != null && trailManager.buffers[i].IsDisposed) {
                                trailManager.buffers[i].Reload();
                            }
                        }
                    }
                },
                clearState: VirtualAssets.Clear,
                preCloneEntities: VirtualAssets.Clear
            );
        }
    }

    private static class AtlasHandler {

        // This Fixes: WaveDashPresentation 中存档, 结束, 再读档, 会崩溃
        // 这里本来想写个通用的版本的, 但最后 bug 太多, 所以就先这样了

        [Initialize]
        private static void Initialize() {
            Tracker.AddTypeToTracker(typeof(WaveDashPresentation));
        }

        internal static void AddAction() {
            SaveLoadAction.InternalSafeAdd(
                loadState: static (_, level) => {
                    if (level.Tracker.GetEntitiesTrackIfNeeded<WaveDashPresentation>() is { } list && list.IsNotNullOrEmpty()) {
#pragma warning disable IDE0220 // 添加显式转换
                        foreach (WaveDashPresentation presentation in list) {
                            presentation.Gfx?.Dispose();
                            presentation.Gfx = Atlas.FromAtlas(Path.Combine("Graphics", "Atlases", "WaveDashing"), Atlas.AtlasDataFormat.Packer);
                            presentation.loading = false;
                        }
#pragma warning restore IDE0220 // 添加显式转换
                    }
                }
            );
        }
    }
}
