using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using Celeste.Mod.Entities;
using Celeste.Mod.SpeedrunTool.Utils;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;
internal static class GraphicResourcesHandler {

    // Some vanilla/mod entities cache a VirtualRenderTarget/VirtualTexture in a field. When the
    // old area unloads, that asset is disposed but the field still points at it; the entity's next
    // BeforeRender can crash in SpriteBatch.Draw. Prefer Reload() over nulling: some owners
    // (StarJumpController, JungleHelper UIText/UIImage, Poem) create their target only in the
    // constructor and never recover from a null field. Owners with a companion "disposed" bool
    // also need that flag cleared after the target is valid again.
    private static readonly Dictionary<Type, FieldInfo[]> DisposableAssetFields = [];
    private static readonly Dictionary<Type, FieldInfo[]> DisposedFlagFields = [];
    private static readonly HashSet<string> LoggedResetFields = [];
    private static readonly MethodInfo VirtualAssetReload =
        typeof(VirtualAsset).GetMethod("Reload", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    internal static void ResetDisposedGlobalResources(Level level) {
#if DEBUG
        Stopwatch stopwatch = Stopwatch.StartNew();
#endif
        // MirrorSurfaces owns a VirtualRenderTarget that is disposed when its old area is
        // unloaded. A cross-chapter state still carries the entity; force it to drop the stale
        // target so its next BeforeRender recreates one for the loaded area.
        foreach (MirrorSurfaces mirror in level.Entities.FindAll<MirrorSurfaces>()) {
            mirror.Dispose();
        }

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

        // Components can own their own VirtualRenderTarget too (census found several mod
        // components). Scan the components of the restored entities; the field filter below only
        // touches VirtualAsset (VirtualTexture/VirtualRenderTarget), never shared Atlas objects.
        ResetDisposedComponents(level);

        ResetDisposedStaticAssets();
        ResetKnownStaticAssets();
#if DEBUG
        Logger.Info("SpeedrunTool/GraphicResourcesHandler",
            $"ResetDisposedGlobalResources: {stopwatch.Elapsed.TotalMilliseconds:F2} ms, " +
            $"entities={level.Entities.Count}");
#endif
    }

    private static void ResetDisposedComponents(Level level) {
        foreach (Entity entity in level.Entities) {
            foreach (Component component in entity.Components) {
                ResetDisposedAssets(component);
            }
        }
    }

    // Static fields cannot be found by walking entity instances: some are shared render targets
    // used by another entity, and the owner type may not be instantiated in the restored level.
    // Keep this list explicit instead of scanning GetAllTypes at runtime.
    private static readonly (string Mod, string Type, string[] Fields)[] KnownStaticAssets = [
        ("MaxHelpingHand", "Celeste.Mod.MaxHelpingHand.Entities.StylegroundFadeController", ["tempRenderTarget"]),
        ("Microlith57Misc", "Celeste.Mod.Microlith57Misc.Entities.BlackHoleRefractionController", ["RefractionFill"]),
        ("StrawberryJam2021", "Celeste.Mod.StrawberryJam2021.Entities.DarkMatterRenderer", ["DarkMatterLightning", "BlurTempBuffer"]),
        ("FrostHelper", "FrostHelper.EntityBatcher", ["temp", "temp2"]),
        ("CommunalHelper", "Celeste.Mod.CommunalHelper.Backdrops.Cloudscape", ["bufferFullscreen", "buffer320x180"]),
        ("PrismaticHelper", "PrismaticHelper.Entities.Panels.WorldPanelManager", ["Displacement2"]),
        ("StyleMaskHelper", "Celeste.Mod.StyleMaskHelper.Entities.BloomMask", ["BloomBuffer", "FadeBuffer"]),
        ("StyleMaskHelper", "Celeste.Mod.StyleMaskHelper.Entities.ColorGradeMask", ["FadeBuffers"]),
    ];

    private static void ResetKnownStaticAssets() {
        foreach ((string mod, string typeName, string[] fieldNames) in KnownStaticAssets) {
            Type type = ModUtils.GetType(mod, typeName);
            if (type is null) {
                continue;
            }
            foreach (string fieldName in fieldNames) {
                FieldInfo field = type.GetField(fieldName, BindingFlags.Static | BindingFlags.Public |
                                                             BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field is null || field.IsInitOnly) {
                    continue;
                }
                ResetField(null, type, field);
            }
        }
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
                    if (IsVirtualAssetContainer(field.FieldType)) {
                        found.Add(field);
                    }
                }
            }
            fields = [.. found];
            DisposableAssetFields[type] = fields;
        }

        bool reset = false;
        foreach (FieldInfo field in fields) {
            reset |= ResetField(target, type, field);
        }

        if (reset) {
            foreach (FieldInfo flag in GetDisposedFlags(type)) {
                try {
                    flag.SetValue(target, false);
                }
                catch (Exception exception) {
                    Logger.Warn("SpeedrunTool/GraphicResourcesHandler",
                        $"Could not clear {type.FullName}.{flag.Name}: {exception.GetType().Name}: {exception.Message}");
                }
            }
        }
    }

    private static FieldInfo[] GetDisposedFlags(Type type) {
        if (DisposedFlagFields.TryGetValue(type, out FieldInfo[] flags)) {
            return flags;
        }
        List<FieldInfo> found = [];
        for (Type current = type; current is not null && current != typeof(object); current = current.BaseType) {
            foreach (FieldInfo field in current.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                          BindingFlags.NonPublic | BindingFlags.DeclaredOnly)) {
                if (!field.IsInitOnly && field.FieldType == typeof(bool)
                    && field.Name.Equals("disposed", StringComparison.OrdinalIgnoreCase)) {
                    found.Add(field);
                }
            }
        }
        flags = [.. found];
        DisposedFlagFields[type] = flags;
        return flags;
    }

    private static bool ResetField(object target, Type type, FieldInfo field) {
        try {
            object fieldTarget = field.IsStatic ? null : target;
            object value = field.GetValue(fieldTarget);
            if (value is VirtualAsset asset) {
                return ReloadDisposedAsset(asset, type, field.Name);
            }
            bool reset = false;
            if (value is Array array && IsVirtualAssetContainer(array.GetType().GetElementType())) {
                for (int i = 0; i < array.Length; i++) {
                    if (array.GetValue(i) is VirtualAsset element) {
                        reset |= ReloadDisposedAsset(element, type, $"{field.Name}[{i}]");
                    }
                }
            }
            else if (value is IList list && !list.IsReadOnly && !list.IsFixedSize
                     && IsVirtualAssetContainer(list.GetType())) {
                for (int i = 0; i < list.Count; i++) {
                    if (list[i] is VirtualAsset element) {
                        reset |= ReloadDisposedAsset(element, type, $"{field.Name}[{i}]");
                    }
                }
            }
            return reset;
        }
        catch (Exception exception) {
            Logger.Warn("SpeedrunTool/GraphicResourcesHandler",
                $"Could not reset disposed asset {type.FullName}.{field.Name}: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private static bool IsVirtualAssetContainer(Type type) {
        if (type is null) {
            return false;
        }
        if (type.IsArray) {
            return IsVirtualAssetContainer(type.GetElementType());
        }
        if (typeof(VirtualAsset).IsAssignableFrom(type)) {
            return true;
        }
        if (typeof(IList).IsAssignableFrom(type) && type.IsGenericType) {
            foreach (Type argument in type.GetGenericArguments()) {
                if (typeof(VirtualAsset).IsAssignableFrom(argument)) {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool ReloadDisposedAsset(VirtualAsset asset, Type type, string label) {
        if (!IsDisposedAsset(asset)) {
            return false;
        }
        try {
            VirtualAssetReload?.Invoke(asset, null);
            LogReset(type, label);
            return true;
        }
        catch (Exception exception) {
            Logger.Warn("SpeedrunTool/GraphicResourcesHandler",
                $"Could not reload disposed asset {type.FullName}.{label}: {exception.GetType().Name}: {exception.Message}");
            return false;
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
