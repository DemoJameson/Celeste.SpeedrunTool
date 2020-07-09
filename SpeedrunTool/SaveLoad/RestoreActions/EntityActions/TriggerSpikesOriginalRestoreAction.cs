using System;
using Celeste.Mod.Entities;
using Celeste.Mod.SpeedrunTool.Extensions;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.EntityActions {
    public class TriggerSpikesOriginalRestoreAction : RestoreAction {
        public TriggerSpikesOriginalRestoreAction() : base(typeof(TriggerSpikesOriginal)) { }

        public override void AfterEntityCreateAndUpdate1Frame(Entity loadedEntity, Entity savedEntity) {
            TriggerSpikesOriginal loaded = (TriggerSpikesOriginal) loadedEntity;
            TriggerSpikesOriginal saved = (TriggerSpikesOriginal) savedEntity;
            
            Array loadedSpikes = loaded.GetField("spikes") as Array;
            Array savedSpikes = saved.GetField("spikes") as Array;
            Array newSpikes = Activator.CreateInstance(loadedSpikes.GetType(), loadedSpikes.Length) as Array;

            for (int i = 0; i < loadedSpikes.Length; i++) {
                object spike = loadedSpikes.GetValue(i);
                object savedSpike = savedSpikes.GetValue(i);
                savedSpike.CopyFields(spike, "Parent");
                newSpikes.SetValue(savedSpike, i);
            }

            loaded.SetField("spikes", newSpikes);
        }
    }
}