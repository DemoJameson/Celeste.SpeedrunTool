
using Celeste.Mod.SpeedrunTool.Utils;
using Microsoft.Xna.Framework.Graphics;
using Monocle;
using System.Collections.Generic;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.ThirdPartySupport;
internal static class BrokemiaHelperUtils {

    [Initialize]

    private static void Initialize() {
        if (ModUtils.GetType("BrokemiaHelper", "BrokemiaHelper.PixelRendered.Vineinator") is { } vineinatorType &&
            ModUtils.GetType("BrokemiaHelper", "BrokemiaHelper.PixelRendered.RWLizard") is { } lizardType) {
            Tracker.AddTypeToTracker(vineinatorType);
            Tracker.AddTypeToTracker(lizardType);
        }
    }

    private static void RebuildPixelComponent(object pixelComponent) {
        if (pixelComponent is null || pixelComponent.GetFieldValue("chunks") is null) {
            return;
        }

        // Drop the old texture handles first; the old scene may already have disposed them, but
        // this also prevents a stale GPU texture from surviving a cross-chapter restore.
        if (pixelComponent.GetFieldValue("textureChunks") is VirtualMap<Texture2D> textures) {
            for (int x = 0; x < textures.Columns; x++) {
                for (int y = 0; y < textures.Rows; y++) {
                    textures[x, y]?.Dispose();
                }
            }
        }
        pixelComponent.SetFieldValue("textureChunks", null);

        // A restored PixelComponent has its chunk data but its dirty flags are clean.
        // CommitChunks only uploads dirty chunks, so mark every chunk dirty first or the
        // vine/lizard can stay invisible after a cross-chapter load.
        if (pixelComponent.GetFieldValue("dirtyChunks") is VirtualMap<bool> dirtyChunks) {
            for (int x = 0; x < dirtyChunks.Columns; x++) {
                for (int y = 0; y < dirtyChunks.Rows; y++) {
                    dirtyChunks[x, y] = true;
                }
            }
        }
        pixelComponent.InvokeMethod("CommitChunks");
    }

    internal static void Support() {
        if (ModUtils.GetType("BrokemiaHelper", "BrokemiaHelper.PixelRendered.Vineinator") is { } vineinatorType &&
            ModUtils.GetType("BrokemiaHelper", "BrokemiaHelper.PixelRendered.RWLizard") is { } lizardType) {
            SaveLoadAction.InternalSafeAdd(
                loadState: (_, level) => {
                    List<Entity> entities = level.Tracker.GetEntitiesTrackIfNeeded(vineinatorType);
                    entities.AddRange(level.Tracker.GetEntitiesTrackIfNeeded(lizardType));

                    foreach (Entity entity in entities) {
                        RebuildPixelComponent(entity.GetFieldValue("pixelComponent"));
                    }
                }
            );
        }
    }
}
