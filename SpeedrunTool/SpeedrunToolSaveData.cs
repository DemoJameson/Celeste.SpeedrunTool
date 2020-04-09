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
            if (SpeedrunToolModule.Settings.MaxNumberOfDeathData > 0 &&
                DeathInfos.Count > SpeedrunToolModule.Settings.MaxNumberOfDeathData) {
                DeathInfos.RemoveRange(SpeedrunToolModule.Settings.MaxNumberOfDeathData,
                    DeathInfos.Count - SpeedrunToolModule.Settings.MaxNumberOfDeathData);
            } else if (SpeedrunToolModule.Settings.MaxNumberOfDeathData == 0 && DeathInfos.Count > 200) {
                DeathInfos.RemoveRange(200, DeathInfos.Count - 200);
            }
        }

        public void Clear() {
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