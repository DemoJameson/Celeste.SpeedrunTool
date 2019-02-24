using System.Collections;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class SeekerAction : AbstractEntityAction {
        private readonly Dictionary<EntityID, Seeker> savedSeekers = new Dictionary<EntityID, Seeker>();

        public override void OnQuickSave(Level level) {
            savedSeekers.AddRange(level.Tracker.GetCastEntities<Seeker>());
        }

        private void RestoreSeekerPosition(On.Celeste.Seeker.orig_ctor_EntityData_Vector2 orig, Seeker self,
            EntityData data, Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart) {
                if (savedSeekers.ContainsKey(entityId)) {
                    Seeker savedSeeker = savedSeekers[self.GetEntityId()];
                    self.Position = savedSeeker.Position;
                    self.Add(new Coroutine(SetStateMachine(self, savedSeeker)));
                }
                else {
                    self.Add(new RemoveSelfComponent());
                }
            }
        }

        private IEnumerator SetStateMachine(Seeker self, Seeker savedSeeker) {
            StateMachine stateMachine = self.GetPrivateField("State") as StateMachine;
            int savedState = (savedSeeker.GetPrivateField("State") as StateMachine).State;
            if (savedState == 6) MuteAudio("event:/game/general/thing_booped");

            stateMachine.State = (savedSeeker.GetPrivateField("State") as StateMachine).State;
            yield return null;
        }

        public override void OnClear() {
            savedSeekers.Clear();
        }

        public override void OnLoad() {
            On.Celeste.Seeker.ctor_EntityData_Vector2 += RestoreSeekerPosition;
        }

        public override void OnUnload() {
            On.Celeste.Seeker.ctor_EntityData_Vector2 -= RestoreSeekerPosition;
        }
    }
}