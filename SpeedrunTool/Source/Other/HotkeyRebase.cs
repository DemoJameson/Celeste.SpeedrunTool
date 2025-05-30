using Celeste.Mod.SpeedrunTool.Utils;
using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using InputButtons = Microsoft.Xna.Framework.Input.Buttons;
using InputKeys = Microsoft.Xna.Framework.Input.Keys;

namespace Celeste.Mod.SpeedrunTool.Other;

// taken from CelesteTAS
// Cannot use MInput, since that will be cloned by SRT and cause some issue

// since SRT v3.25.0
internal static class Hotkeys_Rebase {
    private static readonly Lazy<FieldInfo?> f_CelesteNetClientModule_Instance = new(() => ModUtils.GetType("CelesteNet.Client", "Celeste.Mod.CelesteNet.Client.CelesteNetClientModule")?.GetFieldInfo("Instance"));
    private static readonly Lazy<FieldInfo?> f_CelesteNetClientModule_Context = new(() => ModUtils.GetType("CelesteNet.Client", "Celeste.Mod.CelesteNet.Client.CelesteNetClientModule")?.GetFieldInfo("Context"));
    private static readonly Lazy<FieldInfo?> f_CelesteNetClientContext_Chat = new(() => ModUtils.GetType("CelesteNet.Client", "Celeste.Mod.CelesteNet.Client.CelesteNetClientContext")?.GetFieldInfo("Chat"));
    private static readonly Lazy<PropertyInfo?> p_CelesteNetChatComponent_Active = new(() => ModUtils.GetType("CelesteNet.Client", "Celeste.Mod.CelesteNet.Client.Components.CelesteNetChatComponent")?.GetPropertyInfo("Active"));

    private static KeyboardState kbState;
    private static GamePadState padState;
    public static float RightThumbSticksX => padState.ThumbSticks.Right.X;

    public static bool Disabled = false;

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
        // Only update if the keys aren't already used for something else
        bool updateKey = true, updateButton = true;

        Disabled = MInput.Disabled;
        // Prevent triggering hotkeys while writing text
        if (Engine.Commands.Open) {
            updateKey = false;
        } else if (TextInput.Initialized && typeof(TextInput).GetFieldValue<Action<char>>("_OnInput") is { } inputEvent) {
            // ImGuiHelper is always subscribed, so ignore it
            updateKey &= inputEvent.GetInvocationList().All(d => d.Target?.GetType().FullName == "Celeste.Mod.ImGuiHelper.ImGuiRenderer+<>c");
        }

        if (!TasUtils.Running) {
            if (CelesteNetChatting) {
                updateKey = false;
            }

            if (Engine.Scene?.Tracker is { } tracker) {
                if (tracker.GetEntity<KeyboardConfigUI>() != null) {
                    updateKey = false;
                }
                if (tracker.GetEntity<ButtonConfigUI>() != null) {
                    updateButton = false;
                }
            }
        } else {
            updateKey = updateButton = false;
        }
        foreach (HotkeyConfig hotkey in HotkeyConfigUi.HotkeyConfigs.Values) {
            hotkey.Hotkey_Impl.Update(updateKey, updateButton);
        }


        kbState = Keyboard.GetState();
        padState = GetGamePadState();
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
            } else {
                keyCheck = updateKey && IsKeyDown();
                buttonCheck = updateButton && IsButtonDown();
            }

            Check = keyCheck || buttonCheck;

            var now = DateTime.Now;
            if (Pressed) {
                DoublePressed = now < doublePressTimeout;
                doublePressTimeout = DoublePressed ? default : now + TimeSpan.FromMilliseconds(DoublePressTimeoutMS);

                Repeated = true;
                repeatTimeout = now + TimeSpan.FromMilliseconds(RepeatTimeoutMS);
            } else if (Check) {
                DoublePressed = false;
                Repeated = now >= repeatTimeout;
            } else {
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