using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Celeste.Mod.SpeedrunTool.Utils;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;
internal static class GraphicResourcesHandler {

    // Some vanilla/mod owners cache a VirtualRenderTarget/VirtualTexture in a field. When the old area
    // unloads (or when the owner itself is removed), that asset is disposed and the field keeps
    // pointing at it; the owner's next BeforeRender either crashes in SpriteBatch.Draw or is skipped
    // behind a "disposed" flag. Which owners are handled, how they are found and which repair each one
    // needs is one explicit table: see the comment on KnownDisposedAssets.
    private static readonly HashSet<string> LoggedRepairs = [];

    private static readonly MethodInfo VirtualAssetReload =
        typeof(VirtualAsset).GetMethod("Reload", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);


    /// <summary>
    /// Repairs GPU resources that the saved graph points at but that were disposed after the state was
    /// saved. Two causes, and a single load can hit both:
    /// <list type="bullet">
    /// <item>the old area unloads (cross-chapter only): the area's VirtualContent, the backdrops'
    /// <c>Ended()</c> and the static caches dispose their targets, while the template keeps pointing at
    /// those same instances (VirtualAssets are never cloned);</item>
    /// <item>the owner disposes its target while being removed (<b>every</b> load, same chapter
    /// included): SRT's own UnloadLevel calls <c>Entity.Removed</c> on each non-global entity, and
    /// owners such as Poem and JungleHelper's UIText dispose their render target right there.</item>
    /// </list>
    /// That second cause is why this must not be gated on the cross-chapter (pending) path: an
    /// entity-held target is already stale after a plain same-chapter load. Global entities
    /// (MirrorSurfaces) are skipped by UnloadLevel and so only ever see the first cause.
    /// </summary>
    internal static void RepairDisposedGraphicsResources(Level level) {
#if DEBUG
        Stopwatch stopwatch = Stopwatch.StartNew();
#endif
        RepairKnownDisposedAssets(level);
        RepairDisposedTrailBuffers(level);
#if DEBUG
        Logger.Info("SpeedrunTool/GraphicResourcesHandler",
            $"RepairDisposedGraphicsResources: {stopwatch.Elapsed.TotalMilliseconds:F2} ms, " +
            $"entities={level.Entities.Count}");
#endif
    }


    // ── One table for every diagnosed disposed-asset cache ──────────────────────────────────────
    //
    // Columns: where the field lives, how to find its owner, what to do with it, and whether the
    // owner also needs a companion 'disposed' flag cleared.
    //
    // Why hardcoded, and why it must stay that way:
    //
    // 1. A type that holds a VirtualAsset field is not evidence that anything is broken. Most of
    //    them create their target lazily every time it is null, are disposed together with the area
    //    on purpose, or are never reachable after a cross-chapter restore. The runtime census
    //    (work/SpeedrunTool/type-census/) produced leads; every row below was confirmed by reading
    //    the owner's code, and only owners whose target survives an area unload in a disposed state
    //    are kept.
    //
    // 2. Nothing available at load time can pick the repair. "field is a VirtualAsset and
    //    IsDisposed" looks identical for both lifecycle shapes, but the correct action differs: a
    //    lazy owner (recreates whenever the field is null) only needs the field cleared, which also
    //    defers the reallocation to the first render; a constructor-only owner must have the asset
    //    reloaded in place (clearing it would leave a field nobody ever fills again) and usually
    //    also needs its 'disposed' bool cleared or it keeps drawing nothing. Which shape a type has
    //    is a fact about its code, so it is recorded per row instead of guessed at runtime.
    //
    // 3. Walking every entity/component and reflecting over all their fields was removed on purpose
    //    (9543291): it was a full level pass with reflection on every load, and AGENTS.md §16 owns
    //    the budget for this path. Here each row costs one tracker lookup (entity types are
    //    registered with TrackerWarmup at Initialize) or one pass over the short backdrop lists, and
    //    every FieldInfo is resolved once in [Initialize] -- the load path does no reflection.
    //
    // Adding a row: read the owner's code. Keep it only if its target is created once and outlives
    // the area that created it. Prefer Null when the owner recreates from null; use Reload when the
    // owner only builds the target in a constructor or expects a reloadable instance. Do not replace
    // this with enumeration, and do not infer the repair at runtime.
    // ────────────────────────────────────────────────────────────────────────────────────────────
    private enum AssetOwnerKind {
        // A static field: no instance to look for, the field alone identifies the cache.
        Static,
        // An entity field: found through the level's tracker.
        Entity
    }

