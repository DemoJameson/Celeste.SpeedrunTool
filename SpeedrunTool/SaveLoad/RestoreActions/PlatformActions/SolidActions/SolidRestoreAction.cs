using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.PlatformActions.SolidActions {
    public class SolidRestoreAction : AbstractRestoreAction {
        public SolidRestoreAction() : base(typeof(Solid), new List<AbstractRestoreAction> {
            new FloatySpaceBlockRestoreAction(),
            new MoveBlockRestoreAction()
        }) { }

        public override void AfterEntityCreateAndUpdate1Frame(Entity loadedEntity, Entity savedEntity) {
            Solid loaded = (Solid) loadedEntity;
            Solid saved = (Solid) savedEntity;

            loaded.Speed = saved.Speed;
            loaded.AllowStaticMovers = saved.AllowStaticMovers;
            loaded.EnableAssistModeChecks = saved.EnableAssistModeChecks;
            loaded.DisableLightsInside = saved.DisableLightsInside;
            loaded.StopPlayerRunIntoAnimation = saved.StopPlayerRunIntoAnimation;
            loaded.SquishEvenInAssistMode = saved.SquishEvenInAssistMode;

            HashSet<Actor> loadedRiders = loaded.GetField("riders") as HashSet<Actor>;
            HashSet<Actor> savedRiders = saved.GetField("riders") as HashSet<Actor>;

            if (loadedRiders == null || savedRiders == null) return;

            loadedRiders.Clear();
            foreach (Actor savedActor in savedRiders) {
                if (loaded.Scene.FindFirst(savedActor.GetEntityId2()) is Actor actor) {
                    loadedRiders.Add(actor);
                }
            }
        }
    }
}