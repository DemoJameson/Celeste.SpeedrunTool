namespace Celeste.Mod.SpeedrunTool.Message;

public static class PopupMessageUtils {
    public static void Show(string message, string dialogId) {
        if (ModSettings.PopupMessageStyle == PopupMessageStyle.Tooltip || Engine.Scene is not Level) {
            Tooltip.Show(message);
        } else {
            if (dialogId == null) {
                NonFrozenMiniTextbox.Show(null, message);
            } else {
                NonFrozenMiniTextbox.Show(dialogId, null);
            }
        }
    }


    public static void ShowOptionState(string option, string state) {
        string message = string.Format(Dialog.Get(DialogIds.OptionState), option, state);
        Show(message, null);
    }
}

public enum PopupMessageStyle {
    Tooltip,
    DialogBox
}