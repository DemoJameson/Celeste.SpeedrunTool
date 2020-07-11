using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using FMOD;
using FMOD.Studio;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions {
    public class EventInstanceRestoreAction : RestoreAction {
        public EventInstanceRestoreAction() : base(typeof(Entity)) { }

        public override void OnHook() {
            On.Celeste.Audio.CreateInstance += AudioOnCreateInstance;
            On.FMOD.Studio.EventInstance.setParameterValue += EventInstanceOnsetParameterValue;
        }

        public override void OnUnhook() {
            On.Celeste.Audio.CreateInstance -= AudioOnCreateInstance;
            On.FMOD.Studio.EventInstance.setParameterValue -= EventInstanceOnsetParameterValue;
        }

        private EventInstance AudioOnCreateInstance(On.Celeste.Audio.orig_CreateInstance orig, string path,
            Vector2? position) {
            EventInstance eventInstance = orig(path, position);
            eventInstance?.SavePath(path);
            return eventInstance;
        }

        private RESULT EventInstanceOnsetParameterValue(On.FMOD.Studio.EventInstance.orig_setParameterValue orig,
            EventInstance self, string name, float value) {
            RESULT result = orig(self, name, value);
            self.SaveParameters(name, value);
            return result;
        }
    }


    public static class EventInstanceExtensions {
        private const string EventInstancePathKey = "EventInstancePathKey";
        private const string EventInstanceParametersKey = "EventInstanceParametersKey";

        public static void SavePath(this EventInstance eventInstance, string path) {
            eventInstance.SetExtendedString(EventInstancePathKey, path);
        }

        public static void SaveParameters(this EventInstance eventInstance, string param, float value) {
            Dictionary<string, float> parameters =
                eventInstance.GetExtendedDataValue<Dictionary<string, float>>(EventInstanceParametersKey);
            if (parameters == null) {
                parameters = new Dictionary<string, float>();
            }

            parameters[param] = value;
            eventInstance.SetExtendedDataValue(EventInstanceParametersKey, parameters);
        }

        public static EventInstance Clone(this EventInstance eventInstance) {
            EventInstance clone = Audio.CreateInstance(eventInstance.GetExtendedString(EventInstancePathKey));
            if (clone == null) return null;

            var parameters =
                eventInstance.GetExtendedDataValue<Dictionary<string, float>>(EventInstanceParametersKey);
            if (parameters != null) {
                foreach (var pair in parameters) {
                    clone.setParameterValue(pair.Key, pair.Value);
                }
            }

            clone.start();
            clone.release();

            return clone;
        }
    }
}