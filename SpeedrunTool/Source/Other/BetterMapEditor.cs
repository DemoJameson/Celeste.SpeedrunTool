using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Celeste.Editor;
using Mono.Cecil.Cil;
using MonoMod.Cil;

namespace Celeste.Mod.SpeedrunTool.Other;

public static class BetterMapEditor {
    private const string StartChasingLevel = "3";
    private static bool shouldFixTeleportProblems;
    private static bool shouldFixFarewellSpawnPoint;

    private static readonly HashSet<string> CoreIceRooms = new() {
        // 8A
        "9b-05", "9c-00", "9c-00b", "9c-02", "9c-03", "9d-03", "9d-10",

        // 8B
        "9Ha-03", "9Ha-04", "9Ha-05", "9Hb-02", "9Hb-03", "9Hc-01", "9Hc-06"
    };

    private static readonly HashSet<string> CoreRefillDashRooms = new() {
        // 8A
        "90X", "900", "901", "9space",
        // 8B
        "9H00", "9H01", "9Hspace",
        // 8C
        "9HHintro"
    };

    // 3A 杂乱房间部分的光线调暗
    private static readonly HashSet<string> DarkRooms = new() {
        "09-b", "08-x", "10-x", "11-x", "11-y", "12-y", "11-z", "10-z",
        "10-y", "11-a", "12-x", "13-x", "13-a", "13-b", "12-b", "11-b",
        "10-c", "10-d", "11-d", "12-d", "12-c", "11-c"
    };

    // 2A 需要激活果冻的房间
    private static readonly List<string> DreamDashRooms = new() {
        "0", "1", "2",
        "d0", "d1", "d2", "d3", "d4", "d6", "d5", "d9", "3x",
        "3", "4", "5", "6", "7", "8", "9", "9b", "10",
        "11", "12b", "12c", "12d", "12", "13"
    };

    private static readonly HashSet<Vector2> ExcludeDreamRespawnPoints = new() {
        new Vector2(288, 152),
        new Vector2(632, 144),
        new Vector2(648, 144),
        new Vector2(800, 168),
        new Vector2(792, 248),
        new Vector2(648, 272),
        new Vector2(648, 512),
        new Vector2(952, 328),
        new Vector2(952, 520),
        new Vector2(648, 704),
        new Vector2(952, 712),
        new Vector2(952, 160),
        new Vector2(968, 160),
        new Vector2(1608, 720),
        new Vector2(1616, 600)
    };

    private static readonly HashSet<Vector2> ExcludeFarewellCassettePoints = new() {
        new Vector2(43632, -9976),
        new Vector2(49448, -10296)
    };

    private static readonly HashSet<string> FarewellCassetteRooms = new() {
        "i-00", "i-00b", "i-01", "i-02", "i-03", "i-04", "i-05", "j-00"
    };

    private static readonly HashSet<string> FarewellOneDashRooms = new() {
        "intro-00-past", "intro-01-future",
        "i-00", "i-00b", "i-01", "i-02", "i-03", "i-04", "i-05",
        "j-00", "j-00b", "j-01", "j-02", "j-03", "j-04", "j-05", "j-06", "j-07", "j-08", "j-09",
        "j-10", "j-11", "j-12", "j-13", "j-14", "j-14b", "j-15", "j-16", "j-17", "j-18", "j-19",
        "end-golden", "end-cinematic", "end-granny"
    };
    
    private static bool delayedOpenDebugMap;

    [Load]
    private static void Load() {
        On.Celeste.Editor.MapEditor.LoadLevel += MapEditorOnLoadLevel;
        On.Celeste.WindController.SetAmbienceStrength += FixWindSoundNotPlay;
        On.Celeste.OshiroTrigger.ctor += RestoreOshiroTrigger;
        On.Celeste.Commands.CmdLoad += CommandsOnCmdLoad;
        On.Celeste.LevelLoader.ctor += LevelLoaderOnCtor;
        IL.Celeste.FlingBird.Awake += FlingBirdOnAwake;
        IL.Celeste.NPC06_Theo_Plateau.Awake += NPC06_Theo_PlateauOnAwake;
        On.Monocle.Engine.Update += EngineOnUpdate;

        Hotkey.OpenDebugMap.RegisterPressedAction(scene => {
            if (scene is Level) {
                delayedOpenDebugMap = true;
            }
        });
    }

