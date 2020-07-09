using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.EntityActions {
    // BUG: 连发的子弹保存后会堆叠在一个位置
    public class FinalBossShotRestoreAction : RestoreAction {
        public FinalBossShotRestoreAction() : base(typeof(FinalBossShot)) { }

        private FinalBossShot FinalBossShotOnInit_FinalBoss_Vector2(
            On.Celeste.FinalBossShot.orig_Init_FinalBoss_Vector2 orig, FinalBossShot self, FinalBoss boss,
            Vector2 target) {
            self.SetEntityId2(self.CreateEntityId2(boss.HasEntityId2() ? boss.GetEntityId2().ToString() : "null", target.ToString()));
            return orig(self, boss, target);
        }

        private FinalBossShot FinalBossShotOnInit_FinalBoss_Player_float(
            On.Celeste.FinalBossShot.orig_Init_FinalBoss_Player_float orig, FinalBossShot self, FinalBoss boss,
            Player target, float angleOffset) {
            self.SetEntityId2(self.CreateEntityId2(boss.HasEntityId2() ? boss.GetEntityId2().ToString() : "null", target.GetEntityId2().ToString(), angleOffset.ToString()));
            return orig(self, boss, target, angleOffset);
        }

        public override void OnLoad() {
            On.Celeste.FinalBossShot.Init_FinalBoss_Vector2 += FinalBossShotOnInit_FinalBoss_Vector2;
            On.Celeste.FinalBossShot.Init_FinalBoss_Player_float += FinalBossShotOnInit_FinalBoss_Player_float;
        }

        public override void OnUnload() { }
    }
}