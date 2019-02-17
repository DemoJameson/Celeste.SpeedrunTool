using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad
{
    public class ButtonConfig : TextMenu
    {
        private static readonly List<Buttons> AllButtons = new List<Buttons>
        {
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

        public static readonly IEnumerable<Keys> FixedClearKeys = new List<Keys>
        {
            Keys.F3,
            Keys.F6
        };

        public static readonly IEnumerable<Keys> FixedOpenDebugMapKeys = new List<Keys>
        {
            Keys.F6
        };
        
        

        private static VirtualButton CreateVirtualButton()
        {
            return new VirtualButton(0.08f);
        }

        public static readonly Lazy<VirtualButton> SaveButton = new Lazy<VirtualButton>(CreateVirtualButton);
        public static readonly Lazy<VirtualButton> LoadButton = new Lazy<VirtualButton>(CreateVirtualButton);
        public static readonly Lazy<VirtualButton> ClearButton = new Lazy<VirtualButton>(CreateVirtualButton);
        public static readonly Lazy<VirtualButton> OpenDebugButton = new Lazy<VirtualButton>(CreateVirtualButton);
        public static readonly Lazy<VirtualButton> ResetRoomPbButton = new Lazy<VirtualButton>(CreateVirtualButton);
        
        private static  SpeedrunToolModuleSettings Settings => SpeedrunToolModule.Settings;

        private bool _closing;
        private float _inputDelay;
        private bool _remapping;
        private Mappings _remappingButton;
        private float _remappingEase;
        private bool _remappingKeyboard;
        private float _timeout;

        public ButtonConfig()
        {
            Reload();
            OnESC = OnCancel = () =>
            {
                Focused = false;
                _closing = true;
            };
            MinWidth = 600f;
            Position.Y = ScrollTargetY;
            Alpha = 0.0f;
        }

        private void Reload(int index = -1)
        {
            Clear();

            Add(new Header(Dialog.Clean("BUTTON_CONFIG")));

            Add(new SubHeader(Dialog.Clean("CONTROLLER")));

            if (Settings.ControllerQuickSave == null)
            {
                Add(new Setting(Dialog.Clean("SAVE"), Keys.None).Pressed(() =>
                    Remap(Mappings.Save)));
            }
            else
            {
                Add(new Setting(Dialog.Clean("SAVE"), (Buttons) Settings.ControllerQuickSave).Pressed(() =>
                    Remap(Mappings.Save)));
            }

            if (Settings.ControllerQuickLoad == null)
            {
                Add(new Setting(Dialog.Clean("LOAD"), Keys.None).Pressed(() =>
                    Remap(Mappings.Load)));
            }
            else
            {
                Add(new Setting(Dialog.Clean("LOAD"), (Buttons) Settings.ControllerQuickLoad).Pressed(() =>
                    Remap(Mappings.Load)));
            }

            if (Settings.ControllerQuickClear == null)
            {
                Add(new Setting(Dialog.Clean("CLEAR"), Keys.None).Pressed(() =>
                    Remap(Mappings.Clear)));
            }
            else
            {
                Add(new Setting(Dialog.Clean("CLEAR"), (Buttons) Settings.ControllerQuickClear).Pressed(() =>
                    Remap(Mappings.Clear)));
            }

            if (Settings.ControllerOpenDebugMap == null)
            {
                Add(new Setting(Dialog.Clean("OPEN_DEBUG_MAP"), Keys.None).Pressed(() =>
                    Remap(Mappings.OpenDebugMap)));
            }
            else
            {
                Add(new Setting(Dialog.Clean("OPEN_DEBUG_MAP"), (Buttons) Settings.ControllerOpenDebugMap).Pressed(
                    () =>
                        Remap(Mappings.OpenDebugMap)));
            }

            if (Settings.ControllerResetRoomPb == null)
            {
                Add(new Setting(Dialog.Clean("RESET_ROOM_PB"), Keys.None).Pressed(() =>
                    Remap(Mappings.ResetRoomPb)));
            }
            else
            {
                Add(new Setting(Dialog.Clean("RESET_ROOM_PB"), (Buttons) Settings.ControllerResetRoomPb).Pressed(
                    () =>
                        Remap(Mappings.ResetRoomPb)));
            }

            Add(new SubHeader(Dialog.Clean("KEYBOARD")));
            Add(new Setting(Dialog.Clean("SAVE"), Settings.KeyboardQuickSave).Pressed(() =>
                Remap(Mappings.Save, true)));
            Add(new Setting(Dialog.Clean("LOAD"), Settings.KeyboardQuickLoad).Pressed(() =>
                Remap(Mappings.Load, true)));
            Add(new Setting(Dialog.Clean("CLEAR"), Settings.KeyboardQuickClear).Pressed(() =>
                Remap(Mappings.Clear, true)));
            Add(new Setting(Dialog.Clean("OPEN_DEBUG_MAP"), Settings.KeyboardOpenDebugMap).Pressed(() =>
                Remap(Mappings.OpenDebugMap, true)));
            Add(new Setting(Dialog.Clean("RESET_ROOM_PB"), Settings.KeyboardResetRoomPb).Pressed(() =>
                Remap(Mappings.ResetRoomPb, true)));

            Add(new SubHeader(""));
            Button button = new Button(Dialog.Clean("KEY_CONFIG_RESET"))
            {
                IncludeWidthInMeasurement = false,
                AlwaysCenter = true,
                OnPressed = () =>
                {
                    SetDefaultButtons();
                    Reload(Selection);
                }
            };
            Add(button);

            if (index < 0)
                return;
            Selection = index;
        }

        private void SetDefaultButtons()
        {
            Settings.ControllerQuickSave = null;
            Settings.ControllerQuickLoad = null;
            Settings.ControllerQuickClear = null;
            Settings.ControllerOpenDebugMap = null;
            Settings.ControllerResetRoomPb = null;

            Settings.KeyboardQuickSave = Keys.F7;
            Settings.KeyboardQuickLoad = Keys.F8;
            Settings.KeyboardQuickClear = FixedClearKeys.ToList();
            Settings.KeyboardOpenDebugMap = FixedOpenDebugMapKeys.ToList();
            Settings.KeyboardResetRoomPb = Keys.F9;

            UpdateSaveButton();
            UpdateLoadButton();
            UpdateClearButton();
            UpdateOpenDebugMapButton();
            UpdateResetRoomPbButton();
        }


        private void Remap(Mappings mapping, bool remappingKeyboard = false)
        {
            _remapping = true;
            _remappingKeyboard = remappingKeyboard;
            _remappingButton = mapping;
            _timeout = 5f;
            Focused = false;
        }

        private void SetRemap(Keys key)
        {
            _remapping = false;
            _inputDelay = 0.25f;
            switch (_remappingButton)
            {
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
                default:
                    throw new ArgumentOutOfRangeException();
            }

            Reload(Selection);
        }

        private void SetRemap(Buttons button)
        {
            _remapping = false;
            _inputDelay = 0.25f;
            switch (_remappingButton)
            {
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
                default:
                    throw new ArgumentOutOfRangeException();
            }

            Reload(Selection);
        }

        public static void UpdateSaveButton()
        {
            List<VirtualButton.Node> nodes = SaveButton.Value.Nodes;
            nodes.Clear();
            nodes.Add(new VirtualButton.KeyboardKey(Settings.KeyboardQuickSave));

            if (Settings.ControllerQuickSave != null)
                nodes.Add(new VirtualButton.PadButton(Input.Gamepad, (Buttons) Settings.ControllerQuickSave));
        }

        public static void UpdateLoadButton()
        {
            List<VirtualButton.Node> nodes = LoadButton.Value.Nodes;
            nodes.Clear();
            nodes.Add(new VirtualButton.KeyboardKey(Settings.KeyboardQuickLoad));

            if (Settings.ControllerQuickLoad != null)
                nodes.Add(new VirtualButton.PadButton(Input.Gamepad, (Buttons) Settings.ControllerQuickLoad));
        }

        public static void UpdateClearButton()
        {
            List<VirtualButton.Node> nodes = ClearButton.Value.Nodes;
            nodes.Clear();
            nodes.AddRange(Settings.KeyboardQuickClear.Select(clearKey => new VirtualButton.KeyboardKey(clearKey)));

            if (Settings.ControllerQuickClear != null)
                nodes.Add(new VirtualButton.PadButton(Input.Gamepad, (Buttons) Settings.ControllerQuickClear));
        }

        public static void UpdateOpenDebugMapButton()
        {
            List<VirtualButton.Node> nodes = OpenDebugButton.Value.Nodes;
            nodes.Clear();
            nodes.AddRange(Settings.KeyboardOpenDebugMap.Select(clearKey => new VirtualButton.KeyboardKey(clearKey)));
            if (Settings.ControllerOpenDebugMap != null)
            {
                nodes.Add(new VirtualButton.PadButton(Input.Gamepad, (Buttons) Settings.ControllerOpenDebugMap));
            }
        }

        public static void UpdateResetRoomPbButton()
        {
            List<VirtualButton.Node> nodes = ResetRoomPbButton.Value.Nodes;
            nodes.Clear();
            nodes.Add(new VirtualButton.KeyboardKey(Settings.KeyboardResetRoomPb));
            if (Settings.ControllerResetRoomPb != null)
            {
                nodes.Add(new VirtualButton.PadButton(Input.Gamepad, (Buttons) Settings.ControllerResetRoomPb));
            }
        }

        public override void Update()
        {
            base.Update();
            if (_inputDelay > 0.0 && !_remapping)
            {
                _inputDelay -= Engine.DeltaTime;
                if (_inputDelay <= 0.0)
                    Focused = true;
            }

            _remappingEase = Calc.Approach(_remappingEase, _remapping ? 1f : 0.0f, Engine.DeltaTime * 4f);
            if (_remappingEase > 0.5 && _remapping)
            {
                if (Input.ESC.Pressed || Input.MenuCancel || _timeout <= 0.0)
                {
                    _remapping = false;
                    Focused = true;
                }
                else if (_remappingKeyboard)
                {
                    Keys[] pressedKeys = MInput.Keyboard.CurrentState.GetPressedKeys();
                    if (pressedKeys != null && pressedKeys.Length != 0 &&
                        MInput.Keyboard.Pressed(pressedKeys[pressedKeys.Length - 1]))
                        SetRemap(pressedKeys[pressedKeys.Length - 1]);
                }
                else
                {
                    GamePadState currentState = MInput.GamePads[Input.Gamepad].CurrentState;
                    GamePadState previousState = MInput.GamePads[Input.Gamepad].PreviousState;
                    foreach (Buttons buttons in AllButtons)
                    {
                        if (!currentState.IsButtonDown(buttons) || previousState.IsButtonDown(buttons)) continue;
                        SetRemap(buttons);
                        break;
                    }
                }

                _timeout -= Engine.DeltaTime;
            }

            Alpha = Calc.Approach(Alpha, _closing ? 0.0f : 1f, Engine.DeltaTime * 8f);
            if (!_closing || Alpha > 0.0)
                return;
            Close();
        }

        public override void Render()
        {
            Draw.Rect(-10f, -10f, 1940f, 1100f, Color.Black * Ease.CubeOut(Alpha));
            base.Render();
            if (_remappingEase <= 0.0)
                return;
            Draw.Rect(-10f, -10f, 1940f, 1100f, Color.Black * 0.95f * Ease.CubeInOut(_remappingEase));
            Vector2 position = new Vector2(1920f, 1080f) * 0.5f;

            if (_remappingKeyboard || Input.GuiInputController())
            {
                ActiveFont.Draw(
                    _remappingKeyboard ? Dialog.Get("KEY_CONFIG_CHANGING") : Dialog.Get("BTN_CONFIG_CHANGING"),
                    position + new Vector2(0.0f, -8f),
                    new Vector2(0.5f, 1f),
                    Vector2.One * 0.7f,
                    Color.LightGray * Ease.CubeIn(_remappingEase));
                ActiveFont.Draw(Label(_remappingButton),
                    position + new Vector2(0.0f, 8f), new Vector2(0.5f, 0.0f), Vector2.One * 2f,
                    Color.White * Ease.CubeIn(_remappingEase));
            }
            else
            {
                ActiveFont.Draw(Dialog.Clean("BTN_CONFIG_NOCONTROLLER"), position, new Vector2(0.5f, 0.5f), Vector2.One,
                    Color.White * Ease.CubeIn(_remappingEase));
            }
        }

        private string Label(Mappings mapping)
        {
            switch (mapping)
            {
                case Mappings.Save:
                    return Dialog.Clean("SAVE");
                case Mappings.Load:
                    return Dialog.Clean("LOAD");
                case Mappings.Clear:
                    return Dialog.Clean("CLEAR");
                case Mappings.OpenDebugMap:
                    return Dialog.Clean("OPEN_DEBUG_MAP");
                case Mappings.ResetRoomPb:
                    return Dialog.Clean("RESET_ROOM_PB");
                default:
                    return "Unknown";
            }
        }

        private enum Mappings
        {
            Save,
            Load,
            Clear,
            OpenDebugMap,
            ResetRoomPb
        }
    }
}