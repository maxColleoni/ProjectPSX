using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using ProjectPSX.Devices.Input;
using ProjectPSX.Interop.Hid;
using Hid = ProjectPSX.Interop.Hid.NativeMethods;

namespace ProjectPSX {
    // A DualSense or DualShock 4 read straight from its HID reports (USB or
    // Bluetooth), no Steam or DS4Windows needed. A thread of its own finds the
    // controller and blocks on its reports; Read() hands the emulation thread
    // the latest PlayStation buttons. The emulated pad is digital, so the left
    // stick pushed past half way presses the D-pad.
    internal class SonyPad {

        private const ushort SonyVendor = 0x054C;
        private static readonly ushort[] dualSense = { 0x0CE6, 0x0DF2 };      // DualSense, DualSense Edge
        private static readonly ushort[] dualShock4 = { 0x05C4, 0x09CC, 0x0BA0 }; // DS4 v1, v2, USB wireless adaptor
        private const int StickDeadZone = 64; // of 128 from the center
        private const int RescanMs = 2000;

        private volatile int buttons; // GamepadInputsEnum bits, set by the reader thread

        public SonyPad() {
            var thread = new Thread(Run) { IsBackground = true, Name = "SonyPad" };
            thread.Start();
        }

        public ushort Read() => (ushort)buttons;

        private void Run() {
            while (true) {
                try {
                    using SafeFileHandle device = Find(out bool isDualSense, out int reportLength);
                    if (device != null) {
                        Console.WriteLine($"[SonyPad] {(isDualSense ? "DualSense" : "DualShock 4")} is pad 1");
                        var report = new byte[reportLength];
                        while (Hid.ReadFile(device, report, report.Length, out int read, IntPtr.Zero) && read > 0)
                            buttons = Parse(report, read, isDualSense);
                        buttons = 0;
                        Console.WriteLine("[SonyPad] Controller disconnected");
                    }
                } catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException) {
                    return; // no HID on this system
                }
                Thread.Sleep(RescanMs);
            }
        }

