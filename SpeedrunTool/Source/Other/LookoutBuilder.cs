using System.Collections;
using System.Reflection;
using Celeste.Mod.SpeedrunTool.Utils;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;

namespace Celeste.Mod.SpeedrunTool.Other;

public static class LookoutBuilder {
    private static readonly MethodInfo InteractMethod = typeof(Lookout).GetMethodInfo("Interact");
    private static readonly FieldInfo InteractingField = typeof(Lookout).GetFieldInfo("interacting");

    [Load]
    private static void Load() {
        On.Celeste.Player.Die += PlayerOnDie;
        IL.Celeste.RisingLava.OnPlayer += EnableInvincible;
        IL.Celeste.SandwichLava.OnPlayer += EnableInvincible;
        typeof(Lookout).GetMethodInfo("LookRoutine").GetStateMachineTarget().ILHook(ModLookRoutine);
        Hotkey.SpawnTowerViewer.RegisterPressedAction(OnHotkeyPressed);
    }

    [Unload]
    private static void Unload() {
        On.Celeste.Player.Die -= PlayerOnDie;
        IL.Celeste.RisingLava.OnPlayer -= EnableInvincible;
        IL.Celeste.SandwichLava.OnPlayer -= EnableInvincible;
    }

    private static PlayerDeadBody PlayerOnDie(On.Celeste.Player.orig_Die orig, Player self, Vector2 direction, bool evenIfInvincible, bool registerDeathInStats) {
        // 后面的条件是避免无法通过菜单重试
        if (PortableLookout.Exists && !(evenIfInvincible && registerDeathInStats)) {
            return null;
        }

        return orig(self, direction, evenIfInvincible, registerDeathInStats);
    }

    private static void EnableInvincible(ILContext il) {
        ILCursor ilCursor = new(il);
        if (ilCursor.TryGotoNext(MoveType.After, ins => ins.MatchLdfld<Assists>("Invincible"))) {
            ilCursor.EmitDelegate<Func<bool, bool>>(invincible => PortableLookout.Exists || invincible);
        }
    }

    private static void ModLookRoutine(ILCursor ilCursor, ILContext il) {
        if (ilCursor.TryGotoNext(MoveType.After, ins => ins.MatchCallvirt<Actor>("OnGround"))) {
            ilCursor.EmitDelegate<Func<bool, bool>>(onGround => PortableLookout.Exists || onGround);
        }

        SpeedUp("<accel>");
        SpeedUp("<maxspd>");

        void SpeedUp(string fieldName) {
            if (ilCursor.TryGotoNext(ins => ins.OpCode == OpCodes.Stfld && ins.Operand.ToString().Contains(fieldName))) {
                ilCursor.EmitDelegate<Func<float, float>>(speed => {
                    if (PortableLookout.Exists) {
                        return speed * 2;
                    } else {
                        return speed;
                    }
                });
            }
        }
    }

    private static void OnHotkeyPressed(Scene scene) {
        if (scene is not Level level) {
            return;
        }

        Player player = level.Tracker.GetEntity<Player>();
        if (player == null) {
            return;
        }

        if (level.Paused || level.Transitioning || level.InCutscene || level.SkippingCutscene || player.Dead || !player.InControl) {
            return;
        }

        if (PortableLookout.Exists) {
            return;
        }

        PortableLookout lookout = new(new EntityData {
            Position = player.Position,
            Level = level.Session.LevelData,
            Name = "towerviewer",
            ID = 1234567890
        }, Vector2.Zero);
        lookout.Add(new Coroutine(Look(lookout)));
        level.Add(lookout);

        // 恢复 LookoutBlocker 后用普通望远镜可能会卡住，因为镜头没有完全还原例如 10 j-16，所以干脆不恢复 LookoutBlocker
        level.Remove(level.Tracker.GetEntitiesCopy<LookoutBlocker>());
    }

    private static IEnumerator Look(PortableLookout lookout) {
        Player player = Engine.Scene.Tracker.GetEntity<Player>();
        if (player?.Scene == null || player.Dead) {
            yield break;
        }

        InteractMethod?.Invoke(lookout, new object[] {player});

        Level level = player.SceneAs<Level>();
        Level.CameraLockModes savedCameraLockMode = level.CameraLockMode;
        Vector2 savedCameraPosition = level.Camera.Position;
        level.CameraLockMode = Level.CameraLockModes.None;

        Entity underfootPlatform = player.CollideFirstOutside<FloatySpaceBlock>(player.Position + Vector2.UnitY);

        bool interacting = (bool)InteractingField.GetValue(lookout);
        while (!interacting) {
            player.Position = lookout.Position;
            interacting = (bool)InteractingField.GetValue(lookout);
            yield return null;
        }

        while (interacting) {
            player.Position = lookout.Position;
            interacting = (bool)InteractingField.GetValue(lookout);
            yield return null;
        }

        lookout.Collidable = lookout.Visible = false;

        if (underfootPlatform != null) {
            player.Position.Y = underfootPlatform.Top;
        }

        player.Add(new Coroutine(RestoreCameraLockMode(level, savedCameraLockMode, savedCameraPosition)));

        lookout.RemoveSelf();
    }

    private static IEnumerator RestoreCameraLockMode(Level level, Level.CameraLockModes cameraLockMode,
        Vector2 cameraPosition) {
        while (Vector2.Distance(level.Camera.Position, cameraPosition) > 1f) {
            yield return null;
        }

        level.CameraLockMode = cameraLockMode;
    }

    [Tracked]
    [TrackedAs(typeof(Lookout))]
    private class PortableLookout : Lookout {
        internal PortableLookout(EntityData data, Vector2 offset) : base(data, offset) { }
        internal static bool Exists => Engine.Scene.Tracker.GetEntity<PortableLookout>() != null;
    }
}