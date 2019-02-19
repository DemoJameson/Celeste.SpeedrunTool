using System.Collections;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class TempleGateAction : AbstractEntityAction {
        private Dictionary<EntityID, TempleGate> _savedTempleGates = new Dictionary<EntityID, TempleGate>();

        public override void OnQuickSave(Level level) {
            _savedTempleGates = level.Tracker.GetDictionary<TempleGate>();
        }

        private void TempleGateOnCtorEntityDataVector2String(
            On.Celeste.TempleGate.orig_ctor_EntityData_Vector2_string orig, TempleGate self, EntityData data,
            Vector2 offset, string levelId) {
            orig(self, data, offset, levelId);

            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);

            if (IsLoadStart && _savedTempleGates.ContainsKey(entityId)) {
                self.Add(new Coroutine(SetState(self)));
            }
        }

        private IEnumerator SetState(TempleGate self) {
            yield return null;
            TempleGate saved = _savedTempleGates[self.GetEntityId()];
            if ((bool) saved.GetPrivateField("open")) {
                if (self.Type == TempleGate.Types.TouchSwitches) {
                    MuteAudio("event:/game/05_mirror_temple/gate_main_open");
                }

                self.StartOpen();
            }
            else if ((bool) self.GetPrivateField("open")) {
                MuteAudio("event:/game/05_mirror_temple/gate_main_close");
                self.InvokePrivateMethod("SetHeight", self.GetPrivateField("closedHeight"));
            }
        }


        public override void OnClear() {
            _savedTempleGates.Clear();
        }

        public override void OnLoad() {
            On.Celeste.TempleGate.ctor_EntityData_Vector2_string += TempleGateOnCtorEntityDataVector2String;
        }

        public override void OnUnload() {
            On.Celeste.TempleGate.ctor_EntityData_Vector2_string -= TempleGateOnCtorEntityDataVector2String;
        }

        public override void OnInit() {
            typeof(TempleGate).AddToTracker();
        }

        public override void OnUpdateEntitiesWhenFreeze(Level level) {
            level.UpdateEntities<TempleGate>();
        }
    }
}