using System.Collections.Generic;
using System.IO;
using System.Linq;
using Celeste.Mod.SpeedrunTool.DeathStatistics;

namespace Celeste.Mod.SpeedrunTool;

public class SpeedrunToolSaveData : EverestModuleSaveData {
    public List<DeathInfo> DeathInfos { get; set; } = new();
    public int Selection { get; set; } = -1;

    public string GetTotalLostTime() {
        long total = DeathInfos.Sum(deathInfo => deathInfo.LostTime);

        TimeSpan totalLostTimeSpan = TimeSpan.FromTicks(total);
        if ((int)totalLostTimeSpan.TotalSeconds < 60) {
            return (int)totalLostTimeSpan.TotalSeconds + totalLostTimeSpan.ToString("\\.fff");
        }

        return totalLostTimeSpan.ShortGameplayFormat();
    }

    public int GetTotalDeathCount() => DeathInfos.Count;

    public void Add(DeathInfo deathInfo) {
        DeathInfos.Insert(0, deathInfo);
        int max = ModSettings.MaxNumberOfDeathData * 10;
        if (max <= 0) {
            max = 0;
        }

        if (DeathInfos.Count > max) {
            for (int i = max; i < DeathInfos.Count; i++) {
                if (File.Exists(DeathInfos[i].PlaybackFilePath)) {
                    File.Delete(DeathInfos[i].PlaybackFilePath);
                }
            }

            DeathInfos.RemoveRange(max, DeathInfos.Count - max);
        }
    }

    public void Clear() {
        Selection = -1;
        DeathInfos.Clear();
        DeathStatisticsManager.Clear();
        if (Directory.Exists(DeathStatisticsManager.PlaybackSlotDir)) {
            Directory.Delete(DeathStatisticsManager.PlaybackSlotDir, true);
        }
    }

    public void SetSelection(int selection) {
        Selection = selection;
    }
}