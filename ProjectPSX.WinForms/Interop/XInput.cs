using System.Runtime.InteropServices;
using System.Security;

namespace ProjectPSX.Interop.XInput
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Gamepad
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct State
    {
        public uint dwPacketNumber;
        public Gamepad Gamepad;
    }

    internal static class Buttons
    {
        public const ushort DPadUp = 0x0001;
        public const ushort DPadDown = 0x0002;
        public const ushort DPadLeft = 0x0004;
        public const ushort DPadRight = 0x0008;
        public const ushort Start = 0x0010;
        public const ushort Back = 0x0020;
        public const ushort LeftThumb = 0x0040;
        public const ushort RightThumb = 0x0080;
        public const ushort LeftShoulder = 0x0100;
        public const ushort RightShoulder = 0x0200;
        public const ushort A = 0x1000;
        public const ushort B = 0x2000;
        public const ushort X = 0x4000;
        public const ushort Y = 0x8000;
    }

    [SuppressUnmanagedCodeSecurity]
    internal static class NativeMethods
    {
        public const uint ErrorSuccess = 0;

        [DllImport(ExternDll.XInput)]
        internal static extern uint XInputGetState(uint dwUserIndex, out State pState);
    }
}
