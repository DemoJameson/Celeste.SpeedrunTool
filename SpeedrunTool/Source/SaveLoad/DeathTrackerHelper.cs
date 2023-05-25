using System.Linq;
using Celeste.Mod.SpeedrunTool.Utils;
using Mono.Cecil.Cil;
using MonoMod.Cil;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;

internal static class DeathTrackerHelper {
    private static object generatedObject;

    [Initialize]
    private static void Initialize() {
        if (ModUtils.GetType("DeathTracker", "CelesteDeathTracker.DeathTrackerModule+<>c__DisplayClass6_0")?.GetMethodInfo("<Load>b__2") is not { } onPlayerSpawn) {
            return;
        }

        onPlayerSpawn.ILHook((cursor, _) => {
            cursor.Emit(OpCodes.Ldarg_0).EmitDelegate<Action<object>>(obj => generatedObject = obj);
        });
    }

    public static void AddSupport() {
        if (ModUtils.GetType("DeathTracker", "CelesteDeathTracker.DeathTrackerModule+<>c__DisplayClass6_0") is not { } generatedType) {
            return;
        }

        if (ModUtils.GetType("DeathTracker", "CelesteDeathTracker.DeathDisplay") is not { } deathDisplayType) {
            return;
        }

        if (generatedType.GetMethodInfo("<Load>b__2") is not { } onPlayerSpawn) {
            return;
        }

        onPlayerSpawn.ILHook((cursor, il) => {
            while (cursor.TryGotoNext(MoveType.After, i => i.MatchLdfld(generatedType, "display"))) {
                cursor.EmitDelegate<Func<object, object>>(display => Engine.Scene.GetLevel().Entities.FirstOrDefault(entity => entity.GetType() == deathDisplayType) ?? display);
            }
        });

        SaveLoadAction.SafeAdd(loadState: (_, level) => {
            if (!ModSettings.SaveTimeAndDeaths && generatedObject != null && level.GetPlayer() is {} player) {
                onPlayerSpawn.Invoke(generatedObject, new object[] {player});
            }
        });
    }
}