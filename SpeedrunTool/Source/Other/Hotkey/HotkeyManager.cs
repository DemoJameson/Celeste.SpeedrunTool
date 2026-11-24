#nullable enable annotations
using Celeste.Mod.SpeedrunTool.Message;
using Celeste.Mod.SpeedrunTool.ModInterop;
using Celeste.Mod.SpeedrunTool.Utils;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Celeste.Mod.UI;
using CelesteSettings = Celeste.Settings;

namespace Celeste.Mod.SpeedrunTool.Other;

internal static class HotkeyManager {
    private static readonly Hotkey[] Hotkeys = (Hotkey[])Enum.GetValues(typeof(Hotkey));

    [Load]
    private static void Load() {
        On.Monocle.MInput.Update += MInputOnUpdate;

        Hotkey.ToggleFullscreen.RegisterPressedAction(_ => {
            CelesteSettings.Instance.Fullscreen = !CelesteSettings.Instance.Fullscreen;
            CelesteSettings.Instance.ApplyScreen();
            UserIO.SaveHandler(false, true);
        });

        Hotkey.ToggleHotkeys.RegisterPressedAction(_ => {
            ModSettings.Hotkeys = !ModSettings.Hotkeys;
            SpeedrunToolModule.Instance.SaveSettings();
            string state = (ModSettings.Hotkeys ? DialogIds.On : DialogIds.Off).DialogClean();
            PopupMessageUtils.ShowOptionState(DialogIds.Hotkeys.DialogClean(), state);
        });
    }

    [Initialize]
    private static void Initialize() {
        foreach (HotkeyConfig hotkeyConfig in HotkeyRegistry.All.Values) {
            hotkeyConfig.UpdateVirtualButton();
        }
    }

    [Unload]
    private static void Unload() {
        On.Monocle.MInput.Update -= MInputOnUpdate;
    }

    private static void MInputOnUpdate(On.Monocle.MInput.orig_Update orig) {
        orig();

        if (Engine.Scene is not { } scene || !ModSettings.Enabled) {
            return;
        }

        if (MainThreadHelper.IsMainThread && AssetReloadHelper.IsReloading) {
            return;
        }

        HotkeyDriver.Update();

        // evaluate canInvoke lazily to avoid a ton of checks
        bool? canInvoke = null;

        if (ModSettings.Hotkeys) {
            foreach (Hotkey hotkey in Hotkeys) {
                if (!hotkey.Pressed()) {
                    continue;
                }

                canInvoke ??= CanInvoke(scene);
                if (canInvoke == true) {
                    hotkey.GetHotkeyConfig().OnPressed?.Invoke(scene);
                    // Logger.Log("SpeedrunTool", $"{hotkey} get pressed");
                }
            }
        }
        else if (Hotkey.ToggleHotkeys.Pressed()) {
            canInvoke ??= CanInvoke(scene);
            if (canInvoke == true) {
                Hotkey.ToggleHotkeys.GetHotkeyConfig().OnPressed?.Invoke(scene);
            }
        }
    }

    internal static bool CanInvoke(Scene scene) {
        if (TasUtils.Running) {
            return false;
        }

        if (!MInput.ControllerHasFocus &&
            scene is Overworld {
                Current: OuiFileNaming { UseKeyboardInput: true }
                      or OuiModOptionString { UseKeyboardInput: true }
            }) {
            // 避免输入文字时触发快捷键
            return false;
        }
        if (scene?.Tracker is { } tracker && (tracker.GetEntity<HotkeyConfigUi>() != null
                                           || tracker.GetEntity<KeyboardConfigUI>() != null
                                           || tracker.GetEntity<ButtonConfigUI>() != null)) {
            return false;
        }
        if (Engine.Commands.Open || CelesteNetChatting) {
            // Prevent triggering hotkeys while writing text
            return false;
        }
        if (ImGuiWantsKeyboard) {
            // ImGui 系工具 (MappingUtils 的输入框/代码编辑器、auspicioushelper 的 channels tab、ImGuiHelper 自己的窗口)
            return false;
        }
        if (TextInputOpen) {
            // ImGuiHelper is always subscribed, so ignore it
            return false;
        }

        return true;
    }

    #region CanInvoke helpers
    /// A TextInput.OnInput subscriber that does not mean "a text box is open".
    /// ImGuiHelper subscribes once in ImGuiRenderer's constructor (SetupInput) to feed ImGui's
    /// character queue and never unsubscribes, so it has to be ignored. It is matched by
    /// assembly instead of by the compiler generated type name (ImGuiRenderer+<>c), which
    /// changes whenever that lambda captures something.
    private static readonly Lazy<Assembly?> AlwaysSubscribedTextOnInputAssembly = new(() =>
        ModUtils.GetType("ImGuiHelper", "Celeste.Mod.ImGuiHelper.ImGuiHelperModule")?.Assembly);

    private static bool IsAlwaysSubscribedTextOnInput(Delegate handler) {
        Assembly? alwaysSubscribed = AlwaysSubscribedTextOnInputAssembly.Value;
        if (alwaysSubscribed == null) {
            return false;
        }

        return handler.Target?.GetType().Assembly == alwaysSubscribed
               || handler.Method.DeclaringType?.Assembly == alwaysSubscribed;
    }

