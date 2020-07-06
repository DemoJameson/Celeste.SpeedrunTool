using Celeste.Mod.SpeedrunTool.Extensions;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.EntityActions {
    public class FlyFeatherRestoreAction : AbstractRestoreAction {
        public FlyFeatherRestoreAction() : base(typeof(FlyFeather)) { }
        
        public override void AfterEntityCreateAndUpdate1Frame(Entity loadedEntity, Entity savedEntity) {
            FlyFeather loaded = (FlyFeather) loadedEntity;
            FlyFeather saved = (FlyFeather) savedEntity;
            
            loaded.CopySprite(saved, "sprite");
            loaded.CopyImage(saved, "outline");
            loaded.CopyFields(saved,  "respawnTimer", "moveWiggleDir");
        }
    }
}