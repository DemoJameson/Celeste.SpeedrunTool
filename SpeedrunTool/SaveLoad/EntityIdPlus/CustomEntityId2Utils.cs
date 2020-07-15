using System.Linq;
using System.Reflection;
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
            if (Engine.Scene.GetLevel() is Level level) {
                index = level.Entities.Count(entity => entity is FinalBossShot);
            }

            self.TrySetEntityId2(boss.HasEntityId2() ? boss.GetEntityId2().ToString() : "null", target, index);
            return orig(self, boss, target);
        }

        private static FinalBossShot FinalBossShotOnInit_FinalBoss_Player_float(
            On.Celeste.FinalBossShot.orig_Init_FinalBoss_Player_float orig, FinalBossShot self, FinalBoss boss,
            Player target, float angleOffset) {
            self.TrySetEntityId2(boss.HasEntityId2() ? boss.GetEntityId2().ToString() : "null", target.GetEntityId2(),
                angleOffset);
            return orig(self, boss, target, angleOffset);
        }

        private static FinalBossBeam FinalBossBeamOnInit(On.Celeste.FinalBossBeam.orig_Init orig, FinalBossBeam self,
            FinalBoss boss, Player target) {
            self.TrySetEntityId2(boss.HasEntityId2() ? boss.GetEntityId2().ToString() : "null", target.GetEntityId2());
            self.SetPlayerPosition(target);
            return orig(self, boss, target);
        }

        private static SoundEmitter
            SoundEmitterOnPlay_string(On.Celeste.SoundEmitter.orig_Play_string orig, string sfx) {
            SoundEmitter soundEmitter = orig(sfx);
            soundEmitter.TrySetEntityId2(sfx);
            return soundEmitter;
        }

        private static SoundEmitter SoundEmitterOnPlay_string_Entity_Nullable1(
            On.Celeste.SoundEmitter.orig_Play_string_Entity_Nullable1 orig, string sfx, Entity follow,
            Vector2? offset) {
            SoundEmitter soundEmitter = orig(sfx, follow, offset);
            soundEmitter.TrySetEntityId2(sfx);
            return soundEmitter;
        }

        private static SlashFx SlashFxOnBurst(On.Celeste.SlashFx.orig_Burst orig, Vector2 position, float direction) {
            SlashFx slashFx = orig(position, direction);
            slashFx.TrySetEntityId2(position, direction);
            slashFx.SetStartPosition(position);
            slashFx.SetDirection(direction);
            return slashFx;
        }

        private static SpeedRing SpeedRingOnInit(On.Celeste.SpeedRing.orig_Init orig, SpeedRing self, Vector2 position,
            float angle, Color color) {
            SpeedRing speedRing = orig(self, position, angle, color);
            speedRing.TrySetEntityId2(position, angle, color);
            speedRing.SetStartPosition(position);
            speedRing.SetAngle(angle);
            speedRing.SetColor(color);

            return speedRing;
        }

        private static void BirdTutorialGuiOnCtor(On.Celeste.BirdTutorialGui.orig_ctor orig, BirdTutorialGui self,
            Entity entity, Vector2 position, object info, object[] controls) {
            self.TrySetEntityId2(entity, position, info, string.Join("-", controls));
            self.SetStartPosition(position);
            self.SetControls(controls);
            orig(self, entity, position, info, controls);
        }

        // SeekerStatue 会通过这个构造函数创建 Seeker
        private static void SeekerOnCtor_EntityData_Vector2(On.Celeste.Seeker.orig_ctor_EntityData_Vector2 orig,
            Seeker self, EntityData data, Vector2 offset) {
            self.SetEntityId2(data.ToEntityId2(self));
            self.SetEntityData(data);
            orig(self, data, offset);
        }

        private static Actor DebrisOnInit(On.Celeste.MoveBlock.Debris.orig_Init orig, Actor self, Vector2 position,
            Vector2 center, Vector2 returnTo) {
            Actor debris = orig(self, position, center, returnTo);
            debris.TrySetEntityId2(position, center, returnTo);
            debris.SetCenter(center);
            return debris;
        }
        
        
        private static Debris DebrisOnInit_Vector2_char_bool(On.Celeste.Debris.orig_Init_Vector2_char_bool orig, Debris self, Vector2 pos, char tileset, bool playSound) {
            orig(self, pos, tileset, playSound);
            self.TrySetEntityId2(pos, tileset, playSound);
            return self;
        }

        private static Debris DebrisOnInit_Vector2_char(On.Celeste.Debris.orig_Init_Vector2_char orig, Debris self, Vector2 pos, char tileset) {
            orig(self, pos, tileset);
            self.TrySetEntityId2(pos, tileset, true);
            return self;
        }

        private static void StrawberryPointsOnCtor(On.Celeste.StrawberryPoints.orig_ctor orig, StrawberryPoints self, Vector2 position, bool ghostBerry, int index, bool moonBerry) {
            orig(self, position, ghostBerry, index, moonBerry);
            self.TrySetEntityId2(position, ghostBerry, index, moonBerry);
            self.SetStartPosition(position);
        }

        private static void SolidOnCtor(On.Celeste.Solid.orig_ctor orig, Solid self, Vector2 position, float width, float height, bool safe) {
            orig(self, position, width, height, safe);
            self.TrySetEntityId2(position, width, height, safe);
            self.SaveWidth(width);
            self.SaveHeight(height);
        }

        public static void OnLoad() {
            On.Celeste.FinalBossShot.Init_FinalBoss_Vector2 += FinalBossShotOnInit_FinalBoss_Vector2;
            On.Celeste.FinalBossShot.Init_FinalBoss_Player_float += FinalBossShotOnInit_FinalBoss_Player_float;

            On.Celeste.FinalBossBeam.Init += FinalBossBeamOnInit;

            On.Celeste.SoundEmitter.Play_string += SoundEmitterOnPlay_string;
            On.Celeste.SoundEmitter.Play_string_Entity_Nullable1 += SoundEmitterOnPlay_string_Entity_Nullable1;

            On.Celeste.SlashFx.Burst += SlashFxOnBurst;

            On.Celeste.SpeedRing.Init += SpeedRingOnInit;

            On.Celeste.BirdTutorialGui.ctor += BirdTutorialGuiOnCtor;

            On.Celeste.Seeker.ctor_EntityData_Vector2 += SeekerOnCtor_EntityData_Vector2;

            On.Celeste.MoveBlock.Debris.Init += DebrisOnInit;

            On.Celeste.Debris.Init_Vector2_char += DebrisOnInit_Vector2_char;
            On.Celeste.Debris.Init_Vector2_char_bool += DebrisOnInit_Vector2_char_bool;

            On.Celeste.StrawberryPoints.ctor += StrawberryPointsOnCtor;

            On.Celeste.Solid.ctor += SolidOnCtor;
        }

        public static void OnUnload() {
            On.Celeste.FinalBossShot.Init_FinalBoss_Vector2 -= FinalBossShotOnInit_FinalBoss_Vector2;
            On.Celeste.FinalBossShot.Init_FinalBoss_Player_float -= FinalBossShotOnInit_FinalBoss_Player_float;

            On.Celeste.FinalBossBeam.Init -= FinalBossBeamOnInit;

            On.Celeste.SoundEmitter.Play_string -= SoundEmitterOnPlay_string;
            On.Celeste.SoundEmitter.Play_string_Entity_Nullable1 -= SoundEmitterOnPlay_string_Entity_Nullable1;

            On.Celeste.SlashFx.Burst -= SlashFxOnBurst;

            On.Celeste.SpeedRing.Init -= SpeedRingOnInit;

            On.Celeste.BirdTutorialGui.ctor -= BirdTutorialGuiOnCtor;

            On.Celeste.Seeker.ctor_EntityData_Vector2 -= SeekerOnCtor_EntityData_Vector2;

            On.Celeste.MoveBlock.Debris.Init -= DebrisOnInit;
            
            On.Celeste.Debris.Init_Vector2_char -= DebrisOnInit_Vector2_char;
            On.Celeste.Debris.Init_Vector2_char_bool -= DebrisOnInit_Vector2_char_bool;
            
            On.Celeste.StrawberryPoints.ctor -= StrawberryPointsOnCtor;
            
            On.Celeste.Solid.ctor -= SolidOnCtor;
        }
    }
    
    public static class SolidExtensions {
        private const string SolidWidthKey = "SolidWidthKey";
        private const string SolidHeightKey = "SolidHeightKey";

        public static void SaveWidth(this Solid solid, float width) {
            solid.SetExtendedFloat(SolidWidthKey, width);
        }
        public static void SaveHeight(this Solid solid, float height) {
            solid.SetExtendedFloat(SolidHeightKey, height);
        }
        
        public static Solid Clone(this Solid solid) {
            return new Solid(solid.GetStartPosition(), solid.GetExtendedFloat(SolidWidthKey), solid.GetExtendedFloat(SolidHeightKey), solid.Safe);
        }
    }

    public static class StrawberryPointsExtensions {
        public static StrawberryPoints Clone(this StrawberryPoints points) {
            return new StrawberryPoints(points.GetStartPosition(), (bool) points.GetField("ghostberry"), (int) points.GetField("index"), (bool) points.GetField("moonberry"));
        }
    }

    public static class MoveBlockDebrisExtensions {
        private const string MoveBlockDebrisCenterKey = "MoveBlockDebrisCenterKey";

        // ReSharper disable once PossibleNullReferenceException
        private static MethodInfo CreateDebris = typeof(Pooler)
            .GetMethod("Create").MakeGenericMethod(typeof(MoveBlock).GetNestedType("Debris", BindingFlags.NonPublic));

        public static Actor CloneMoveBlockDebris(this Actor moveBlockDebris) {
            return CreateDebris.Invoke(Engine.Pooler,new object[]{}).InvokeMethod("Init", moveBlockDebris.GetStartPosition(), moveBlockDebris.GetCenter(), moveBlockDebris.GetField("home")) as Actor;
        }

        public static Vector2 GetCenter(this Actor moveBlockDebris) {
            return moveBlockDebris.GetExtendedDataValue<Vector2>(MoveBlockDebrisCenterKey);
        }

        public static void SetCenter(this Actor moveBlockDebris, Vector2 center) {
            moveBlockDebris.SetExtendedDataValue(MoveBlockDebrisCenterKey, center);
        }
    }

    public static class BirdTutorialGuiExtensions {
        private const string BirdTutorialGuiControlsKey = "BirdTutorialGuiControlsKey";

        public static BirdTutorialGui Clone(this BirdTutorialGui birdTutorialGui) {
            object entity = birdTutorialGui.Entity.TryFindOrCloneObject();
            if (entity == null) return null;
            return new BirdTutorialGui((Entity) entity, birdTutorialGui.GetStartPosition(),
                birdTutorialGui.GetField("info"),
                birdTutorialGui.GetControls());
        }

        public static object[] GetControls(this BirdTutorialGui birdTutorialGui) {
            return birdTutorialGui.GetExtendedDataValue<object[]>(BirdTutorialGuiControlsKey);
        }

        public static void SetControls(this BirdTutorialGui birdTutorialGui, object[] control) {
            birdTutorialGui.SetExtendedDataValue(BirdTutorialGuiControlsKey, control);
        }
    }

    public static class FinalBoosExtensions {
        private const string FinalBossBeamPlayerPositionKey = "FinalBossBeamPlayerPositionKey";

        public static FinalBossShot Clone(this FinalBossShot finalBossShot) {
            FinalBoss boss = finalBossShot.GetField("boss")?.TryFindOrCloneObject() as FinalBoss;
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
            FinalBoss boss = finalBossBeam.GetField("boss")?.TryFindOrCloneObject() as FinalBoss;
            if (boss == null) return null;

            return Engine.Pooler.Create<FinalBossBeam>().Init(boss,
                new Player(finalBossBeam.GetPlayerPosition(), PlayerSpriteMode.Madeline));
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
            return Engine.Pooler.Create<SpeedRing>()
                .Init(speedRing.GetStartPosition(), speedRing.GetAngle(), speedRing.GetColor());
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