    private static readonly Lazy<Func<bool>> CelesteNetChattingAccessor = new(() => {
        try {
            Type? moduleType = ModUtils.GetType("CelesteNet.Client", "Celeste.Mod.CelesteNet.Client.CelesteNetClientModule");
            Type? contextType = ModUtils.GetType("CelesteNet.Client", "Celeste.Mod.CelesteNet.Client.CelesteNetClientContext");
            Type? chatType = ModUtils.GetType("CelesteNet.Client", "Celeste.Mod.CelesteNet.Client.Components.CelesteNetChatComponent");
            FieldInfo? instanceField = moduleType?.GetFieldInfo("Instance");
            FieldInfo? contextField = moduleType?.GetFieldInfo("Context");
            FieldInfo? chatField = contextType?.GetFieldInfo("Chat");
            MethodInfo? activeGetter = chatType?.GetPropertyInfo("Active")?.GetGetMethod();
            if (instanceField == null || contextField == null || chatField == null || activeGetter == null) {
                return static () => false;
            }

            Expression instance = Expression.Field(null, instanceField);
            Expression context = Expression.Field(instance, contextField);
            Expression chat = Expression.Field(context, chatField);
            Expression body = Expression.AndAlso(
                Expression.NotEqual(instance, Expression.Constant(null, instance.Type)),
                Expression.AndAlso(
                    Expression.NotEqual(context, Expression.Constant(null, context.Type)),
                    Expression.AndAlso(
                        Expression.NotEqual(chat, Expression.Constant(null, chat.Type)),
                        Expression.Call(chat, activeGetter))));

            // Compile the exception guard into the delegate: a failure here can never be cached as
            // a faulted Lazy and the property getter stays a plain call.
            return Expression.Lambda<Func<bool>>(
                Expression.TryCatch(body, Expression.Catch(typeof(Exception), Expression.Constant(false)))).Compile();
        }
        catch {
            return static () => false;
        }
    });

    /// Checks if the CelesteNet chat is open
    private static bool CelesteNetChatting => CelesteNetChattingAccessor.Value();

    private static readonly Lazy<Func<bool>> ImGuiWantsKeyboardAccessor = new(() => {
        try {
            FieldInfo? managerField = ModUtils.GetType("ImGuiHelper", "Celeste.Mod.ImGuiHelper.ImGuiHelperModule")?.GetFieldInfo("imGuiManager");
            MethodInfo? getter = ModUtils.GetType("ImGuiHelper", "Celeste.Mod.ImGuiHelper.ImGuiManager")?.GetPropertyInfo("WantCaptureKeyboard")?.GetGetMethod();
            if (managerField == null || getter == null) {
                return static () => false;
            }

            Expression manager = Expression.Field(null, managerField);
            Expression body = Expression.AndAlso(
                Expression.NotEqual(manager, Expression.Constant(null, manager.Type)),
                Expression.Call(manager, getter));

            // Compile the exception guard into the delegate: a failure here can never be cached as
            // a faulted Lazy and the property getter stays a plain call.
            return Expression.Lambda<Func<bool>>(
                Expression.TryCatch(body, Expression.Catch(typeof(Exception), Expression.Constant(false)))).Compile();
        }
        catch {
            return static () => false;
        }
    });

    /// Checks if an ImGui window is currently capturing the keyboard.
    /// ImGuiHelper copies ImGui.GetIO().WantCaptureKeyboard onto this property at the end of
    /// every RenderHandlers pass, so reading it is the same question without touching ImGui
    /// directly. Covers ImGui based tools that never go through TextInput, e.g. MappingUtils'
    /// own input boxes and code editor, and auspicioushelper's "channels" tab inside it.
    private static bool ImGuiWantsKeyboard => ImGuiWantsKeyboardAccessor.Value();

    private static readonly Lazy<FieldInfo?> f_TextInputOnInput = new(() => typeof(TextInput).GetFieldInfo("_OnInput"));

    // Delegates are immutable: _OnInput's identity only changes when someone subscribes or
    // unsubscribes, so the invocation list only needs to be inspected when that happens.
    private static Action<char>? lastTextInputOnInput;
    private static bool lastTextInputHasForeignSubscriber;

    /// Checks if a text box is open, i.e. someone subscribed to TextInput.OnInput that is not one
    /// of the always-subscribed helpers. TextInput.Initialized is not used on purpose: Everest
    /// initializes text input once at startup, so it is true for the whole session.
    private static bool TextInputOpen {
        get {
            if (f_TextInputOnInput.Value?.GetValue(null) is not Action<char> onInput) {
                lastTextInputOnInput = null;
                lastTextInputHasForeignSubscriber = false;
                return false;
            }

            if (!ReferenceEquals(onInput, lastTextInputOnInput)) {
                lastTextInputOnInput = onInput;
                lastTextInputHasForeignSubscriber = onInput.GetInvocationList()
                    .Any(d => !IsAlwaysSubscribedTextOnInput(d));
            }

            return lastTextInputHasForeignSubscriber;
        }
    }
    #endregion
}
