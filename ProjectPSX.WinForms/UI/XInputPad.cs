using System;
using ProjectPSX.Devices.Input;
using ProjectPSX.Interop.XInput;
using XInput = ProjectPSX.Interop.XInput.NativeMethods;

namespace ProjectPSX {
    // An Xbox-style controller (XInput). The face buttons go by position
    // (A = Cross, B = Circle, X = Square, Y = Triangle), the triggers are
    // L2 / R2, the stick clicks L3 / R3, Back is Select. The emulated pad is
    // digital, so the left stick pushed past half way presses the D-pad.
    internal class XInputPad {

        private const short StickDeadZone = 16384;   // half way
        private const byte TriggerThreshold = 30;     // XINPUT_GAMEPAD_TRIGGER_THRESHOLD
        private const int RescanFrames = 120;         // asking an empty slot is slow: every 2 s

        private static readonly (ushort xinput, GamepadInputsEnum psx)[] buttonMap = {
            (Buttons.DPadUp, GamepadInputsEnum.Up),
            (Buttons.DPadDown, GamepadInputsEnum.Down),
            (Buttons.DPadLeft, GamepadInputsEnum.Left),
            (Buttons.DPadRight, GamepadInputsEnum.Right),
            (Buttons.Start, GamepadInputsEnum.Enter),
            (Buttons.Back, GamepadInputsEnum.Space),
            (Buttons.LeftThumb, GamepadInputsEnum.Z),
            (Buttons.RightThumb, GamepadInputsEnum.C),
            (Buttons.LeftShoulder, GamepadInputsEnum.Q),
            (Buttons.RightShoulder, GamepadInputsEnum.E),
            (Buttons.A, GamepadInputsEnum.S),
            (Buttons.B, GamepadInputsEnum.D),
            (Buttons.X, GamepadInputsEnum.A),
            (Buttons.Y, GamepadInputsEnum.W),
        };

        private int slot = -1;        // the XInput user index in use, -1 = none found
        private int rescan;
        private bool unavailable;     // no XInput on this system

        // Once a frame: the PlayStation buttons the controller holds.
        public ushort Read() {
            if (unavailable)
                return 0;
            try {
                if (slot < 0) {
                    if (rescan-- > 0)
                        return 0;
                    rescan = RescanFrames;
                    for (uint i = 0; i < 4 && slot < 0; i++)
                        if (XInput.XInputGetState(i, out _) == XInput.ErrorSuccess)
                            slot = (int)i;
                    if (slot < 0)
                        return 0;
                    Console.WriteLine($"[XInput] Controller {slot + 1} is pad 1");
                }
                if (XInput.XInputGetState((uint)slot, out State state) != XInput.ErrorSuccess) {
                    Console.WriteLine("[XInput] Controller disconnected");
                    slot = -1;
                    return 0;
                }
                return ToPsx(state.Gamepad);
            } catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException) {
                unavailable = true;
                return 0;
            }
        }

        private static ushort ToPsx(in Gamepad pad) {
            ushort buttons = 0;
            foreach (var (xinput, psx) in buttonMap)
                if ((pad.wButtons & xinput) != 0)
                    buttons |= (ushort)psx;
            if (pad.bLeftTrigger > TriggerThreshold) buttons |= (ushort)GamepadInputsEnum.D1;
            if (pad.bRightTrigger > TriggerThreshold) buttons |= (ushort)GamepadInputsEnum.D3;
            if (pad.sThumbLX < -StickDeadZone) buttons |= (ushort)GamepadInputsEnum.Left;
            if (pad.sThumbLX > StickDeadZone) buttons |= (ushort)GamepadInputsEnum.Right;
            if (pad.sThumbLY > StickDeadZone) buttons |= (ushort)GamepadInputsEnum.Up;
            if (pad.sThumbLY < -StickDeadZone) buttons |= (ushort)GamepadInputsEnum.Down;
            return buttons;
        }
    }
}
