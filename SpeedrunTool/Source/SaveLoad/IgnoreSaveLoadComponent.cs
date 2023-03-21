using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.RoomTimer;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;

[Tracked]
public class IgnoreSaveLoadComponent : Component {
    private static readonly Dictionary<Entity, bool> IgnoredEntities = new();

    private bool based;

    // backward compatibility
    public IgnoreSaveLoadComponent() : base(false, false) { }

    public IgnoreSaveLoadComponent(bool based) : base(false, false) {
        this.based = based;
    }

    public static void RemoveAll(Level level) {
        IgnoredEntities.Clear();
        level.Tracker.GetComponentsCopy<IgnoreSaveLoadComponent>().ForEach(component => {
            IgnoredEntities.Add(component.Entity, ((IgnoreSaveLoadComponent)component).based);
            level.RemoveImmediately(component.Entity);
        });
    }

    public static void ReAddAll(Level level) {
        foreach (KeyValuePair<Entity, bool> pair in IgnoredEntities) {
            level.AddImmediately(pair.Key, pair.Value);
            if (pair.Key is EndPoint point) {
                point.ReadyForTime();
            }
        }

        IgnoredEntities.Clear();
    }
}

[Tracked]
public class ClearBeforeSaveComponent : Component {
    public ClearBeforeSaveComponent() : base(false, false) { }

    public static void RemoveAll(Level level) {
        level.Tracker.GetComponentsCopy<ClearBeforeSaveComponent>().ForEach(component => level.RemoveImmediately(component.Entity));
    }
}

internal static class EntityExtensions {
    public static void AddImmediately(this Level level, Entity entity, bool based = false) {
        EntityList entityList = level.Entities;

        if (entityList.current.Add(entity)) {
            entityList.entities.Add(entity);
            level.TagLists.EntityAdded(entity);
            level.Tracker.EntityAdded(entity);
            if (based) {
                entity.BasedAdded(level);
            } else {
                entity.Added(level);
            }
        }
    }

    public static void RemoveImmediately(this Level level, Entity entity, bool based = false) {
        EntityList entityList = level.Entities;

        if (entityList.current.Remove(entity)) {
            entityList.entities.Remove(entity);
            if (based) {
                entity.BasedRemoved(level);
            } else {
                entity.Removed(level);
            }

            level.TagLists.EntityRemoved(entity);
            level.Tracker.EntityRemoved(entity);
            Engine.Pooler.EntityRemoved(entity);
        }
    }

    private static void BasedAdded(this Entity entity, Level level) {
        entity.Scene = level;
        if (entity.Components != null) {
            foreach (Component component in entity.Components) {
                component.EntityAdded(level);
            }
        }

        level.SetActualDepth(entity);
    }

    private static void BasedRemoved(this Entity entity, Level level) {
        if (entity.Components != null) {
            foreach (Component component in entity.Components) {
                component.EntityRemoved(level);
            }
        }

        entity.Scene = null;
    }
}