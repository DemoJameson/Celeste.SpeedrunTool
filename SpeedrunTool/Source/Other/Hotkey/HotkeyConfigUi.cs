using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;
using System.Linq;

namespace Celeste.Mod.SpeedrunTool.Other;

[Tracked]
public class HotkeyConfigUi : TextMenu {
    private static readonly List<Buttons> AllButtons = new() {
        Buttons.A,
        Buttons.B,
        Buttons.X,
        Buttons.Y,
        Buttons.LeftShoulder,
        Buttons.RightShoulder,
        Buttons.LeftTrigger,
        Buttons.RightTrigger,
        Buttons.LeftStick,
        Buttons.RightStick,
        Buttons.Back,
        Buttons.BigButton,
    };

    private static readonly HashSet<Keys> ModifierKeys = new() {
        Keys.LeftControl,
        Keys.RightControl,
        Keys.LeftAlt,
        Keys.RightAlt,
        Keys.LeftShift,
        Keys.RightShift,
    };

    private static readonly HashSet<Keys> DisallowKeys = new() {
        Keys.F1,
        Keys.F2,
        Keys.F3,
        Keys.F5,
    };

    private bool closing;
    private float inputDelay;
    private bool remapping;
    private float remappingEase;
    private bool remappingKeyboard;
    private Hotkey remappingType;
    private float timeout;

    static HotkeyConfigUi() {
        if (Celeste.Instance.Version >= new Version(1, 3, 3, 12)) {
            AllButtons.AddRange(new[] {
                Buttons.DPadUp,
                Buttons.DPadDown,
                Buttons.DPadLeft,
                Buttons.DPadRight,
                Buttons.LeftThumbstickUp,
                Buttons.LeftThumbstickDown,
                Buttons.LeftThumbstickLeft,
                Buttons.LeftThumbstickRight,
                Buttons.RightThumbstickUp,
                Buttons.RightThumbstickDown,
                Buttons.RightThumbstickLeft,
                Buttons.RightThumbstickRight,
            });
        }
    }

    public HotkeyConfigUi() {
        Reload();
        OnESC = OnCancel = () => {
            Focused = false;
            closing = true;
        };
        MinWidth = 600f;
        Position.Y = ScrollTargetY;
        Alpha = 0.0f;
    }

    private void Reload(int index = -1) {
        Clear();

        Add(new Header(Dialog.Clean(DialogIds.HotkeysConfig)));
        Add(new SubHeader(Dialog.Clean(DialogIds.PressDeleteToClearHotkeys)));

        Add(new SubHeader(Dialog.Clean(DialogIds.Keyboard)));
        foreach (KeyValuePair<Hotkey, HotkeyConfig> pair in HotkeyRegistry.All) {
            AddKeyboardSetting(pair.Key, pair.Value.GetKeys());
        }

        Add(new SubHeader(Dialog.Clean(DialogIds.Controller)));
        foreach (KeyValuePair<Hotkey, HotkeyConfig> pair in HotkeyRegistry.All) {
            AddControllerSetting(pair.Key, pair.Value.GetButtons());
        }

        Add(new SubHeader(""));

        AddResetButton();

        if (index < 0) {
            return;
        }

        Selection = index;
    }

    private void AddResetButton() {
        Button resetButton = new(Dialog.Clean(DialogIds.KeyConfigReset)) {
            IncludeWidthInMeasurement = false,
            AlwaysCenter = true,
            OnPressed = () => {
                SetDefaultButtons();
                Reload(Selection);
            }
        };
        Add(resetButton);
    }

    private void AddControllerSetting(Hotkey hotkeyType, List<Buttons> buttons) {
        HotkeyConfig hotkeyConfig = hotkeyType.GetHotkeyConfig();
        Add(new Setting(hotkeyConfig.GetLabel(), buttons).Pressed(() => Remap(hotkeyType)));
    }

    private void AddKeyboardSetting(Hotkey hotkeyType, List<Keys> keys) {
        HotkeyConfig hotkeyConfig = hotkeyType.GetHotkeyConfig();
        Add(new Setting(hotkeyConfig.GetLabel(), keys).Pressed(() => Remap(hotkeyType, true)));
    }

    private static void SetDefaultButtons() {
        foreach (HotkeyConfig hotkeyConfig in HotkeyRegistry.All.Values) {
            hotkeyConfig.SetButtons(new List<Buttons>());
            hotkeyConfig.SetKeys(hotkeyConfig.DefaultKeys.ToList());
            hotkeyConfig.UpdateVirtualButton();
        }
    }

    private void Remap(Hotkey hotkey, bool remapKeyboard = false) {
        remapping = true;
        remappingKeyboard = remapKeyboard;
        remappingType = hotkey;
        timeout = 5f;
        Focused = false;
    }

