using System;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Components {
    // 因为有些物体在构造函数中恢复状态后又 Update 了一帧，位置发生了偏移，所以再需要恢复一次以便达成与保存的状态一致

    public class RestoreState : Component{
        private readonly Action action;
        private readonly RunType runType;

        public RestoreState(RunType runType, Action action) : base(true, true) {
            this.action = action;
            this.runType = runType;
        }

        public override void Added(Entity entity) {
            base.Added(entity);

            if (runType.HasFlag(RunType.Added)) {
                action();
            }
        }

        public override void Update() {
            if (runType.HasFlag(RunType.LoadStart) && StateManager.Instance.IsLoadStart) {
                action();
            }

            if (runType.HasFlag(RunType.LoadComplete) && StateManager.Instance.IsLoadComplete) {
                action();
                RemoveSelf();
            } 
        }
    }

    [Flags]
    public enum RunType {
        Added = 0,
        LoadStart = 1,
        LoadComplete = 2
    }
}