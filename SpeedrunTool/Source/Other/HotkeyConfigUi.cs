using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste.Mod.Helpers;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;
using CelesteSettings = Celeste.Settings;

namespace Celeste.Mod.SpeedrunTool.Other {
    [Tracked]
    public class HotkeyConfigUi : TextMenu {
        private static FieldInfo tasRunning;
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

        private static readonly Dictionary<Hotkey, HotkeyConfig> HotkeyConfigs = new List<HotkeyConfig> {
            new(Hotkey.SaveState, Keys.F7),
            new(Hotkey.LoadState, Keys.F8),
            new(Hotkey.ClearState, Keys.F4),
            new(Hotkey.OpenDebugMap, Keys.F6, true),
            new(Hotkey.ResetRoomTimerPb, Keys.F9),
            new(Hotkey.SwitchRoomTimer, Keys.F10),
            new(Hotkey.SetEndPoint, Keys.F11),
            new(Hotkey.SetAdditionalEndPoint),
            new(Hotkey.CheckDeathStatistics),
            new(Hotkey.TeleportToPreviousRoom, Keys.PageUp),
            new(Hotkey.TeleportToNextRoom, Keys.PageDown),
            new(Hotkey.SwitchAutoLoadState),
            new(Hotkey.ToggleFullscreen),
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

        private static SpeedrunToolSettings Settings => SpeedrunToolModule.Settings;

        [Load]
        private static void Load() {
            On.Monocle.MInput.Update += MInputOnUpdate;

            Hotkey.ToggleFullscreen.RegisterPressedAction(scene => {
                if (!MInput.ControllerHasFocus && scene is Overworld {
                    Current: OuiFileNaming {UseKeyboardInput: true} or OuiModOptionString {UseKeyboardInput: true}
                }) {
                    return;
                }

                CelesteSettings.Instance.Fullscreen = !CelesteSettings.Instance.Fullscreen;
                CelesteSettings.Instance.ApplyScreen();
                UserIO.SaveHandler(false, true);
            });
        }

        [Unload]
        private static void Unload() {
            On.Monocle.MInput.Update -= MInputOnUpdate;
        }

        [Initialize]
        private static void Initialize() {
        Assembly assembly = FakeAssembly.GetFakeEntryAssembly();
        tasRunning = assembly.GetType("TAS.Manager")?.GetFieldInfo("Running");
        celesteNetClientModuleInstance = assembly.GetType("Celeste.Mod.CelesteNet.Client.CelesteNetClientModule")?.GetFieldInfo("Instance");
        celesteNetClientModuleContext = assembly.GetType("Celeste.Mod.CelesteNet.Client.CelesteNetClientModule")?.GetFieldInfo("Context");
        celesteNetClientContextChat = assembly.GetType("Celeste.Mod.CelesteNet.Client.CelesteNetClientContext")?.GetFieldInfo("Chat");
        celesteNetChatComponentActive = assembly.GetType("Celeste.Mod.CelesteNet.Client.Components.CelesteNetChatComponent")?.GetPropertyInfo("Active");
            
            foreach (HotkeyConfig buttonInfo in HotkeyConfigs.Values) {
                buttonInfo.UpdateVirtualButton();
            }
        }

        private static void MInputOnUpdate(On.Monocle.MInput.orig_Update orig) {
            orig();

            if (Engine.Scene is { } scene && Settings.Enabled) {
                foreach (Hotkey hotkey in Enum.GetValues(typeof(Hotkey)).Cast<Hotkey>()) {
                    HotkeyConfig hotkeyConfig = GetHotkeyConfig(hotkey);
                    if (Pressed(hotkey, scene)) {
                        hotkeyConfig.VirtualButton.Value.ConsumePress();
                        hotkeyConfig.OnPressed?.Invoke(scene);
                    }
                }
            }
        }

        private static bool Pressed(Hotkey hotkey, Scene scene) {
            bool pressed = GetVirtualButton(hotkey).Pressed;
            if (!pressed) {
                return false;
            }

            if (tasRunning?.GetValue(null) as bool? == true) {
                return false;
            }

            if (CelesteNetChatting) {
                return false;
            }

            if (scene.Tracker.Entities.TryGetValue(typeof(HotkeyConfigUi), out List<Entity> entities) && entities.Count > 0) {
                return false;
            }

            return true;
        }

        public static HotkeyConfig GetHotkeyConfig(Hotkey hotkey) {
            return HotkeyConfigs[hotkey];
        }

        public static VirtualButton GetVirtualButton(Hotkey hotkey) {
            return HotkeyConfigs[hotkey].VirtualButton.Value;
        }

        private void Reload(int index = -1) {
            Clear();

            Add(new Header(Dialog.Clean(DialogIds.HotkeyConfig)));

            Add(new SubHeader(Dialog.Clean(DialogIds.PressDeleteToRemoveButton)));

            Add(new SubHeader(Dialog.Clean(DialogIds.Keyboard)));
            foreach (KeyValuePair<Hotkey, HotkeyConfig> pair in HotkeyConfigs) {
                AddKeyboardSetting(pair.Key, pair.Value.GetKeys());
            }

            Add(new SubHeader(Dialog.Clean(DialogIds.Controller)));
            foreach (KeyValuePair<Hotkey, HotkeyConfig> pair in HotkeyConfigs) {
                AddControllerSetting(pair.Key, pair.Value.GetButton());
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

        private void AddControllerSetting(Hotkey hotkeyType, Buttons? button) {
            Setting setting = new(HotkeyConfigs[hotkeyType].GetLabel(), Keys.None);
            setting.Pressed(() => Remap(hotkeyType));
            if (button != null) {
                setting.Set(new List<Buttons> {(Buttons)button});
            }

            Add(setting);
        }

        private void AddKeyboardSetting(Hotkey hotkeyType, List<Keys> keys) {
            HotkeyConfig hotkeyConfig = HotkeyConfigs[hotkeyType];
            Add(new Setting(hotkeyConfig.GetLabel(), keys).Pressed(() => Remap(hotkeyType, true)));
        }

        private static void SetDefaultButtons() {
            foreach (HotkeyConfig buttonInfo in HotkeyConfigs.Values) {
                buttonInfo.SetButton(null);
                buttonInfo.SetKeys(buttonInfo.DefaultKeys.ToList());
                buttonInfo.UpdateVirtualButton();
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
            HotkeyConfig info = HotkeyConfigs[remappingType];
            if (info.GetButton() is { } currentButton && currentButton == button) {
                info.SetButton(null);
            } else {
                info.SetButton(button);
                foreach (HotkeyConfig otherInfo in HotkeyConfigs.Values) {
                    if (otherInfo == info) {
                        continue;
                    }

                    if (otherInfo.GetButton() is { } otherButton && otherButton == button) {
                        otherInfo.SetButton(null);
                        otherInfo.UpdateVirtualButton();
                    }
                }
            }

            info.UpdateVirtualButton();
            Reload(Selection);
        }

        private void SetRemap(Keys key) {
            remapping = false;
            inputDelay = 0.25f;
            HotkeyConfigs[remappingType].With(info => {
                if (info.GetKeys().Contains(key) && !(info.FixedDefaultKeys && info.DefaultKeys.Contains(key))) {
                    info.GetKeys().Remove(key);
                } else {
                    if (!info.FixedDefaultKeys) {
                        info.GetKeys().Clear();
                    }

                    info.GetKeys().Add(key);
                    foreach (HotkeyConfig otherInfo in HotkeyConfigs.Values) {
                        if (otherInfo == info) {
                            continue;
                        }

                        if (otherInfo.GetKeys().Contains(key) && !(otherInfo.FixedDefaultKeys && otherInfo.DefaultKeys.Contains(key))) {
                            otherInfo.GetKeys().Remove(key);
                            otherInfo.UpdateVirtualButton();
                        }
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
                    if (pressedKeys != null && pressedKeys.Length != 0 &&
                        MInput.Keyboard.Pressed(pressedKeys[pressedKeys.Length - 1])) {
                        SetRemap(pressedKeys[pressedKeys.Length - 1]);
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
            } else if (MInput.Keyboard.Pressed(Keys.Delete) && Selection >= 3 && Selection < Items.Count - 1) {
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
                    if (hotkeyConfig.FixedDefaultKeys) {
                        hotkeyConfig.SetKeys(hotkeyConfig.DefaultKeys.ToList());
                    }
                } else {
                    hotkeyConfig.SetButton(null);
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
        public readonly bool FixedDefaultKeys;

        public readonly Hotkey Hotkey;
        public readonly Lazy<VirtualButton> VirtualButton = new(() => new VirtualButton(0.08f));
        public Action<Scene> OnPressed;

        public HotkeyConfig(Hotkey hotkey, Keys? defaultKey = null, bool fixedDefaultKeys = false) {
            Hotkey = hotkey;
            DefaultKeys = defaultKey == null ? new Keys[0] : new[] {defaultKey.Value};
            FixedDefaultKeys = fixedDefaultKeys;
        }

        private static SpeedrunToolSettings Settings => SpeedrunToolModule.Settings;

        public void UpdateVirtualButton() {
            List<VirtualButton.Node> nodes = VirtualButton.Value.Nodes;
            nodes.Clear();
            nodes.AddRange(GetKeys().Select(key => new VirtualButton.KeyboardKey(key)));

            if (GetButton() is { } button) {
                nodes.Add(new VirtualButton.PadButton(Input.Gamepad, button));
            }
        }

        public Buttons? GetButton() {
            return (Buttons?)Settings.GetPropertyValue($"Controller{Hotkey}");
        }

        public void SetButton(Buttons? button) {
            Settings.SetPropertyValue($"Controller{Hotkey}", button);
        }

        public List<Keys> GetKeys() {
            List<Keys> result = (List<Keys>)Settings.GetPropertyValue($"Keyboard{Hotkey}");
            if (result == null) {
                result = new List<Keys>();
                SetKeys(result);
            }

            return result;
        }

        public void SetKeys(List<Keys> keys) {
            Settings.SetPropertyValue($"Keyboard{Hotkey}", keys);
        }

        public string GetLabel() {
            return (typeof(DialogIds).GetFieldValue(Hotkey.ToString()) as string).DialogClean();
        }
    }

    public enum Hotkey {
        SaveState,
        LoadState,
        ClearState,
        OpenDebugMap,
        ResetRoomTimerPb,
        SwitchRoomTimer,
        SetEndPoint,
        SetAdditionalEndPoint,
        CheckDeathStatistics,
        TeleportToPreviousRoom,
        TeleportToNextRoom,
        SwitchAutoLoadState,
        ToggleFullscreen
    }

    internal static class HotkeysExtensions {
        public static void RegisterPressedAction(this Hotkey hotkey, Action<Scene> onPressed) {
            HotkeyConfigUi.GetHotkeyConfig(hotkey).OnPressed = onPressed;
        }

        public static List<Keys> GetDefaultKeys(this Hotkey hotkey) {
            return HotkeyConfigUi.GetHotkeyConfig(hotkey).DefaultKeys.ToList();
        }
    }
}