    private enum AssetRepair {
        // Clear the field; the owner creates its target again when it next needs one.
        Clear,
        // Reload the disposed asset in place; the field itself is never assigned.
        Reload
    }

    private static readonly (AssetOwnerKind Kind, string Mod, string TypeName, string[] Fields, AssetRepair Repair, string DisposedFlag)[] KnownDisposedAssets = [
        // Shared render targets held in static fields. The owner type may not even be instantiated,
        // so there is nothing to walk -- which is why these are named explicitly.
        (AssetOwnerKind.Static, null, "Celeste.StarClimbGraphicsController", ["BlockFill"], AssetRepair.Clear, null),
        (AssetOwnerKind.Static, "MaxHelpingHand", "Celeste.Mod.MaxHelpingHand.Entities.StylegroundFadeController", ["tempRenderTarget"], AssetRepair.Reload, null),
        (AssetOwnerKind.Static, "Microlith57Misc", "Celeste.Mod.Microlith57Misc.Entities.BlackHoleRefractionController", ["RefractionFill"], AssetRepair.Reload, null),
        (AssetOwnerKind.Static, "StrawberryJam2021", "Celeste.Mod.StrawberryJam2021.Entities.DarkMatterRenderer", ["DarkMatterLightning", "BlurTempBuffer"], AssetRepair.Reload, null),
        (AssetOwnerKind.Static, "FrostHelper", "FrostHelper.EntityBatcher", ["temp", "temp2"], AssetRepair.Reload, null),
        (AssetOwnerKind.Static, "CommunalHelper", "Celeste.Mod.CommunalHelper.Backdrops.Cloudscape", ["bufferFullscreen", "buffer320x180"], AssetRepair.Reload, null),
        (AssetOwnerKind.Static, "PrismaticHelper", "PrismaticHelper.Entities.Panels.WorldPanelManager", ["Displacement2"], AssetRepair.Reload, null),
        (AssetOwnerKind.Static, "StyleMaskHelper", "Celeste.Mod.StyleMaskHelper.Entities.BloomMask", ["BloomBuffer", "FadeBuffer"], AssetRepair.Reload, null),
        (AssetOwnerKind.Static, "StyleMaskHelper", "Celeste.Mod.StyleMaskHelper.Entities.ColorGradeMask", ["FadeBuffers"], AssetRepair.Reload, null),

        // Caches held on an instance. Only owners that gate rendering on a "disposed" bool need a row:
        // their asset is reloaded by the generic pass too, but nobody else can clear that flag.
        // Owners without such a flag (NorthernLights, MirrorSurfaces, AltLevelsHelper's PoemPage, ...)
        // need no row at all -- their asset is in the cloned graph, so the clone processor reports it
        // and ReloadDisposedAssets repairs it.
        (AssetOwnerKind.Entity, null, "Celeste.Poem", ["poem", "smoke", "temp"], AssetRepair.Reload, "disposed"),
        (AssetOwnerKind.Entity, "FemtoHelper", "Celeste.Mod.FemtoHelper.Entities.SessionHeart+Poem", ["poem", "smoke", "temp"], AssetRepair.Reload, "disposed"),
        (AssetOwnerKind.Entity, "JungleHelper", "Celeste.Mod.JungleHelper.Triggers.UIText", ["textTarget"], AssetRepair.Reload, "disposed"),
        (AssetOwnerKind.Entity, "JungleHelper", "Celeste.Mod.JungleHelper.Triggers.UIImage", ["imageTarget"], AssetRepair.Reload, "disposed"),
        (AssetOwnerKind.Entity, "JackalHelper", "Celeste.Mod.JackalHelper.Entities.CustomPoem", ["poem", "smoke", "temp"], AssetRepair.Reload, "disposed"),
        (AssetOwnerKind.Entity, "XaphanHelper", "Celeste.Mod.XaphanHelper.UI_Elements.CustomPoem", ["poem", "smoke", "temp"], AssetRepair.Reload, "disposed"),
    ];

