
namespace Celeste.Mod.SpeedrunTool.RoomTimer;

internal static class TimeSpanFix {

    // taken from CelesteTAS
    public static long SecondsToTicks(this float seconds) {
        // .NET Framework rounded TimeSpan.FromSeconds to the nearest millisecond.
        // See: https://github.com/EverestAPI/Everest/blob/dev/NETCoreifier/Patches/TimeSpan.cs
        double millis = seconds * 1000 + (seconds >= 0 ? +0.5 : -0.5);
        return (long)millis * TimeSpan.TicksPerMillisecond;
    }
}
