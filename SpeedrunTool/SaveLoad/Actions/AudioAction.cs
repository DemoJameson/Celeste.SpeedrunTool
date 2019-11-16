using System.Collections.Generic;
using FMOD.Studio;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class AudioAction : AbstractEntityAction {
        private static readonly List<string> MuteSoundSourcePaths = new List<string>();
        private static readonly List<string> MuteAudioPaths = new List<string>();

        public static void MuteSoundSource(string audioPath) {
            MuteSoundSourcePaths.Add(audioPath);
        }

        public static void MuteAudio(string audioPath) {
            MuteAudioPaths.Add(audioPath);
        }

        public override void OnQuickSave(Level level) {
        }

        public override void OnClear() {
            MuteSoundSourcePaths.Clear();
            MuteAudioPaths.Clear();
        }

        public override void OnLoad() {
            On.Celeste.SoundSource.Play += SoundSourceOnPlay;
            On.Celeste.Audio.Play_string_Vector2 += AudioOnPlayStringVector2;
        }

        public override void OnUnload() {
            On.Celeste.SoundSource.Play -= SoundSourceOnPlay;
            On.Celeste.Audio.Play_string_Vector2 -= AudioOnPlayStringVector2;
        }

        private static SoundSource SoundSourceOnPlay(On.Celeste.SoundSource.orig_Play orig, SoundSource self, string path,
            string param, float value) {
            if (MuteSoundSourcePaths.Contains(path)) {
                MuteSoundSourcePaths.Remove(path);
                return null;
            }

            return orig(self, path, param, value);
        }

        private static EventInstance AudioOnPlayStringVector2(On.Celeste.Audio.orig_Play_string_Vector2 orig, string path,
            Vector2 position) {
            if (MuteAudioPaths.Contains(path)) {
                MuteAudioPaths.Remove(path);
                return null;
            }

            return orig(path, position);
        }
    }
}