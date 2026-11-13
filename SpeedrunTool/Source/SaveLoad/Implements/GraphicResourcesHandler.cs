using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Celeste.Mod.Entities;
using Celeste.Mod.SpeedrunTool.Utils;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;
internal static class GraphicResourcesHandler {

    // Some vanilla/mod global entities cache a VirtualRenderTarget/VirtualTexture in a field and
    // only create it when the field is null. When the old area unloads, that asset is disposed but
    // the field still points at the disposed object; the entity's next BeforeRender can crash in
    // SpriteBatch.Draw (e.g. GlassBlockBg -> StarJumpBlock). Nulling disposed fields lets the
    // entity's own lazy initializer recreate them on the first render of the loaded area.
    private static readonly Dictionary<Type, FieldInfo[]> DisposableAssetFields = [];
    private static readonly HashSet<string> LoggedResetFields = [];

    internal static void ResetDisposedGlobalResources(Level level) {
        // Not only Global entities own disposable graphics caches. StarJumpController is a
        // regular level entity and its public BlockFill target is disposed when the old area
        // unloads; StarJumpBlock.Render reads it directly. Reset disposed fields on every entity;
        // only already-disposed assets are touched, so live resources stay intact.
        foreach (Entity entity in level.Entities) {
            ResetDisposedAssets(entity);
        }

        // Backdrops are not Entities. NorthernLights caches a VirtualRenderTarget in a private
        // field; Ended() disposes it, but a cloned backdrop can still hold the disposed object.
        ResetDisposedBackdrops(level.Background);
        ResetDisposedBackdrops(level.Foreground);

        ResetDisposedStaticAssets();
    }

    private static void ResetDisposedBackdrops(BackdropRenderer renderer) {
        if (renderer is null) {
            return;
        }
        foreach (Backdrop backdrop in renderer.Backdrops) {
            ResetDisposedAssets(backdrop);
        }
    }

    private static void ResetDisposedStaticAssets() {
        // Targeted known static cache. Do not enumerate every mod type here: that belongs in a
        // separate audit build, not in the normal load path.
        if (StarClimbGraphicsController.BlockFill is { IsDisposed: true }) {
            StarClimbGraphicsController.BlockFill = null;
            LogReset(typeof(StarClimbGraphicsController), "BlockFill (static)");
        }
    }

    private static void ResetDisposedAssets(object target) {
        Type type = target.GetType();
        if (!DisposableAssetFields.TryGetValue(type, out FieldInfo[] fields)) {
            List<FieldInfo> found = [];
            for (Type current = type; current is not null && current != typeof(Entity); current = current.BaseType) {
                foreach (FieldInfo field in current.GetFields(BindingFlags.Instance | BindingFlags.Static |
                                                              BindingFlags.Public | BindingFlags.NonPublic |
                                                              BindingFlags.DeclaredOnly)) {
                    if (field.IsInitOnly) {
                        continue;
                    }
                    if (typeof(VirtualAsset).IsAssignableFrom(field.FieldType) || field.FieldType.IsArray
                        && typeof(VirtualAsset).IsAssignableFrom(field.FieldType.GetElementType())) {
                        found.Add(field);
                    }
                }
            }
            fields = [.. found];
            DisposableAssetFields[type] = fields;
        }

        foreach (FieldInfo field in fields) {
            try {
                object fieldTarget = field.IsStatic ? null : target;
                object value = field.GetValue(fieldTarget);
                if (value is VirtualAsset asset) {
                    if (IsDisposedAsset(asset)) {
                        field.SetValue(fieldTarget, null);
                        LogReset(type, field.Name);
                    }
                }
                else if (value is Array array && array.GetType().GetElementType() is { } elementType
                         && typeof(VirtualAsset).IsAssignableFrom(elementType)) {
                    for (int i = 0; i < array.Length; i++) {
                        if (array.GetValue(i) is VirtualAsset element && IsDisposedAsset(element)) {
                            array.SetValue(null, i);
                            LogReset(type, $"{field.Name}[{i}]");
                        }
                    }
                }
            }
            catch (Exception exception) {
                Logger.Warn("SpeedrunTool/GraphicResourcesHandler",
                    $"Could not reset disposed asset {type.FullName}.{field.Name}: {exception.GetType().Name}: {exception.Message}");
            }
        }
    }

    private static bool IsDisposedAsset(VirtualAsset asset) {
        return asset is VirtualTexture { IsDisposed: true } or VirtualRenderTarget { IsDisposed: true };
    }

    private static void LogReset(Type type, string field) {
        string key = type.FullName + "." + field;
        if (LoggedResetFields.Add(key)) {
            Logger.Info("SpeedrunTool/GraphicResourcesHandler",
                $"Reset disposed global resource [{type.FullName}].{field}; the owner should recreate it lazily.");
        }
    }

    public static void Add(VirtualAsset asset) {
        VirtualAssetsHandler.Add(asset);
    }

    internal static void AddSaveLoadAction() {
        VirtualAssetsHandler.ReloadVirtualAssets();

        // Cloning WaveDashPresentation is not reliable: its async LoadingThread, page state and
        // render targets are not all cloneable. Treat it as transient, but close it cleanly: the
        // owning cutscene waits on presentation.Viewing, so deleting the entity alone leaves the
        // cutscene stuck until the player skips it.
        SaveLoadAction.InternalSafeAdd(
            beforeSaveState: RemoveWaveDashPresentations,
            loadState: static (_, level) => RemoveWaveDashPresentations(level));
    }

    private static readonly PropertyInfo WaveDashViewingProperty =
        typeof(WaveDashPresentation).GetProperty("Viewing");

    private static void RemoveWaveDashPresentations(Level level) {
        for (int i = level.Entities.Count - 1; i >= 0; i--) {
            if (level.Entities[i] is not WaveDashPresentation presentation) {
                continue;
            }
            try {
                WaveDashViewingProperty?.SetValue(presentation, false);
            }
            catch (Exception exception) {
                Logger.Warn("SpeedrunTool/GraphicResourcesHandler",
                    $"Could not close WaveDashPresentation: {exception.GetType().Name}: {exception.Message}");
            }
            level.Remove(presentation);
        }
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


}
