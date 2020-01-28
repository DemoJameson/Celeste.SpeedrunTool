using System;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Component {
    public class FastForwardEntity<T> : Monocle.Entity where T : Entity {
        private bool isFastForward;
        private readonly T entity;
        private readonly T savedEntity;
        private readonly FastForwardAction onFastForward;

        public FastForwardEntity(T entity, T savedEntity, FastForwardAction onFastForward) {
            this.entity = entity;
            this.savedEntity = savedEntity;
            this.onFastForward = onFastForward;
        }

        public override void Update() {
            if (!isFastForward) {
                isFastForward = true;
                
                onFastForward(entity, savedEntity);
                RemoveSelf();
            }
        }

        public delegate void FastForwardAction(T entity, T savedEntity);
    }
}