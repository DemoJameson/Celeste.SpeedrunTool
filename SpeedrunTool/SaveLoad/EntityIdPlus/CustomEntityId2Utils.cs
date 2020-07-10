using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus {
    public static class CustomEntityId2Utils {
        private static FinalBossShot FinalBossShotOnInit_FinalBoss_Vector2(
            On.Celeste.FinalBossShot.orig_Init_FinalBoss_Vector2 orig, FinalBossShot self, FinalBoss boss,
            Vector2 target) {
            // 区分连发的炮弹
            int index = 0;
            EntityId2 entityId2 = self.CreateEntityId2(boss.HasEntityId2() ? boss.GetEntityId2().ToString() : "null", target);
            if (Engine.Scene.GetLevel() is Level level) {
                index = level.Entities.Count(entity => entity is FinalBossShot);
            }

            self.TrySetEntityId2(boss.HasEntityId2() ? boss.GetEntityId2().ToString() : "null", target, index);
            return orig(self, boss, target);
        }

        private static FinalBossShot FinalBossShotOnInit_FinalBoss_Player_float(
            On.Celeste.FinalBossShot.orig_Init_FinalBoss_Player_float orig, FinalBossShot self, FinalBoss boss,
            Player target, float angleOffset) {
            self.TrySetEntityId2(boss.HasEntityId2() ? boss.GetEntityId2().ToString() : "null", target.GetEntityId2(), angleOffset);
            return orig(self, boss, target, angleOffset);
        }

        private static FinalBossBeam FinalBossBeamOnInit(On.Celeste.FinalBossBeam.orig_Init orig, FinalBossBeam self, FinalBoss boss, Player target) {
            self.TrySetEntityId2(boss.HasEntityId2() ? boss.GetEntityId2().ToString() : "null", target.GetEntityId2());
            self.SetPlayerPosition(target);
            return orig(self, boss, target);
        }

        private static void SoundEmitterOnCtor_string_Entity_Vector2(
            On.Celeste.SoundEmitter.orig_ctor_string_Entity_Vector2 orig, SoundEmitter self, string sfx, Entity follow,
            Vector2 offset) {
            self.TrySetEntityId2(sfx);
            orig(self, sfx, follow, offset);
        }

        private static void SoundEmitterOnCtor_string(On.Celeste.SoundEmitter.orig_ctor_string orig, SoundEmitter self,
            string sfx) {
            self.TrySetEntityId2(sfx);
            orig(self, sfx);
        }

        private static SlashFx SlashFxOnBurst(On.Celeste.SlashFx.orig_Burst orig, Vector2 position, float direction) {
            SlashFx slashFx = orig(position, direction);
            slashFx.TrySetEntityId2(position, direction);
            slashFx.SetStartPosition(position);
            slashFx.SetDirection(direction);
            return slashFx;
        }

        private static SpeedRing SpeedRingOnInit(On.Celeste.SpeedRing.orig_Init orig, SpeedRing self, Vector2 position, float angle, Color color) {
            SpeedRing speedRing = orig(self, position, angle, color);
            speedRing.TrySetEntityId2(position, angle, color);
            speedRing.SetStartPosition(position);
            speedRing.SetAngle(angle);
            speedRing.SetColor(color);
            
            return speedRing;
        }

        public static void OnLoad() {
            On.Celeste.FinalBossShot.Init_FinalBoss_Vector2 += FinalBossShotOnInit_FinalBoss_Vector2;
            On.Celeste.FinalBossShot.Init_FinalBoss_Player_float += FinalBossShotOnInit_FinalBoss_Player_float;
            
            On.Celeste.FinalBossBeam.Init += FinalBossBeamOnInit;
            
            On.Celeste.SoundEmitter.ctor_string += SoundEmitterOnCtor_string;
            On.Celeste.SoundEmitter.ctor_string_Entity_Vector2 += SoundEmitterOnCtor_string_Entity_Vector2;

            On.Celeste.SlashFx.Burst += SlashFxOnBurst;
            
            On.Celeste.SpeedRing.Init += SpeedRingOnInit;
        }

        public static void OnUnload() {
            On.Celeste.FinalBossShot.Init_FinalBoss_Vector2 -= FinalBossShotOnInit_FinalBoss_Vector2;
            On.Celeste.FinalBossShot.Init_FinalBoss_Player_float -= FinalBossShotOnInit_FinalBoss_Player_float;
            
            On.Celeste.FinalBossBeam.Init -= FinalBossBeamOnInit;
            
            On.Celeste.SoundEmitter.ctor_string -= SoundEmitterOnCtor_string;
            On.Celeste.SoundEmitter.ctor_string_Entity_Vector2 -= SoundEmitterOnCtor_string_Entity_Vector2;

            On.Celeste.SlashFx.Burst -= SlashFxOnBurst;
            
            On.Celeste.SpeedRing.Init -= SpeedRingOnInit;
        }
    }

    public static class FinalBoosExtensions {
        private const string FinalBossBeamPlayerPositionKey = "FinalBossBeamPlayerPositionKey";
        public static FinalBossShot Clone(this FinalBossShot finalBossShot) {
            FinalBoss boss = finalBossShot.GetField("boss")?.FindOrCreateSpecifiedType() as FinalBoss;
            if (boss == null) return null;

            if (finalBossShot.GetField("target") == null) {
                return Engine.Pooler.Create<FinalBossShot>()
                    .Init(boss, (Vector2) finalBossShot.GetField("targetPt"));
            }
            if (Engine.Scene.GetPlayer() is Player player) {
                return Engine.Pooler.Create<FinalBossShot>()
                    .Init(boss, player, (float) finalBossShot.GetField("angleOffset"));
            }

            return null;
        }
        
        public static FinalBossBeam Clone(this FinalBossBeam finalBossBeam) {
            FinalBoss boss = finalBossBeam.GetField("boss")?.FindOrCreateSpecifiedType() as FinalBoss;
            if (boss == null) return null;

            return Engine.Pooler.Create<FinalBossBeam>().Init(boss, new Player(finalBossBeam.GetPlayerPosition(), PlayerSpriteMode.Madeline));
        }
        
        public static Vector2 GetPlayerPosition(this FinalBossBeam finalBossBeam) {
            return finalBossBeam.GetExtendedDataValue<Vector2>(FinalBossBeamPlayerPositionKey);
        }

        public static void SetPlayerPosition(this FinalBossBeam finalBossBeam, Player player) {
            finalBossBeam.SetExtendedDataValue(FinalBossBeamPlayerPositionKey, player.Position);
        }
    }
    
    public static class SlashFxExtensions {
        private const string SlashFxDirectionKey = "SlashFxDirectionKey";

        public static SlashFx Clone(this SlashFx slashFx) {
            return SlashFx.Burst(slashFx.GetStartPosition(), slashFx.GetDirection());
        }

        public static float GetDirection(this SlashFx slashFx) {
            return slashFx.GetExtendedDataValue<float>(SlashFxDirectionKey);
        }

        public static void SetDirection(this SlashFx slashFx, float direction) {
            slashFx.SetExtendedDataValue(SlashFxDirectionKey, direction);
        }
    }
    
    public static class SpeedRingExtensions {
        private const string SpeedRingAngleKey = "SpeedRingAngleKey";
        private const string SpeedRingColorKey = "SpeedRingColorKey";
        
        public static SpeedRing Clone(this SpeedRing speedRing) {
            return Engine.Pooler.Create<SpeedRing>().Init(speedRing.GetStartPosition(), speedRing.GetAngle(), speedRing.GetColor());
        }

        public static float GetAngle(this SpeedRing speedRing) {
            return speedRing.GetExtendedDataValue<float>(SpeedRingAngleKey);
        }

        public static void SetAngle(this SpeedRing speedRing, float direction) {
            speedRing.SetExtendedDataValue(SpeedRingAngleKey, direction);
        }
        
        public static Color GetColor(this SpeedRing speedRing) {
            return speedRing.GetExtendedDataValue<Color>(SpeedRingColorKey);
        }

        public static void SetColor(this SpeedRing speedRing, Color direction) {
            speedRing.SetExtendedDataValue(SpeedRingColorKey, direction);
        }
    }
}