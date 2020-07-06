using Celeste.Mod.SpeedrunTool.Extensions;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.PlatformActions.SolidActions {
    public class FloatySpaceBlockRestoreAction : AbstractRestoreAction {
        public FloatySpaceBlockRestoreAction() : base(typeof(FloatySpaceBlock)) { }
        
        public override void AfterEntityCreateAndUpdate1Frame(Entity loadedEntity, Entity savedEntity) {
            FloatySpaceBlock loaded = (FloatySpaceBlock) loadedEntity;
            FloatySpaceBlock saved = (FloatySpaceBlock) savedEntity;
            
            loaded.CopyFields(saved, "yLerp", "sinkTimer", "sineWave", "dashEase", "dashDirection");
        }
    }
}