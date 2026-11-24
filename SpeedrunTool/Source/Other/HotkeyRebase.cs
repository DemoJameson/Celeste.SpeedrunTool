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
// Cannot use MInput, since that will be cloned by SRT and cause some issue

// since SRT v3.25.0
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
    private static readonly Lazy<FieldInfo?> f_CelesteNetClientModule_Instance = new(() => ModUtils.GetType("CelesteNet.Client", "Celeste.Mod.CelesteNet.Client.CelesteNetClientModule")?.GetFieldInfo("Instance"));
    private static readonly Lazy<FieldInfo?> f_CelesteNetClientModule_Context = new(() => ModUtils.GetType("CelesteNet.Client", "Celeste.Mod.CelesteNet.Client.CelesteNetClientModule")?.GetFieldInfo("Context"));
    private static readonly Lazy<FieldInfo?> f_CelesteNetClientContext_Chat = new(() => ModUtils.GetType("CelesteNet.Client", "Celeste.Mod.CelesteNet.Client.CelesteNetClientContext")?.GetFieldInfo("Chat"));
    private static readonly Lazy<PropertyInfo?> p_CelesteNetChatComponent_Active = new(() => ModUtils.GetType("CelesteNet.Client", "Celeste.Mod.CelesteNet.Client.Components.CelesteNetChatComponent")?.GetPropertyInfo("Active"));

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
    public static float RightThumbSticksX => padState.ThumbSticks.Right.X;

    /// Checks if the CelesteNet chat is open
    private static bool CelesteNetChatting {
        get {
            if (f_CelesteNetClientModule_Instance.Value?.GetValue(null) is not { } instance) {
                return false;
            }
            if (f_CelesteNetClientModule_Context.Value?.GetValue(instance) is not { } context) {
                return false;
            }
            if (f_CelesteNetClientContext_Chat.Value?.GetValue(context) is not { } chat) {
                return false;
            }

            return p_CelesteNetChatComponent_Active.Value?.GetValue(chat) as bool? == true;
        }
    }

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
        for (int i = 0; i < 4; i++) {
            var state = GamePad.GetState((PlayerIndex)i);
            if (state.IsConnected) {
                return state;
            }
        }

        // No controller connected
        return default;
    }

    internal static void Update() {
        // Update hotkeys anyway, but choose whether their OnPress should be invoked
        kbState = Keyboard.GetState();
        padState = GetGamePadState();

        foreach (HotkeyConfig hotkey in HotkeyConfigUi.HotkeyConfigs.Values) {
            hotkey.Hotkey_Impl.Update(true, true);
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
        if (Engine.Scene?.Tracker is { } tracker && (tracker.GetEntity<KeyboardConfigUI>() != null || tracker.GetEntity<ButtonConfigUI>() != null)) {
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
    public static Hotkey_Rebase BindingToHotkey(List<InputKeys> keys, List<InputButtons> buttons, bool held = false) {
        return new(keys, buttons, true, held);
    }

    /// Hotkey which is independent of the game Update loop
    public class Hotkey_Rebase(List<InputKeys> keys, List<InputButtons> buttons, bool keyCombo, bool held) {
        public readonly List<InputKeys> Keys = keys;
        public readonly List<InputButtons> Buttons = buttons;

        internal bool OverrideCheck;

        private DateTime doublePressTimeout;
        private DateTime repeatTimeout;

        public bool Check { get; private set; }
        public bool Pressed => !LastCheck && Check;
        public bool Released => LastCheck && !Check;

        public bool DoublePressed { get; private set; }
        public bool Repeated { get; private set; }

        public bool LastCheck { get; set; }

        internal const double DoublePressTimeoutMS = 200.0;
        internal const double RepeatTimeoutMS = 500.0;

        internal void Update(bool updateKey = true, bool updateButton = true) {
            LastCheck = Check;

            bool keyCheck;
            bool buttonCheck;

            if (OverrideCheck) {
                keyCheck = buttonCheck = true;
                if (!held) {
                    OverrideCheck = false;
                }
            }
            else {
                keyCheck = updateKey && IsKeyDown();
                buttonCheck = updateButton && IsButtonDown();
            }

            Check = keyCheck || buttonCheck;

            DateTime now = DateTime.Now;
            if (Pressed) {
                DoublePressed = now < doublePressTimeout;
                doublePressTimeout = DoublePressed ? default : now + TimeSpan.FromMilliseconds(DoublePressTimeoutMS);

                Repeated = true;
                repeatTimeout = now + TimeSpan.FromMilliseconds(RepeatTimeoutMS);
            }
            else if (Check) {
                DoublePressed = false;
                Repeated = now >= repeatTimeout;
            }
            else {
                DoublePressed = false;
                Repeated = false;
                repeatTimeout = default;
            }
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