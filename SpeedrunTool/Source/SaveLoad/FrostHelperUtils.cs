using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Utils;
using Force.DeepCloner;
using Force.DeepCloner.Helpers;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;

internal static class FrostHelperUtils {
    private static readonly Lazy<Type> AttachedDataHelperType = new(() =>
        ModUtils.GetType("FrostHelper", "FrostHelper.Helpers.AttachedDataHelper"));

    private static readonly Lazy<Func<object, object[]>> GetAllData = new(() =>
        (Func<object, object[]>)AttachedDataHelperType.Value?.GetMethodInfo("GetAllData")?.CreateDelegate(typeof(Func<object, object[]>)));

    private static readonly Lazy<Action<object, object[]>> SetAllData = new(() =>
        (Action<object, object[]>)AttachedDataHelperType.Value?.GetMethodInfo("SetAllData")?.CreateDelegate(typeof(Action<object, object[]>)));

    public static void CloneDataStore(object sourceObj, object clonedObj, DeepCloneState deepCloneState) {
        if (GetAllData.Value == null || SetAllData.Value == null) {
            return;
        }

        if (GetAllData.Value(sourceObj) is { } data) {
            SetAllData.Value(clonedObj, data.DeepClone(deepCloneState));
        }
    }

    public static void SupportFrostHelper() {
        if (AttachedDataHelperType.Value != null && GetAllData.Value == null
            && AttachedDataHelperType.Value.GetMethodInfo("SetAttached") is { } setAttached
            && ModUtils.GetType("FrostHelper", "FrostHelper.Entities.Boosters.GenericCustomBooster") is { } genericCustomBoosterType
            && genericCustomBoosterType.GetMethodInfo("GetBoosterThatIsBoostingPlayer") is { } getBoosterThatIsBoostingPlayer
           ) {
            setAttached = setAttached.MakeGenericMethod(genericCustomBoosterType);

            SaveLoadAction.SafeAdd(
                saveState: (values, level) => {
                    Dictionary<string, object> dict = new();
                    List<Entity> players = level.Tracker.GetEntities<Player>();
                    List<object> boosters = players.Select(player => getBoosterThatIsBoostingPlayer.Invoke(null, new object[] {player})).ToList();
                    dict["players"] = players;
                    dict["boosters"] = boosters;
                    values[genericCustomBoosterType] = dict.DeepCloneShared();
                },
                loadState: (values, level) => {
                    Dictionary<string, object> dict = values[genericCustomBoosterType].DeepCloneShared();
                    if (dict.TryGetValue("players", out object players) && dict.TryGetValue("boosters", out object boosters)) {
                        if (players is List<Entity> playerList && boosters is List<object> boosterList) {
                            for (int i = 0; i < playerList.Count; i++) {
                                setAttached.Invoke(null, new[] {playerList[i], boosterList[i]});
                            }
                        }
                    }
                });
        }

        if (ModUtils.GetType("FrostHelper", "FrostHelper.ChangeDashSpeedOnce") is { } changeDashSpeedOnceType) {
            SaveLoadAction.SafeAdd(
                (savedValues, _) => SaveLoadAction.SaveStaticMemberValues(savedValues, changeDashSpeedOnceType, "NextDashSpeed", "NextSuperJumpSpeed"),
                (savedValues, _) => SaveLoadAction.LoadStaticMemberValues(savedValues));
        }
        
        if (ModUtils.GetType("FrostHelper", "FrostHelper.TimeBasedClimbBlocker ") is { } timeBasedClimbBlockerType) {
            SaveLoadAction.SafeAdd(
                (savedValues, _) => SaveLoadAction.SaveStaticMemberValues(savedValues, timeBasedClimbBlockerType, "_NoClimbTimer"),
                (savedValues, _) => SaveLoadAction.LoadStaticMemberValues(savedValues));
        }
    }
}