    private void SetRemap(Buttons button) {
        remapping = false;
        inputDelay = 0.25f;
        remappingType.GetHotkeyConfig().With(info => {
            if (info.GetButtons().Contains(button)) {
                info.GetButtons().Remove(button);
            }
            else {
                info.GetButtons().Add(button);
            }

            info.UpdateVirtualButton();
        });
        Reload(Selection);
    }

    private void SetRemap(Keys key) {
        remapping = false;
        inputDelay = 0.25f;
        remappingType.GetHotkeyConfig().With(info => {
            if (info.GetKeys().Contains(key)) {
                info.GetKeys().Remove(key);
            }
            else {
                if (ModifierKeys.Contains(key)) {
                    info.GetKeys().Insert(0, key);
                }
                else {
                    info.GetKeys().Add(key);
                }
            }

            info.UpdateVirtualButton();
        });
        Reload(Selection);
    }

    public override void Update() {
        base.Update();
        if (inputDelay > 0.0 && !remapping) {
            inputDelay -= Engine.DeltaTime;
            if (inputDelay <= 0.0) {
                Focused = true;
            }
        }

        remappingEase = Calc.Approach(remappingEase, remapping ? 1f : 0.0f, Engine.DeltaTime * 4f);
        if (remappingEase > 0.5 && remapping) {
            if (Input.ESC.Pressed || Input.MenuCancel || timeout <= 0.0) {
                Input.ESC.ConsumePress();
                remapping = false;
                Focused = true;
            }
            else if (remappingKeyboard) {
                Keys[] pressedKeys = MInput.Keyboard.CurrentState.GetPressedKeys();
                if (pressedKeys?.LastOrDefault() is { } pressedKey && MInput.Keyboard.Pressed(pressedKey) && !DisallowKeys.Contains(pressedKey)) {
                    SetRemap(pressedKey);
                }
            }
            else {
                GamePadState currentState = MInput.GamePads[Input.Gamepad].CurrentState;
                GamePadState previousState = MInput.GamePads[Input.Gamepad].PreviousState;
                foreach (Buttons buttons in AllButtons) {
                    if (!currentState.IsButtonDown(buttons) || previousState.IsButtonDown(buttons)) {
                        continue;
                    }

                    SetRemap(buttons);
                    break;
                }
            }

            timeout -= Engine.DeltaTime;
        }
        else if ((Input.MenuJournal.Pressed || MInput.Keyboard.Pressed(Keys.Delete) || MInput.Keyboard.Pressed(Keys.Back)) && Selection >= 4 &&
                   Selection < Items.Count - 1) {
            int index = Selection - 3;
            bool keyboard = true;
            if (index > HotkeyRegistry.All.Count - 1) {
                index--;
                index %= HotkeyRegistry.All.Count;
                keyboard = false;
            }

            HotkeyConfig hotkeyConfig = HotkeyRegistry.All.Values.ToList()[index];
            if (keyboard) {
                hotkeyConfig.GetKeys().Clear();
            }
            else {
                hotkeyConfig.GetButtons().Clear();
            }

            hotkeyConfig.UpdateVirtualButton();
            Reload(Selection);
        }

        Alpha = Calc.Approach(Alpha, closing ? 0.0f : 1f, Engine.DeltaTime * 8f);
        if (!closing || Alpha > 0.0) {
            return;
        }

        Close();
    }

    public override void Render() {
        Draw.Rect(-10f, -10f, 1940f, 1100f, Color.Black * Ease.CubeOut(Alpha));
        base.Render();
        if (remappingEase <= 0.0) {
            return;
        }

        Draw.Rect(-10f, -10f, 1940f, 1100f, Color.Black * 0.95f * Ease.CubeInOut(remappingEase));
        Vector2 position = new Vector2(1920f, 1080f) * 0.5f;

        if (remappingKeyboard || Input.GuiInputController()) {
            ActiveFont.Draw(
                Dialog.Get(DialogIds.ComboHotkeyDescription),
                position + new Vector2(0.0f, -32f),
                new Vector2(0.5f, 2f),
                Vector2.One * 0.7f,
                Color.LightGray * Ease.CubeIn(remappingEase));
            ActiveFont.Draw(
                remappingKeyboard
                    ? Dialog.Get(DialogIds.KeyConfigChanging)
                    : Dialog.Get(DialogIds.BtnConfigChanging),
                position + new Vector2(0.0f, -8f),
                new Vector2(0.5f, 1f),
                Vector2.One * 0.7f,
                Color.LightGray * Ease.CubeIn(remappingEase));
            ActiveFont.Draw(remappingType.GetHotkeyConfig().GetLabel(),
                position + new Vector2(0.0f, 8f), new Vector2(0.5f, 0.0f), Vector2.One * 2f,
                Color.White * Ease.CubeIn(remappingEase));
        }
        else {
            ActiveFont.Draw(Dialog.Clean(DialogIds.BtnConfigNoController), position, new Vector2(0.5f, 0.5f),
                Vector2.One,
                Color.White * Ease.CubeIn(remappingEase));
        }
    }
}

