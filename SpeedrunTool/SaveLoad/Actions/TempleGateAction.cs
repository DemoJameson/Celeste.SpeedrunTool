using System.Collections;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class TempleGateAction : AbstractEntityAction {
        private Dictionary<EntityId2, TempleGate> savedTempleGates = new Dictionary<EntityId2, TempleGate>();

        public override void OnQuickSave(Level level) {
            savedTempleGates = level.Entities.FindAllToDict<TempleGate>();
        }

        private void TempleGateOnCtorEntityDataVector2String(
            On.Celeste.TempleGate.orig_ctor_EntityData_Vector2_string orig, TempleGate self, EntityData data,
            Vector2 offset, string levelId) {
            orig(self, data, offset, levelId);

            EntityId2 entityId = data.ToEntityId2(self.GetType());
            self.SetEntityId2(entityId);

            if (IsLoadStart && savedTempleGates.ContainsKey(entityId)) {
                self.Add(new Coroutine(SetState(self)));
            }
        }

        private IEnumerator SetState(TempleGate self) {
            TempleGate saved = savedTempleGates[self.GetEntityId2()];
            if ((bool) saved.GetField(typeof(TempleGate), "open") || saved.ClaimedByASwitch) {
                if (self.Type == TempleGate.Types.TouchSwitches) {
                    AudioAction.MuteAudioPathVector2("event:/game/05_mirror_temple/gate_main_open");
                }

                self.StartOpen();

                if (self.Type == TempleGate.Types.CloseBehindPlayer ||
                    self.Type == TempleGate.Types.CloseBehindPlayerAlways) {
                    self.Add(new Coroutine((IEnumerator) self.InvokeMethod("CloseBehindPlayer")));
                }
            } else if ((bool) self.GetField(typeof(TempleGate), "open")) {
                AudioAction.MuteAudioPathVector2("event:/game/05_mirror_temple/gate_main_close");
                self.InvokeMethod(typeof(TempleGate), "SetHeight",
                    self.GetField(typeof(TempleGate), "closedHeight"));
            }

            yield break;
        }


        public override void OnClear() {
            savedTempleGates.Clear();
        }

        public override void OnLoad() {
            On.Celeste.TempleGate.ctor_EntityData_Vector2_string += TempleGateOnCtorEntityDataVector2String;
        }

        public override void OnUnload() {
            On.Celeste.TempleGate.ctor_EntityData_Vector2_string -= TempleGateOnCtorEntityDataVector2String;
        }
    }
}