using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;
using CelesteSettings = Celeste.Settings;

namespace Celeste.Mod.SpeedrunTool.Other {
    [Tracked]
    public class ButtonConfigUi : TextMenu {
        public static void Load() {
            On.Monocle.Scene.BeforeUpdate += SceneOnBeforeUpdate;
        }

        public static void Unload() {
            On.Monocle.Scene.BeforeUpdate -= SceneOnBeforeUpdate;
        }

        private static void SceneOnBeforeUpdate(On.Monocle.Scene.orig_BeforeUpdate orig, Scene self) {
            orig(self);
            if (Mappings.ToggleFullscreen.Pressed()) {
                Mappings.SwitchAutoLoadState.ConsumePress();
                CelesteSettings.Instance.Fullscreen = !CelesteSettings.Instance.Fullscreen;
                CelesteSettings.Instance.ApplyScreen();
                UserIO.SaveHandler(false, true);
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

        static ButtonConfigUi() {
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

        public class ButtonInfo {
            public readonly Mappings Mappings;
            public readonly Keys[] DefaultKeys;
            public readonly bool FixedDefaultKeys;
            public readonly Lazy<VirtualButton> VirtualButton = new(CreateVirtualButton);

            public ButtonInfo(Mappings mappings, Keys? defaultKey = null, bool fixedDefaultKeys = false) {
                Mappings = mappings;
                DefaultKeys = defaultKey == null ? new Keys[0] : new[] { defaultKey.Value };
                FixedDefaultKeys = fixedDefaultKeys;
            }

            public void UpdateVirtualButton() {
                List<VirtualButton.Node> nodes = VirtualButton.Value.Nodes;
                nodes.Clear();
                nodes.AddRange(GetKeys().Select(key => new VirtualButton.KeyboardKey(key)));

                if (GetButton() != null) {
                    nodes.Add(new VirtualButton.PadButton(Input.Gamepad, (Buttons)GetButton()));
                }
            }

            public Buttons? GetButton() {
                return (Buttons?)Settings.GetPropertyValue($"Controller{Mappings}");
            }

            public void SetButton(Buttons? button) {
                Settings.SetPropertyValue($"Controller{Mappings}", button);
            }

            public void SetKeys(List<Keys> keys) {
                Settings.SetPropertyValue($"Keyboard{Mappings}", keys);
            }

            public List<Keys> GetKeys() {
                return (List<Keys>)Settings.GetPropertyValue($"Keyboard{Mappings}");
            }

            public string GetLabel() {
                return (typeof(DialogIds).GetFieldValue(Mappings.ToString()) as string).DialogClean();
            }
        }

        private static readonly Dictionary<Mappings, ButtonInfo> ButtonInfos = new List<ButtonInfo> {
            new(Mappings.SaveState, Keys.F7),
            new(Mappings.LoadState, Keys.F8),
            new(Mappings.ClearState, Keys.F3),
            new(Mappings.OpenDebugMap, Keys.F6, true),
            new(Mappings.ResetRoomTimerPb, Keys.F9),
            new(Mappings.SwitchRoomTimer, Keys.F10),
            new(Mappings.SetEndPoint, Keys.F11),
            new(Mappings.SetAdditionalEndPoint),
            new(Mappings.CheckDeathStatistics, Keys.F12),
            new(Mappings.TeleportToLastRoom, Keys.PageUp),
            new(Mappings.TeleportToNextRoom, Keys.PageDown),
            new(Mappings.SwitchAutoLoadState),
            new(Mappings.ToggleFullscreen),
        }.ToDictionary(info => info.Mappings, info => info);

        public static ButtonInfo GetButtonInfo(Mappings mappings) {
            return ButtonInfos[mappings];
        }

        public static VirtualButton GetVirtualButton(Mappings mappings) {
            return ButtonInfos[mappings].VirtualButton.Value;
        }

        public static void Init() {
            foreach (ButtonInfo buttonInfo in ButtonInfos.Values) {
                buttonInfo.UpdateVirtualButton();
            }
        }

        private bool closing;
        private float inputDelay;
        private bool remapping;
        private Mappings remappingType;
        private float remappingEase;
        private bool remappingKeyboard;
        private float timeout;

        public ButtonConfigUi() {
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

        private static VirtualButton CreateVirtualButton() {
            return new VirtualButton(0.08f);
        }

        private void Reload(int index = -1) {
            Clear();

            Add(new Header(Dialog.Clean(DialogIds.ButtonConfig)));

            Add(new SubHeader(Dialog.Clean(DialogIds.PressDeleteToRemoveButton)));

            Add(new SubHeader(Dialog.Clean(DialogIds.Keyboard)));
            foreach (KeyValuePair<Mappings, ButtonInfo> pair in ButtonInfos) {
                AddKeyboardSetting(pair.Key, pair.Value.GetKeys());
            }

            Add(new SubHeader(Dialog.Clean(DialogIds.Controller)));
            foreach (KeyValuePair<Mappings, ButtonInfo> pair in ButtonInfos) {
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

        private void AddControllerSetting(Mappings mappingType, Buttons? button) {
            Setting setting = new(ButtonInfos[mappingType].GetLabel(), Keys.None);
            setting.Pressed(() => Remap(mappingType));
            if (button != null) {
                setting.Set(new List<Buttons> { (Buttons)button });
            }

            Add(setting);
        }

        private void AddKeyboardSetting(Mappings mappingType, List<Keys> keys) {
            ButtonInfo buttonInfo = ButtonInfos[mappingType];
            Add(new Setting(buttonInfo.GetLabel(), keys).Pressed(() => Remap(mappingType, true)));
        }

        private static void SetDefaultButtons() {
            foreach (ButtonInfo buttonInfo in ButtonInfos.Values) {
                buttonInfo.SetButton(null);
                buttonInfo.SetKeys(buttonInfo.DefaultKeys.ToList());
                buttonInfo.UpdateVirtualButton();
            }
        }


        private void Remap(Mappings mapping, bool remapKeyboard = false) {
            remapping = true;
            remappingKeyboard = remapKeyboard;
            remappingType = mapping;
            timeout = 5f;
            Focused = false;
        }

        private void SetRemap(Buttons button) {
            remapping = false;
            inputDelay = 0.25f;
            ButtonInfo info = ButtonInfos[remappingType];
            if (info.GetButton().HasValue && info.GetButton().Value == button) {
                info.SetButton(null);
            } else {
                info.SetButton(button);
                foreach (ButtonInfo otherInfo in ButtonInfos.Values) {
                    if (otherInfo == info) {
                        continue;
                    }

                    if (otherInfo.GetButton().HasValue && otherInfo.GetButton().Value == button) {
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
            ButtonInfos[remappingType].With(info => {
                if (info.GetKeys().Contains(key) && !(info.FixedDefaultKeys && info.DefaultKeys.Contains(key))) {
                    info.GetKeys().Remove(key);
                } else {
                    if (!info.FixedDefaultKeys) {
                        info.GetKeys().Clear();
                    }

                    info.GetKeys().Add(key);
                    foreach (ButtonInfo otherInfo in ButtonInfos.Values) {
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
                if (Input.ESC.Pressed || Input.MenuCancel || MInput.Keyboard.Pressed(Keys.Delete) || timeout <= 0.0) {
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
                if (index > ButtonInfos.Count - 1) {
                    index--;
                    index %= ButtonInfos.Count;
                    keyboard = false;
                }

                ButtonInfo buttonInfo = ButtonInfos.Values.ToList()[index];
                if (keyboard) {
                    buttonInfo.GetKeys().Clear();
                    if (buttonInfo.FixedDefaultKeys) {
                        buttonInfo.SetKeys(buttonInfo.DefaultKeys.ToList());
                    }
                } else {
                    buttonInfo.SetButton(null);
                }

                buttonInfo.UpdateVirtualButton();
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
                ActiveFont.Draw(ButtonInfos[remappingType].GetLabel(),
                    position + new Vector2(0.0f, 8f), new Vector2(0.5f, 0.0f), Vector2.One * 2f,
                    Color.White * Ease.CubeIn(remappingEase));
            } else {
                ActiveFont.Draw(Dialog.Clean(DialogIds.BtnConfigNoController), position, new Vector2(0.5f, 0.5f),
                    Vector2.One,
                    Color.White * Ease.CubeIn(remappingEase));
            }
        }

        public enum Mappings {
            SaveState,
            LoadState,
            ClearState,
            OpenDebugMap,
            ResetRoomTimerPb,
            SwitchRoomTimer,
            SetEndPoint,
            SetAdditionalEndPoint,
            CheckDeathStatistics,
            TeleportToLastRoom,
            TeleportToNextRoom,
            SwitchAutoLoadState,
            ToggleFullscreen,
        }
    }

    internal static class MappingsExtensions {
        private static readonly Lazy<FieldInfo> TasRunning = new(() =>
            Type.GetType("TAS.Manager, CelesteTAS-EverestInterop")?.GetFieldInfo("Running")
        );

        public static bool Pressed(this ButtonConfigUi.Mappings mappings) {
            if (TasRunning.Value?.GetValue(null) as bool? == true) {
                return false;
            }

            if (Engine.Scene.Tracker.Entities.TryGetValue(typeof(ButtonConfigUi), out List<Entity> entities) && entities.Count > 0) {
                return false;
            }

            return ButtonConfigUi.GetVirtualButton(mappings).Pressed;
        }

        public static void ConsumePress(this ButtonConfigUi.Mappings mappings) {
            ButtonConfigUi.GetVirtualButton(mappings).ConsumePress();
        }
    }
}