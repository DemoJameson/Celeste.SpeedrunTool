#if DEBUG
#endif

namespace Celeste.Mod.SpeedrunTool.Extensions {
    internal static class LoggerExtensions {
        private const string Tag = "SpeedrunTool";

        public static void Log(this object message, LogLevel logLevel = LogLevel.Warn) {
            string levelInfo = "";
            if (Engine.Scene.GetSession() is { } session) {
                levelInfo += $"[{session.Area.SID} {session.Level}] ";
            }

            string frames = "";
            if (Engine.Scene != null) {
                frames = "[" + (int) Math.Round(Engine.Scene.RawTimeActive / 0.0166667) + "] ";
            }

            Logger.Log(logLevel, Tag, $"{levelInfo}{frames}{message}");
        }

#if DEBUG
        public static void DebugLog(this object message, LogLevel logLevel = LogLevel.Info) {
            message.Log(logLevel);
            Color color;
            switch (logLevel) {
                case LogLevel.Warn:
                    color = Color.Yellow;
                    break;
                case LogLevel.Error:
                    color = Color.Red;
                    break;
                default:
                    color = Color.Cyan;
                    break;
            }

            try {
                Engine.Commands?.Log($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{Tag}] {logLevel}: {message}", color);
            } catch (Exception) {
                // ignored
            }
        }
#endif
    }
}