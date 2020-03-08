using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using FMOD;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class CassetteBlockManagerAction : AbstractEntityAction {
        private CassetteBlockManager savedCassetteBlockManager;
        private bool disableAudio;

        public override void OnQuickSave(Level level) {
            savedCassetteBlockManager = level.Entities.FindFirst<CassetteBlockManager>();
        }

        private void RestoreCassetteBlockManager(On.Celeste.CassetteBlockManager.orig_Update orig, CassetteBlockManager self) {
            List<CassetteBlock> cassetteBlocks = self.Scene.Entities.FindAll<CassetteBlock>();
            if (IsLoadStart && savedCassetteBlockManager != null) {
                disableAudio = true; // sfx.setParameterValue("sixteenth_note", GetSixteenthNote());
                int tickNumber = (int) self.GetField(typeof(CassetteBlockManager), "beatsPerTick") * (int) self.GetField(typeof(CassetteBlockManager), "ticksPerSwap");
                while ((int) self.GetField(typeof(CassetteBlockManager), "currentIndex") != (int) savedCassetteBlockManager.GetField(typeof(CassetteBlockManager), "currentIndex") ||
                       (int) self.GetField(typeof(CassetteBlockManager), "beatIndex") % tickNumber != (int) savedCassetteBlockManager.GetField(typeof(CassetteBlockManager), "beatIndex") % tickNumber) {

                    AudioAction.MuteAudioPath("event:/game/general/cassette_block_switch_1");
                    AudioAction.MuteAudioPath("event:/game/general/cassette_block_switch_2");
                    orig(self);
                    AudioAction.EnableAudioPath("event:/game/general/cassette_block_switch_1");
                    AudioAction.EnableAudioPath("event:/game/general/cassette_block_switch_2");

                    cassetteBlocks.ForEach(entity => entity.Update());
                }
            }

            disableAudio = false; // sfx.setParameterValue("sixteenth_note", GetSixteenthNote());

            orig(self);
        }

        private RESULT EventInstanceOnSetParameterValue(On.FMOD.Studio.EventInstance.orig_setParameterValue orig, FMOD.Studio.EventInstance self, string name, float value) {
            if (disableAudio && name == "sixteenth_note") {
                return RESULT.OK;
            }

            return orig(self, name, value);
        }

        public override void OnClear() {
            savedCassetteBlockManager = null;
        }

        public override void OnLoad() {
            On.Celeste.CassetteBlockManager.Update += RestoreCassetteBlockManager;
            On.FMOD.Studio.EventInstance.setParameterValue += EventInstanceOnSetParameterValue;
        }

        public override void OnUnload() {
            On.Celeste.CassetteBlockManager.Update -= RestoreCassetteBlockManager;
            On.FMOD.Studio.EventInstance.setParameterValue -= EventInstanceOnSetParameterValue;
        }
    }
}