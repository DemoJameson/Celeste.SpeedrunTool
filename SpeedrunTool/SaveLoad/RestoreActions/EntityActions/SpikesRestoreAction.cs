using Celeste.Mod.SpeedrunTool.Extensions;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.EntityActions {
    public class SpikesRestoreAction : AbstractRestoreAction {
        public SpikesRestoreAction() : base(typeof(Spikes)) { }
        
        public override void AfterEntityCreateAndUpdate1Frame(Entity loadedEntity, Entity savedEntity) {
            Spikes loaded = (Spikes) loadedEntity;
            Spikes saved = (Spikes) savedEntity;

            loaded.VisibleWhenDisabled = saved.VisibleWhenDisabled;
            loaded.CopyFields(saved,  "imageOffset");
        }
    }
}