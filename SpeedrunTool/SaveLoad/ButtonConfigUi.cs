using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad
{
    public class ButtonConfigUi : TextMenu
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

        private SpeedrunToolModuleSettings _settings => SpeedrunToolModule.Settings;

        private bool _closing;
        private float _inputDelay;
        private bool _remapping;
        private Mappings _remappingButton;
        private float _remappingEase;
        private bool _remappingKeyboard;
        private float _timeout;

        public ButtonConfigUi()
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
            Add(new Setting(Dialog.Clean("SAVE"), _settings.ControllerQuickSave).Pressed(() =>
                Remap(Mappings.Save)));
            Add(new Setting(Dialog.Clean("LOAD"), _settings.ControllerQuickLoad).Pressed(() =>
                Remap(Mappings.Load)));
            Add(new Setting(Dialog.Clean("CLEAR"), _settings.ControllerQuickClear).Pressed(() =>
                Remap(Mappings.Clear)));
            if (_settings.ControllerOpenDebugMap == null)
            {
                Add(new Setting(Dialog.Clean("OPEN_DEBUG_MAP"), Keys.None).Pressed(() =>
                    Remap(Mappings.OpenDebugMap)));
            }
            else
            {
                Add(new Setting(Dialog.Clean("OPEN_DEBUG_MAP"), (Buttons) _settings.ControllerOpenDebugMap).Pressed(
                    () =>
                        Remap(Mappings.OpenDebugMap)));
            }

            Add(new SubHeader(Dialog.Clean("KEYBOARD")));
            Add(new Setting(Dialog.Clean("SAVE"), _settings.KeyboardQuickSave).Pressed(() =>
                Remap(Mappings.Save, true)));
            Add(new Setting(Dialog.Clean("LOAD"), _settings.KeyboardQuickLoad).Pressed(() =>
                Remap(Mappings.Load, true)));
            Add(new Setting(Dialog.Clean("CLEAR"), _settings.KeyboardQuickClear).Pressed(() =>
                Remap(Mappings.Clear, true)));
            Add(new Setting(Dialog.Clean("OPEN_DEBUG_MAP"), _settings.KeyboardOpenDebugMap).Pressed(() =>
                Remap(Mappings.OpenDebugMap, true)));

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
            _settings.ControllerQuickSave = Buttons.LeftStick;
            _settings.ControllerQuickLoad = Buttons.RightStick;
            _settings.ControllerQuickClear = Buttons.Back;
            _settings.ControllerOpenDebugMap = null;
            _settings.KeyboardQuickSave = Keys.F7;
            _settings.KeyboardQuickLoad = Keys.F8;
            _settings.KeyboardQuickClear = FixedClearKeys.ToList();
            _settings.KeyboardOpenDebugMap = FixedOpenDebugMapKeys.ToList();
            UpdateSaveButton();
            UpdateLoadButton();
            UpdateClearButton();
            UpdateOpenDebugMapButton();
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
                    _settings.KeyboardQuickSave = key;
                    UpdateSaveButton();
                    break;
                case Mappings.Load:
                    _settings.KeyboardQuickLoad = key;
                    UpdateLoadButton();
                    break;
                case Mappings.Clear:
                    _settings.KeyboardQuickClear.Clear();
                    _settings.KeyboardQuickClear.AddRange(FixedClearKeys);
                    _settings.KeyboardQuickClear.Add(key);
                    UpdateClearButton();
                    break;
                case Mappings.OpenDebugMap:
                    _settings.KeyboardOpenDebugMap.Clear();
                    _settings.KeyboardOpenDebugMap.AddRange(FixedOpenDebugMapKeys);
                    _settings.KeyboardOpenDebugMap.Add(key);
                    UpdateOpenDebugMapButton();
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
                    _settings.ControllerQuickSave = button;
                    UpdateSaveButton();
                    break;
                case Mappings.Load:
                    _settings.ControllerQuickLoad = button;
                    UpdateLoadButton();
                    break;
                case Mappings.Clear:
                    _settings.ControllerQuickClear = button;
                    UpdateClearButton();
                    break;
                case Mappings.OpenDebugMap:
                    _settings.ControllerOpenDebugMap = button;
                    UpdateOpenDebugMapButton();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            Reload(Selection);
        }

        private void UpdateSaveButton()
        {
            List<VirtualButton.Node> nodes = SaveLoadManager.Instance.SaveButton.Nodes;
            nodes.Clear();
            nodes.Add(new VirtualButton.KeyboardKey(_settings.KeyboardQuickSave));
            nodes.Add(new VirtualButton.PadButton(Input.Gamepad, _settings.ControllerQuickSave));
        }

        private void UpdateLoadButton()
        {
            List<VirtualButton.Node> nodes = SaveLoadManager.Instance.LoadButton.Nodes;
            nodes.Clear();
            nodes.Add(new VirtualButton.KeyboardKey(_settings.KeyboardQuickLoad));
            nodes.Add(new VirtualButton.PadButton(Input.Gamepad, _settings.ControllerQuickLoad));
        }

        private void UpdateClearButton()
        {
            List<VirtualButton.Node> nodes = SaveLoadManager.Instance.ClearButton.Nodes;
            nodes.Clear();
            nodes.AddRange(_settings.KeyboardQuickClear.Select(clearKey => new VirtualButton.KeyboardKey(clearKey)));
            nodes.Add(new VirtualButton.PadButton(Input.Gamepad, _settings.ControllerQuickClear));
        }

        private void UpdateOpenDebugMapButton()
        {
            List<VirtualButton.Node> nodes = BetterMapEditor.Instance.OpenDebugButton.Nodes;
            nodes.Clear();
            nodes.AddRange(_settings.KeyboardOpenDebugMap.Select(clearKey => new VirtualButton.KeyboardKey(clearKey)));
            if (_settings.ControllerOpenDebugMap != null)
            {
                nodes.Add(new VirtualButton.PadButton(Input.Gamepad, (Buttons) _settings.ControllerOpenDebugMap));
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
                default:
                    return "Unknown";
            }
        }

        private enum Mappings
        {
            Save,
            Load,
            Clear,
            OpenDebugMap
        }
    }
}