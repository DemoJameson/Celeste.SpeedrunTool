using System.Collections.Concurrent;
using System.Collections.Generic;
using FMOD;
using FMOD.Studio;

namespace Celeste.Mod.SpeedrunTool.SaveLoad; 

internal static class EventInstanceUtils {
    [Load]
    private static void Load() {
        On.FMOD.Studio.EventInstance.setParameterValue += EventInstanceOnsetParameterValue;
    }

    [Unload]
    private static void OnUnhook() {
        On.FMOD.Studio.EventInstance.setParameterValue -= EventInstanceOnsetParameterValue;
    }

    private static RESULT EventInstanceOnsetParameterValue(On.FMOD.Studio.EventInstance.orig_setParameterValue orig,
        EventInstance self, string name, float value) {
        RESULT result = orig(self, name, value);
        if (result == RESULT.OK) {
            self.SaveParameters(name, value);
        }

        return result;
    }
}

internal static class EventInstanceExtensions {
    private const string NeedManualCloneKey = "EventInstanceExtensions-NeedManualCloneKey";
    private const string ParametersKey = "EventInstanceExtensions-ParametersKey";
    private const string TimelinePositionKey = "EventInstanceExtensions-TimelinePositionKey";

    public static EventInstance NeedManualClone(this EventInstance eventInstance) {
        eventInstance.SetExtendedBoolean(NeedManualCloneKey, true);
        return eventInstance;
    }

    public static bool IsNeedManualClone(this EventInstance eventInstance) {
        return eventInstance.GetExtendedBoolean(NeedManualCloneKey);
    }

    private static ConcurrentDictionary<string, float> GetSavedParameterValues(this EventInstance eventInstance) {
        ConcurrentDictionary<string, float> parameters = eventInstance.GetExtendedDataValue<ConcurrentDictionary<string, float>>(ParametersKey);
        if (parameters == null) {
            parameters = new ConcurrentDictionary<string, float>();
        }

        return parameters;
    }

    internal static void SaveParameters(this EventInstance eventInstance, string param, float value) {
        if (param == null) {
            return;
        }

        ConcurrentDictionary<string, float> parameters = eventInstance.GetSavedParameterValues();
        parameters[param] = value;
        eventInstance.SetExtendedDataValue(ParametersKey, parameters);
    }

    public static int LoadTimelinePosition(this EventInstance eventInstance) {
        int saved = eventInstance.GetExtendedInt(TimelinePositionKey);
        if (saved > 0) {
            return saved;
        }

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
        string path = Audio.GetEventName(eventInstance);
        if (string.IsNullOrEmpty(path)) {
            return null;
        }

        EventInstance cloneInstance = Audio.CreateInstance(path);
        if (cloneInstance == null) {
            return null;
        }

        if (eventInstance.IsNeedManualClone()) {
            cloneInstance.NeedManualClone();
        }

        ConcurrentDictionary<string, float> parameters = eventInstance.GetSavedParameterValues();
        if (parameters != null) {
            foreach (KeyValuePair<string, float> pair in parameters) {
                cloneInstance.setParameterValue(pair.Key, pair.Value);
            }
        }

        cloneInstance.CopyTimelinePosition(eventInstance);

        return cloneInstance;
    }

    public static void CopyParametersFrom(this EventInstance eventInstance, EventInstance otherEventInstance) {
        if (eventInstance == null || otherEventInstance == null) {
            return;
        }

        if (Audio.GetEventName(eventInstance) != Audio.GetEventName(otherEventInstance)) {
            return;
        }

        ConcurrentDictionary<string, float> parameterValues = new(eventInstance.GetSavedParameterValues());
        ConcurrentDictionary<string, float> clonedParameterValues = otherEventInstance.GetSavedParameterValues();
        foreach (KeyValuePair<string, float> pair in clonedParameterValues) {
            eventInstance.setParameterValue(pair.Key, pair.Value);
        }
        foreach (KeyValuePair<string, float> pair in parameterValues) {
            if (!clonedParameterValues.ContainsKey(pair.Key)) {
                if (eventInstance.getDescription(out EventDescription description) != RESULT.OK) {
                    continue;
                }

                if (description.getParameter(pair.Key, out PARAMETER_DESCRIPTION parameterDescription) != RESULT.OK) {
                    continue;
                }

                eventInstance.setParameterValue(pair.Key, parameterDescription.defaultvalue);
            }
        }
    }
}