using System;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.Extensions {
    public static class LoggerExtensions {
        private const string Tag = "SpeedrunTool";

        public static bool DebugLog(this object message, object prefix = null, object suffix = null) {
#if DEBUG
            DateTime now = DateTime.Now;
            Logger.Log(Tag,
                $"Time: {now.ToShortTimeString()}.{now.Millisecond}\tFrame: {Math.Round(Engine.Scene.RawTimeActive / 0.0166667)}\t{prefix}\t{message}\t{suffix}");
#endif
            return true;
        }
    }
}