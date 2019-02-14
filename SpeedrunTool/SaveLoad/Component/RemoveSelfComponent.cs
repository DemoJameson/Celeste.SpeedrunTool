using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Component
{
    public class RemoveSelfComponent : Monocle.Component
    {
        public RemoveSelfComponent() : base(true, true) { }

        public override void Render()
        {
            Entity.Collidable = false;
            Entity.Visible = false;
            Visible = false;
        }

        public override void Update()
        {
            Entity?.RemoveSelf();
            RemoveSelf();
        }
    }
}