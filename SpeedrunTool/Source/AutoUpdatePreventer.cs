using Celeste.Mod.Helpers;
using Celeste.Mod.SpeedrunTool.Utils;
using System.Collections.Generic;

namespace Celeste.Mod.SpeedrunTool;

internal class AutoUpdatePreventer {

    [Initialize]
    public static void Initialize() {
        Logger.Warn("SpeedrunTool - legacy", "This is a legacy version. It doesn't support multiple saveslots, and it won't be auto-updated!");
        Logger.Warn("SpeedrunTool - legacy", "For update or bugfix, check: https://github.com/DemoJameson/Celeste.SpeedrunTool/releases");
        typeof(ModUpdaterHelper).GetMethodInfo("GetAsyncLoadedModUpdates").ILHook((cursor, _) => {
            cursor.Goto(-1);
            cursor.EmitDelegate(Handler);
        });

        static SortedDictionary<ModUpdateInfo, EverestModuleMetadata> Handler(SortedDictionary<ModUpdateInfo, EverestModuleMetadata> updateList) {
            ModUpdateInfo srt = null;
            foreach (ModUpdateInfo info in updateList.Keys) {
                if (info.Name == "SpeedrunTool") {
                    srt = info;
                    break;
                }
            }
            if (srt is not null) {
                updateList.Remove(srt);
            }
            return updateList;
        }
    }
}