    [Unload]
    private static void Unload() {
        On.Celeste.Editor.MapEditor.LoadLevel -= MapEditorOnLoadLevel;
        On.Celeste.WindController.SetAmbienceStrength -= FixWindSoundNotPlay;
        On.Celeste.OshiroTrigger.ctor -= RestoreOshiroTrigger;
        On.Celeste.Commands.CmdLoad -= CommandsOnCmdLoad;
        On.Celeste.LevelLoader.ctor -= LevelLoaderOnCtor;
        IL.Celeste.FlingBird.Awake -= FlingBirdOnAwake;
        IL.Celeste.NPC06_Theo_Plateau.Awake -= NPC06_Theo_PlateauOnAwake;
        On.Monocle.Engine.Update -= EngineOnUpdate;
    }

    private static void CommandsOnCmdLoad(On.Celeste.Commands.orig_CmdLoad orig, int id, string level) {
        shouldFixTeleportProblems = ModSettings.Enabled;
        shouldFixFarewellSpawnPoint = ModSettings.Enabled && id == 10 && level == "g-06";
        orig(id, level);
    }

    // 修复 3C 第三面最后的传送点 Oshiro 不出现的问题
    private static void RestoreOshiroTrigger(On.Celeste.OshiroTrigger.orig_ctor orig, OshiroTrigger self,
        EntityData data,
        Vector2 offset) {
        orig(self, data, offset);

        if (!ModSettings.Enabled) {
            return;
        }

        Vector2 oshiro3C = new(1520, -272);
        Level level = Engine.Scene.GetLevel();

        if (level != null && level.Session.Area.ToString() == "3HH" && level.StartPosition != null &&
            level.Session.GetSpawnPoint((Vector2)level.StartPosition) == oshiro3C) {
            self.Add(new Coroutine(EnterOshiroTrigger(self)));
        }
    }

    private static IEnumerator EnterOshiroTrigger(OshiroTrigger self) {
        Player player = self.SceneAs<Level>().GetPlayer();
        if (player != null) {
            self.OnEnter(player);
        }

        yield break;
    }

    private static void FixWindSoundNotPlay(On.Celeste.WindController.orig_SetAmbienceStrength orig, WindController self, bool strong) {
        if (ModSettings.Enabled && Audio.CurrentAmbienceEventInstance == null && Engine.Scene.GetSession()?.Area.LevelSet == "Celeste") {
            Audio.SetAmbience("event:/env/amb/04_main");
        }

        orig(self, strong);
    }

    private static void MapEditorOnLoadLevel(On.Celeste.Editor.MapEditor.orig_LoadLevel orig, Editor.MapEditor self,
        LevelTemplate level, Vector2 at) {
        if (!ModSettings.Enabled) {
            orig(self, level, at);
            return;
        }

        shouldFixTeleportProblems = true;
        orig(self, level, at);
    }

    private static void LevelLoaderOnCtor(On.Celeste.LevelLoader.orig_ctor orig, LevelLoader self, Session session,
        Vector2? startPosition) {
        if (shouldFixTeleportProblems) {
            shouldFixTeleportProblems = false;
            FixTeleportProblems(session, startPosition);
        }

        orig(self, session, startPosition);
    }

