using System;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32.SafeHandles;

namespace ProjectPSX.Interop.Hid
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct DeviceInterfaceData
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Attributes
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    // HIDP_CAPS: only the report lengths are used.
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct Caps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        public fixed ushort Reserved[17];
        public fixed ushort Counts[10];
    }

    [SuppressUnmanagedCodeSecurity]
    internal static class NativeMethods
    {
        public const uint DigcfPresent = 0x2;
        public const uint DigcfDeviceInterface = 0x10;
        public const uint GenericRead = 0x80000000;
        public const uint GenericWrite = 0x40000000;
        public const uint FileShareRead = 0x1;
        public const uint FileShareWrite = 0x2;
        public const uint OpenExisting = 3;

        [DllImport(ExternDll.Hid)]
        internal static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport(ExternDll.Hid, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool HidD_GetAttributes(SafeFileHandle device, ref Attributes attributes);

        [DllImport(ExternDll.Hid, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsedData);

        [DllImport(ExternDll.Hid)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport(ExternDll.Hid)]
        internal static extern int HidP_GetCaps(IntPtr preparsedData, out Caps capabilities);

        [DllImport(ExternDll.SetupApi, SetLastError = true)]
        internal static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

        [DllImport(ExternDll.SetupApi, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid,
                                                                uint memberIndex, ref DeviceInterfaceData deviceInterfaceData);

        [DllImport(ExternDll.SetupApi, SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref DeviceInterfaceData deviceInterfaceData,
                                                                    IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize,
                                                                    out uint requiredSize, IntPtr deviceInfoData);

        [DllImport(ExternDll.SetupApi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport(ExternDll.Kernel32, SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
                                                         uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport(ExternDll.Kernel32, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ReadFile(SafeFileHandle file, byte[] buffer, int numberOfBytesToRead, out int numberOfBytesRead,
                                             IntPtr overlapped);
    }
}
