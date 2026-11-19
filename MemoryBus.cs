using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace RgbPanel
{
    // SMBus and ENE register sequences adapted from OpenRGB 1.1 (GPL-2.0-or-later).
    // The PawnIO device IOCTL format comes from PawnIO's published user-mode ABI.
    sealed class MemoryBus : IDisposable
    {
        const uint LoadModule = (41394U << 16) | (0x821U << 2);
        const uint ExecuteFunction = (41394U << 16) | (0x841U << 2);
        const ulong BoardBusIdentity = 0x50011458790B1022UL;
        const ushort ColorBase = 0x8000;
        const ushort Direct = 0x8020;
        const ushort Apply = 0x80A0;
        const byte DefaultAddress = 0x77;
        const int BusByte = 1;
        const int BusByteData = 2;
        const int BusWordData = 3;
        const int BusBlockData = 5;
        const int LedCount = 5;
        static readonly byte[] KnownAddresses = { 0x74, 0x75, 0x70, 0x71, 0x72, 0x73, 0x76 };
        static readonly byte[] TransferName = Encoding.ASCII.GetBytes("ioctl_smbus_xfer");

        readonly SafeFileHandle handle;
        readonly Mutex busMutex;
        readonly byte[] transferRequest = new byte[104];
        readonly ulong[] transferResult = new ulong[5];

        public MemoryBus()
        {
            handle = OpenPawnIo();
            try
            {
                busMutex = new Mutex(false, @"Global\Access_SMBUS.HTP.Method");
                byte[] module;
                using (Stream bytes = Assembly.GetExecutingAssembly().GetManifestResourceStream("RgbPanel.SmbusPIIX4.bin"))
                {
                    if (bytes == null)
                        throw new IOException("The embedded PawnIO SMBus module is missing.");
                    using (var buffer = new MemoryStream())
                    {
                        bytes.CopyTo(buffer);
                        module = buffer.ToArray();
                    }
                }

                int written;
                if (!DeviceIoControl(handle, LoadModule, module, module.Length, null, 0, out written, IntPtr.Zero))
                    throw LastError("PawnIO could not load the AMD SMBus module");

                WithBus(() =>
                {
                    Execute("ioctl_piix4_port_sel", new ulong[] { 0 }, 1);
                    ulong[] identity = Execute("ioctl_identity", new ulong[] { 0 }, 3);
                    if (identity[2] != BoardBusIdentity)
                        throw new IOException("The SMBus controller does not match this motherboard.");
                });
            }
            catch
            {
                Dispose();
                throw;
            }
            Buffer.BlockCopy(TransferName, 0, transferRequest, 0, TransferName.Length);
        }

        public byte[] Discover()
        {
            var active = new List<byte>();
            WithBus(() =>
            {
                active.AddRange(KnownAddresses.Where(IsExpectedDimm));

                // The ENE chips can share 0x77 after a full power loss. Give each
                // validated DIMM its own volatile RGB address before use.
                for (byte slot = 0; slot < 8 && active.Count < 2 && IsExpectedDimm(DefaultAddress); slot++)
                {
                    byte next = KnownAddresses.FirstOrDefault(address => !TryTransfer(address, true, 0, BusByte, 0));
                    if (next == 0)
                        break;

                    WriteRegister(DefaultAddress, 0x80F8, slot);
                    WriteRegister(DefaultAddress, 0x80F9, (byte)(next << 1));
                    if (IsExpectedDimm(next))
                        active.Add(next);
                }
            });
            return active.Take(2).ToArray();
        }

        bool IsExpectedDimm(byte address)
        {
            for (int i = 0; i < 16; i++)
            {
                if (!TryTransfer(address, true, (byte)(0xA0 + i), BusByteData, 0) || (byte)transferResult[0] != i)
                    return false;
            }

            try
            {
                byte[] name = new byte[13];
                for (int i = 0; i < name.Length; i++)
                    name[i] = ReadRegister(address, (ushort)(0x1000 + i));
                return Encoding.ASCII.GetString(name) == "DIMM_LED-0103" && ReadRegister(address, 0x1C02) == LedCount;
            }
            catch (IOException) { return false; }
        }

        public void WriteFrame(byte address, System.Drawing.Color[] colors, bool enterDirect)
        {
            if (colors.Length != LedCount || !KnownAddresses.Contains(address))
                throw new ArgumentException("Invalid G.Skill address or frame size.");

            WithBus(() =>
            {
                for (int i = 0; i < colors.Length; i++)
                {
                    var color = colors[i];
                    PointAt(address, (ushort)(ColorBase + 3 * i));
                    uint data = 3U | ((uint)color.R << 8) | ((uint)color.B << 16) | ((uint)color.G << 24);
                    if (!TryTransfer(address, false, 0x03, BusBlockData, data))
                    {
                        PointAt(address, (ushort)(ColorBase + 3 * i));
                        RequireTransfer(address, false, 0x01, BusByteData, color.R);
                        RequireTransfer(address, false, 0x01, BusByteData, color.B);
                        RequireTransfer(address, false, 0x01, BusByteData, color.G);
                    }
                }
                if (enterDirect)
                {
                    WriteRegister(address, Direct, 1);
                    WriteRegister(address, Apply, 1);
                }
            });
        }

        public System.Drawing.Color[] ReadFrame(byte address)
        {
            var colors = new System.Drawing.Color[LedCount];
            WithBus(() =>
            {
                for (int i = 0; i < colors.Length; i++)
                {
                    ushort start = (ushort)(ColorBase + 3 * i);
                    byte red = ReadRegister(address, start);
                    byte blue = ReadRegister(address, (ushort)(start + 1));
                    byte green = ReadRegister(address, (ushort)(start + 2));
                    colors[i] = System.Drawing.Color.FromArgb(red, green, blue);
                }
            });
            return colors;
        }

        byte ReadRegister(byte address, ushort register)
        {
            PointAt(address, register);
            RequireTransfer(address, true, 0x81, BusByteData, 0);
            return (byte)transferResult[0];
        }

        void WriteRegister(byte address, ushort register, byte value)
        {
            PointAt(address, register);
            RequireTransfer(address, false, 0x01, BusByteData, value);
        }

        void PointAt(byte address, ushort register)
        {
            ushort swapped = (ushort)((register << 8) | (register >> 8));
            RequireTransfer(address, false, 0x00, BusWordData, swapped);
        }

        void RequireTransfer(byte address, bool read, byte command, int type, uint data)
        {
            if (!TryTransfer(address, read, command, type, data))
                throw LastError(String.Format("SMBus transfer failed for lighting controller 0x{0:X2}", address));
        }

        bool TryTransfer(byte address, bool read, byte command, int type, uint data)
        {
            // Function name is a NUL-padded 32-byte field, followed by nine 64-bit arguments.
            transferRequest[32] = address;
            transferRequest[40] = read ? (byte)1 : (byte)0;
            transferRequest[48] = command;
            transferRequest[56] = (byte)type;
            transferRequest[64] = (byte)data;
            transferRequest[65] = (byte)(data >> 8);
            transferRequest[66] = (byte)(data >> 16);
            transferRequest[67] = (byte)(data >> 24);
            int written;
            bool ok = DeviceIoControl(handle, ExecuteFunction, transferRequest, transferRequest.Length,
                transferResult, transferResult.Length * sizeof(ulong), out written, IntPtr.Zero);
            return ok && (!read || written >= sizeof(ulong));
        }

        ulong[] Execute(string function, ulong[] args, int outputCount)
        {
            var input = new byte[32 + args.Length * sizeof(ulong)];
            Encoding.ASCII.GetBytes(function, 0, function.Length, input, 0);
            Buffer.BlockCopy(args, 0, input, 32, args.Length * sizeof(ulong));
            var output = new ulong[outputCount];
            int written;
            if (!DeviceIoControl(handle, ExecuteFunction, input, input.Length,
                output, output.Length * sizeof(ulong), out written, IntPtr.Zero))
                throw LastError("PawnIO could not initialize AMD SMBus");
            if (function == "ioctl_identity" && written != output.Length * sizeof(ulong))
                throw new IOException("PawnIO did not return the AMD SMBus identifier.");
            return output;
        }

        void WithBus(Action action)
        {
            bool locked = false;
            try
            {
                try { locked = busMutex.WaitOne(2000); }
                catch (AbandonedMutexException) { locked = true; }
                if (!locked)
                    throw new IOException("Another application is using SMBus.");
                action();
            }
            finally { if (locked) busMutex.ReleaseMutex(); }
        }

        static SafeFileHandle OpenPawnIo()
        {
            const string deviceName = @"\Device\PawnIO";
            IntPtr characters = Marshal.StringToHGlobalUni(deviceName);
            IntPtr nameBuffer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(UnicodeString)));
            try
            {
                Marshal.StructureToPtr(new UnicodeString
                {
                    Length = (ushort)(deviceName.Length * 2),
                    Capacity = (ushort)((deviceName.Length + 1) * 2),
                    Buffer = characters
                }, nameBuffer, false);
                var attributes = new ObjectAttributes
                {
                    Length = Marshal.SizeOf(typeof(ObjectAttributes)), Name = nameBuffer, Attributes = 0x40
                };
                SafeFileHandle device;
                IoStatus io;
                int status = NtOpenFile(out device, 0xC0100000, ref attributes, out io, 7, 0x20);
                if (status >= 0 && !device.IsInvalid)
                    return device;
                if (device != null)
                    device.Dispose();
                throw new IOException(String.Format("PawnIO is unavailable. Check administrator access (0x{0:X8}).", status));
            }
            finally
            {
                Marshal.FreeHGlobal(nameBuffer);
                Marshal.FreeHGlobal(characters);
            }
        }

        static IOException LastError(string message)
        {
            int code = Marshal.GetLastWin32Error();
            return new IOException(message + ". Windows: " + code + ".", new Win32Exception(code));
        }

        public void Dispose()
        {
            if (busMutex != null)
                busMutex.Dispose();
            handle.Dispose();
        }

        [StructLayout(LayoutKind.Sequential)]
        struct UnicodeString { public ushort Length; public ushort Capacity; public IntPtr Buffer; }

        [StructLayout(LayoutKind.Sequential)]
        struct ObjectAttributes
        {
            public int Length;
            public IntPtr Root;
            public IntPtr Name;
            public uint Attributes;
            public IntPtr SecurityDescriptor;
            public IntPtr SecurityQualityOfService;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct IoStatus { public IntPtr Status; public UIntPtr Information; }

        [DllImport("ntdll.dll")]
        static extern int NtOpenFile(out SafeFileHandle file, uint access, ref ObjectAttributes attributes,
            out IoStatus io, uint share, uint options);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool DeviceIoControl(SafeFileHandle file, uint control, byte[] input, int inputLength,
            [Out] ulong[] output, int outputLength, out int returnedLength, IntPtr overlapped);
    }
}
