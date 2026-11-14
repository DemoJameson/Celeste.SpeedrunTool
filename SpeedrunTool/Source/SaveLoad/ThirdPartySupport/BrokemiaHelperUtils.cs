
using Celeste.Mod.SpeedrunTool.Utils;
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

    internal static void Support() {
        if (ModUtils.GetType("BrokemiaHelper", "BrokemiaHelper.PixelRendered.Vineinator") is { } vineinatorType &&
            ModUtils.GetType("BrokemiaHelper", "BrokemiaHelper.PixelRendered.RWLizard") is { } lizardType) {
            SaveLoadAction.InternalSafeAdd(
                loadState: (_, level) => {
                    List<Entity> entities = level.Tracker.GetEntitiesTrackIfNeeded(vineinatorType);
                    entities.AddRange(level.Tracker.GetEntitiesTrackIfNeeded(lizardType));

                    foreach (Entity entity in entities) {
                        object pixelComponent = entity.GetFieldValue("pixelComponent");
                        pixelComponent.SetFieldValue("textureChunks", null);
                        // A restored PixelComponent has its chunk data but its dirty flags are clean.
                        // CommitChunks only uploads dirty chunks, so mark every chunk dirty first or
                        // the vine/lizard can stay invisible after a cross-chapter load.
                        if (pixelComponent.GetFieldValue("dirtyChunks") is VirtualMap<bool> dirtyChunks) {
                            for (int x = 0; x < dirtyChunks.Columns; x++) {
                                for (int y = 0; y < dirtyChunks.Rows; y++) {
                                    dirtyChunks[x, y] = true;
                                }
                            }
                        }
                        pixelComponent.InvokeMethod("CommitChunks");
                    }
                }
            );
        }
    }
}
