using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class SoundSourceAction : ComponentAction {
        public static List<SoundSource> PausedSoundSources = new List<SoundSource>();
        public override void OnSaveSate(Level level) {
            foreach (SoundSource soundSource in level.Tracker.Components[typeof(SoundSource)].Cast<SoundSource>()) {
                soundSource.SavePlayingValue();
                soundSource.SaveTimelinePositionValue();
            }
        }

        public override void OnLoading(Level level, Player player, Player savedPlayer) {
            PausedSoundSources.ForEach(soundSource => soundSource.Resume());
        }

        public override void OnClear() {
            PausedSoundSources.Clear();
        }
    }

    public static class SoundSourceExtensions {
        private const string SoundSourcePlayingKey = "SoundSourcePlayingKey";
        private const string SoundSourceTimelinePositionKey = "SoundSourceTimelinePositionKey";

        public static bool LoadPlayingValue(this SoundSource soundSource) {
            return soundSource.GetExtendedBoolean(SoundSourcePlayingKey);
        }

        public static void SavePlayingValue(this SoundSource soundSource) {
            soundSource.SetExtendedBoolean(SoundSourcePlayingKey, soundSource.Playing);
        }
        
        public static int LoadTimelinePositionValue(this SoundSource soundSource) {
            return soundSource.GetExtendedInt(SoundSourceTimelinePositionKey);
        }

        public static void SaveTimelinePositionValue(this SoundSource soundSource) {
            object[] args = {0};
            soundSource.GetField("instance")?.InvokeMethod("getTimelinePosition", args);
            soundSource.SetExtendedInt(SoundSourceTimelinePositionKey, (int) args[0]);
        }
        
        public static void SetTime(this SoundSource soundSource, SoundSource otherSoundSource) {
            soundSource.GetField("instance")?.InvokeMethod("setTimelinePosition", otherSoundSource.LoadTimelinePositionValue());
        }
    }
}