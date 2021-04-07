using System;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.DeathStatistics;
using Monocle;

namespace Celeste.Mod.SpeedrunTool {
    public class SpeedrunToolSaveData : EverestModuleSaveData {
        public List<DeathInfo> DeathInfos { get; set; } = new List<DeathInfo>();
        public int Selection { get; set; } = -1;

        public string GetTotalLostTime() {
            long total = DeathInfos.Sum(deathInfo => deathInfo.LostTime);

            TimeSpan totalLostTimeSpan = TimeSpan.FromTicks(total);
            if ((int) totalLostTimeSpan.TotalSeconds < 60) {
                return (int) totalLostTimeSpan.TotalSeconds + totalLostTimeSpan.ToString("\\.fff");
            }

            return totalLostTimeSpan.ShortGameplayFormat();
        }

        public int GetTotalDeathCount() => DeathInfos.Count;

        public void Add(DeathInfo deathInfo) {
            DeathInfos.Insert(0, deathInfo);
            int max = SpeedrunToolModule.Settings.MaxNumberOfDeathData * 10;
            if (max <= 0) {
                max = 0;
            }

            if (DeathInfos.Count > max) {
                DeathInfos.RemoveRange(max, DeathInfos.Count - max);
            }
        }

        public void Clear() {
            Selection = -1;
            DeathInfos.Clear();
            DeathStatisticsManager.Instance.Clear();
            Save();
        }

        public void SetSelection(int selection) {
            Selection = selection;
        }

        private static void Save() {
            SpeedrunToolModule.Instance.SaveSaveData(SpeedrunToolModule.SaveData.Index);
        }
    }
}