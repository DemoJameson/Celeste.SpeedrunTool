using Monocle;

namespace Celeste.Mod.SpeedrunTool.Extensions {
    public static class EntityExtensions {
        public static void CopyFrom<T>(this T entity, T otherEntity) where T : Entity {
            entity.Active = otherEntity.Active;
            entity.Visible = otherEntity.Visible;
            entity.Collidable = otherEntity.Collidable;
            entity.Position = otherEntity.Position;
            entity.Tag = otherEntity.Tag;
            entity.Collider = otherEntity.Collider;
            entity.Depth = otherEntity.Depth;
        }
    }
}