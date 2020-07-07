using System;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.ActorActions;
using Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.EntityActions;
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
                new PlayerRestoreAction(),
                new KeyRestoreAction(),
                new StrawberryRestoreAction(),
            }
        );

        private EntityRestoreAction(List<AbstractRestoreAction> subclassRestoreActions) : base(typeof(Entity),
            subclassRestoreActions) { }

        public override void AfterEntityCreateAndUpdate1Frame(Entity loadedEntity, Entity savedEntity) {
            // Player 需要特殊处理，由 PlayerRestoreAction 负责
            if (loadedEntity is Player) return;

            // CrystalStaticSpinner 看不见的地方等于不存在，这么处理就行了
            if (loadedEntity is CrystalStaticSpinner spinner) {
                loadedEntity.Position = savedEntity.Position;
                loadedEntity.CopyFields(typeof(CrystalStaticSpinner), savedEntity, "expanded");
                return;
            }
            
            loadedEntity.CopyAllFrom(savedEntity, typeof(Entity));

        }
    }
}