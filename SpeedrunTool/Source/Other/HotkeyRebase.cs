#nullable enable annotations
using Celeste.Mod.SpeedrunTool.ModInterop;
using Celeste.Mod.SpeedrunTool.Utils;
using Celeste.Mod.UI;
using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using InputButtons = Microsoft.Xna.Framework.Input.Buttons;
using InputKeys = Microsoft.Xna.Framework.Input.Keys;

namespace Celeste.Mod.SpeedrunTool.Other;

// taken from CelesteTAS
// Don't hold on to MInput's VirtualInput/VirtualButton objects: SRT clones and replaces them
// (savestates, TAS loads). Reading the raw MInput.Keyboard / MInput.GamePads snapshots right
// after MInput.Update is safe, since nothing is cached between frames.

internal static class Hotkeys_Rebase {

    /// A TextInput.OnInput subscriber that does not mean "a text box is open".
    /// ImGuiHelper subscribes once in ImGuiRenderer's constructor (SetupInput) to feed ImGui's
    /// character queue and never unsubscribes, so it has to be ignored. It is matched by
    /// assembly instead of by the compiler generated type name (ImGuiRenderer+<>c), which
    /// changes whenever that lambda captures something.
    /// Only one assembly is supported today; if another mod starts subscribing permanently, turn
    /// this into a collection and add its assembly. BingoClient used to be listed here by type
    /// name, but since 0.1.17 it only subscribes while its chat is open (rhelmot/CelesteBingoClient@419ad6e)
    /// and older versions are intentionally unsupported.
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

#pragma warning disable IDE1006 // 命名样式
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

    private static readonly Lazy<FieldInfo?> f_ImGuiHelperManager = new(() =>
        ModUtils.GetType("ImGuiHelper", "Celeste.Mod.ImGuiHelper.ImGuiHelperModule")?.GetFieldInfo("imGuiManager"));
    private static readonly Lazy<PropertyInfo?> p_ImGuiWantCaptureKeyboard = new(() =>
        ModUtils.GetType("ImGuiHelper", "Celeste.Mod.ImGuiHelper.ImGuiManager")?.GetPropertyInfo("WantCaptureKeyboard"));

    private static readonly Lazy<FieldInfo?> f_TextInputOnInput = new(() => typeof(TextInput).GetFieldInfo("_OnInput"));
#pragma warning restore IDE1006

    // Delegates are immutable: _OnInput's identity only changes when someone subscribes or
    // unsubscribes, so the invocation list only needs to be inspected when that happens.
    private static Action<char>? lastTextInputOnInput;
    private static bool lastTextInputHasForeignSubscriber;

    private static KeyboardState kbState;
    private static GamePadState padState;
    private static bool inputWasAvailable;

    /// Checks if the CelesteNet chat is open
    private static bool CelesteNetChatting => CelesteNetChattingAccessor.Value();

    private static readonly Lazy<Func<bool>> ImGuiWantsKeyboardAccessor = new(() => {
        try {
            FieldInfo? managerField = f_ImGuiHelperManager.Value;
            MethodInfo? getter = p_ImGuiWantCaptureKeyboard.Value?.GetGetMethod();
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


    private static GamePadState GetGamePadState() {
        if (MInput.GamePads is not { } pads) {
            return default;
        }

        foreach (var pad in pads) {
            if (pad is { Attached: true }) {
                return pad.CurrentState;
            }
        }

        // No controller connected
        return default;
    }

    internal static void Update() {
        // Read the device snapshots MInput already refreshed in orig() instead of polling the
        // keyboard/gamepads again. Only raw state is read here, no VirtualInput objects, so the
        // savestate cloning of MInput cannot break it.
        bool inputAvailable = Engine.Instance is { IsActive: true } && MInput.Active;
        kbState = MInput.Keyboard?.CurrentState ?? default;
        padState = GetGamePadState();

        foreach (HotkeyConfig hotkey in HotkeyConfigUi.HotkeyConfigs.Values) {
            hotkey.Hotkey_Impl.Update();
            if (!inputWasAvailable) {
                // Input just came back (window refocus, or ImGuiHelper stopped handing the keyboard
                // to ImGui, which makes MInput null the states). A key held through that gap would
                // otherwise go false -> true and look like a fresh press.
                hotkey.Hotkey_Impl.LastCheck = hotkey.Hotkey_Impl.Check;
            }
        }

        inputWasAvailable = inputAvailable;
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
        if (scene?.Tracker is { } tracker && (tracker.GetEntity<KeyboardConfigUI>() != null || tracker.GetEntity<ButtonConfigUI>() != null)) {
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

    public static Hotkey_Rebase BindingToHotkey(List<InputKeys> keys, List<InputButtons> buttons) {
        return new(keys, buttons, true);
    }

    /// Hotkey which is independent of the game Update loop
    public class Hotkey_Rebase(List<InputKeys> keys, List<InputButtons> buttons, bool keyCombo) {
        public readonly List<InputKeys> Keys = keys;
        public readonly List<InputButtons> Buttons = buttons;

        public bool Check { get; private set; }
        public bool Pressed => !LastCheck && Check;
        public bool Released => LastCheck && !Check;

        public bool LastCheck { get; set; }

        internal void Update(bool updateKey = true, bool updateButton = true) {
            LastCheck = Check;

            bool keyCheck = updateKey && IsKeyDown();
            bool buttonCheck = updateButton && IsButtonDown();

            Check = keyCheck || buttonCheck;
        }

        private bool IsKeyDown() {
            if (Keys.Count == 0 || kbState == default) {
                return false;
            }

            return keyCombo ? Keys.All(kbState.IsKeyDown) : Keys.Any(kbState.IsKeyDown);
        }
        private bool IsButtonDown() {
            if (Buttons.Count == 0 || padState == default) {
                return false;
            }

            return keyCombo ? Buttons.All(padState.IsButtonDown) : Buttons.Any(padState.IsButtonDown);
        }

        public override string ToString() {
            List<string> result = new();
            if (Keys.IsNotEmpty()) {
                result.Add(string.Join("+", Keys.Select(key => key switch {
                    InputKeys.D0 => "0",
                    InputKeys.D1 => "1",
                    InputKeys.D2 => "2",
                    InputKeys.D3 => "3",
                    InputKeys.D4 => "4",
                    InputKeys.D5 => "5",
                    InputKeys.D6 => "6",
                    InputKeys.D7 => "7",
                    InputKeys.D8 => "8",
                    InputKeys.D9 => "9",
                    _ => key.ToString(),
                })));
            }

            if (Buttons.IsNotEmpty()) {
                result.Add(string.Join("+", Buttons));
            }

            return string.Join("/", result);
        }
    }
}