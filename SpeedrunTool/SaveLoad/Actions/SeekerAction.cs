using System.Collections;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class SeekerAction : AbstractEntityAction {
        private const string RemoveStatue = "RemoveStatue";
        private const int StRegenerate = 6;

        private readonly Dictionary<EntityID, Seeker> savedSeekers = new Dictionary<EntityID, Seeker>();

        private readonly Dictionary<EntityID, SeekerStatue> savedSeekerStatues =
            new Dictionary<EntityID, SeekerStatue>();

        public override void OnQuickSave(Level level) {
            savedSeekers.AddRange(level.Entities.FindAll<Seeker>());
            savedSeekerStatues.AddRange(level.Entities.FindAll<SeekerStatue>());
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
                } else {
                    self.Visible = false;
                    self.Add(new RemoveSelfComponent());
                }
            }
        }

        private void SeekerStatueOnCtor(On.Celeste.SeekerStatue.orig_ctor orig, SeekerStatue self, EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            self.SetEntityData(data);
            orig(self, data, offset);

            if (IsLoadStart && !savedSeekerStatues.ContainsKey(entityId)) {
                self.SetExtendedBoolean(RemoveStatue, true);
            }
        }

        private IEnumerator SetStateMachine(Seeker self, Seeker savedSeeker) {
            StateMachine stateMachine = self.GetField(typeof(Seeker), "State") as StateMachine;
            int savedState = (savedSeeker.GetField(typeof(Seeker), "State") as StateMachine).State;
            if (savedState == StRegenerate) {
                AudioAction.MuteAudioPathVector2("event:/game/general/thing_booped");
            }

            stateMachine.State = savedState;
            yield break;
        }

        private void SeekerStatueOnUpdate(On.Celeste.SeekerStatue.orig_Update orig, SeekerStatue self) {
            if (self.GetExtendedBoolean(RemoveStatue)) {
                self.SetExtendedBoolean(RemoveStatue, false);
                if (savedSeekers.ContainsKey(self.GetEntityId())) {
                    Seeker savedSeeker = savedSeekers[self.GetEntityId()];
                    Seeker seeker = new Seeker(self.GetEntityData(), Vector2.Zero) {Position = savedSeeker.Position};
                    seeker.Add(new Coroutine(SetStateMachine(seeker, savedSeeker)));
                    self.Scene.Add(seeker);
                }
                self.RemoveSelf();
                return;
            }
            
            orig(self);
        }

        public override void OnClear() {
            savedSeekers.Clear();
            savedSeekerStatues.Clear();
        }

        public override void OnLoad() {
            On.Celeste.Seeker.ctor_EntityData_Vector2 += RestoreSeekerPosition;
            On.Celeste.SeekerStatue.ctor += SeekerStatueOnCtor;
            On.Celeste.SeekerStatue.Update += SeekerStatueOnUpdate;
        }

        public override void OnUnload() {
            On.Celeste.Seeker.ctor_EntityData_Vector2 -= RestoreSeekerPosition;
            On.Celeste.SeekerStatue.ctor -= SeekerStatueOnCtor;
        }
    }
}