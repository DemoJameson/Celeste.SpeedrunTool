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
        private const string PathKey = "EventInstanceExtensions-PathKey";
        private const string ParametersKey = "EventInstanceExtensions-ParametersKey";
        private const string TimelinePositionKey = "EventInstanceExtensions-TimelinePositionKey";

        public static void SavePath(this EventInstance eventInstance, string path) {
            eventInstance.SetExtendedString(PathKey, path);
        }

        public static void SaveParameters(this EventInstance eventInstance, string param, float value) {
            if (param == null) return;

            Dictionary<string, float> parameters =
                eventInstance.GetExtendedDataValue<Dictionary<string, float>>(ParametersKey);
            if (parameters == null) {
                parameters = new Dictionary<string, float>();
            }

            parameters[param] = value;
            eventInstance.SetExtendedDataValue(ParametersKey, parameters);
        }

        public static int LoadTimelinePosition(this EventInstance eventInstance) {
            int saved = eventInstance.GetExtendedInt(TimelinePositionKey);
            if (saved > 0) return saved;

            object[] args = {0};
            eventInstance.InvokeMethod("getTimelinePosition", args);
            return (int) args[0];
        }

        public static void SaveTimelinePosition(this EventInstance eventInstance, int timelinePosition) {
            eventInstance.SetExtendedInt(TimelinePositionKey, timelinePosition);
        }

        private static void CopyTimelinePosition(this EventInstance eventInstance, EventInstance otherEventInstance) {
            int timelinePosition = otherEventInstance.LoadTimelinePosition();
            if (timelinePosition > 0) {
                eventInstance.InvokeMethod("setTimelinePosition", timelinePosition);
                eventInstance.SaveTimelinePosition(otherEventInstance.LoadTimelinePosition());
            }
        }

        public static EventInstance Clone(this EventInstance eventInstance) {
            string path = eventInstance.GetExtendedString(PathKey);
            if (string.IsNullOrEmpty(path)) return null;

            EventInstance cloneInstance = Audio.CreateInstance(path);
            if (cloneInstance == null) return null;

            cloneInstance.SavePath(path);

            var parameters =
                eventInstance.GetExtendedDataValue<Dictionary<string, float>>(ParametersKey);
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
}