using System.Collections;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Component {
    public class RestorePositionComponent : Coroutine {
        public RestorePositionComponent(Entity self, Entity saved) : base(RestorePosition(self, saved)) { }

        private static IEnumerator RestorePosition(Entity self, Entity saved) {
            self.Position = saved.Position;
            yield break;
        }
    }
}