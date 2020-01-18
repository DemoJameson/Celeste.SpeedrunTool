using System;
using System.Collections;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class TriggerSpikesAction : AbstractEntityAction {
        private Dictionary<EntityID, TriggerSpikes> savedTriggerSpikes = new Dictionary<EntityID, TriggerSpikes>();

        public override void OnQuickSave(Level level) {
            savedTriggerSpikes = level.Entities.GetDictionary<TriggerSpikes>();
        }

        private void TriggerSpikesOnCtorEntityDataVector2Directions(
            On.Celeste.TriggerSpikes.orig_ctor_EntityData_Vector2_Directions orig, TriggerSpikes self, EntityData data,
            Vector2 offset, TriggerSpikes.Directions dir) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset, dir);

            if (IsLoadStart) {
                if (savedTriggerSpikes.ContainsKey(entityId)) {
                    TriggerSpikes savedTriggerSpike = savedTriggerSpikes[entityId];
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

        private static IEnumerator RestoreTriggerState(TriggerSpikes self, TriggerSpikes savedTriggerSpikes) {
            Array spikes = self.GetPrivateField("spikes") as Array;
            Array savedSpikes = savedTriggerSpikes.GetPrivateField("spikes") as Array;
            Array newSpikes = Activator.CreateInstance(spikes.GetType(), spikes.Length) as Array;

            for (var i = 0; i < spikes.Length; i++) {
                var spike = spikes.GetValue(i);
                var savedSpike = savedSpikes.GetValue(i);
                savedSpike.CopyPrivateField("Parent", spike);
                newSpikes.SetValue(savedSpike, i);
            }

            self.SetPrivateField("spikes", newSpikes);
            yield break;
        }

        public override void OnClear() {
            savedTriggerSpikes.Clear();
        }

        public override void OnLoad() {
            On.Celeste.TriggerSpikes.ctor_EntityData_Vector2_Directions +=
                TriggerSpikesOnCtorEntityDataVector2Directions;
        }

        public override void OnUnload() {
            On.Celeste.TriggerSpikes.ctor_EntityData_Vector2_Directions -=
                TriggerSpikesOnCtorEntityDataVector2Directions;
        }
    }
}