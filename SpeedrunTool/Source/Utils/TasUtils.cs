using Celeste.Mod.Helpers;
using Celeste.Mod.SpeedrunTool.Extensions;
using TAS;

namespace Celeste.Mod.SpeedrunTool.Utils {
    internal static class TasUtils {
        private static bool installed;
        private static bool running => Manager.Running;
        public static bool Running => installed && running;

        [Initialize]
        private static void Initialize() {
            installed = FakeAssembly.GetFakeEntryAssembly().GetType("TAS.Manager") != null;
        }
    }
}