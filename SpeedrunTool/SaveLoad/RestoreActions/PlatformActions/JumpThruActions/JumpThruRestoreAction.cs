
using System.Collections.Generic;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.PlatformActions.JumpThruActions {
    public class JumpThruRestoreAction : AbstractRestoreAction {
        public JumpThruRestoreAction() : base(typeof(JumpThru), new List<AbstractRestoreAction> {
            new CloudRestoreAction()
        }) { }

        public override void AfterEntityCreateAndUpdate1Frame(Entity loadedEntity, Entity savedEntity) {
            JumpThru loaded = (JumpThru) loadedEntity;
            JumpThru saved = (JumpThru) savedEntity;

            // There are no fields in JumpThru
        }
    }
}