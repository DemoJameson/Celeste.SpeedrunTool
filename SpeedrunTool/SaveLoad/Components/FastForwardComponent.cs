using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Components {
    public class FastForwardComponent<T> : Component where T : Entity {
        private readonly T savedEntity;
        private readonly FastForwardAction onFastForward;

        public FastForwardComponent(T savedEntity, FastForwardAction onFastForward) : base(true, false) {
            this.savedEntity = savedEntity;
            this.onFastForward = onFastForward;
        }

        public override void EntityAdded(Scene scene) {
            scene.Add(new FastForwardEntity((T) Entity, savedEntity, onFastForward));
        }
        
        public delegate void FastForwardAction(T entity, T savedEntity);

        class FastForwardEntity : Entity {
            private bool isFastForward;
            private readonly T entity;
            private readonly T savedEntity;
            private readonly FastForwardAction onFastForward;

            internal FastForwardEntity(T entity, T savedEntity, FastForwardAction onFastForward) {
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
        }
    }
}