    private static void FlingBirdOnAwake(ILContext il) {
        ILCursor ilCursor = new(il);
        if (ilCursor.TryGotoNext(MoveType.After,
                ins => ins.OpCode == OpCodes.Ldarg_1,
                ins => ins.MatchCallvirt<Scene>("get_Tracker"),
                ins => ins.OpCode == OpCodes.Callvirt && ins.Operand.ToString().Contains("Celeste.Player")
            )) {
            ilCursor.Emit(OpCodes.Ldarg_1).Emit(OpCodes.Ldarg_0).EmitDelegate<Func<Player, Scene, FlingBird, Player>>((player, scene, bird) => {
                if (ModSettings.Enabled && player != null && scene is Level level && level.Session.Area.ToString() == "10" &&
                    level.Session.Level == "j-16"
                    && scene.Entities.FindAll<FlingBird>().FirstOrDefault(flingBird => flingBird == bird) != null) {
                    for (int i = 0; i < bird.NodeSegments.Count; i++) {
                        if (player.X > bird.NodeSegments[i][0].X) {
                            continue;
                        }

                        if (i > 0) {
                            bird.segmentIndex = i;
                            bird.Add(new Coroutine(DelayPosition(bird, bird.NodeSegments[i][0])));
                        }

                        break;
                    }

                    return null;
                }

                return player;
            });
        }
    }

    private static void NPC06_Theo_PlateauOnAwake(ILContext il) {
        ILCursor ilCursor = new(il);
        if (!ilCursor.TryGotoNext(ins => ins.MatchCallvirt<Scene>("Add"))) {
            return;
        }

        Instruction skipCs06Campfire = ilCursor.Next.Next;
        if (!ilCursor.TryGotoPrev(MoveType.After, ins => ins.MatchCall<Entity>("Awake"))) {
            return;
        }

        Vector2 startPoint = new(-176, 312);
        ilCursor.EmitDelegate<Func<bool>>(() => {
            Session session = Engine.Scene.GetSession();
            bool skip = ModSettings.Enabled && session.GetFlag("campfire_chat") || session.RespawnPoint != startPoint;
            if (skip && Engine.Scene.GetLevel() is { } level && level.GetPlayer() is { } player
                && level.Entities.FindFirst<NPC06_Theo_Plateau>() is { } theo && level.Tracker.GetEntity<Bonfire>() is { } bonfire) {
                session.SetFlag("campfire_chat");
                level.Session.BloomBaseAdd = 1f;
                level.Bloom.Base = AreaData.Get(level).BloomBase + 1f;
                level.Session.Dreaming = true;
                level.Add(new StarJumpController());
                level.Add(new CS06_StarJumpEnd(theo, player, new Vector2(-4, 312), new Vector2(-184, 177.6818f)));
                level.Add(new FlyFeather(new Vector2(88, 256), shielded: false, singleUse: false));
                bonfire.Activated = false;
                bonfire.SetMode(Bonfire.Mode.Lit);
                theo.Position = new Vector2(-40, 312);
                theo.Sprite.Play("sleep");
                theo.Sprite.SetAnimationFrame(theo.Sprite.CurrentAnimationTotalFrames - 1);
                if (level.Session.RespawnPoint == startPoint) {
                    player.Position = new Vector2(-4, 312);
                    player.Facing = Facings.Left;
                }
            }

            return skip;
        });
        ilCursor.Emit(OpCodes.Brtrue, skipCs06Campfire);
    }

    private static void EngineOnUpdate(On.Monocle.Engine.orig_Update orig, Engine self, GameTime gameTime) {
        orig(self, gameTime);
        if (delayedOpenDebugMap) {
            delayedOpenDebugMap = false;
            if (Engine.Scene is Level level && Engine.NextScene is not MapEditor) {
                Engine.Scene = new MapEditor(level.Session.Area);
                Engine.Commands.Open = false;
            }
        }
    }

    // 过早修改位置会使其排在其它鸟的后面导致被删除
    private static IEnumerator DelayPosition(FlingBird bird, Vector2 position) {
        bird.Position = position;
        yield break;
    }

