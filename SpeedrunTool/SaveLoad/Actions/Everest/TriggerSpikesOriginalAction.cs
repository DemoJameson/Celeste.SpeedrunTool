using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Components;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions.Everest {
    public class TriggerSpikesOriginalAction : AbstractEntityAction {
        private Dictionary<EntityId2, Entity> savedTriggerSpikes = new Dictionary<EntityId2, Entity>();

        public override void OnSaveSate(Level level) {
            savedTriggerSpikes = level.Entities.FindAll<Entity>()
                .Where(entity => entity.GetType().FullName == "Celeste.Mod.Entities.TriggerSpikesOriginal").GetDictionary();
        }

        private void EntityOnCtor_Vector2(On.Monocle.Entity.orig_ctor_Vector2 orig, Entity self, Vector2 position) {
            orig(self, position);
            if (self.GetType().FullName != "Celeste.Mod.Entities.TriggerSpikesOriginal") {
                return;
            }

            EntityId2 entityId = self.CreateEntityId2(position.ToString(), self.GetField("direction").ToString());
            if (entityId == default) {
                return;
            }
            self.SetEntityId2(entityId);

            if (IsLoadStart) {
                if (savedTriggerSpikes.ContainsKey(entityId)) {
                    var savedTriggerSpike = savedTriggerSpikes[entityId];
                    var platform = savedTriggerSpike.Get<StaticMover>()?.Platform;
                    if (platform is CassetteBlock) {
                        return;
                    }

                    if (platform is FloatySpaceBlock) {
                        self.Add(new RestorePositionComponent(self, savedTriggerSpike));
                    }
                    else {
                        self.Position = savedTriggerSpike.Position;
                    }

                    self.Collidable = savedTriggerSpike.Collidable;
                    self.Visible = savedTriggerSpike.Visible;
                    self.Add(new Coroutine(RestoreTriggerState(self, savedTriggerSpike)));
                }
                else {
                    self.Add(new RemoveSelfComponent());
                }
            } 
        }

        private static IEnumerator RestoreTriggerState(Entity self, Entity savedTriggerSpikes) {
            Array spikes = self.GetField("spikes") as Array;
            Array savedSpikes = savedTriggerSpikes.GetField("spikes") as Array;
            Array newSpikes = Activator.CreateInstance(spikes.GetType(), spikes.Length) as Array;

            for (var i = 0; i < spikes.Length; i++) {
                var spike = spikes.GetValue(i);
                var savedSpike = savedSpikes.GetValue(i);
                savedSpike.CopyFields(spike, "Parent");
                newSpikes.SetValue(savedSpike, i);
            }

            self.SetField("spikes", newSpikes);
            yield break;
        }

        public override void OnClear() {
            savedTriggerSpikes.Clear();
        }

        public override void OnLoad() {
            On.Monocle.Entity.ctor_Vector2 += EntityOnCtor_Vector2;
        }

        public override void OnUnload() {
            On.Monocle.Entity.ctor_Vector2 -= EntityOnCtor_Vector2;
        }
    }
}