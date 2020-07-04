using System;

namespace Celeste.Mod.SpeedrunTool.Extensions {
    public static class LoggerExtensions {
        private const string Tag = "SpeedrunTool";

        public static bool Log(this object message, string prefix = "", string suffix = "") {
            Logger.Log(Tag,  prefix + message + suffix);
            return true;
        }

        public static void LogDetail(this object message, string prefix = "", string suffix = "") {
            Logger.LogDetailed(Tag, prefix + message + suffix);
        }

        public static void LogDetail(this Exception e) {
            Logger.LogDetailed(e, Tag);
        }
    }
}