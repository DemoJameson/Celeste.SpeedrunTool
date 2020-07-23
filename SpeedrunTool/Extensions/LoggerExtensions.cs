using System;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.Extensions {
    public static class LoggerExtensions {
        private const string Tag = "SpeedrunTool";

        // TODO 增加记录信息到指定文件的功能，以便后台记录自己想要的调试信息
        public static void Log(this object message, LogLevel logLevel = LogLevel.Warn) {
            string levelInfo = "";
            if (Engine.Scene.GetSession() is Session session) {
                levelInfo += $"[{session.Area.SID} {session.Level}] ";
            }

            string frames = "";
            if (Engine.Scene != null) {
                frames = "[" + (int) Math.Round(Engine.Scene.RawTimeActive / 0.0166667) + "] ";
            }

            Logger.Log(Tag, $"{levelInfo}{frames}{message}");
        }

        public static void DebugLog(this object message, LogLevel logLevel = LogLevel.Info) {
#if DEBUG
            message.Log(logLevel);
#endif
        }
    }
}