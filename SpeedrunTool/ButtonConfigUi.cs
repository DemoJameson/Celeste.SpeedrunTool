using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;

namespace Celeste.Mod.SpeedrunTool {
    public class ButtonConfigUi : TextMenu {
        public const Keys DefaultKeyboardSave = Keys.F7;
        public const Keys DefaultKeyboardLoad = Keys.F8;
        public const Keys DefaultKeyboardResetPb = Keys.F9;
        public const Keys DefaultKeyboardSwitchRoomTimer = Keys.F10;
        public const Keys DefaultKeyboardSetEndPoint = Keys.F11;
        public const Keys DefaultKeyboardCheckDeathStatistics = Keys.F12;
        public const Keys DefaultKeyboardLastRoom = Keys.PageUp;
        public const Keys DefaultKeyboardNextRoom = Keys.PageDown;

        public static readonly List<Keys> FixedClearKeys = new List<Keys> {Keys.F3, Keys.F6};
        public static readonly List<Keys> FixedOpenDebugMapKeys = new List<Keys> {Keys.F6};

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
            Buttons.Back
        };

        public static readonly Lazy<VirtualButton> SaveButton = new Lazy<VirtualButton>(CreateVirtualButton);
        public static readonly Lazy<VirtualButton> LoadButton = new Lazy<VirtualButton>(CreateVirtualButton);
        public static readonly Lazy<VirtualButton> ClearButton = new Lazy<VirtualButton>(CreateVirtualButton);
        public static readonly Lazy<VirtualButton> OpenDebugButton = new Lazy<VirtualButton>(CreateVirtualButton);
        public static readonly Lazy<VirtualButton> ResetRoomPbButton = new Lazy<VirtualButton>(CreateVirtualButton);
        public static readonly Lazy<VirtualButton> SwitchRoomTimerButton = new Lazy<VirtualButton>(CreateVirtualButton);
        public static readonly Lazy<VirtualButton> SetEndPointButton = new Lazy<VirtualButton>(CreateVirtualButton);
        public static readonly Lazy<VirtualButton> CheckDeathStatisticsButton = new Lazy<VirtualButton>(CreateVirtualButton);
        public static readonly Lazy<VirtualButton> LastRoomButton = new Lazy<VirtualButton>(CreateVirtualButton);
        public static readonly Lazy<VirtualButton> NextRoomButton = new Lazy<VirtualButton>(CreateVirtualButton);

        private bool closing;
        private float inputDelay;
        private bool remapping;
        private Mappings remappingButton;
        private float remappingEase;
        private bool remappingKeyboard;
        private float timeout;

        public class ButtonInfo {
            public Mappings Mapping;
            public Func<Buttons?> GetButton;
            public Action<Buttons?> SetButton;
            public Func<List<Keys>> GetKeys;
            public Action<List<Keys>> SetKeys;
            
        }

