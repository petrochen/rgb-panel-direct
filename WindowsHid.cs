using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RgbPanel
{
    sealed class WindowsHid : IDisposable
    {
        const uint InterfacePresent = 0x12;
        const uint OpenExisting = 3;
        readonly SafeFileHandle handle;
        readonly int outputLength;
        readonly int featureLength;

        WindowsHid(SafeFileHandle handle, IntPtr caps)
        {
            this.handle = handle;
            outputLength = (ushort)Marshal.ReadInt16(caps, 6);
            featureLength = (ushort)Marshal.ReadInt16(caps, 8);
        }

        public static WindowsHid Open(ushort vendor, ushort product, ushort usagePage, ushort usage)
        {
            Guid hidGuid;
            HidD_GetHidGuid(out hidGuid);
            using (var devices = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, InterfacePresent))
            {
                if (devices.IsInvalid)
                    throw LastError("Windows could not enumerate HID devices");

                string match = String.Format("vid_{0:x4}&pid_{1:x4}", vendor, product);
                for (uint index = 0; ; index++)
                {
                    var entry = new DeviceInterface { Size = Marshal.SizeOf(typeof(DeviceInterface)) };
                    if (!SetupDiEnumDeviceInterfaces(devices, IntPtr.Zero, ref hidGuid, index, ref entry))
                    {
                        if (Marshal.GetLastWin32Error() == 259)
                            break;
                        throw LastError("HID enumeration failed");
                    }

                    string path = DevicePath(devices, ref entry);
                    if (path.IndexOf(match, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    WindowsHid hid = TryOpen(path, usagePage, usage);
                    if (hid != null)
                        return hid;
                }
            }
            throw new IOException(String.Format("HID device {0:X4}:{1:X4} is unavailable.", vendor, product));
        }

        static string DevicePath(DeviceSet devices, ref DeviceInterface entry)
        {
            int bytes;
            SetupDiGetDeviceInterfaceDetail(devices, ref entry, IntPtr.Zero, 0, out bytes, IntPtr.Zero);
            if (bytes < 6 || bytes > 65536)
                throw LastError("Could not get the HID device path");
            IntPtr memory = Marshal.AllocHGlobal(bytes);
            try
            {
                Marshal.WriteInt32(memory, IntPtr.Size == 8 ? 8 : 6);
                if (!SetupDiGetDeviceInterfaceDetail(devices, ref entry, memory, bytes, out bytes, IntPtr.Zero))
                    throw LastError("Could not read the HID device path");
                return Marshal.PtrToStringUni(IntPtr.Add(memory, 4));
            }
            finally { Marshal.FreeHGlobal(memory); }
        }

        static WindowsHid TryOpen(string path, ushort usagePage, ushort usage)
        {
            SafeFileHandle file = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (file.IsInvalid)
            {
                file.Dispose();
                return null;
            }

            bool selected = false;
            try
            {
                IntPtr data;
                if (!HidD_GetPreparsedData(file, out data))
                    return null;
                IntPtr caps = Marshal.AllocHGlobal(64); // HIDP_CAPS
                try
                {
                    if (HidP_GetCaps(data, caps) != 0x00110000 ||
                        (ushort)Marshal.ReadInt16(caps, 0) != usage ||
                        (ushort)Marshal.ReadInt16(caps, 2) != usagePage)
                        return null;

                    var device = new WindowsHid(file, caps);
                    selected = true;
                    return device;
                }
                finally
                {
                    Marshal.FreeHGlobal(caps);
                    HidD_FreePreparsedData(data);
                }
            }
            finally { if (!selected) file.Dispose(); }
        }

        public void Write(byte[] report)
        {
            report = PadReport(report, outputLength);
            int count;
            if (!WriteFile(handle, report, report.Length, out count, IntPtr.Zero) || count != report.Length)
                throw LastError("Could not send the color over USB");
        }

        public void SetFeature(byte[] report)
        {
            report = PadReport(report, featureLength);
            if (!HidD_SetFeature(handle, report, report.Length))
                throw LastError("The motherboard did not accept the lighting report");
        }

        public byte[] ReadFeature(byte reportId, int minimumLength)
        {
            var report = PadReport(new byte[minimumLength], featureLength);
            report[0] = reportId;
            if (!HidD_GetFeature(handle, report, report.Length))
                throw LastError("The motherboard did not return a lighting report");
            return report;
        }

        static byte[] PadReport(byte[] report, int length)
        {
            if (length < report.Length)
                throw new IOException("Unexpected USB report size.");
            if (length != report.Length)
                Array.Resize(ref report, length);
            return report;
        }

        static IOException LastError(string message)
        {
            var reason = new Win32Exception(Marshal.GetLastWin32Error());
            return new IOException(message + ". Windows: " + reason.NativeErrorCode + ".", reason);
        }

        public void Dispose() { handle.Dispose(); }

        [StructLayout(LayoutKind.Sequential)]
        struct DeviceInterface
        {
            public int Size;
            public Guid Guid;
            public int Flags;
            public UIntPtr Reserved;
        }

        sealed class DeviceSet : SafeHandleMinusOneIsInvalid
        {
            public DeviceSet() : base(true) { }
            protected override bool ReleaseHandle() { return SetupDiDestroyDeviceInfoList(handle); }
        }

        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)]
        static extern bool HidD_GetPreparsedData(SafeFileHandle file, out IntPtr data);
        [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)]
        static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data, IntPtr caps);
        [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)]
        static extern bool HidD_SetFeature(SafeFileHandle file, byte[] data, int size);
        [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)]
        static extern bool HidD_GetFeature(SafeFileHandle file, [In, Out] byte[] data, int size);

        [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", SetLastError = true)]
        static extern DeviceSet SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiEnumDeviceInterfaces(DeviceSet devices, IntPtr device, ref Guid guid, uint index, ref DeviceInterface entry);
        [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)]
        static extern bool SetupDiGetDeviceInterfaceDetail(DeviceSet devices, ref DeviceInterface entry, IntPtr details, int size, out int required, IntPtr info);
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiDestroyDeviceInfoList(IntPtr devices);

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint action, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteFile(SafeFileHandle file, byte[] data, int length, out int bytesWritten, IntPtr overlapped);
    }
}
