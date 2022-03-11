using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Celeste.Mod.SpeedrunTool.RoomTimer;

namespace Celeste.Mod.SpeedrunTool.SaveLoad; 

[Tracked]
public class IgnoreSaveLoadComponent : Component {
    private static readonly HashSet<Entity> All = new();
    private static readonly ConditionalWeakTable<Entity, object> ReAdd = new();

    public IgnoreSaveLoadComponent() : base(false, false) { }

    public override void EntityAdded(Scene scene) {
        base.EntityAdded(scene);
        All.Add(Entity);
    }

    public override void EntityRemoved(Scene scene) {
        base.EntityRemoved(scene);
        All.Remove(Entity);

        // 重新添加 RemoveAll 中被移除的实体
        if (ReAdd.TryGetValue(Entity, out object _)) {
            All.Add(Entity);
            ReAdd.Remove(Entity);
        }
    }

    public override void SceneEnd(Scene scene) {
        All.Remove(Entity);
    }

    public static void RemoveAll(Level level) {
        All.Clear();
        level.Tracker.GetComponentsCopy<IgnoreSaveLoadComponent>().ForEach(component => {
            ReAdd.Add(component.Entity, null);
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
        level.Tracker.GetComponentsCopy<ClearBeforeSaveComponent>().ForEach(component => { component.Entity.RemoveSelf(); });
    }
}