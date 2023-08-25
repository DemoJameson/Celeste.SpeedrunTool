using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Celeste.Mod.SpeedrunTool.Message;
using Celeste.Mod.SpeedrunTool.Utils;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;

namespace Celeste.Mod.SpeedrunTool.Other;

public static class LookoutBuilder {
    private static bool UnlockCamera => ModSettings.UnlockCamera && PortableLookout.Exists;

    [Load]
    private static void Load() {
        On.Celeste.Player.Die += PlayerOnDie;
        IL.Celeste.RisingLava.OnPlayer += EnableInvincible;
        IL.Celeste.SandwichLava.OnPlayer += EnableInvincible;
        IL.Celeste.Lookout.Hud.Update += HudOnUpdate;
        On.Celeste.Lookout.Interact += LookoutOnInteract;
        MethodInfo lookRoutineMethod = typeof(Lookout).GetMethodInfo("LookRoutine").GetStateMachineTarget();
        lookRoutineMethod.ILHook(ModLookRoutine);
        lookRoutineMethod.ILHook(DisableLookoutBlocker);
        Hotkey.SpawnTowerViewer.RegisterPressedAction(OnHotkeyPressed);
    }

    [Unload]
    private static void Unload() {
        On.Celeste.Player.Die -= PlayerOnDie;
        IL.Celeste.RisingLava.OnPlayer -= EnableInvincible;
        IL.Celeste.SandwichLava.OnPlayer -= EnableInvincible;
        IL.Celeste.Lookout.Hud.Update -= HudOnUpdate;
        On.Celeste.Lookout.Interact -= LookoutOnInteract;
    }

    private static PlayerDeadBody PlayerOnDie(On.Celeste.Player.orig_Die orig, Player self, Vector2 direction, bool evenIfInvincible,
        bool registerDeathInStats) {
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

    private static void HudOnUpdate(ILContext ilContext) {
        ILCursor ilCursor = new(ilContext);
        while (ilCursor.TryGotoNext(MoveType.After,
                   ins => ins.OpCode == OpCodes.Callvirt && ins.Operand.ToString().Contains("CollideCheck<Celeste.LookoutBlocker>"))) {
            ilCursor.EmitDelegate<Func<bool, bool>>(collide => UnlockCamera ? false : collide);
        }
    }

    private static void LookoutOnInteract(On.Celeste.Lookout.orig_Interact orig, Lookout self, Player player) {
        if (player.SceneAs<Level>() is { } level) {
            DynamicData dynamicData = new(player);
            if (dynamicData.Get("SpeedrunTool_SavedCameraLockMode") is Level.CameraLockModes cameraLockMode) {
                level.CameraLockMode = cameraLockMode;
                dynamicData.Set("SpeedrunTool_SavedCameraLockMode", null);
            }

            if (dynamicData.Get("SpeedrunTool_SavedCameraPosition") is Vector2 cameraPosition) {
                level.Camera.position = cameraPosition;
                dynamicData.Set("SpeedrunTool_SavedCameraPosition", null);
            }

            if (dynamicData.Get("SpeedrunTool_RestoreCameraCoroutine") is Coroutine coroutine) {
                player.Remove(coroutine);
                dynamicData.Set("SpeedrunTool_RestoreCameraCoroutine", null);
            }
        }

        orig(self, player);
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

    private static void DisableLookoutBlocker(ILCursor ilCursor, ILContext ilContext) {
        if (ilCursor.TryGotoNext(MoveType.After,
                ins => ins.OpCode == OpCodes.Callvirt && ins.Operand.ToString().EndsWith("GetEntities<Celeste.LookoutBlocker>()"))) {
            ilCursor.EmitDelegate<Func<List<Entity>, List<Entity>>>(list => UnlockCamera ? new List<Entity>() : list);
        }
    }

    private static void OnHotkeyPressed(Scene scene) {
        if (scene is not Level level) {
            return;
        }

        if (level.GetPlayer() is not { } player) {
            return;
        }

        if (level.Tracker.GetEntity<PortableLookout>() is { } portableLookout) {
            if (portableLookout.interacting && !portableLookout.sprite.CurrentAnimationID.EndsWith("idle")) {
                ToggleUnblockCamera();
            }

            return;
        }

        if (level.Paused || level.Transitioning || level.InCutscene || level.SkippingCutscene || player.Dead || !player.InControl) {
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
    }

    private static void ToggleUnblockCamera() {
        ModSettings.UnlockCamera = !ModSettings.UnlockCamera;
        SpeedrunToolModule.Instance.SaveSettings();
        string state = (ModSettings.UnlockCamera ? DialogIds.On : DialogIds.Off).DialogClean();
        string message = string.Format(Dialog.Get(DialogIds.OptionState), DialogIds.UnlockCamera.DialogClean(), state);
        Tooltip.Show(message);
    }

    private static IEnumerator Look(PortableLookout lookout) {
        Player player = Engine.Scene.Tracker.GetEntity<Player>();
        if (player?.Scene == null || player.Dead) {
            yield break;
        }

        lookout.Interact(player);

        Level level = player.SceneAs<Level>();
        Level.CameraLockModes savedCameraLockMode = level.CameraLockMode;
        Vector2 savedCameraPosition = player.CameraTarget;
        level.CameraLockMode = Level.CameraLockModes.None;

        Entity underfootPlatform = player.CollideFirstOutside<FloatySpaceBlock>(player.Position + Vector2.UnitY);

        while (!lookout.interacting) {
            player.Position = lookout.Position;
            yield return null;
        }

        while (lookout.interacting) {
            player.Position = lookout.Position;
            yield return null;
        }

        lookout.Collidable = lookout.Visible = false;

        if (underfootPlatform != null) {
            player.Position.Y = underfootPlatform.Top;
        }

        Coroutine coroutine = new(RestoreCamera(level, player, savedCameraLockMode, savedCameraPosition));
        player.Add(coroutine);
        lookout.RemoveSelf();

        DynamicData dynamicData = new(player);
        dynamicData.Set("SpeedrunTool_SavedCameraLockMode", savedCameraLockMode);
        dynamicData.Set("SpeedrunTool_SavedCameraPosition", savedCameraPosition);
        dynamicData.Set("SpeedrunTool_RestoreCameraCoroutine", coroutine);
    }

    private static IEnumerator RestoreCamera(Level level, Player player, Level.CameraLockModes cameraLockMode, Vector2 cameraPosition) {
        Camera camera = level.Camera;

        while (Vector2.Distance(camera.Position, cameraPosition) > 5f) {
            yield return null;
        }

        float ease = 0f;
        while (true) {
            ease = Math.Min(1f, ease + 1f/20);
            camera.Approach(cameraPosition, Ease.CubeInOut(ease));
            if (Vector2.Distance(camera.Position, cameraPosition) < 1f) {
                break;
            }

            yield return null;
        }

        camera.Position = cameraPosition;
        level.CameraLockMode = cameraLockMode;

        DynamicData dynamicData = new(player);
        dynamicData.Set("SpeedrunTool_SavedCameraLockMode", null);
        dynamicData.Set("SpeedrunTool_SavedCameraPosition", null);
        dynamicData.Set("SpeedrunTool_RestoreCameraCoroutine", null);
    }

    [Tracked]
    [TrackedAs(typeof(Lookout))]
    private class PortableLookout : Lookout {
        internal PortableLookout(EntityData data, Vector2 offset) : base(data, offset) { }
        internal static bool Exists => Engine.Scene.Tracker.GetEntity<PortableLookout>() != null;
    }
}