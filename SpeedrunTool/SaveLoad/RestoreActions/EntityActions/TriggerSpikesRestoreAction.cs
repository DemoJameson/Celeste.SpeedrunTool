using System;
using Celeste.Mod.SpeedrunTool.Extensions;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.EntityActions {
    // TODO TriggerSpikesOriginal
    public class TriggerSpikesRestoreAction : RestoreAction {
        public TriggerSpikesRestoreAction() : base(typeof(TriggerSpikes)) { }

        public override void AfterEntityCreateAndUpdate1Frame(Entity loadedEntity, Entity savedEntity) {
            TriggerSpikes loaded = (TriggerSpikes) loadedEntity;
            TriggerSpikes saved = (TriggerSpikes) savedEntity;
            
            Array spikes = loaded.GetField(typeof(TriggerSpikes), "spikes") as Array;
            Array savedSpikes = saved.GetField(typeof(TriggerSpikes), "spikes") as Array;
            Array newSpikes = Activator.CreateInstance(spikes.GetType(), spikes.Length) as Array;

            for (var i = 0; i < spikes.Length; i++) {
                var spike = spikes.GetValue(i);
                var savedSpike = savedSpikes.GetValue(i);
                savedSpike.CopyFields(spike, "Parent");
                newSpikes.SetValue(savedSpike, i);
            }

            loaded.SetField(typeof(TriggerSpikes), "spikes", newSpikes);
        }
    }
}