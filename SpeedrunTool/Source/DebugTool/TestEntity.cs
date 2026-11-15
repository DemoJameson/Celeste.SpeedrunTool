#if DEBUG


namespace Celeste.Mod.SpeedrunTool.DebugTool;
internal class TestEntity : Entity {

    public TestEntity() {
        Tag = Tags.Global;
    }

    public override void Removed(Scene scene) {
        base.Removed(scene);
        Logger.Debug("SpeedrunTool", $"TestEntity removed");
    }

    //[Load]
    //private static void Load() {
    //    On.Celeste.Level.LoadLevel += Level_LoadLevel;
    //}


    //[Unload]
    //private static void Unload() {
    //    On.Celeste.Level.LoadLevel -= Level_LoadLevel;
    //}

    private static void Level_LoadLevel(On.Celeste.Level.orig_LoadLevel orig, Level self, Player.IntroTypes playerIntro, bool isFromLoader) {
        orig(self, playerIntro, isFromLoader);
        self.Add(new TestEntity());
    }
}
#endif