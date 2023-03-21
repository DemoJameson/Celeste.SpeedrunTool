using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste.Mod.SpeedrunTool.Message;
using Celeste.Mod.SpeedrunTool.Utils;
using Celeste.Mod.UI;
using Microsoft.Xna.Framework.Input;
using CelesteSettings = Celeste.Settings;

namespace Celeste.Mod.SpeedrunTool.Other;

[Tracked]
public class HotkeyConfigUi : TextMenu {
    private static FieldInfo celesteNetClientModuleInstance;
    private static FieldInfo celesteNetClientModuleContext;
    private static FieldInfo celesteNetClientContextChat;
    private static PropertyInfo celesteNetChatComponentActive;

    private static bool CelesteNetChatting {
        get {
            if (celesteNetClientModuleInstance?.GetValue(null) is not { } instance) {
                return false;
            }

            if (celesteNetClientModuleContext?.GetValue(instance) is not { } context) {
                return false;
            }

            if (celesteNetClientContextChat?.GetValue(context) is not { } chat) {
                return false;
            }

            return celesteNetChatComponentActive?.GetValue(chat) as bool? == true;
        }
    }

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

    public static readonly Dictionary<Hotkey, HotkeyConfig> HotkeyConfigs = new List<HotkeyConfig> {
        new(Hotkey.ToggleHotkeys),
        new(Hotkey.SaveState, Keys.F7),
        new(Hotkey.LoadState, Keys.F8),
        new(Hotkey.ClearState, Keys.F4),
        new(Hotkey.OpenDebugMap),
        new(Hotkey.ResetRoomTimerPb, Keys.F9),
        new(Hotkey.SwitchRoomTimer, Keys.F10),
        new(Hotkey.IncreaseTimedRooms),
        new(Hotkey.DecreaseTimedRooms),
        new(Hotkey.SetEndPoint, Keys.F11),
        new(Hotkey.SetAdditionalEndPoint),
        new(Hotkey.CheckDeathStatistics),
        new(Hotkey.TeleportToPreviousRoom, Keys.PageUp),
        new(Hotkey.TeleportToNextRoom, Keys.PageDown),
        new(Hotkey.SwitchAutoLoadState),
        new(Hotkey.SpawnTowerViewer),
        new(Hotkey.ToggleFullscreen),
        new(Hotkey.ExportRoomTimes),
    }.ToDictionary(info => info.Hotkey, info => info);

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
        if (ModUtils.GetAssembly("CelesteNet.Client") is { } assembly) {
            celesteNetClientModuleInstance = assembly.GetType("Celeste.Mod.CelesteNet.Client.CelesteNetClientModule")?.GetFieldInfo("Instance");
            celesteNetClientModuleContext = assembly.GetType("Celeste.Mod.CelesteNet.Client.CelesteNetClientModule")?.GetFieldInfo("Context");
            celesteNetClientContextChat = assembly.GetType("Celeste.Mod.CelesteNet.Client.CelesteNetClientContext")?.GetFieldInfo("Chat");
            celesteNetChatComponentActive =
                assembly.GetType("Celeste.Mod.CelesteNet.Client.Components.CelesteNetChatComponent")?.GetPropertyInfo("Active");
        }