        public static readonly List<ButtonInfo> ButtonInfos = new List<ButtonInfo> {
            new ButtonInfo {
                Mapping = Mappings.Save,
                GetButton = () => Settings.ControllerQuickSave,
                SetButton = button => Settings.ControllerQuickSave = button,
                GetKeys = () => new List<Keys>{Settings.KeyboardQuickSave},
                SetKeys = keys => Settings.KeyboardQuickSave = keys[0],
            },
            new ButtonInfo {
                Mapping = Mappings.Load,
                GetButton = () => Settings.ControllerQuickLoad,
                SetButton = button => Settings.ControllerQuickLoad = button,
                GetKeys = () => new List<Keys>{Settings.KeyboardQuickLoad},
                SetKeys = keys => Settings.KeyboardQuickLoad = keys[0],
            },
            new ButtonInfo {
                Mapping = Mappings.Clear,
                GetButton = () => Settings.ControllerQuickClear,
                SetButton = button => Settings.ControllerQuickClear = button,
                GetKeys = () => Settings.KeyboardQuickClear,
                SetKeys = keys => Settings.KeyboardQuickClear = keys,
            },
            new ButtonInfo {
                Mapping = Mappings.OpenDebugMap,
                GetButton = () => Settings.ControllerOpenDebugMap,
                SetButton = button => Settings.ControllerOpenDebugMap = button,
                GetKeys = () => Settings.KeyboardOpenDebugMap,
                SetKeys = keys => Settings.KeyboardOpenDebugMap = keys,
            },
            new ButtonInfo {
                Mapping = Mappings.ResetRoomPb,
                GetButton = () => Settings.ControllerResetRoomPb,
                SetButton = button => Settings.ControllerResetRoomPb = button,
                GetKeys = () => new List<Keys>{Settings.KeyboardResetRoomPb},
                SetKeys = keys => Settings.KeyboardResetRoomPb = keys[0],
            },
            new ButtonInfo {
                Mapping = Mappings.SwitchRoomTimer,
                GetButton = () => Settings.ControllerSwitchRoomTimer,
                SetButton = button => Settings.ControllerSwitchRoomTimer = button,
                GetKeys = () => new List<Keys>{Settings.KeyboardSwitchRoomTimer},
                SetKeys = keys => Settings.KeyboardSwitchRoomTimer = keys[0],
            },
            new ButtonInfo {
                Mapping = Mappings.SetEndPoint,
                GetButton = () => Settings.ControllerSetEndPoint,
                SetButton = button => Settings.ControllerSetEndPoint = button,
                GetKeys = () => new List<Keys>{Settings.KeyboardSetEndPoint},
                SetKeys = keys => Settings.KeyboardSetEndPoint = keys[0],
            },
            new ButtonInfo {
                Mapping = Mappings.CheckDeathStatistics,
                GetButton = () => Settings.ControllerCheckDeathStatistics,
                SetButton = button => Settings.ControllerCheckDeathStatistics = button,
                GetKeys = () => new List<Keys>{Settings.KeyboardCheckDeathStatistics},
                SetKeys = keys => Settings.KeyboardCheckDeathStatistics = keys[0],
            },
            new ButtonInfo {
                Mapping = Mappings.LastRoom,
                GetButton = () => Settings.ControllerLastRoom,
                SetButton = button => Settings.ControllerLastRoom = button,
                GetKeys = () => new List<Keys>{Settings.KeyboardLastRoom},
                SetKeys = keys => Settings.KeyboardLastRoom = keys[0],
            },
            new ButtonInfo {
                Mapping = Mappings.NextRoom,
                GetButton = () => Settings.ControllerNextRoom,
                SetButton = button => Settings.ControllerNextRoom = button,
                GetKeys = () => new List<Keys>{Settings.KeyboardNextRoom},
                SetKeys = keys => Settings.KeyboardNextRoom = keys[0],
            },
        };

