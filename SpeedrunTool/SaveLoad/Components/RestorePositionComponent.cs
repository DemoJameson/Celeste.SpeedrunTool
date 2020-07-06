using System.Collections;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Components {
    public class RestorePositionComponent : Coroutine {
        public RestorePositionComponent(Entity self, Entity saved) : base(RestorePosition(self, saved)) { }

        private static IEnumerator RestorePosition(Entity self, Entity saved) {
            self.Position = saved.Position;
            self.Collidable = saved.Collidable;
            self.Visible = saved.Visible;
            yield break;
        }
    }
}