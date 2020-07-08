using System.Collections.Generic;
using FMOD.Studio;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions.Deprecated {
    public class AudioAction : ComponentAction {
        private static readonly List<string> MuteSoundSourcePathList = new List<string>();
        private static readonly List<string> MuteAudioPathVector2List = new List<string>();
        private static readonly List<string> MuteAudioPathList = new List<string>();

        public static void MuteSoundSource(string audioPath) {
            MuteSoundSourcePathList.Add(audioPath);
        }
        
        public static void EnableSoundSource(string audioPath) {
            MuteSoundSourcePathList.Remove(audioPath);
        }

        public static void MuteAudioPathVector2(string audioPath) {
            MuteAudioPathVector2List.Add(audioPath);
        }
        
        public static void EnableAudioPathVector2(string audioPath) {
            MuteAudioPathVector2List.Remove(audioPath);
        }
        
        public static void MuteAudioPath(string audioPath) {
            MuteAudioPathList.Add(audioPath);
        }
        
        public static void EnableAudioPath(string audioPath) {
            MuteAudioPathList.Remove(audioPath);
        }

        public override void OnSaveSate(Level level) {
        }

        public override void OnClear() {
            MuteSoundSourcePathList.Clear();
            MuteAudioPathVector2List.Clear();
            MuteAudioPathList.Clear();
        }

        public override void OnLoad() {
            On.Celeste.SoundSource.Play += SoundSourceOnPlay;
            On.Celeste.Audio.Play_string_Vector2 += AudioOnPlayStringVector2;
            On.Celeste.Audio.Play_string += AudioOnPlayString;
        }

        public override void OnUnload() {
            On.Celeste.SoundSource.Play -= SoundSourceOnPlay;
            On.Celeste.Audio.Play_string_Vector2 -= AudioOnPlayStringVector2;
            On.Celeste.Audio.Play_string -= AudioOnPlayString;
        }

        private static SoundSource SoundSourceOnPlay(On.Celeste.SoundSource.orig_Play orig, SoundSource self, string path,
            string param, float value) {
            if (MuteSoundSourcePathList.Contains(path)) {
                MuteSoundSourcePathList.Remove(path);
                return null;
            }

            return orig(self, path, param, value);
        }

        private static EventInstance AudioOnPlayStringVector2(On.Celeste.Audio.orig_Play_string_Vector2 orig, string path,
            Vector2 position) {
            if (MuteAudioPathVector2List.Contains(path)) {
                MuteAudioPathVector2List.Remove(path);
                return null;
            }

            return orig(path, position);
        }

        private static EventInstance AudioOnPlayString(On.Celeste.Audio.orig_Play_string orig, string path) {
            if (MuteAudioPathList.Contains(path)) {
                MuteAudioPathList.Remove(path);
                return null;
            }

            return orig(path);
        }
    }
}