using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    // TODO 可能 SoundSource 里的 Params 或者其他参数也需要保存还原
    public class SoundSourceAction : ComponentAction {
        public static readonly List<SoundSource> PlayingSoundSources = new List<SoundSource>();
        // 因为 SoundSource 里的值会因为保存操作发生改变，所以需要提前读取再附加到对象中
        public override void OnSaveSate(Level level) {
            foreach (SoundSource soundSource in level.Tracker.Components[typeof(SoundSource)].Cast<SoundSource>()) {
                soundSource.SavePlayingValue();
                soundSource.SaveTimelinePositionValue();
            }
        }

        // 等待 Player 复活完毕后再重新播放声音
        public override void OnLoading(Level level, Player player, Player savedPlayer) {
            PlayingSoundSources.ForEach(soundSource => soundSource.Resume());
        }

        public override void OnClear() {
            PlayingSoundSources.Clear();
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