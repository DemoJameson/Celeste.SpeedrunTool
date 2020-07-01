namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public abstract class AbstractEntityAction {
        protected static bool IsLoadStart => StateManager.Instance.IsLoadStart;
        protected static bool IsFrozen => StateManager.Instance.IsLoadFrozen;
        protected static bool IsLoading => StateManager.Instance.IsLoading;
        protected static bool IsLoadComplete => StateManager.Instance.IsLoadComplete;

        public abstract void OnQuickSave(Level level);
        public abstract void OnClear();
        public abstract void OnLoad();
        public abstract void OnUnload();

        public virtual void OnInit() { }

        public virtual void OnQuickLoadStart(Level level) { }
        public virtual void OnQuickLoading(Level level, Player player, Player savedPlayer) { }
    }
}