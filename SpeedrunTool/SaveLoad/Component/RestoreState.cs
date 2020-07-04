using System;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Component {
    // 因为有些物体在构造函数中恢复状态后又 Update 了一帧，位置发生了偏移，所以再需要恢复一次以便达成与保存的状态一致

    public class RestoreState : Monocle.Component{
        private readonly Action action;
        private readonly bool added;
        private readonly bool loadStart;
        private readonly bool loadComplete;

        public RestoreState(Action action, bool added = false, bool loadStart = false, bool loadComplete = false) : base(true, true) {
            this.action = action;
            this.added = added;
            this.loadStart = loadStart;
            this.loadComplete = loadComplete;
        }

        public override void Added(Entity entity) {
            base.Added(entity);

            if (added) {
                action();
            }
        }

        public override void Update() {
            if (loadStart && StateManager.Instance.IsLoadStart) {
                action();
            }

            if (loadComplete && StateManager.Instance.IsLoadComplete) {
                action();
                RemoveSelf();
            } 
        }
    }
}