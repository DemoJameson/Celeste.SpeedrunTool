using Monocle;

namespace Celeste.Mod.SpeedrunTool.Other {
    public static class MuteInBackground {
        private static bool lastActive;
        private static bool lastSetting;
        private static SpeedrunToolSettings ModSettings => SpeedrunToolModule.Settings;

        public static void Load() {
            On.Monocle.Scene.BeforeUpdate += SceneOnBeforeUpdate;
        }

        public static void Unload() {
            On.Monocle.Scene.BeforeUpdate += SceneOnBeforeUpdate;
        }

        private static void SceneOnBeforeUpdate(On.Monocle.Scene.orig_BeforeUpdate orig, Scene self) {
            orig(self);

            if (ModSettings.MuteInBackground && lastActive != Engine.Instance.IsActive) {
                if (Engine.Instance.IsActive) {
                    RestoreAudio();
                } else {
                    Audio.MusicVolume = 0f;
                    Audio.SfxVolume = 0f;
                }
            }

            if (lastSetting != ModSettings.MuteInBackground && !ModSettings.MuteInBackground) {
                RestoreAudio();
            }

            lastActive = Engine.Instance.IsActive;
            lastSetting = ModSettings.MuteInBackground;
        }

        private static void RestoreAudio() {
            Settings.Instance.ApplyMusicVolume();
            Settings.Instance.ApplySFXVolume();
        }
    }
}