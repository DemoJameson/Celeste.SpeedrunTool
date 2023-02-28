#pragma warning disable CS0649

using System.Reflection;
using Celeste.Mod.SpeedrunTool.Utils;
using Mono.Cecil.Cil;
using MonoMod.Cil;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;

internal static class StrawberryJamUtils {
    private static float currentOldFreezeTimer;
    private static float? savedOldFreezeTimer;
    private static float? loadOldFreezeTimer;

    private static readonly Lazy<MethodInfo> EngineUpdate = new(() =>
        ModUtils.GetType("StrawberryJam2021", "Celeste.Mod.StrawberryJam2021.Entities.WonkyCassetteBlockController")?.GetMethodInfo("Engine_Update"));

    private static bool hooked;

    [Initialize]
    private static void Initialize() {
        EngineUpdate.Value?.ILHook((cursor, _) => {
            int localIndex = 0;
            if (cursor.TryGotoNext(MoveType.Before, i => i.MatchLdsfld<Engine>("FreezeTimer"), i => i.MatchStloc(out localIndex))) {
                cursor.Index++;
                cursor.Emit(OpCodes.Dup).Emit(OpCodes.Stsfld,
                    typeof(StrawberryJamUtils).GetFieldInfo(nameof(currentOldFreezeTimer)));

                if (cursor.TryGotoNext(MoveType.After, i => i.MatchLdloc(localIndex))) {
                    cursor.EmitDelegate(RestoreOldFreezeTimer);
                    hooked = true;
                }
            }
        });
    }

    private static float RestoreOldFreezeTimer(float oldFreezeTimer) {
        if (loadOldFreezeTimer is { } value) {
            loadOldFreezeTimer = null;
            return value;
        } else {
            return oldFreezeTimer;
        }
    }

    public static void AddSupport() {
        if (hooked) {
            SaveLoadAction.SafeAdd(
                (_, _) => savedOldFreezeTimer = currentOldFreezeTimer,
                (_, _) => loadOldFreezeTimer = savedOldFreezeTimer,
                () => savedOldFreezeTimer = null
            );
        }
    }
}