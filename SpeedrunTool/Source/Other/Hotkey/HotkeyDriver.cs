using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;
using System.Linq;
using InputButtons = Microsoft.Xna.Framework.Input.Buttons;
using InputKeys = Microsoft.Xna.Framework.Input.Keys;

namespace Celeste.Mod.SpeedrunTool.Other;

/// <summary>
/// Polls the keyboard/gamepad once per frame and updates every <see cref="HotkeyBinding"/>.
/// </summary>
internal static class HotkeyDriver {

    private static KeyboardState kbState;
    private static GamePadState padState;
    private static bool inputWasAvailable;

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

        foreach (HotkeyConfig hotkey in HotkeyRegistry.All.Values) {
            hotkey.Binding.Update();
            if (!inputWasAvailable) {
                // Input just came back (window refocus, or ImGuiHelper stopped handing the keyboard
                // to ImGui, which makes MInput null the states). A key held through that gap would
                // otherwise go false -> true and look like a fresh press.
                hotkey.Binding.LastCheck = hotkey.Binding.Check;
            }
        }

        inputWasAvailable = inputAvailable;
    }

    public static HotkeyBinding CreateBinding(List<InputKeys> keys, List<InputButtons> buttons) {
        return new(keys, buttons, true);
    }

    /// Hotkey which is independent of the game Update loop
    public class HotkeyBinding(List<InputKeys> keys, List<InputButtons> buttons, bool keyCombo) {
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
            List<string> result = [];
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