using System.Collections;
using System.Threading;

namespace Celeste.Mod.SpeedrunTool.Extensions;

internal static class CelesteExtensions {
    public static Level GetLevel(this Scene scene) {
        if (scene is Level level) {
            return level;
        }

        if (scene is LevelLoader levelLoader) {
            return levelLoader.Level;
        }

        return null;
    }

    public static Session GetSession(this Scene scene) {
        return scene.GetLevel()?.Session;
    }

    public static Player GetPlayer(this Scene scene) {
        if (scene.GetLevel()?.Tracker.GetEntity<Player>() is { } player) {
            return player;
        }

        return null;
    }

    public static bool IsPlayerDead(this Scene scene) {
        return scene.GetPlayer()?.Dead != false;
    }

    public static IEnumerator Current(this Coroutine coroutine) {
        if (coroutine.enumerators is {Count: > 0} enumerators) {
            return enumerators.Peek();
        } else {
            return null;
        }
    }

    public static bool IsMainThread(this Thread thread) {
        return thread == MainThreadHelper.MainThread;
    }

    public static bool IsCheck(this VirtualInput virtualInput) {
        return virtualInput switch {
            VirtualButton virtualButton => virtualButton.Check,
            VirtualIntegerAxis virtualIntegerAxis => virtualIntegerAxis.Value != 0,
            VirtualJoystick virtualJoystick => virtualJoystick.Value != Vector2.Zero,
            VirtualAxis virtualAxis => virtualAxis.Value != 0,
            _ => false
        };
    }

    public static bool IsPressed(this VirtualInput virtualInput) {
        return virtualInput switch {
            VirtualButton virtualButton => virtualButton.Pressed,
            VirtualIntegerAxis virtualIntegerAxis => virtualIntegerAxis.turned,
            VirtualJoystick virtualJoystick => virtualJoystick.hTurned || virtualJoystick.vTurned,
            _ => false
        };
    }
}