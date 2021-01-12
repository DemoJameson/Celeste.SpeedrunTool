using System;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;

namespace Celeste.Mod.SpeedrunTool {
    public class ButtonConfigUi : TextMenu {
        private static readonly List<Buttons> AllButtons = new List<Buttons> {
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
            Buttons.BigButton
        };

        public class ButtonInfo {
            public Func<Buttons?> GetButton;
            public Action<Buttons?> SetButton;
            public Func<List<Keys>> GetKeys;
            public Action<List<Keys>> SetKeys;
            public Keys[] DefaultKeys;
            public Func<string> GetLabel;
            public readonly Lazy<VirtualButton> VirtualButton = new Lazy<VirtualButton>(CreateVirtualButton);
            public bool FixedDefaultKeys;

            public void UpdateVirtualButton() {
                List<VirtualButton.Node> nodes = VirtualButton.Value.Nodes;
                nodes.Clear();
                nodes.AddRange(GetKeys().Select(key => new VirtualButton.KeyboardKey(key)));

                if (GetButton() != null) {
                    nodes.Add(new VirtualButton.PadButton(Input.Gamepad, (Buttons) GetButton()));
                }
            }
        }

        private static readonly Dictionary<Mappings, ButtonInfo> ButtonInfos = new Dictionary<Mappings, ButtonInfo> {
            {
                Mappings.Save,
                new ButtonInfo {
                    GetButton = () => Settings.ControllerQuickSave,
                    SetButton = button => Settings.ControllerQuickSave = button,
                    GetKeys = () => Settings.KeyboardQuickSave,
                    SetKeys = keys => Settings.KeyboardQuickSave = keys,
                    DefaultKeys = new[] {Keys.F7},
                    GetLabel = () => DialogIds.Save.DialogClean(),
                }
            }, {
                Mappings.Load, new ButtonInfo {
                    GetButton = () => Settings.ControllerQuickLoad,
                    SetButton = button => Settings.ControllerQuickLoad = button,
                    GetKeys = () => Settings.KeyboardQuickLoad,
                    SetKeys = keys => Settings.KeyboardQuickLoad = keys,
                    DefaultKeys = new[] {Keys.F8},
                    GetLabel = () => DialogIds.Load.DialogClean(),
                }
            }, {
                Mappings.Clear, new ButtonInfo {
                    GetButton = () => Settings.ControllerQuickClear,
                    SetButton = button => Settings.ControllerQuickClear = button,
                    GetKeys = () => Settings.KeyboardQuickClear,
                    SetKeys = keys => Settings.KeyboardQuickClear = keys,
                    DefaultKeys = new[] {Keys.F3, Keys.F6},
                    FixedDefaultKeys = true,
                    GetLabel = () => DialogIds.Clear.DialogClean(),
                }
            }, {
                Mappings.OpenDebugMap, new ButtonInfo {
                    GetButton = () => Settings.ControllerOpenDebugMap,
                    SetButton = button => Settings.ControllerOpenDebugMap = button,
                    GetKeys = () => Settings.KeyboardOpenDebugMap,
                    SetKeys = keys => Settings.KeyboardOpenDebugMap = keys,
                    DefaultKeys = new[] {Keys.F6},
                    FixedDefaultKeys = true,
                    GetLabel = () => DialogIds.OpenDebugMap.DialogClean(),
                }
            }, {
                Mappings.ResetRoomPb, new ButtonInfo {
                    GetButton = () => Settings.ControllerResetRoomPb,
                    SetButton = button => Settings.ControllerResetRoomPb = button,
                    GetKeys = () => Settings.KeyboardResetRoomPb,
                    SetKeys = keys => Settings.KeyboardResetRoomPb = keys,
                    DefaultKeys = new[] {Keys.F9},
                    GetLabel = () => DialogIds.ResetRoomPb.DialogClean(),
                }
            }, {
                Mappings.SwitchRoomTimer, new ButtonInfo {
                    GetButton = () => Settings.ControllerSwitchRoomTimer,
                    SetButton = button => Settings.ControllerSwitchRoomTimer = button,
                    GetKeys = () => Settings.KeyboardSwitchRoomTimer,
                    SetKeys = keys => Settings.KeyboardSwitchRoomTimer = keys,
                    DefaultKeys = new[] {Keys.F10},
                    GetLabel = () => DialogIds.SwitchRoomTimer.DialogClean(),
                }
            }, {
                Mappings.SetEndPoint, new ButtonInfo {
                    GetButton = () => Settings.ControllerSetEndPoint,
                    SetButton = button => Settings.ControllerSetEndPoint = button,
                    GetKeys = () => Settings.KeyboardSetEndPoint,
                    SetKeys = keys => Settings.KeyboardSetEndPoint = keys,
                    DefaultKeys = new[] {Keys.F11},
                    GetLabel = () => DialogIds.SetEndPoint.DialogClean(),
                }
            }, {
                Mappings.SetAdditionalEndPoint, new ButtonInfo {
                    GetButton = () => Settings.ControllerSetAdditionalEndPoint,
                    SetButton = button => Settings.ControllerSetAdditionalEndPoint = button,
                    GetKeys = () => Settings.KeyboardSetAdditionalEndPoint,
                    SetKeys = keys => Settings.KeyboardSetAdditionalEndPoint = keys,
                    DefaultKeys = new Keys[]{},
                    GetLabel = () => DialogIds.SetAdditionalEndPoint.DialogClean(),
                }
            }, {
                Mappings.CheckDeathStatistics, new ButtonInfo {
                    GetButton = () => Settings.ControllerCheckDeathStatistics,
                    SetButton = button => Settings.ControllerCheckDeathStatistics = button,
                    GetKeys = () => Settings.KeyboardCheckDeathStatistics,
                    SetKeys = keys => Settings.KeyboardCheckDeathStatistics = keys,
                    DefaultKeys = new[] {Keys.F12},
                    GetLabel = () => DialogIds.CheckDeathStatistics.DialogClean(),
                }
            }, {
                Mappings.LastRoom, new ButtonInfo {
                    GetButton = () => Settings.ControllerLastRoom,
                    SetButton = button => Settings.ControllerLastRoom = button,
                    GetKeys = () => Settings.KeyboardLastRoom,
                    SetKeys = keys => Settings.KeyboardLastRoom = keys,
                    DefaultKeys = new[] {Keys.PageUp},
                    GetLabel = () => DialogIds.TeleportToLastRoom.DialogClean(),
                }
            }, {
                Mappings.NextRoom, new ButtonInfo {
                    GetButton = () => Settings.ControllerNextRoom,
                    SetButton = button => Settings.ControllerNextRoom = button,
                    GetKeys = () => Settings.KeyboardNextRoom,
                    SetKeys = keys => Settings.KeyboardNextRoom = keys,
                    DefaultKeys = new[] {Keys.PageDown},
                    GetLabel = () => DialogIds.TeleportToNextRoom.DialogClean(),
                }
            }, {
                Mappings.SwitchAutoLoadState, new ButtonInfo {
                    GetButton = () => Settings.ControllerAutoLoadStateAfterDeath,
                    SetButton = button => Settings.ControllerAutoLoadStateAfterDeath = button,
                    GetKeys = () => Settings.KeyboardAutoLoadStateAfterDeath,
                    SetKeys = keys => Settings.KeyboardAutoLoadStateAfterDeath = keys,
                    DefaultKeys = new Keys[]{} ,
                    GetLabel = () => DialogIds.SwitchAutoLoadState.DialogClean(),
                }
            }
        };

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

