using FMOD.Studio;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class AudioAction : AbstractEntityAction {
        private static string muteSoundSourcePath;
        private static string muteAudioPath;

        public static void MuteSoundSource(string audioPath) {
            muteSoundSourcePath = audioPath;
        }

        public static void MuteAudio(string audioPath) {
            muteAudioPath = audioPath;
        }

        public override void OnQuickSave(Level level) {
        }

        public override void OnClear() {
            muteSoundSourcePath = null;
            muteAudioPath = null;
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
            if (path == muteSoundSourcePath) {
                muteSoundSourcePath = null;
                return null;
            }

            return orig(self, path, param, value);
        }

        private static EventInstance AudioOnPlayStringVector2(On.Celeste.Audio.orig_Play_string_Vector2 orig, string path,
            Vector2 position) {
            if (path == muteAudioPath) {
                muteAudioPath = null;
                return null;
            }

            return orig(path, position);
        }
    }
}