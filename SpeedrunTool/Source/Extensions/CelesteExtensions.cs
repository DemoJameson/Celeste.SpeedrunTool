using System.Collections;
using System.Collections.Generic;
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
        if (coroutine.GetFieldValue("enumerators") is Stack<IEnumerator> {Count: > 0} enumerators) {
            return enumerators.Peek();
        } else {
            return null;
        }
    }

    public static bool IsMainThread(this Thread thread) {
        return thread == MainThreadHelper.MainThread;
    }
}