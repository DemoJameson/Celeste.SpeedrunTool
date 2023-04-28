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

    [Initialize]
    private static void Initialize() {
        if (!Directory.Exists(DeathStatisticsManager.PlaybackDir)) {
            return;
        }

        int max = ModSettings.MaxNumberOfDeathData * 10;
        string[] saveFiles = Directory.GetFiles(DeathStatisticsManager.SavePath);
        foreach (string directory in Directory.GetDirectories(DeathStatisticsManager.PlaybackDir)) {
            String directoryName = Path.GetFileName(directory);
            String saveFileName = new DirectoryInfo(directory).Name + ".celeste";
            if (saveFileName == "-1.celeste") {
                saveFileName = "debug.celeste";
            }

            if (saveFiles.Any(file => file.EndsWith(saveFileName))) {
                List<string> files = Directory.GetFiles(directory).ToList();
                files.Sort((a, b) => string.Compare(b, a, StringComparison.Ordinal));
                foreach (string path in files.Skip(max)) {
                    File.Delete(path);
                }
            } else if (int.TryParse(directoryName, out _)) {
                Directory.Delete(directory, true);
            }
        }
    }

    public void Add(DeathInfo deathInfo) {
        DeathInfos.Insert(0, deathInfo);
        int max = ModSettings.MaxNumberOfDeathData * 10;
        if (max <= 0) {
            max = 0;
        }

        if (DeathInfos.Count > max) {
            DeathInfos.RemoveRange(max, DeathInfos.Count - max);

            foreach (string filePath in Directory.GetFiles(DeathStatisticsManager.PlaybackSlotDir)) {
                if (!DeathInfos.Exists(info => info.PlaybackFilePath == filePath)) {
                    File.Delete(filePath);
                }
            }
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