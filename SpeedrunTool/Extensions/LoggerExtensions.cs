using System;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.Extensions {
    public static class LoggerExtensions {
        private const string Tag = "SpeedrunTool";

        // TODO 增加记录信息到指定文件的功能，以便后台记录自己想要的调试信息
        public static void DebugLog(this object message, object prefix = null, object suffix = null) {
            int frames = 0;
            if (Engine.Scene != null) {
                frames = (int) Math.Round(Engine.Scene.RawTimeActive / 0.0166667);
            }

            DateTime now = DateTime.Now;
            Logger.Log(Tag,
                $"Time: {now.ToLongTimeString()}.{now.Millisecond}\tFrame: {frames}\t{prefix}\t{message}\t{suffix}");
        }
    }
}