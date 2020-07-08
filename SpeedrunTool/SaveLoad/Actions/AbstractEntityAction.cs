namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public abstract class AbstractEntityAction {
        protected static bool IsLoadStart => StateManager.Instance.IsLoadStart;
        protected static bool IsFrozen => StateManager.Instance.IsLoadFrozen;

        public abstract void OnSaveSate(Level level);
        public abstract void OnClear();
        public abstract void OnLoad();
        public abstract void OnUnload();

        public virtual void OnLoadStart(Level level, Player player, Player savedPlayer) {
        }
        public virtual void OnLoading(Level level, Player player, Player savedPlayer) { }
    }
}