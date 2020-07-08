using Monocle;

namespace Celeste.Mod.SpeedrunTool.Extensions {
    // TODO 删除
    public static class EntityExtensions {
        public static void CopyEntity<T>(this T entity, T otherEntity) where T : Entity {
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