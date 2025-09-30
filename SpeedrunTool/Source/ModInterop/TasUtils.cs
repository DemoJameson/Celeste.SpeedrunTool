using Celeste.Mod.SpeedrunTool.Utils;
using MonoMod.ModInterop;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Celeste.Mod.SpeedrunTool.ModInterop;


internal static class TasUtils {

    public static bool HideGamePlay => LegacyOrUnstableTasImports.HideGamePlay;
    public static bool Running => TasImports.Installed ? TasImports.ManagerIsRunning : LegacyOrUnstableTasImports.LegacyRunning;

    public static int GroupCounter {
        get => CelesteTasImports.GetGroupCounter?.Invoke() ?? 0;
        set {
            CelesteTasImports.SetGroupCounter?.Invoke(value);
        }
    }

    internal static class TasImports {

        internal static bool Installed = false;

        [Initialize]
        public static void Initialize() {
            typeof(CelesteTasImports).ModInterop();
            Installed = CelesteTasImports.IsTasActive is not null;
        }

        public static bool ManagerIsRunning => Installed && CelesteTasImports.IsTasActive();

    }
    private static class LegacyOrUnstableTasImports {

        internal static bool HideGamePlay => hasGameplay && !showGamePlay;

        private static bool hasGameplay;

        private static bool showGamePlay {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get => TAS.Module.CelesteTasSettings.Instance?.ShowGameplay ?? true;
        }
        internal static bool LegacyRunning {
            get {
                if (hasRunning_BeforeModInterop) {
                    // v3.42 - 3.44
                    return (bool)running_BeforeModInterop.GetValue(null);
                }
                if (hasRunning_Legacy) {
                    // < v3.42
                    return (bool)running_LegacyFieldInfo.GetValue(null);
                }
                return false;
            }
        }


        private static bool hasRunning_BeforeModInterop; // CelesteTAS >= v3.42.0

        private static bool hasRunning_Legacy; // CelesteTAS < v3.42.0
                                               // some people are still using CelesteTAS 3.39

        private static FieldInfo running_LegacyFieldInfo;

        private static PropertyInfo running_BeforeModInterop; // it's a property instead of a field now


        [Initialize]
        private static void Initialize() {
            hasGameplay = ModUtils.GetType("CelesteTAS", "TAS.Module.CelesteTasSettings")?.GetPropertyInfo("ShowGameplay") != null;
            running_BeforeModInterop = ModUtils.GetType("CelesteTAS", "TAS.Manager")?.GetPropertyInfo("Running");
            hasRunning_BeforeModInterop = running_BeforeModInterop != null;
            running_LegacyFieldInfo = ModUtils.GetType("CelesteTAS", "TAS.Manager")?.GetFieldInfo("Running");
            hasRunning_Legacy = running_LegacyFieldInfo != null;
        }
    }
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

    #region GroupCounter

    public static Func<int> GetGroupCounter = null!;

    public static Action<int> SetGroupCounter = null!;

    #endregion
}