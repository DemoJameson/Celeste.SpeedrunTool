using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.RoomTimer;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad {
    [Tracked]
    public class IgnoreSaveLoadComponent : Component {
        private static readonly HashSet<Entity> All = new();
        public IgnoreSaveLoadComponent() : base(false, false) { }

        public override void EntityAdded(Scene scene) {
            base.EntityAdded(scene);
            if (StateManager.Instance.State == StateManager.States.None) {
                All.Add(Entity);
            }
        }

        public override void EntityRemoved(Scene scene) {
            base.EntityRemoved(scene);
            if (StateManager.Instance.State == StateManager.States.None) {
                All.Remove(Entity);
            }
        }

        public override void SceneEnd(Scene scene) {
            if (StateManager.Instance.State == StateManager.States.None) {
                All.Remove(Entity);
            }
        }

        public static void RemoveAll(Level level) {
            All.Clear();
            level.Tracker.GetComponentsCopy<IgnoreSaveLoadComponent>().ForEach(component => {
                All.Add(component.Entity);
                component.Entity.RemoveSelf();
            });
        }

        public static void ReAddAll(Level level) {
            foreach (Entity entity in All) {
                level.Add(entity);
                if (entity is EndPoint point) {
                    point.ReadyForTime();
                }
            }
        }
    }

    [Tracked]
    public class ClearBeforeSaveComponent : Component {
        public ClearBeforeSaveComponent() : base(false, false) { }

        public static void RemoveAll(Level level) {
            level.Tracker.GetComponentsCopy<ClearBeforeSaveComponent>().ForEach(component => {
                component.Entity.RemoveSelf();
            });
        }
    }
}