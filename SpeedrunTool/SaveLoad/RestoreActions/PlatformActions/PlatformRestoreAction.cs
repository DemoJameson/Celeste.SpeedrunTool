
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.PlatformActions.JumpThruActions;
using Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.PlatformActions.SolidActions;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.PlatformActions {
    public class PlatformRestoreAction : AbstractRestoreAction {
        public PlatformRestoreAction() : base(typeof(Platform), new List<AbstractRestoreAction> {
            new JumpThruRestoreAction(),
            new SolidRestoreAction(),
        }) { }

        public override void AfterEntityCreateAndUpdate1Frame(Entity loadedEntity, Entity savedEntity) {
            Platform loaded = (Platform) loadedEntity;
            Platform saved = (Platform) savedEntity;

            // TODO protected List<StaticMover> staticMovers
            loaded.CopyFields(saved,
                "movementCounter", "shakeAmount", "shaking", "shakeTimer"
                );

            loaded.LiftSpeed = saved.LiftSpeed;
            loaded.Safe = saved.Safe;
            loaded.BlockWaterfalls = saved.BlockWaterfalls;
            loaded.SurfaceSoundIndex = saved.SurfaceSoundIndex;
            loaded.SurfaceSoundPriority = saved.SurfaceSoundPriority;
            loaded.BlockWaterfalls = saved.BlockWaterfalls;
            loaded.BlockWaterfalls = saved.BlockWaterfalls;
            loaded.BlockWaterfalls = saved.BlockWaterfalls;
        }
    }
}