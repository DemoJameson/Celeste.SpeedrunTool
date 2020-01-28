using System;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Component {
    public class FastForwardComponent<T> : Monocle.Component where T : Entity {
        private readonly T savedEntity;
        private readonly FastForwardEntity<T>.FastForwardAction onFastForward;

        public FastForwardComponent(T savedEntity, FastForwardEntity<T>.FastForwardAction onFastForward) : base(true, false) {
            this.savedEntity = savedEntity;
            this.onFastForward = onFastForward;
        }

        public override void EntityAdded(Scene scene) {
            scene.Add(new FastForwardEntity<T>((T) Entity, savedEntity, onFastForward));
        }
    }
}