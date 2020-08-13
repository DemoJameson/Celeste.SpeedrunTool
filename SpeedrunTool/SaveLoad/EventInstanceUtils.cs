using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using FMOD;
using FMOD.Studio;

namespace Celeste.Mod.SpeedrunTool.SaveLoad {
    internal static class EventInstanceUtils {
        public static void OnHook() {
            On.FMOD.Studio.EventInstance.setParameterValue += EventInstanceOnsetParameterValue;
        }

        public static void OnUnhook() {
            On.FMOD.Studio.EventInstance.setParameterValue -= EventInstanceOnsetParameterValue;
        }

        private static RESULT EventInstanceOnsetParameterValue(On.FMOD.Studio.EventInstance.orig_setParameterValue orig,
            EventInstance self, string name, float value) {
            RESULT result = orig(self, name, value);
            self.SaveParameters(name, value);
            return result;
        }
    }

    internal static class EventInstanceExtensions {
        private const string EventInstancePathKey = "EventInstanceExtensions-EventInstancePathKey";
        private const string EventInstanceParametersKey = "EventInstanceExtensions-EventInstanceParametersKey";
        private const string EventInstanceTimelinePositionKey = "EventInstanceExtensions-EventInstanceTimelinePositionKey";


        public static bool IsPlaying(this EventInstance eventInstance) {
            if (eventInstance == null) return false;
            eventInstance.getPlaybackState(out PLAYBACK_STATE state);
            return state == PLAYBACK_STATE.PLAYING || state == PLAYBACK_STATE.STARTING || state == PLAYBACK_STATE.SUSTAINING;
        }

        public static void SavePath(this EventInstance eventInstance, string path) {
            eventInstance.SetExtendedString(EventInstancePathKey, path);
        }

        public static void SaveParameters(this EventInstance eventInstance, string param, float value) {
            if (param == null) return;

            Dictionary<string, float> parameters =
                eventInstance.GetExtendedDataValue<Dictionary<string, float>>(EventInstanceParametersKey);
            if (parameters == null) {
                parameters = new Dictionary<string, float>();
            }

            parameters[param] = value;
            eventInstance.SetExtendedDataValue(EventInstanceParametersKey, parameters);
        }


        public static int LoadTimelinePositionValue(this EventInstance eventInstance) {
            return eventInstance.GetExtendedInt(EventInstanceTimelinePositionKey);
        }

        public static void SaveTimelinePositionValue(this EventInstance eventInstance) {
            object[] args = {0};
            eventInstance.InvokeMethod("getTimelinePosition", args);
            eventInstance.SetExtendedInt(EventInstanceTimelinePositionKey, (int) args[0]);
        }

        public static void CopyTimelinePosition(this EventInstance eventInstance, EventInstance otherEventInstance) {
            eventInstance.InvokeMethod("setTimelinePosition", otherEventInstance.LoadTimelinePositionValue());
            eventInstance.SaveTimelinePositionValue();
        }

        public static EventInstance Clone(this EventInstance eventInstance) {
            string path = eventInstance.GetExtendedString(EventInstancePathKey);
            if (string.IsNullOrEmpty(path)) return null;

            EventInstance cloneInstance = Audio.CreateInstance(path);
            if (cloneInstance == null) return null;

            cloneInstance.SavePath(path);

            var parameters =
                eventInstance.GetExtendedDataValue<Dictionary<string, float>>(EventInstanceParametersKey);
            if (parameters != null) {
                foreach (var pair in parameters) {
                    cloneInstance.setParameterValue(pair.Key, pair.Value);
                    cloneInstance.SaveParameters(pair.Key, pair.Value);
                }
            }

            cloneInstance.CopyTimelinePosition(eventInstance);

            return cloneInstance;
        }
    }

    internal static class SoundSourceExtensions {
        private const string SoundSourcePlayingKey = "SoundSourceExtensions-SoundSourcePlayingKey";

        public static bool LoadPlayingValue(this SoundSource soundSource) {
            return soundSource.GetExtendedBoolean(SoundSourcePlayingKey);
        }

        public static void SavePlayingValue(this SoundSource soundSource) {
            soundSource.SetExtendedBoolean(SoundSourcePlayingKey, soundSource.Playing);
        }

    }
}