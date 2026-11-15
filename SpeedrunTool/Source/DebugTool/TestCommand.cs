#if DEBUG
using Celeste.Mod.SpeedrunTool.SaveLoad;

namespace Celeste.Mod.SpeedrunTool.DebugTool;
internal static class TestCommand {

    [Command("test_srt", "SpeedrunTool debug test")]
    public static void Test() {
        // do anything you want to test here
        StateManager.Instance.GcCollect(force: true);
    }

    [Command("test2_srt", "SpeedrunTool debug test")]
    public static void Test2() {
        // do anything you want to test here
    }

    [Command("mark_srt", "SpeedrunTool debug test")]
    public static void Mark() {
        // do anything you want to test here
        MemoryTracker.CheckFragmentation("test");
    }

    [Initialize]
    public static void Initialize() {
        // test something here
    }

}
#endif