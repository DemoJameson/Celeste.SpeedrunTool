using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus {
    public static class CustomEntityId2Utils {
        public static void OnLoad() {
            On.Celeste.FinalBossShot.Added += FinalBossShotOnAdded;
        }

        public static void OnUnload() {
            On.Celeste.FinalBossShot.Added += FinalBossShotOnAdded;
        }

        private static void FinalBossShotOnAdded(On.Celeste.FinalBossShot.orig_Added orig, FinalBossShot self,
            Scene scene) {
            orig(self, scene);
            if (self.IsSidEmpty()) {
                self.SetEntityId2(new object[] {self.Position, scene.Entities.FindAll<FinalBossShot>().Count});
            }
        }
    }
}