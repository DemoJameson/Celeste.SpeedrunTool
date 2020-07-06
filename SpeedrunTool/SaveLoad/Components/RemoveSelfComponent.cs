using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Components {
    public class RemoveSelfComponent : Component {
        public RemoveSelfComponent() : base(true, false) { }

        public override void Added(Entity entity) {
            base.Added(entity);
            entity.Collidable = false;
            entity.Visible = false;
        }

        public override void Update() {
            Entity?.RemoveSelf();
            RemoveSelf();
        }
    }
}