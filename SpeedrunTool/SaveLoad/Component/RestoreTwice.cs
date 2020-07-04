using System;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Component {
    // 用于 base.Update 方法是在开头调用的 Entity
    // 一帧执行一次恢复方法
    // 第一次是为了让物体显示到相应位置
    // 第二次是因为人物是从这一帧才开始移动，而有些物体已经跑了一帧，位置发生了偏移，所以再恢复一次以便达成与保存的状态一致
    public class RestoreTwice : Monocle.Component{
        private readonly Action action;
        private int restoreTimes;

        public RestoreTwice(Action action) : base(true, true) {
            this.action = action;
        }

        public override void Update() {
            if (StateManager.Instance.IsLoadStart) {
                action();
                restoreTimes++;
            } else if(restoreTimes > 0) {
                action();
                RemoveSelf();
            }
        }
    }
    
    // 用于 base.Update 方法是在结尾调用的 Entity
    public class RestoreOnce : Monocle.Component{
        private readonly Action action;

        public RestoreOnce(Action action) : base(true, true) {
            this.action = action;
        }

        public override void Update() {
            if (!StateManager.Instance.IsLoadStart) return;
            
            action();
            RemoveSelf();
        }
    }
}