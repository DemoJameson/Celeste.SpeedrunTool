
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.PlatformActions.SolidActions;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.PlatformActions {
    public class PlatformRestoreAction : AbstractRestoreAction {
        public PlatformRestoreAction() : base(typeof(Platform), new List<AbstractRestoreAction> {
            new SolidRestoreAction(),
        }) { }

        public override void AfterEntityCreateAndUpdate1Frame(Entity loadedEntity, Entity savedEntity) {
            Platform loaded = (Platform) loadedEntity;
            Platform saved = (Platform) savedEntity;

            // TODO protected List<StaticMover> staticMovers
        }
    }
}