        // The first Sony controller among the HID devices, opened for reading.
        private static SafeFileHandle Find(out bool isDualSense, out int reportLength) {
            isDualSense = false;
            reportLength = 0;
            Hid.HidD_GetHidGuid(out Guid hidGuid);
            IntPtr set = Hid.SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, Hid.DigcfPresent | Hid.DigcfDeviceInterface);
            if (set == IntPtr.Zero || set == new IntPtr(-1))
                return null;
            try {
                var data = new DeviceInterfaceData { cbSize = Marshal.SizeOf<DeviceInterfaceData>() };
                for (uint i = 0; Hid.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, i, ref data); i++) {
                    string path = DevicePath(set, ref data);
                    if (path == null)
                        continue;
                    // Read and write as other programs (Steam) usually have it open too.
                    SafeFileHandle device = Hid.CreateFile(path, Hid.GenericRead | Hid.GenericWrite, Hid.FileShareRead | Hid.FileShareWrite,
                                                           IntPtr.Zero, Hid.OpenExisting, 0, IntPtr.Zero);
                    if (device.IsInvalid)
                        device = Hid.CreateFile(path, Hid.GenericRead, Hid.FileShareRead | Hid.FileShareWrite, IntPtr.Zero,
                                                Hid.OpenExisting, 0, IntPtr.Zero);
                    if (device.IsInvalid) {
                        device.Dispose();
                        continue;
                    }
                    var attributes = new Attributes { Size = Marshal.SizeOf<Attributes>() };
                    bool sony = Hid.HidD_GetAttributes(device, ref attributes) && attributes.VendorID == SonyVendor;
                    bool ds = sony && Array.IndexOf(dualSense, attributes.ProductID) >= 0;
                    bool ds4 = sony && Array.IndexOf(dualShock4, attributes.ProductID) >= 0;
                    int length = (ds || ds4) ? InputReportLength(device) : 0;
                    if (length > 0) {
                        isDualSense = ds;
                        reportLength = length;
                        return device;
                    }
                    device.Dispose();
                }
            } finally {
                Hid.SetupDiDestroyDeviceInfoList(set);
            }
            return null;
        }

        private static string DevicePath(IntPtr set, ref DeviceInterfaceData data) {
            Hid.SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out uint size, IntPtr.Zero);
            if (size == 0)
                return null;
            IntPtr detail = Marshal.AllocHGlobal((int)size);
            try {
                // SP_DEVICE_INTERFACE_DETAIL_DATA_W: cbSize, then the path.
                Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                if (!Hid.SetupDiGetDeviceInterfaceDetail(set, ref data, detail, size, out _, IntPtr.Zero))
                    return null;
                return Marshal.PtrToStringUni(detail + 4);
            } finally {
                Marshal.FreeHGlobal(detail);
            }
        }

        private static int InputReportLength(SafeFileHandle device) {
            if (!Hid.HidD_GetPreparsedData(device, out IntPtr preparsed))
                return 0;
            try {
                return Hid.HidP_GetCaps(preparsed, out Caps caps) >= 0 ? caps.InputReportByteLength : 0;
            } finally {
                Hid.HidD_FreePreparsedData(preparsed);
            }
        }

        // Reports: the left stick's X at `at`, then Y, the right stick, and the
        // three button bytes (D-pad hat and faces; shoulders, triggers, Create /
        // Share, Options, stick clicks; PS and touchpad) at `buttonsAt`.
        //   DualSense USB 0x01 (64 bytes) and Bluetooth 0x31: sticks, triggers,
        //   a counter, then the buttons. DualShock 4 USB 0x01, Bluetooth 0x11,
        //   and the DualSense's short Bluetooth 0x01: the buttons right after
        //   the sticks.
        private static int Parse(byte[] r, int length, bool isDualSense) {
            int at, buttonsAt;
            if (isDualSense && r[0] == 0x01 && length >= 64) { at = 1; buttonsAt = 8; }
            else if (isDualSense && r[0] == 0x31 && length >= 12) { at = 2; buttonsAt = 9; }
            else if (r[0] == 0x01 && length >= 8) { at = 1; buttonsAt = 5; }
            else if (!isDualSense && r[0] == 0x11 && length >= 10) { at = 3; buttonsAt = 7; }
            else return 0;

            int b0 = r[buttonsAt], b1 = r[buttonsAt + 1], b2 = r[buttonsAt + 2];
            int psx = 0;
            switch (b0 & 0x0F) { // the D-pad: 0 = up, clockwise, 8 = released
                case 0: psx |= (int)GamepadInputsEnum.Up; break;
                case 1: psx |= (int)(GamepadInputsEnum.Up | GamepadInputsEnum.Right); break;
                case 2: psx |= (int)GamepadInputsEnum.Right; break;
                case 3: psx |= (int)(GamepadInputsEnum.Down | GamepadInputsEnum.Right); break;
                case 4: psx |= (int)GamepadInputsEnum.Down; break;
                case 5: psx |= (int)(GamepadInputsEnum.Down | GamepadInputsEnum.Left); break;
                case 6: psx |= (int)GamepadInputsEnum.Left; break;
                case 7: psx |= (int)(GamepadInputsEnum.Up | GamepadInputsEnum.Left); break;
            }
            if ((b0 & 0x10) != 0) psx |= (int)GamepadInputsEnum.A;     // Square
            if ((b0 & 0x20) != 0) psx |= (int)GamepadInputsEnum.S;     // Cross
            if ((b0 & 0x40) != 0) psx |= (int)GamepadInputsEnum.D;     // Circle
            if ((b0 & 0x80) != 0) psx |= (int)GamepadInputsEnum.W;     // Triangle
            if ((b1 & 0x01) != 0) psx |= (int)GamepadInputsEnum.Q;     // L1
            if ((b1 & 0x02) != 0) psx |= (int)GamepadInputsEnum.E;     // R1
            if ((b1 & 0x04) != 0) psx |= (int)GamepadInputsEnum.D1;    // L2
            if ((b1 & 0x08) != 0) psx |= (int)GamepadInputsEnum.D3;    // R2
            if ((b1 & 0x10) != 0) psx |= (int)GamepadInputsEnum.Space; // Create / Share: Select
            if ((b1 & 0x20) != 0) psx |= (int)GamepadInputsEnum.Enter; // Options: Start
            if ((b1 & 0x40) != 0) psx |= (int)GamepadInputsEnum.Z;     // L3
            if ((b1 & 0x80) != 0) psx |= (int)GamepadInputsEnum.C;     // R3
            if ((b2 & 0x02) != 0) psx |= (int)GamepadInputsEnum.Space; // the touchpad: Select too

            int x = r[at] - 128, y = r[at + 1] - 128; // 0, 0 is up and left
            if (x < -StickDeadZone) psx |= (int)GamepadInputsEnum.Left;
            if (x > StickDeadZone) psx |= (int)GamepadInputsEnum.Right;
            if (y < -StickDeadZone) psx |= (int)GamepadInputsEnum.Up;
            if (y > StickDeadZone) psx |= (int)GamepadInputsEnum.Down;
            return psx;
        }
    }
}
