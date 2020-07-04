namespace Celeste.Mod.SpeedrunTool.Extensions {
    public static class LoggerExtensions {
        private const string Tag = "SpeedrunTool";

        public static bool Log(this object message, string prefix = "", string suffix = "") {
            Logger.Log(Tag, prefix + " " + message + " " + suffix);
            return true;
        }
    }
}