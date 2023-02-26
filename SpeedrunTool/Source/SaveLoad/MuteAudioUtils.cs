using System.Collections.Generic;
using FMOD;
using FMOD.Studio;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;

internal static class MuteAudioUtils {
    private static readonly HashSet<string> RequireMuteAudioPaths = new() {
        "event:/game/general/strawberry_get",
        "event:/game/general/strawberry_laugh",
        "event:/game/general/strawberry_flyaway",
        "event:/game/general/seed_complete_main",
        "event:/game/general/key_get",
        "event:/game/general/cassette_get",
        "event:/game/05_mirror_temple/eyewall_destroy",
        "event:/char/badeline/boss_hug",
        "event:/char/badeline/boss_laser_fire",
    };

    private static readonly List<EventInstance> RequireMuteAudios = new();

    [Load]
    private static void Load() {
        On.FMOD.Studio.EventDescription.createInstance += EventDescriptionOnCreateInstance;
    }

    [Unload]
    private static void Unload() {
        On.FMOD.Studio.EventDescription.createInstance -= EventDescriptionOnCreateInstance;
    }

    private static RESULT EventDescriptionOnCreateInstance(On.FMOD.Studio.EventDescription.orig_createInstance orig, EventDescription self,
        out EventInstance instance) {
        RESULT result = orig(self, out instance);

        if (StateManager.Instance.IsSaved && instance != null && self.getPath(out string path) == RESULT.OK && path != null &&
            RequireMuteAudioPaths.Contains(path)) {
            RequireMuteAudios.Add(instance);
        }

        return result;
    }

    public static void AddAction() {
        SaveLoadAction.SafeAdd(
            loadState: (_, level) => {
                level.Entities.FindAll<SoundEmitter>().ForEach(emitter => {
                    if (emitter.Source.instance is { } eventInstance) {
                        eventInstance.setVolume(0f);
                    }
                });

                foreach (EventInstance sfx in RequireMuteAudios) {
                    sfx.setVolume(0f);
                }

                RequireMuteAudios.Clear();
            }, clearState: () => RequireMuteAudios.Clear()
        );
    }
}