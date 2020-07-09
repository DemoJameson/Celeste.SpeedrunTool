using System.Collections.Generic;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public abstract class ComponentAction {
        public static readonly List<ComponentAction> All = new List<ComponentAction> {
            new CoroutineAction(),
            new SoundSourceAction(),
            new WindControllerAction(),
        };

        protected static bool IsLoadStart => StateManager.Instance.IsLoadStart;

        public virtual void OnSaveSate(Level level) { }
        public virtual void OnClear() { }
        public virtual void OnLoad() { }
        public virtual void OnUnload() { }

        public virtual void OnLoadStart(Level level, Player player, Player savedPlayer) { }
        public virtual void OnLoading(Level level, Player player, Player savedPlayer) { }
    }
}