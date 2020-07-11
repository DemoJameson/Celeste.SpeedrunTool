using System;
using Celeste.Mod.Entities;
using Celeste.Mod.SpeedrunTool.Extensions;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions {
    public class TriggerSpikesRestoreAction : RestoreAction {
        public TriggerSpikesRestoreAction() : base(typeof(Entity)) { }

        // spikes 是个 ValueType 数组，所以不能单纯的修改元素内容，得整个替换
        public override void AfterEntityAwake(Entity loadedEntity, Entity savedEntity) {
            if (!(loadedEntity is TriggerSpikes) && !(loadedEntity is TriggerSpikesOriginal)) return;
            
            Type type = loadedEntity.GetType();
            
            Array loadedSpikes = loadedEntity.GetField(type, "spikes") as Array;
            Array savedSpikes = savedEntity.GetField(type, "spikes") as Array;
            Array newSpikes = Activator.CreateInstance(loadedSpikes.GetType(), loadedSpikes.Length) as Array;
            
            for (int i = 0; i < loadedSpikes.Length; i++) {
                object spike = loadedSpikes.GetValue(i);
                object savedSpike = savedSpikes.GetValue(i);
                savedSpike.CopyFields(spike, "Parent");
                newSpikes.SetValue(savedSpike, i);
            }
            
            loadedEntity.SetField(type, "spikes", newSpikes);
        }
    }
}