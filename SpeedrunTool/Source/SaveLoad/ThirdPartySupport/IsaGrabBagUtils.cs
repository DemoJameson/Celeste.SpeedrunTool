using Celeste.Mod.SpeedrunTool.Utils;
using System.Linq;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.ThirdPartySupport;
internal static class IsaGrabBagUtils {

    internal static void Support() {
        // 解决 v1.6.0 之前的版本读档后影像残留在屏幕中
        if (ModUtils.GetModule("IsaGrabBag") is { } module && module.Metadata.Version < new Version(1, 6, 0) &&
            ModUtils.GetType("IsaGrabBag", "Celeste.Mod.IsaGrabBag.DreamSpinnerBorder") is { } borderType) {
            SaveLoadAction.InternalSafeAdd(
                loadState: (_, level) => level.Entities.FirstOrDefault(entity => entity.GetType() == borderType)?.Update()
            );
        }

        // 解决读档后冲进 DreamSpinner 会被刺死
        SaveLoadAction.CloneModTypeFields("IsaGrabBag", "Celeste.Mod.IsaGrabBag.GrabBagModule", "ZipLineState", "playerInstance");
        SaveLoadAction.CloneModTypeFields("IsaGrabBag", "Celeste.Mod.IsaGrabBag.BadelineFollower", "booster", "LookForBubble");
    }
}
