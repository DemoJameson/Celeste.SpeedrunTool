namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public abstract class AbstractEntityAction {
        protected static bool IsLoadStart => StateManager.Instance.IsLoadStart;
        protected static bool IsFrozen => StateManager.Instance.IsLoadFrozen;

        public abstract void OnQuickSave(Level level);
        public abstract void OnClear();
        public abstract void OnLoad();
        public abstract void OnUnload();

        public virtual void OnQuickLoadStart(Level level, Player player, Player savedPlayer) {
        }
        public virtual void OnQuickLoading(Level level, Player player, Player savedPlayer) { }
    }
}