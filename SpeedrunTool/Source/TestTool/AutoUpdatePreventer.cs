namespace Celeste.Mod.SpeedrunTool.TestTool;
internal class AutoUpdatePreventer {

    /*
    public static Version NextReleaseVersion = new Version(3, 25, 0);

    [Initialize]
    public static void Initialize() {
        Logger.Log(LogLevel.Info, "SpeedrunTool", $"You are in dev build, related mods won't be auto-updated unless SpeedrunTool v{NextReleaseVersion} is released.");
        typeof(Helpers.ModUpdaterHelper).GetMethodInfo("GetAsyncLoadedModUpdates").ILHook((cursor, _) => {
            cursor.Goto(-1);
            cursor.EmitDelegate(Handler);
        });
    }

    private static SortedDictionary<Helpers.ModUpdateInfo, EverestModuleMetadata> Handler(SortedDictionary<Helpers.ModUpdateInfo, EverestModuleMetadata> updateList) {
        if (updateList is null) {
            return null;
        }
        bool found = false;
        foreach (Helpers.ModUpdateInfo info in updateList.Keys) {
            if (info.Name == "SpeedrunTool" && Version.Parse(info.Version) < NextReleaseVersion) {
                found = true;
                break;
            }
        }
        if (found) {
            var list = updateList.Select(x => x.Key).Where(x => FilteredMods.Contains(x.Name)).ToList();
            foreach (var info in list) {
                updateList.Remove(info);
            }
        }
        return updateList;
    }

    private static List<string> FilteredMods = new List<string>() { "SpeedrunTool", "CelesteTAS", "TASHelper", "GhostModForTas" };
    */
}
