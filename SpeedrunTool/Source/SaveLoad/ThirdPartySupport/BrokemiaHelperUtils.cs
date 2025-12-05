
using Celeste.Mod.SpeedrunTool.Utils;
using System.Collections.Generic;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.ThirdPartySupport;
internal static class BrokemiaHelperUtils {

    internal static void Support() {
        if (ModUtils.GetType("BrokemiaHelper", "BrokemiaHelper.PixelRendered.Vineinator") is { } vineinatorType &&
            ModUtils.GetType("BrokemiaHelper", "BrokemiaHelper.PixelRendered.RWLizard") is { } lizardType) {
            Tracker.AddTypeToTracker(vineinatorType);
            Tracker.AddTypeToTracker(lizardType);
            SaveLoadAction.InternalSafeAdd(
                loadState: (_, level) => {
                    List<Entity> entities = level.Tracker.GetEntitiesTrackIfNeeded(vineinatorType);
                    entities.AddRange(level.Tracker.GetEntitiesTrackIfNeeded(lizardType));

                    foreach (Entity entity in entities) {
                        object pixelComponent = entity.GetFieldValue("pixelComponent");
                        pixelComponent.SetFieldValue("textureChunks", null);
                        pixelComponent.InvokeMethod("CommitChunks");
                    }
                });
        }
    }
}
