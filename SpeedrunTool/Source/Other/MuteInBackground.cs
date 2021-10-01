using System;
using Celeste.Mod.SpeedrunTool.Extensions;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.Other {
    public static class MuteInBackground {
        private static bool init;
        private static SpeedrunToolSettings ModSettings => SpeedrunToolModule.Settings;

        [Load]
        private static void Load() {
            On.Celeste.Audio.Init += AudioOnInit;
            On.Celeste.Audio.Unload += AudioOnUnload;
            if ((bool)typeof(Audio).GetFieldValue("ready")) {
                Engine.Instance.Window.ClientSizeChanged += WindowOnClientSizeChanged;
                Engine.Instance.Activated += InstanceOnActivated;
                Engine.Instance.Deactivated += InstanceOnDeactivated;
            }
        }

        [Unload]
        private static void Unload() {
            On.Celeste.Audio.Init -= AudioOnInit;
            On.Celeste.Audio.Unload -= AudioOnUnload;
            Engine.Instance.Window.ClientSizeChanged -= WindowOnClientSizeChanged;
            Engine.Instance.Activated -= InstanceOnActivated;
            Engine.Instance.Deactivated -= InstanceOnDeactivated;
        }

        /*延迟添加这些事件，不然 FNA 版本启动崩溃
        System.NullReferenceException: Object reference not set to an instance of an object.
        at Celeste.Audio.VCAVolume(String path, Nullable`1 volume)*/
        private static void AudioOnInit(On.Celeste.Audio.orig_Init orig) {
            orig();

            if (!init) {
                init = true;
                Engine.Instance.Window.ClientSizeChanged += WindowOnClientSizeChanged;
                Engine.Instance.Activated += InstanceOnActivated;
                Engine.Instance.Deactivated += InstanceOnDeactivated;
            }
        }

        private static void AudioOnUnload(On.Celeste.Audio.orig_Unload orig) {
            orig();
            init = false;
            Engine.Instance.Window.ClientSizeChanged -= WindowOnClientSizeChanged;
            Engine.Instance.Activated -= InstanceOnActivated;
            Engine.Instance.Deactivated -= InstanceOnDeactivated;
        }

        private static void InstanceOnActivated(object sender, EventArgs e) {
            RestoreAudio();
        }

        private static void InstanceOnDeactivated(object sender, EventArgs e) {
            MuteAudio();
        }

        private static void WindowOnClientSizeChanged(object sender, EventArgs e) {
            if (Engine.Instance.Window.ClientBounds.Width == 0) {
                MuteAudio();
            } else {
                RestoreAudio();
            }
        }

        private static void MuteAudio() {
            if (ModSettings.MuteInBackground) {
                Audio.MusicVolume = 0f;
                Audio.SfxVolume = 0f;
            }
        }

        private static void RestoreAudio() {
            if (ModSettings.MuteInBackground) {
                Settings.Instance.ApplyMusicVolume();
                Settings.Instance.ApplySFXVolume();
            }
        }
    }
}