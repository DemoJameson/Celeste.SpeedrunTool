using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Components;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    // TODO: still desync after savestate
    public class SeekerAction : AbstractEntityAction {
        private const string RemoveStatue = "RemoveStatue";
        private const int StRegenerate = 6;

        private readonly Dictionary<EntityId2, Seeker> savedSeekers = new Dictionary<EntityId2, Seeker>();

        private readonly Dictionary<EntityId2, SeekerStatue> savedSeekerStatues =
            new Dictionary<EntityId2, SeekerStatue>();

        public override void OnSaveSate(Level level) {
            savedSeekers.AddRange(level.Entities.FindAll<Seeker>());
            savedSeekerStatues.AddRange(level.Entities.FindAll<SeekerStatue>());
        }

        private void SeekerOnCtor_EntityData_Vector2(On.Celeste.Seeker.orig_ctor_EntityData_Vector2 orig, Seeker self,
            EntityData data, Vector2 offset) {
            EntityId2 entityId = data.ToEntityId2(self.GetType());
            self.SetEntityId2(entityId);
            orig(self, data, offset);
        }

        private void SeekerOnAdded(On.Celeste.Seeker.orig_Added orig, Seeker self, Scene scene) {
            orig(self, scene);


            EntityId2 entityId = self.GetEntityId2();
            if (IsLoadStart) {
                if (savedSeekers.ContainsKey(entityId)) {
                    Seeker savedSeeker = savedSeekers[self.GetEntityId2()];

                    self.Add(new RestoreState(RunType.Added | RunType.LoadComplete,
                        () => { RestoreSeekerState(self, savedSeeker); }));
                } else {
                    self.Add(new RemoveSelfComponent());
                }
            }
        }

        private static void RestoreSeekerState(Seeker self, Seeker savedSeeker) {
            self.CopyEntity(savedSeeker);
            self.CopyFields(typeof(Actor), savedSeeker, "movementCounter");

            self.Speed = savedSeeker.Speed;

            (self.GetField("idleSineX") as SineWave).Counter = (savedSeeker.GetField("idleSineX") as SineWave).Counter;
            (self.GetField("idleSineY") as SineWave).Counter = (savedSeeker.GetField("idleSineY") as SineWave).Counter;

            self.CopyFields(savedSeeker,
                "lastSpottedAt",
                "lastPathTo",
                "canSeePlayer",
                "lastPathFound",
                "pathIndex",
                "dead",
                "facing",
                "spriteFacing",
                "nextSprite",
                "patrolWaitTimer",
                "spottedLosePlayerTimer",
                "spottedTurnDelay",
                "attackSpeed",
                "attackWindUp",
                "strongSkid"
            );

            self.CopySprite(savedSeeker, "sprite");

            // StateMachine stateMachine = self.GetField("State") as StateMachine;
            // stateMachine.State = (savedSeeker.GetField("State") as StateMachine).State;
        }

        private void SeekerStatueOnCtor(On.Celeste.SeekerStatue.orig_ctor orig, SeekerStatue self, EntityData data,
            Vector2 offset) {
            EntityId2 entityId = data.ToEntityId2(self.GetType());
            self.SetEntityId2(entityId);
            self.SetEntityData(data);
            orig(self, data, offset);

            if (IsLoadStart) {
                if (savedSeekerStatues.ContainsKey(entityId)) {
                    SeekerStatue saved = savedSeekerStatues[entityId];
                    self.CopySprite(saved, "sprite");
                } else {
                    self.SetExtendedBoolean(RemoveStatue, true);
                }
            }
        }

        private void SeekerStatueOnUpdate(On.Celeste.SeekerStatue.orig_Update orig, SeekerStatue self) {
            if (self.GetExtendedBoolean(RemoveStatue)) {
                self.SetExtendedBoolean(RemoveStatue, false);
                if (savedSeekers.ContainsKey(self.GetEntityId2())) {
                    Seeker savedSeeker = savedSeekers[self.GetEntityId2()];
                    Seeker seeker = new Seeker(self.GetEntityData(), Vector2.Zero) {Position = savedSeeker.Position};
                    self.Scene.Add(seeker);
                    RestoreSeekerState(seeker, savedSeeker);
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
            On.Celeste.Seeker.ctor_EntityData_Vector2 += SeekerOnCtor_EntityData_Vector2;
            On.Celeste.Seeker.Added += SeekerOnAdded;
            On.Celeste.SeekerStatue.ctor += SeekerStatueOnCtor;
            On.Celeste.SeekerStatue.Update += SeekerStatueOnUpdate;
        }


        public override void OnUnload() {
            On.Celeste.Seeker.ctor_EntityData_Vector2 -= SeekerOnCtor_EntityData_Vector2;
            On.Celeste.Seeker.Added -= SeekerOnAdded;
            On.Celeste.SeekerStatue.ctor -= SeekerStatueOnCtor;
            On.Celeste.SeekerStatue.Update -= SeekerStatueOnUpdate;
        }
    }
}