        public ButtonConfigUi() {
            Reload();
            OnESC = OnCancel = () => {
                Focused = false;
                closing = true;
                SpeedrunToolModule.Instance.SaveSettings();
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
            Add(new SubHeader(Dialog.Clean(DialogIds.Controller)));
            
            foreach (ButtonInfo buttonInfo in ButtonInfos) {
                AddControllerSetting(buttonInfo.Mapping, buttonInfo.GetButton());
            }

            Add(new SubHeader(Dialog.Clean(DialogIds.Keyboard)));

            foreach (ButtonInfo buttonInfo in ButtonInfos) {
                AddKeyboardSetting(buttonInfo.Mapping, buttonInfo.GetKeys());
            }
            
            Add(new SubHeader(""));

            Button resetButton = new Button(Dialog.Clean(DialogIds.KeyConfigReset)) {
                IncludeWidthInMeasurement = false,
                AlwaysCenter = true,
                OnPressed = () => {
                    SetDefaultButtons();
                    Reload(Selection);
                }
            };
            Add(resetButton);

            if (index < 0) {
                return;
            }

            Selection = index;
        }

        private void AddControllerSetting(Mappings mappingType, Buttons? button) {
            Setting setting = new Setting(Label(mappingType), Keys.None);
            setting.Pressed(() => Remap(mappingType));
            if (button != null) {
                setting.Set(new List<Buttons> {(Buttons) button});
            }
            Add(setting);
        }

        private void AddKeyboardSetting(Mappings mappingType, List<Keys> keys) {
            Add(new Setting(Label(mappingType), keys).Pressed(() => Remap(mappingType, true)));
        }

        private static void SetDefaultButtons() {
            foreach (ButtonInfo buttonInfo in ButtonInfos) {
                buttonInfo.SetButton(null);
            }

            Settings.KeyboardQuickSave = DefaultKeyboardSave;
            Settings.KeyboardQuickLoad = DefaultKeyboardLoad;
            Settings.KeyboardQuickClear = FixedClearKeys;
            Settings.KeyboardOpenDebugMap = FixedOpenDebugMapKeys;
            Settings.KeyboardResetRoomPb = DefaultKeyboardResetPb;
            Settings.KeyboardSwitchRoomTimer = DefaultKeyboardSwitchRoomTimer;
            Settings.KeyboardSetEndPoint = DefaultKeyboardSetEndPoint;
            Settings.KeyboardCheckDeathStatistics = DefaultKeyboardCheckDeathStatistics;
            Settings.KeyboardLastRoom = DefaultKeyboardLastRoom;
            Settings.KeyboardNextRoom = DefaultKeyboardNextRoom;

            UpdateSaveButton();
            UpdateLoadButton();
            UpdateClearButton();
            UpdateOpenDebugMapButton();
            UpdateResetRoomPbButton();
            UpdateSwitchRoomTimerButton();
            UpdateSetEndPointButton();
            UpdateCheckDeathStatisticsButton();
            UpdateLastRoomButton();
            UpdateNextRoomButton();
        }


        private void Remap(Mappings mapping, bool remapKeyboard = false) {
            remapping = true;
            remappingKeyboard = remapKeyboard;
            remappingButton = mapping;
            timeout = 5f;
            Focused = false;
        }

        private void SetRemap(Buttons button) {
            remapping = false;
            inputDelay = 0.25f;
            switch (remappingButton) {
                case Mappings.Save:
                    Settings.ControllerQuickSave = button;
                    UpdateSaveButton();
                    break;
                case Mappings.Load:
                    Settings.ControllerQuickLoad = button;
                    UpdateLoadButton();
                    break;
                case Mappings.Clear:
                    Settings.ControllerQuickClear = button;
                    UpdateClearButton();
                    break;
                case Mappings.OpenDebugMap:
                    Settings.ControllerOpenDebugMap = button;
                    UpdateOpenDebugMapButton();
                    break;
                case Mappings.ResetRoomPb:
                    Settings.ControllerResetRoomPb = button;
                    UpdateResetRoomPbButton();
                    break;
                case Mappings.SwitchRoomTimer:
                    Settings.ControllerSwitchRoomTimer = button;
                    UpdateSwitchRoomTimerButton();
                    break;
                case Mappings.SetEndPoint:
                    Settings.ControllerSetEndPoint = button;
                    UpdateSetEndPointButton();
                    break;
                case Mappings.CheckDeathStatistics:
                    Settings.ControllerCheckDeathStatistics = button;
                    UpdateCheckDeathStatisticsButton();
                    break;
                case Mappings.LastRoom:
                    Settings.ControllerLastRoom = button;
                    UpdateLastRoomButton();
                    break;
                case Mappings.NextRoom:
                    Settings.ControllerNextRoom = button;
                    UpdateNextRoomButton();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            Reload(Selection);
        }

        private void SetRemap(Keys key) {
            remapping = false;
            inputDelay = 0.25f;
            switch (remappingButton) {
                case Mappings.Save:
                    Settings.KeyboardQuickSave = key;
                    UpdateSaveButton();
                    break;
                case Mappings.Load:
                    Settings.KeyboardQuickLoad = key;
                    UpdateLoadButton();
                    break;
                case Mappings.Clear:
                    Settings.KeyboardQuickClear.Clear();
                    Settings.KeyboardQuickClear.AddRange(FixedClearKeys);
                    Settings.KeyboardQuickClear.Add(key);
                    UpdateClearButton();
                    break;
                case Mappings.OpenDebugMap:
                    Settings.KeyboardOpenDebugMap.Clear();
                    Settings.KeyboardOpenDebugMap.AddRange(FixedOpenDebugMapKeys);
                    Settings.KeyboardOpenDebugMap.Add(key);
                    UpdateOpenDebugMapButton();
                    break;
                case Mappings.ResetRoomPb:
                    Settings.KeyboardResetRoomPb = key;
                    UpdateResetRoomPbButton();
                    break;
                case Mappings.SwitchRoomTimer:
                    Settings.KeyboardSwitchRoomTimer = key;
                    UpdateSwitchRoomTimerButton();
                    break;
                case Mappings.SetEndPoint:
                    Settings.KeyboardSetEndPoint = key;
                    UpdateSetEndPointButton();
                    break;
                case Mappings.CheckDeathStatistics:
                    Settings.KeyboardCheckDeathStatistics = key;
                    UpdateCheckDeathStatisticsButton();
                    break;
                case Mappings.LastRoom:
                    Settings.KeyboardLastRoom = key;
                    UpdateLastRoomButton();
                    break;
                case Mappings.NextRoom:
                    Settings.KeyboardNextRoom = key;
                    UpdateNextRoomButton();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            Reload(Selection);
        }

        public static void UpdateSaveButton() {
            List<VirtualButton.Node> nodes = SaveButton.Value.Nodes;
            nodes.Clear();
            nodes.Add(new VirtualButton.KeyboardKey(Settings.KeyboardQuickSave));

            if (Settings.ControllerQuickSave != null) {
                nodes.Add(new VirtualButton.PadButton(Input.Gamepad, (Buttons) Settings.ControllerQuickSave));
            }
        }

        public static void UpdateLoadButton() {
            List<VirtualButton.Node> nodes = LoadButton.Value.Nodes;
            nodes.Clear();
            nodes.Add(new VirtualButton.KeyboardKey(Settings.KeyboardQuickLoad));

            if (Settings.ControllerQuickLoad != null) {
                nodes.Add(new VirtualButton.PadButton(Input.Gamepad, (Buttons) Settings.ControllerQuickLoad));
            }
        }

        public static void UpdateClearButton() {
            List<VirtualButton.Node> nodes = ClearButton.Value.Nodes;
            nodes.Clear();
            nodes.AddRange(Settings.KeyboardQuickClear.Select(clearKey => new VirtualButton.KeyboardKey(clearKey)));

            if (Settings.ControllerQuickClear != null) {
                nodes.Add(new VirtualButton.PadButton(Input.Gamepad, (Buttons) Settings.ControllerQuickClear));
            }
        }

        public static void UpdateOpenDebugMapButton() {
            List<VirtualButton.Node> nodes = OpenDebugButton.Value.Nodes;
            nodes.Clear();
            nodes.AddRange(Settings.KeyboardOpenDebugMap.Select(clearKey => new VirtualButton.KeyboardKey(clearKey)));
            if (Settings.ControllerOpenDebugMap != null) {
                nodes.Add(new VirtualButton.PadButton(Input.Gamepad, (Buttons) Settings.ControllerOpenDebugMap));
            }
        }

        public static void UpdateResetRoomPbButton() {
            List<VirtualButton.Node> nodes = ResetRoomPbButton.Value.Nodes;
            nodes.Clear();
            nodes.Add(new VirtualButton.KeyboardKey(Settings.KeyboardResetRoomPb));
            if (Settings.ControllerResetRoomPb != null) {
                nodes.Add(new VirtualButton.PadButton(Input.Gamepad, (Buttons) Settings.ControllerResetRoomPb));
            }
        }

        public static void UpdateSwitchRoomTimerButton() {
            List<VirtualButton.Node> nodes = SwitchRoomTimerButton.Value.Nodes;
            nodes.Clear();
            nodes.Add(new VirtualButton.KeyboardKey(Settings.KeyboardSwitchRoomTimer));
            if (Settings.ControllerSwitchRoomTimer != null) {
                nodes.Add(new VirtualButton.PadButton(Input.Gamepad, (Buttons) Settings.ControllerSwitchRoomTimer));
            }
        }

        public static void UpdateSetEndPointButton() {
            List<VirtualButton.Node> nodes = SetEndPointButton.Value.Nodes;
            nodes.Clear();
            nodes.Add(new VirtualButton.KeyboardKey(Settings.KeyboardSetEndPoint));
            if (Settings.ControllerSetEndPoint != null) {
                nodes.Add(new VirtualButton.PadButton(Input.Gamepad, (Buttons) Settings.ControllerSetEndPoint));
            }
        }

        public static void UpdateCheckDeathStatisticsButton() {
            List<VirtualButton.Node> nodes = CheckDeathStatisticsButton.Value.Nodes;
            nodes.Clear();
            nodes.Add(new VirtualButton.KeyboardKey(Settings.KeyboardCheckDeathStatistics));
            if (Settings.ControllerCheckDeathStatistics != null) {
                nodes.Add(new VirtualButton.PadButton(Input.Gamepad, (Buttons) Settings.ControllerCheckDeathStatistics));
            }
        }

        public static void UpdateLastRoomButton() {
            List<VirtualButton.Node> nodes = LastRoomButton.Value.Nodes;
            nodes.Clear();
            nodes.Add(new VirtualButton.KeyboardKey(Settings.KeyboardLastRoom));
            if (Settings.ControllerLastRoom != null) {
                nodes.Add(new VirtualButton.PadButton(Input.Gamepad, (Buttons) Settings.ControllerLastRoom));
            }
        }

        public static void UpdateNextRoomButton() {
            List<VirtualButton.Node> nodes = NextRoomButton.Value.Nodes;
            nodes.Clear();
            nodes.Add(new VirtualButton.KeyboardKey(Settings.KeyboardNextRoom));
            if (Settings.ControllerNextRoom != null) {
                nodes.Add(new VirtualButton.PadButton(Input.Gamepad, (Buttons) Settings.ControllerNextRoom));
            }
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
                    remappingKeyboard ? Dialog.Get(DialogIds.KeyConfigChanging) : Dialog.Get(DialogIds.BtnConfigChanging),
                    position + new Vector2(0.0f, -8f),
                    new Vector2(0.5f, 1f),
                    Vector2.One * 0.7f,
                    Color.LightGray * Ease.CubeIn(remappingEase));
                ActiveFont.Draw(Label(remappingButton),
                    position + new Vector2(0.0f, 8f), new Vector2(0.5f, 0.0f), Vector2.One * 2f,
                    Color.White * Ease.CubeIn(remappingEase));
            } else {
                ActiveFont.Draw(Dialog.Clean(DialogIds.BtnConfigNoController), position, new Vector2(0.5f, 0.5f), Vector2.One,
                    Color.White * Ease.CubeIn(remappingEase));
            }
        }

        private static string Label(Mappings mapping) {
            switch (mapping) {
                case Mappings.Save:
                    return Dialog.Clean(DialogIds.Save);
                case Mappings.Load:
                    return Dialog.Clean(DialogIds.Load);
                case Mappings.Clear:
                    return Dialog.Clean(DialogIds.Clear);
                case Mappings.OpenDebugMap:
                    return Dialog.Clean(DialogIds.OpenDebugMap);
                case Mappings.ResetRoomPb:
                    return Dialog.Clean(DialogIds.ResetRoomPb);
                case Mappings.SwitchRoomTimer:
                    return Dialog.Clean(DialogIds.SwitchRoomTimer);
                case Mappings.SetEndPoint:
                    return Dialog.Clean(DialogIds.SetEndPoint);
                case Mappings.CheckDeathStatistics:
                    return Dialog.Clean(DialogIds.CheckDeathStatistics);
                case Mappings.LastRoom:
                    return Dialog.Clean(DialogIds.TeleportToLastRoom);
                case Mappings.NextRoom:
                    return Dialog.Clean(DialogIds.TeleportToNextRoom);
                default:
                    return "Unknown";
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
            CheckDeathStatistics,
            LastRoom,
            NextRoom
        }
    }
}