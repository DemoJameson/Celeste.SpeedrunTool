
using Celeste.Mod.SpeedrunTool.Utils;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.ThirdPartySupport;
internal static class BrokemiaHelperUtils {

    internal static void Support() {
        if (ModUtils.GetType("BrokemiaHelper", "BrokemiaHelper.PixelRendered.Vineinator") is { } vineinatorType &&
            ModUtils.GetType("BrokemiaHelper", "BrokemiaHelper.PixelRendered.RWLizard") is { } lizardType) {
            SaveLoadAction.InternalSafeAdd(
                loadState: (_, level) => {
                    foreach (Entity entity in level.Entities) {
                        Type type = entity.GetType();
                        if (type == vineinatorType || type == lizardType) {
                            object pixelComponent = entity.GetFieldValue("pixelComponent");
                            pixelComponent.SetFieldValue("textureChunks", null);
                            pixelComponent.InvokeMethod("CommitChunks");
                        }
                    }
                });
        }
    }
}