    public static void FixTeleportProblems(Session session, Vector2? startPosition) {
        if (ModSettings.Enabled && session.LevelData != null) {
            Vector2 spawnPoint;
            if (session.RespawnPoint.HasValue) {
                spawnPoint = session.RespawnPoint.Value;
            } else if (startPosition.HasValue) {
                spawnPoint = session.GetSpawnPoint(startPosition.Value);
            } else {
                Rectangle bounds = session.LevelData.Bounds;
                spawnPoint = session.GetSpawnPoint(new Vector2(bounds.Left, bounds.Bottom));
            }

            FixCoreMode(session);
            FixCoreRefillDash(session);
            FixBadelineChase(session, spawnPoint);
            FixHugeMessRoomLight(session);
            FixMirrorTempleColorGrade(session);
            FixReflectionBloomBase(session);
            FixFarewellCassetteRoomColorGrade(session, spawnPoint);
            FixFarewellDashes(session);
            FixFarewellSpawnPoint(session);
        }
    }

    private static void FixFarewellCassetteRoomColorGrade(Session session, Vector2 spawnPoint) {
        // Logger.Log("Exclude Respawn Point", $"new Vector2({spawnPoint.X}, {spawnPoint.Y}),");
        if (ExcludeFarewellCassettePoints.Contains(spawnPoint)) {
            return;
        }

        if (session.Area.ToString() == "10") {
            if (FarewellCassetteRooms.Contains(session.Level)) {
                session.ColorGrade = "feelingdown";
            } else if (session.Level == "end-golden") {
                session.ColorGrade = "golden";
            } else {
                session.ColorGrade = "none";
            }
        }
    }

    private static void FixFarewellSpawnPoint(Session session) {
        if (shouldFixFarewellSpawnPoint) {
            shouldFixFarewellSpawnPoint = false;
            session.RespawnPoint = new Vector2(28280, -8080);
        }
    }

    private static void FixFarewellDashes(Session session) {
        if (session.Area.ToString() == "10") {
            if (session.Level == "intro-02-launch") {
                session.Inventory.Dashes = 2;
            } else if (FarewellOneDashRooms.Contains(session.Level)) {
                session.Inventory.Dashes = 1;
            }
        }
    }

    private static void FixBadelineChase(Session session, Vector2 spawnPoint) {
        // Logger.Log("Exclude Respawn Point", $"new Vector2({spawnPoint.X}, {spawnPoint.Y}),");
        if (ExcludeDreamRespawnPoints.Contains(spawnPoint)) {
            return;
        }

        if (session.Area.ToString() == "2" && DreamDashRooms.Contains(session.Level)) {
            session.Inventory.DreamDash = true;

            // 根据 BadelineOldsite 的代码得知设置这两个 Flag 后才会启动追逐
            if (DreamDashRooms.IndexOf(session.Level) >= DreamDashRooms.IndexOf(StartChasingLevel)) {
                session.SetFlag(CS02_BadelineIntro.Flag);
            }

            session.LevelFlags.Add(StartChasingLevel);
        }
    }

    private static void FixHugeMessRoomLight(Session session) {
        if (session.Area.ToString() == "3") {
            if (DarkRooms.Contains(session.Level)) {
                session.LightingAlphaAdd = 0.15f;
            } else {
                session.LightingAlphaAdd = 0;
            }
        }
    }

    private static void FixMirrorTempleColorGrade(Session session) {
        if (session.Area.ToString() == "5") {
            session.ColorGrade = session.Level == "void" ? "templevoid" : null;
        }
    }

    private static void FixReflectionBloomBase(Session session) {
        if (session.Area.ToString() == "6") {
            session.BloomBaseAdd = session.Level == "start" ? 1f : 0f;
        }
    }

    private static void FixCoreMode(Session session) {
        if (session.Area.ID == 9) {
            session.CoreMode = CoreIceRooms.Contains(session.Area + session.Level) ? Session.CoreModes.Cold : Session.CoreModes.Hot;
        }
    }

    private static void FixCoreRefillDash(Session session) {
        if (session.Area.ID == 9 && ModSettings.FixCoreRefillDashAfterTeleport) {
            session.Inventory.NoRefills = !CoreRefillDashRooms.Contains(session.Level);
        }
    }
}