using MonoMod.ModInterop;

namespace Celeste.Mod.SpeedrunTool.ModInterop;

// requested by https://discord.com/channels/403698615446536203/908809001834274887/1416327417689145344
internal static class RoomTimerExport {

    [Load]
    private static void Initialize() {
        typeof(Exports).ModInterop();
    }


    [ModExportName("SpeedrunTool.RoomTimer")]
    internal static class Exports {
        public static bool RoomTimerIsCompleted() => RoomTimer.RoomTimerManager.Data_Auto.IsCompleted;
        public static long GetRoomTime() => RoomTimer.RoomTimerManager.Data_Auto.Time;
    }
}

