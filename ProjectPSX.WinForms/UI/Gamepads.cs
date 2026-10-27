using ProjectPSX.Devices.Input;

namespace ProjectPSX {
    // Controllers on the PC as the PlayStation's pad 1, alongside the
    // keyboard: a DualSense or DualShock 4 (SonyPad, read directly) and an
    // Xbox-style one (XInputPad). Whatever any of them holds is held.
    internal class Gamepads {

        private readonly ProjectPSX psx;
        private readonly XInputPad xinput = new XInputPad();
        private readonly SonyPad sony = new SonyPad();
        private ushort held; // PlayStation buttons the controllers hold down

        public Gamepads(ProjectPSX psx) {
            this.psx = psx;
        }

        // Once a frame, from the emulation thread. Presses and releases only
        // what changed, so the keyboard keeps working.
        public void Poll() {
            ushort buttons = (ushort)(xinput.Read() | sony.Read());
            ushort changed = (ushort)(buttons ^ held);
            for (int bit = 0; bit < 16; bit++) {
                ushort mask = (ushort)(1 << bit);
                if ((changed & mask) == 0)
                    continue;
                if ((buttons & mask) != 0)
                    psx.JoyPadDown((GamepadInputsEnum)mask);
                else
                    psx.JoyPadUp((GamepadInputsEnum)mask);
            }
            held = buttons;
        }
    }
}
