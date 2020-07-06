using Monocle;

namespace Celeste.Mod.SpeedrunTool.Extensions {
    public static class LoggerExtensions {
        private const string Tag = "SpeedrunTool";

        public static bool Log(this object message, object prefix = null, object suffix = null) {
            Logger.Log(Tag, Engine.Scene.RawTimeActive + " " + prefix + " " + message + " " + suffix);
            return true;
        }
    }
}