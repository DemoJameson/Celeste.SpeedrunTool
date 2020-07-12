using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions {
    public class SoundSourceAction : RestoreAction {
        public static readonly List<SoundSource> PlayingSoundSources = new List<SoundSource>();
        public SoundSourceAction() : base(typeof(Entity)) { }

        // 因为 SoundSource 里的值会因为保存操作发生改变，所以需要提前读取再附加到对象中
        public override void OnSaveState(Level level) {
            foreach (SoundSource soundSource in level.Tracker.Components[typeof(SoundSource)].Cast<SoundSource>()) {
                soundSource.SavePlayingValue();
                soundSource.SaveTimelinePositionValue();
            }
        }

        // 等待 Player 复活完毕后再重新播放声音
        public override void OnLoadComplete(Level level) {
            PlayingSoundSources.ForEach(soundSource => soundSource.Resume());
        }

        public override void OnLoadStart(Level level) {
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