    // Resolved once in [Initialize]: type and field lookups (and the readonly check) must not be
    // repeated on the load path.
    private static readonly List<(Type Type, FieldInfo[] Fields, AssetRepair Repair, FieldInfo DisposedFlag)> StaticOwners = [];
    private static readonly List<(Type Type, FieldInfo[] Fields, AssetRepair Repair, FieldInfo DisposedFlag)> EntityOwners = [];

    [Initialize]
    private static void ResolveKnownDisposedAssets() {
        StaticOwners.Clear();
        EntityOwners.Clear();

        foreach ((AssetOwnerKind kind, string mod, string typeName, string[] fieldNames, AssetRepair rowRepair, string disposedFlag) in KnownDisposedAssets) {
            Type type = mod is null ? ModUtils.VanillaAssembly.GetType(typeName) : ModUtils.GetType(mod, typeName);
            if (type is null) {
                continue; // that mod is not installed
            }

            BindingFlags fieldFlags = kind == AssetOwnerKind.Static
                ? BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
                : BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            List<FieldInfo> fields = [];
            foreach (string fieldName in fieldNames) {
                if (type.GetField(fieldName, fieldFlags) is { } field) {
                    fields.Add(field);
                }
            }

            if (fields.Count == 0) {
                continue;
            }

            AssetRepair repair = rowRepair;
            if (repair == AssetRepair.Clear && fields.Exists(field => field.IsInitOnly)) {
                // A readonly field cannot be cleared. Reloading in place still repairs the asset, which
                // is all the owner needs to render again -- it is just not the cheapest repair.
                Logger.Warn("SpeedrunTool/GraphicResourcesHandler",
                    $"{type.FullName} keeps its cache in a readonly field; reloading it instead of clearing it.");
                repair = AssetRepair.Reload;
            }

            FieldInfo flag = disposedFlag is null || kind == AssetOwnerKind.Static
                ? null
                : type.GetField(disposedFlag, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            (kind == AssetOwnerKind.Static ? StaticOwners : EntityOwners).Add((type, [.. fields], repair, flag));

            if (kind == AssetOwnerKind.Entity) {
                // Indexed by the tracker so the lookup is a list, not a walk over every entity.
                TrackerWarmup.Register(type);
            }
        }
    }

    // Runs from LoadStateCore before OnLoadState, so every asset the table needs is still disposed:
    // the whole point of the Reload here is to learn whether the flag may be cleared. Moving this
    // after the generic asset pass would make every Reload return false and silently stop the flags
    // from being cleared.
    private static void RepairKnownDisposedAssets(Level level) {
        foreach ((Type type, FieldInfo[] fields, AssetRepair repair, FieldInfo flag) in StaticOwners) {
            RepairOwner(null, type, fields, repair, flag);
        }

        foreach ((Type type, FieldInfo[] fields, AssetRepair repair, FieldInfo flag) in EntityOwners) {
            foreach (Entity entity in TrackerWarmup.GetEntities(level, type)) {
                RepairOwner(entity, type, fields, repair, flag);
            }
        }
    }

    private static void RepairOwner(object target, Type type, FieldInfo[] fields, AssetRepair repair, FieldInfo flag) {
        bool changed = false;
        foreach (FieldInfo field in fields) {
            changed |= repair == AssetRepair.Clear
                ? ClearDisposedField(target, type, field)
                : RepairField(target, type, field);
        }

        if (!changed || flag is null) {
            return;
        }

        try {
            flag.SetValue(target, false);
        }
        catch (Exception exception) {
            Logger.Warn("SpeedrunTool/GraphicResourcesHandler",
                $"Could not clear {type.FullName}.{flag.Name}: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static bool ClearDisposedField(object target, Type type, FieldInfo field) {
        try {
            object fieldTarget = field.IsStatic ? null : target;
            if (field.GetValue(fieldTarget) is VirtualAsset asset && IsDisposedAsset(asset)) {
                field.SetValue(fieldTarget, null);
                LogRepair(type, field.Name);
                return true;
            }
        }
        catch (Exception exception) {
            Logger.Warn("SpeedrunTool/GraphicResourcesHandler",
                $"Could not clear disposed asset {type.FullName}.{field.Name}: {exception.GetType().Name}: {exception.Message}");
        }

        return false;
    }

    // TrailManager keeps one 512x512 render target per snapshot slot, created lazily with a null check
    // only (BeforeRenderPatch), so a disposed-but-non-null entry would be drawn into. It is not a plain
    // table row because the repair is per array element: clear the entries and make the generic asset
    // pass forget them.
    //
    // Clearing rather than reloading is deliberate: Clear() runs before every save, so the restored
    // manager has no snapshots to draw, and reloading would allocate the targets for nothing (a few MB
    // per cross-chapter load). A cleared entry is recreated on demand by the next trail.
    private static void RepairDisposedTrailBuffers(Level level) {
        if (level.Tracker.GetEntity<TrailManager>() is not { } trailManager) {
            return;
        }

        for (int i = 0; i < trailManager.buffers.Length; i++) {
            if (trailManager.buffers[i] is not VirtualRenderTarget { IsDisposed: true } buffer) {
                continue;
            }

            trailManager.buffers[i] = null;
            ForgetAsset(buffer);
            LogRepair(typeof(TrailManager), $"buffers[{i}]");
        }
    }


    private static bool RepairField(object target, Type type, FieldInfo field) {
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
            LogRepair(type, label);
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

    private static void LogRepair(Type type, string field) {
        string key = type.FullName + "." + field;
        if (LoggedRepairs.Add(key)) {
            Logger.Info("SpeedrunTool/GraphicResourcesHandler",
                $"Repaired disposed graphics cache [{type.FullName}].{field}");
        }
    }

    // ── The generic asset repair ────────────────────────────────────────────────────────────────
    //
    // Every VirtualAsset the clone processor meets while walking the saved graph is recorded here.
    // That makes the set "the assets the saved template holds" without a census or a walk: the clone
    // already visits every field, so it reports what it sees. Static caches are not in the graph and
    // are handled by KnownDisposedAssets instead.
    //
    // On load every still-disposed asset in the set is reloaded in place. This is the layer that
    // covers owners with no table row -- other mods, components, and any asset an area unload
    // disposed without the owner disposing it. It cannot clear an owner's "disposed" bool, which is
    // the only reason those owners also have a row in that table.
    private static readonly HashSet<VirtualAsset> SavedGraphAssets = [];

    // The set is lock-protected like the other state a background pre-clone appends to, even though
    // the unbounded pre-clone wait rules out a concurrent writer today (see
    // StateManager.WaitForPreClone). The lock is the defensive half of that invariant, not a claim
    // that the invariant can be dropped.
    internal static void TrackAsset(VirtualAsset asset) {
        lock (SavedGraphAssets) {
            SavedGraphAssets.Add(asset);
        }
    }

    // A repair that cleared the field must also stop the generic pass from reloading that instance:
    // otherwise the entry is reloaded into nothing, which is exactly the allocation clearing avoided.
    private static void ForgetAsset(VirtualAsset asset) {
        lock (SavedGraphAssets) {
            SavedGraphAssets.Remove(asset);
        }
    }

    private static void ClearAssets() {
        lock (SavedGraphAssets) {
            SavedGraphAssets.Clear();
        }
    }

    internal static void AddSaveLoadAction() {
        SaveLoadAction.InternalSafeAdd(
            loadState: static (_, _) => ReloadDisposedAssets(),
            clearState: ClearAssets,
            preCloneEntities: ClearAssets
        );

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

    private static void ReloadDisposedAssets() {
        VirtualAsset[] batch;
        lock (SavedGraphAssets) {
            batch = [.. SavedGraphAssets];
        }
        int total = batch.Length;
        int reloaded = 0;
        foreach (VirtualAsset asset in batch) {
            switch (asset) {
                case VirtualTexture { IsDisposed: true } texture:
                    // Fix: 全屏切换然后读档煤球红边消失
                    if (!texture.Name.StartsWith("dust-noise-")) {
                        texture.Reload();
                        reloaded++;
                    }

                    break;
                case VirtualRenderTarget { IsDisposed: true } target:
                    target.Reload();
                    reloaded++;
                    break;
            }
        }

        ClearAssets();

#if DEBUG
        Logger.Info("SpeedrunTool/GraphicResourcesHandler",
            $"Saved graph assets: {total}, reloaded {reloaded} disposed one(s)");
#endif
    }
}
