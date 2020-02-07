using System;
using System.Collections.Generic;
using System.Linq;
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
        public const Keys DefaultKeyboardLastRoom = Keys.PageUp;
        public const Keys DefaultKeyboardNextRoom = Keys.PageDown;

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

        public static readonly Keys[] FixedClearKeys = {Keys.F3, Keys.F6};
        public static readonly Keys[] FixedOpenDebugMapKeys = {Keys.F6};

        public static readonly Lazy<VirtualButton> SaveButton = new Lazy<VirtualButton>(CreateVirtualButton);
        public static readonly Lazy<VirtualButton> LoadButton = new Lazy<VirtualButton>(CreateVirtualButton);
        public static readonly Lazy<VirtualButton> ClearButton = new Lazy<VirtualButton>(CreateVirtualButton);
        public static readonly Lazy<VirtualButton> OpenDebugButton = new Lazy<VirtualButton>(CreateVirtualButton);
        public static readonly Lazy<VirtualButton> ResetRoomPbButton = new Lazy<VirtualButton>(CreateVirtualButton);
        public static readonly Lazy<VirtualButton> SwitchRoomTimerButton = new Lazy<VirtualButton>(CreateVirtualButton);
        public static readonly Lazy<VirtualButton> SetEndPointButton = new Lazy<VirtualButton>(CreateVirtualButton);
        public static readonly Lazy<VirtualButton> LastRoomButton = new Lazy<VirtualButton>(CreateVirtualButton);
        public static readonly Lazy<VirtualButton> NextRoomButton = new Lazy<VirtualButton>(CreateVirtualButton);

        private bool closing;
        private float inputDelay;
        private bool remapping;
        private Mappings remappingButton;
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
            Add(new SubHeader(Dialog.Clean(DialogIds.Controller)));

            AddControllerSetting(Mappings.Save, Settings.ControllerQuickSave);
            AddControllerSetting(Mappings.Load, Settings.ControllerQuickLoad);
            AddControllerSetting(Mappings.Clear, Settings.ControllerQuickClear);
            AddControllerSetting(Mappings.OpenDebugMap, Settings.ControllerOpenDebugMap);
            AddControllerSetting(Mappings.ResetRoomPb, Settings.ControllerResetRoomPb);
            AddControllerSetting(Mappings.SwitchRoomTimer, Settings.ControllerSwitchRoomTimer);
            AddControllerSetting(Mappings.SetEndPoint, Settings.ControllerSetEndPoint);
            AddControllerSetting(Mappings.LastRoom, Settings.ControllerLastRoom);
            AddControllerSetting(Mappings.NextRoom, Settings.ControllerNextRoom);

            Add(new SubHeader(Dialog.Clean(DialogIds.Keyboard)));
            
            Add(new Setting(Label(Mappings.Save), Settings.KeyboardQuickSave).Pressed(() =>
                Remap(Mappings.Save, true)));
            Add(new Setting(Label(Mappings.Load), Settings.KeyboardQuickLoad).Pressed(() =>
                Remap(Mappings.Load, true)));
            Add(new Setting(Label(Mappings.Clear), Settings.KeyboardQuickClear).Pressed(() =>
                Remap(Mappings.Clear, true)));
            Add(new Setting(Label(Mappings.OpenDebugMap), Settings.KeyboardOpenDebugMap).Pressed(() =>
                Remap(Mappings.OpenDebugMap, true)));
            Add(new Setting(Label(Mappings.ResetRoomPb), Settings.KeyboardResetRoomPb).Pressed(() =>
                Remap(Mappings.ResetRoomPb, true)));
            Add(new Setting(Label(Mappings.SwitchRoomTimer), Settings.KeyboardSwitchRoomTimer).Pressed(() =>
                Remap(Mappings.SwitchRoomTimer, true)));
            Add(new Setting(Label(Mappings.SetEndPoint), Settings.KeyboardSetEndPoint).Pressed(() =>
                Remap(Mappings.SetEndPoint, true)));
            Add(new Setting(Label(Mappings.LastRoom), Settings.KeyboardLastRoom).Pressed(() =>
                Remap(Mappings.LastRoom, true)));
            Add(new Setting(Label(Mappings.NextRoom), Settings.KeyboardNextRoom).Pressed(() =>
                Remap(Mappings.NextRoom, true)));

            Add(new SubHeader(""));
            Button button = new Button(Dialog.Clean(DialogIds.KeyConfigReset)) {
                IncludeWidthInMeasurement = false,
                AlwaysCenter = true,
                OnPressed = () => {
                    SetDefaultButtons();
                    Reload(Selection);
                }
            };
            Add(button);

            if (index < 0) {
                return;
            }

            Selection = index;
        }

        private void AddControllerSetting(Mappings mappingType, Buttons? button) {
            Add(new Setting(Label(mappingType), Keys.None).With(setting => {
                    if (button != null) {
                        setting.Set(new List<Buttons> {(Buttons) button});
                    }
                }
            ).Pressed(() => Remap(mappingType)));
        }

        private static void SetDefaultButtons() {
            Settings.ControllerQuickSave = null;
            Settings.ControllerQuickLoad = null;
            Settings.ControllerQuickClear = null;
            Settings.ControllerOpenDebugMap = null;
            Settings.ControllerResetRoomPb = null;
            Settings.ControllerSwitchRoomTimer = null;
            Settings.ControllerSetEndPoint = null;
            Settings.ControllerLastRoom = null;
            Settings.ControllerNextRoom = null;

            Settings.KeyboardQuickSave = DefaultKeyboardSave;
            Settings.KeyboardQuickLoad = DefaultKeyboardLoad;
            Settings.KeyboardQuickClear = FixedClearKeys.ToList();
            Settings.KeyboardOpenDebugMap = FixedOpenDebugMapKeys.ToList();
            Settings.KeyboardResetRoomPb = DefaultKeyboardResetPb;
            Settings.KeyboardSwitchRoomTimer = DefaultKeyboardSwitchRoomTimer;
            Settings.KeyboardSetEndPoint = DefaultKeyboardSetEndPoint;
            Settings.KeyboardLastRoom = DefaultKeyboardLastRoom;
            Settings.KeyboardNextRoom = DefaultKeyboardNextRoom;

            UpdateSaveButton();
            UpdateLoadButton();
            UpdateClearButton();
            UpdateOpenDebugMapButton();
            UpdateResetRoomPbButton();
            UpdateSwitchRoomTimerButton();
            UpdateSetEndPointButton();
            UpdateLastRoomButton();
            UpdateNextRoomButton();
        }


        private void Remap(Mappings mapping, bool remappingKeyboard = false) {
            remapping = true;
            this.remappingKeyboard = remappingKeyboard;
            remappingButton = mapping;
            timeout = 5f;
            Focused = false;
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
                }
                else if (remappingKeyboard) {
                    Keys[] pressedKeys = MInput.Keyboard.CurrentState.GetPressedKeys();
                    if (pressedKeys != null && pressedKeys.Length != 0 &&
                        MInput.Keyboard.Pressed(pressedKeys[pressedKeys.Length - 1])) {
                        SetRemap(pressedKeys[pressedKeys.Length - 1]);
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
            }
            else {
                ActiveFont.Draw(Dialog.Clean(DialogIds.BtnConfigNoController), position, new Vector2(0.5f, 0.5f), Vector2.One,
                    Color.White * Ease.CubeIn(remappingEase));
            }
        }

        private string Label(Mappings mapping) {
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
                case Mappings.LastRoom:
                    return Dialog.Clean(DialogIds.TeleportToLastRoom);
                case Mappings.NextRoom:
                    return Dialog.Clean(DialogIds.TeleportToNextRoom);
                default:
                    return "Unknown";
            }
        }

        private enum Mappings {
            Save,
            Load,
            Clear,
            OpenDebugMap,
            ResetRoomPb,
            SwitchRoomTimer,
            SetEndPoint,
            LastRoom,
            NextRoom
        }
    }
}