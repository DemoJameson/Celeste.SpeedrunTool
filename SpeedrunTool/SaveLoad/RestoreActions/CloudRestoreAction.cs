using Celeste.Mod.SpeedrunTool.Extensions;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions {
    public class CloudRestoreAction : AbstractRestoreAction {
        public CloudRestoreAction() : base(typeof(Cloud)) { }
        
        public override void AfterEntityCreateAndUpdate1Frame(Entity loadedEntity, Entity savedEntity) {
            Cloud loaded = (Cloud) loadedEntity;
            Cloud saved = (Cloud) savedEntity;
            
            loaded.CopyEntity(saved);
            loaded.CopySprite(saved, "sprite");
            loaded.CopyFields(saved, "waiting", "returning", "timer", "scale", "canRumble", "respawnTimer",
                "speed");
        }
    }
}