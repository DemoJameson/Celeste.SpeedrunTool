using MonoMod.ModInterop;

namespace Celeste.Mod.SpeedrunTool.ModInterop;


internal static class TasImports {

    internal static bool Installed = false;

    [Initialize]
    public static void Initialize() {
        typeof(CelesteTasImports).ModInterop();
        Installed = CelesteTasImports.IsTasActive is not null;
    }

    public static bool ManagerIsRunning => Installed && CelesteTasImports.IsTasActive();
}


[ModImportName("CelesteTAS")]
internal static class CelesteTasImports {
    public delegate void AddSettingsRestoreHandlerDelegate(EverestModule module, (Func<object> Backup, Action<object> Restore)? handler);
    public delegate void RemoveSettingsRestoreHandlerDelegate(EverestModule module);
    public delegate void DrawAccurateLineDelegate(Vector2 from, Vector2 to, Color color);

    /// Checks if a TAS is active (i.e. running / paused / etc.)
    public static Func<bool> IsTasActive = null!;

    /// Checks if a TAS is currently actively running (i.e. not paused)
    public static Func<bool> IsTasRunning = null!;

    /// Checks if the current TAS is being recorded with TAS Recorder
    public static Func<bool> IsTasRecording = null!;

    /// Registers custom delegates for backing up and restoring mod setting before / after running a TAS
    /// A `null` handler causes the settings to not be backed up and later restored
    public static AddSettingsRestoreHandlerDelegate AddSettingsRestoreHandler = null!;

    /// De-registers a previously registered handler for the module
    public static RemoveSettingsRestoreHandlerDelegate RemoveSettingsRestoreHandler = null!;
}