        foreach (HotkeyConfig hotkeyConfig in HotkeyConfigs.Values) {
            hotkeyConfig.UpdateVirtualButton();
        }
    }

    [Unload]
    private static void Unload() {
        On.Monocle.MInput.Update -= MInputOnUpdate;
        foreach (HotkeyConfig hotkeyConfig in HotkeyConfigs.Values) {
            hotkeyConfig.VirtualButton.Value.Deregister();
        }
    }

    private static void MInputOnUpdate(On.Monocle.MInput.orig_Update orig) {
        orig();

        if (Engine.Scene is { } scene && ModSettings.Enabled && !TasUtils.Running) {
            foreach (Hotkey hotkey in Enum.GetValues(typeof(Hotkey)).Cast<Hotkey>()) {
                HotkeyConfig hotkeyConfig = hotkey.GetHotkeyConfig();
                if (Pressed(hotkey, scene)) {
                    hotkeyConfig.VirtualButton.Value.ConsumePress();
                    hotkeyConfig.OnPressed?.Invoke(scene);
                }
            }
        }
    }

    private static bool Pressed(Hotkey hotkey, Scene scene) {
        if (!ModSettings.Hotkeys && hotkey != Hotkey.ToggleHotkeys) {
            return false;
        }

        bool pressed = hotkey.Pressed();
        if (!pressed) {
            return false;
        }

        if (CelesteNetChatting) {
            return false;
        }

        if (scene.Tracker.Entities.TryGetValue(typeof(HotkeyConfigUi), out List<Entity> entities) && entities.Count > 0) {
            return false;
        }

        // 反射兼容 v1312
        // 避免输入文字时触发快捷键
        if (!typeof(MInput).GetFieldValue<bool>("ControllerHasFocus") && scene is Overworld {
                Current: OuiFileNaming {UseKeyboardInput: true} or OuiModOptionString {UseKeyboardInput: true}
            }) {
            return false;
        }

        return true;
    }

    private void Reload(int index = -1) {
        Clear();

        Add(new Header(Dialog.Clean(DialogIds.HotkeysConfig)));
        Add(new SubHeader(Dialog.Clean(DialogIds.PressDeleteToClearHotkeys)));

        Add(new SubHeader(Dialog.Clean(DialogIds.Keyboard)));
        foreach (KeyValuePair<Hotkey, HotkeyConfig> pair in HotkeyConfigs) {
            AddKeyboardSetting(pair.Key, pair.Value.GetKeys());
        }

        Add(new SubHeader(Dialog.Clean(DialogIds.Controller)));
        foreach (KeyValuePair<Hotkey, HotkeyConfig> pair in HotkeyConfigs) {
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
        HotkeyConfig hotkeyConfig = HotkeyConfigs[hotkeyType];
        Add(new Setting(hotkeyConfig.GetLabel(), buttons).Pressed(() => Remap(hotkeyType)));
    }

    private void AddKeyboardSetting(Hotkey hotkeyType, List<Keys> keys) {
        HotkeyConfig hotkeyConfig = HotkeyConfigs[hotkeyType];
        Add(new Setting(hotkeyConfig.GetLabel(), keys).Pressed(() => Remap(hotkeyType, true)));
    }

    private static void SetDefaultButtons() {
        foreach (HotkeyConfig hotkeyConfig in HotkeyConfigs.Values) {
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
        HotkeyConfigs[remappingType].With(info => {
            if (info.GetButtons().Contains(button)) {
                info.GetButtons().Remove(button);
            } else {
                info.GetButtons().Add(button);
            }

            info.UpdateVirtualButton();
        });
        Reload(Selection);
    }

    private void SetRemap(Keys key) {
        remapping = false;
        inputDelay = 0.25f;
        HotkeyConfigs[remappingType].With(info => {
            if (info.GetKeys().Contains(key)) {
                info.GetKeys().Remove(key);
            } else {
                if (ModifierKeys.Contains(key)) {
                    info.GetKeys().Insert(0, key);
                } else {
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
            } else if (remappingKeyboard) {
                Keys[] pressedKeys = MInput.Keyboard.CurrentState.GetPressedKeys();
                if (pressedKeys?.LastOrDefault() is { } pressedKey && MInput.Keyboard.Pressed(pressedKey) && !DisallowKeys.Contains(pressedKey)) {
                    SetRemap(pressedKey);
                }
            } else {
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
        } else if ((Input.MenuJournal.Pressed || MInput.Keyboard.Pressed(Keys.Delete) || MInput.Keyboard.Pressed(Keys.Back)) && Selection >= 4 &&
                   Selection < Items.Count - 1) {
            int index = Selection - 3;
            bool keyboard = true;
            if (index > HotkeyConfigs.Count - 1) {
                index--;
                index %= HotkeyConfigs.Count;
                keyboard = false;
            }

            HotkeyConfig hotkeyConfig = HotkeyConfigs.Values.ToList()[index];
            if (keyboard) {
                hotkeyConfig.GetKeys().Clear();
            } else {
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
            ActiveFont.Draw(HotkeyConfigs[remappingType].GetLabel(),
                position + new Vector2(0.0f, 8f), new Vector2(0.5f, 0.0f), Vector2.One * 2f,
                Color.White * Ease.CubeIn(remappingEase));
        } else {
            ActiveFont.Draw(Dialog.Clean(DialogIds.BtnConfigNoController), position, new Vector2(0.5f, 0.5f),
                Vector2.One,
                Color.White * Ease.CubeIn(remappingEase));
        }
    }
}

public class HotkeyConfig {
    public readonly Keys[] DefaultKeys;

    public readonly Hotkey Hotkey;
    public readonly Lazy<VirtualButton> VirtualButton = new(() => new VirtualButton(0.08f));
    public Action<Scene> OnPressed;
    public List<VirtualButton.KeyboardKey> KeyboardKeys { get; private set; } = new();
    public List<VirtualButton.PadButton> PadButtons { get; private set; } = new();

    public HotkeyConfig(Hotkey hotkey, params Keys[] defaultKeys) {
        Hotkey = hotkey;
        DefaultKeys = defaultKeys;
    }

    public void UpdateVirtualButton() {
        List<VirtualButton.Node> nodes = VirtualButton.Value.Nodes;
        nodes.Clear();
        nodes.AddRange(KeyboardKeys = GetKeys().Select(key => new VirtualButton.KeyboardKey(key)).ToList());
        nodes.AddRange(PadButtons = GetButtons().Select(button => new VirtualButton.PadButton(Input.Gamepad, button)).ToList());
    }

    public List<Buttons> GetButtons() {
        List<Buttons> result = (List<Buttons>)ModSettings.GetPropertyValue($"Controller{Hotkey}");
        if (result == null) {
            result = new List<Buttons>();
            SetButtons(result);
        }

        return result;
    }

    public void SetButtons(List<Buttons> buttons) {
        ModSettings.SetPropertyValue($"Controller{Hotkey}", buttons);
    }

    public List<Keys> GetKeys() {
        List<Keys> result = (List<Keys>)ModSettings.GetPropertyValue($"Keyboard{Hotkey}");
        if (result == null) {
            result = new List<Keys>();
            SetKeys(result);
        }

        return result;
    }

    public void SetKeys(List<Keys> keys) {
        ModSettings.SetPropertyValue($"Keyboard{Hotkey}", keys);
    }

    public string GetLabel() {
        if (typeof(DialogIds).GetFieldValue(Hotkey.ToString()) is string label) {
            return label.DialogClean();
        } else {
            return $"DialogIds.{Hotkey} not found";
        }
    }
}

public enum Hotkey {
    ToggleHotkeys,
    SaveState,
    LoadState,
    ClearState,
    OpenDebugMap,
    ResetRoomTimerPb,
    SwitchRoomTimer,
    IncreaseTimedRooms,
    DecreaseTimedRooms,
    SetEndPoint,
    SetAdditionalEndPoint,
    CheckDeathStatistics,
    TeleportToPreviousRoom,
    TeleportToNextRoom,
    SwitchAutoLoadState,
    SpawnTowerViewer,
    ToggleFullscreen,
    ExportRoomTimes,
}

internal static class HotkeysExtensions {
    public static HotkeyConfig GetHotkeyConfig(this Hotkey hotkey) {
        return HotkeyConfigUi.HotkeyConfigs[hotkey];
    }

    public static void RegisterPressedAction(this Hotkey hotkey, Action<Scene> onPressed) {
        hotkey.GetHotkeyConfig().OnPressed = onPressed;
    }

    public static List<Keys> GetDefaultKeys(this Hotkey hotkey) {
        return hotkey.GetHotkeyConfig().DefaultKeys.ToList();
    }

    // 检查键盘或者手柄全部按键按下
    public static bool Pressed(this Hotkey hotkey) {
        HotkeyConfig hotkeyConfig = hotkey.GetHotkeyConfig();

        List<VirtualButton.KeyboardKey> keyboardKeys = hotkeyConfig.KeyboardKeys;
        bool keyPressed = keyboardKeys.Count > 0 && keyboardKeys.All(node => node.Check) && keyboardKeys.Any(node => node.Pressed);
        if (keyPressed) {
            return true;
        }

        List<VirtualButton.PadButton> padButtons = hotkeyConfig.PadButtons;
        bool buttonPressed = padButtons.Count > 0 && padButtons.All(node => node.Check) && padButtons.Any(node => node.Pressed);
        return buttonPressed;
    }
}