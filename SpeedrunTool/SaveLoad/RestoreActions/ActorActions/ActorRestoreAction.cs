using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.ActorActions {
    public class ActorRestoreAction : AbstractRestoreAction {
        public ActorRestoreAction() : base(typeof(Actor), new List<AbstractRestoreAction> {
            new PlayerRestoreAction()
        }) { }

        public override void AfterEntityCreateAndUpdate1Frame(Entity loadedEntity, Entity savedEntity) {
            Actor loaded = (Actor) loadedEntity;
            Actor saved = (Actor) savedEntity;

            loaded.TreatNaive = saved.TreatNaive;
            loaded.IgnoreJumpThrus = saved.IgnoreJumpThrus;
            loaded.AllowPushing = saved.AllowPushing;
            loaded.LiftSpeedGraceTime = saved.LiftSpeedGraceTime;
            // loaded.LiftSpeed = saved.LiftSpeed;

            loaded.CopyFields(saved,
                "currentLiftSpeed", "lastLiftSpeed", "liftSpeedTimer", "movementCounter"
            );
        }
    }
}