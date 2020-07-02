using System;

namespace Celeste.Mod.SpeedrunTool.Extensions {
    public static class LoggerExtensions {
        private const string TAG = "SpeedrunTool";

        public static void Log(this string message) {
            Logger.Log(TAG, message);
        }

        public static void LogDetail(this string message) {
            Logger.LogDetailed(TAG, message);
        }

        public static void LogDetail(this Exception e) {
            Logger.LogDetailed(e, TAG);
        }
    }
}