            Add(new SubHeader(Dialog.Clean(DialogIds.Keyboard)));
            foreach (var pair in ButtonInfos) {
                AddKeyboardSetting(pair.Key, pair.Value.GetKeys());
            }

            Add(new SubHeader(Dialog.Clean(DialogIds.Controller)));
            foreach (var pair in ButtonInfos) {
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
            Button resetButton = new Button(Dialog.Clean(DialogIds.KeyConfigReset)) {
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
            Setting setting = new Setting(ButtonInfos[mappingType].GetLabel(), Keys.None);
            setting.Pressed(() => Remap(mappingType));
            if (button != null) {
                setting.Set(new List<Buttons> {(Buttons) button});
            }

            Add(setting);
        }

        private void AddKeyboardSetting(Mappings mappingType, List<Keys> keys) {
            Add(new Setting(ButtonInfos[mappingType].GetLabel(), keys).Pressed(() => Remap(mappingType, true)));
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
            ButtonInfos[remappingType].SetButton(button);
            ButtonInfos[remappingType].UpdateVirtualButton();
            Reload(Selection);
        }

        private void SetRemap(Keys key) {
            remapping = false;
            inputDelay = 0.25f;
            ButtonInfos[remappingType].With(info => {
                info.GetKeys().Clear();

                if (info.FixedDefaultKeys) {
                    info.GetKeys().AddRange(info.DefaultKeys.ToList());
                }

                foreach (ButtonInfo otherInfo in ButtonInfos.Values) {
                    if (otherInfo == info) continue;
                    if (otherInfo.GetKeys().Contains(key) && !(otherInfo.FixedDefaultKeys && otherInfo.DefaultKeys.Contains(key))) {
                        otherInfo.GetKeys().Remove(key);
                        otherInfo.UpdateVirtualButton();
                    }
                }

                if (!info.GetKeys().Contains(key)) {
                    info.GetKeys().Add(key);
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
            Save,
            Load,
            Clear,
            OpenDebugMap,
            ResetRoomPb,
            SwitchRoomTimer,
            SetEndPoint,
            SetAdditionalEndPoint,
            CheckDeathStatistics,
            LastRoom,
            NextRoom,
            SwitchAutoLoadState,
        }
    }
}