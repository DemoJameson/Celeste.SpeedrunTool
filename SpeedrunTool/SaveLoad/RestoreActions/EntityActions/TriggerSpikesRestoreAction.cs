using System;
using Celeste.Mod.Entities;
using Celeste.Mod.SpeedrunTool.Extensions;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.EntityActions {
    public class TriggerSpikesRestoreAction : RestoreAction {
        public TriggerSpikesRestoreAction() : base(typeof(Entity)) { }

        public override void AfterEntityCreateAndUpdate1Frame(Entity loadedEntity, Entity savedEntity) {
            if (!(loadedEntity is TriggerSpikes) && !(loadedEntity is TriggerSpikesOriginal)) return;
            
            Type type = loadedEntity.GetType();

            Array loadedSpikes = loadedEntity.GetField(type, "spikes") as Array;
            Array savedSpikes = savedEntity.GetField(type, "spikes") as Array;
            Array newSpikes = Activator.CreateInstance(loadedSpikes.GetType(), loadedSpikes.Length) as Array;

            for (int i = 0; i < loadedSpikes.Length; i++) {
                object spike = loadedSpikes.GetValue(i);
                object savedSpike = savedSpikes.GetValue(i);
                savedSpike.CopyFields(type, spike, "Parent");
                newSpikes.SetValue(savedSpike, i);
            }

            loadedEntity.SetField(type, "spikes", newSpikes);
        }
    }
}