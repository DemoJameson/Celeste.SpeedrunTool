using FMOD.Studio;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public abstract class AbstractEntityAction {
        protected static bool IsLoadStart => StateManager.Instance.IsLoadStart;
        protected static bool IsFrozen => StateManager.Instance.IsLoadFrozen;
        protected static bool IsLoading => StateManager.Instance.IsLoadFrozen || StateManager.Instance.IsLoading;
        protected static bool IsLoadComplete => StateManager.Instance.IsLoadComplete;

        public abstract void OnQuickSave(Level level);
        public abstract void OnClear();
        public abstract void OnLoad();
        public abstract void OnUnload();

        public virtual void OnInit() { }

        public virtual void OnUpdateEntitiesWhenFreeze(Level level) { }

        public virtual void OnQuickLoadStart(Level level) { }

        protected static void MuteAudio(string audioPath) {
            EventInstance AudioOnPlayStringVector2(On.Celeste.Audio.orig_Play_string_Vector2 orig, string path,
                Vector2 position) {
                if (path == audioPath) {
                    On.Celeste.Audio.Play_string_Vector2 -= AudioOnPlayStringVector2;
                    return null;
                }

                return orig(path, position);
            }

            On.Celeste.Audio.Play_string_Vector2 += AudioOnPlayStringVector2;
        }

        protected static void MuteSoundSource(string audioPath) {
            SoundSource SoundSourceOnPlay(On.Celeste.SoundSource.orig_Play orig, SoundSource self, string path,
                string param, float value) {
                if (path == audioPath) {
                    On.Celeste.SoundSource.Play -= SoundSourceOnPlay;
                    return null;
                }

                return orig(self, path, param, value);
            }

            On.Celeste.SoundSource.Play += SoundSourceOnPlay;
        }
    }
}