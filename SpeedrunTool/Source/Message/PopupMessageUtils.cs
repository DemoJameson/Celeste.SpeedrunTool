namespace Celeste.Mod.SpeedrunTool.Message {
    public static class PopupMessageUtils {
        public static void Show(Level level, string message, string dialogId) {
            if (SpeedrunToolModule.Settings.PopupMessageStyle == PopupMessageStyle.Tooltip) {
                Tooltip.Show(level, message);
            } else {
                if (dialogId == null) {
                    NonFrozenMiniTextbox.Show(level, null, message);
                } else {
                    NonFrozenMiniTextbox.Show(level, dialogId, null);
                }
            }
        }


        public static void ShowOptionState(Level level, string option, string state) {
            string message = string.Format(Dialog.Get(DialogIds.OptionState), option, state);
            Show(level, message, null);
        }
    }

    public enum PopupMessageStyle {
       Tooltip,
       DialogBox
    }
}