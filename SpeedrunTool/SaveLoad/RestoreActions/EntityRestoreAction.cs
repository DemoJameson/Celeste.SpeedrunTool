using System;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.ActorActions;
using Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.EntityActions;
using Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.PlatformActions;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions {
    public class EntityRestoreAction : AbstractRestoreAction {
        public static List<AbstractRestoreAction> AllRestoreActions => AllRestoreActionsLazy.Value;

        private static readonly Lazy<List<AbstractRestoreAction>> AllRestoreActionsLazy =
            new Lazy<List<AbstractRestoreAction>>(
                () => {
                    List<AbstractRestoreAction> result = new List<AbstractRestoreAction>();

                    void AddThisAndSubclassRestoreActions(AbstractRestoreAction action) {
                        result.Add(action);
                        action.SubclassRestoreActions.ForEach(AddThisAndSubclassRestoreActions);
                    }

                    AddThisAndSubclassRestoreActions(Instance);

                    return result;
                });

        private static readonly EntityRestoreAction Instance = new EntityRestoreAction(
            new List<AbstractRestoreAction> {
                new ActorRestoreAction(),
                new PlatformRestoreAction(),
                
                // EntityActions
                new BoosterRestoreAction(),
                new FlyFeatherRestoreAction(),
                new KeyRestoreAction(),
                new SpikesRestoreAction(),
                new StrawberryRestoreAction(),
            }
        );

        private EntityRestoreAction(List<AbstractRestoreAction> subclassRestoreActions) : base(typeof(Entity),
            subclassRestoreActions) { }

        public override void AfterEntityCreateAndUpdate1Frame(Entity loadedEntity, Entity savedEntity) {
            loadedEntity.Active = savedEntity.Active;
            loadedEntity.Depth = savedEntity.Depth;
            loadedEntity.Collidable = savedEntity.Collidable;
            loadedEntity.Collider = savedEntity.Collider;
            loadedEntity.Position = savedEntity.Position;
            loadedEntity.Tag = savedEntity.Tag;
            loadedEntity.Visible = savedEntity.Visible;
        }
    }
}