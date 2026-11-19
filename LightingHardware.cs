using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Microsoft.Win32;

namespace RgbPanel
{
    enum DeviceKind { Memory, Motherboard, Cooler }

    sealed class LightingTarget
    {
        public readonly string Name;
        public readonly int LedCount;
        public readonly Action<Color[], bool> Write;
        public readonly DeviceKind Kind;
        public readonly string DisplayName;
        public readonly string Category;

        public LightingTarget(string name, int ledCount, Action<Color[], bool> write,
            DeviceKind kind = DeviceKind.Memory, string displayName = null, string category = null)
        {
            Name = name;
            LedCount = ledCount;
            Write = write;
            Kind = kind;
            DisplayName = displayName ?? name;
            Category = category ?? "Memory";
        }

        public override string ToString() { return Name; }
    }

    sealed class LightingHardware : IDisposable
    {
        public const string Board = "X570 I AORUS PRO WIFI";
        public const string Kraken = "NZXT Kraken X3";
        public readonly List<LightingTarget> Devices = new List<LightingTarget>();
        public readonly List<string> Problems = new List<string>();

        readonly List<IDisposable> handles = new List<IDisposable>();

        public static LightingHardware Open()
        {
            var hardware = new LightingHardware();
            try
            {
                if (IsThisBoard())
                {
                    hardware.AddMemory();
                    hardware.AddBoard();
                }
                else
                {
                    hardware.Problems.Add("Memory and board lighting require the " + Board + ".");
                }
                hardware.AddKraken();
                return hardware;
            }
            catch
            {
                hardware.Dispose();
                throw;
            }
        }

        static bool IsThisBoard()
        {
            using (var bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS"))
                return bios != null && String.Equals(bios.GetValue("BaseBoardProduct") as string, Board,
                    StringComparison.OrdinalIgnoreCase);
        }

        void AddMemory()
        {
            MemoryBus memory = null;
            try
            {
                memory = new MemoryBus();
                byte[] addresses = memory.Discover();
                if (addresses.Length != 2)
                    Problems.Add("Found " + addresses.Length + " of 2 G.Skill modules.");
                for (int i = 0; i < addresses.Length; i++)
                {
                    byte address = addresses[i];
                    Devices.Add(new LightingTarget("G.Skill Trident Z RGB module " + (i + 1), 5,
                        (colors, first) =>
                        {
                            memory.WriteFrame(address, colors, first);
                            if (!first)
                                return;
                            Color[] confirmed = memory.ReadFrame(address);
                            for (int led = 0; led < colors.Length; led++)
                                if (confirmed[led].ToArgb() != colors[led].ToArgb())
                                    throw new IOException("The memory controller did not confirm the color.");
                        }, DeviceKind.Memory, "G.Skill Trident Z RGB", "Memory module " + (i + 1)));
                }
                handles.Add(memory);
            }
            catch (Exception error)
            {
                if (memory != null)
                    memory.Dispose();
                Problems.Add("Memory: " + error.Message);
            }
        }

        void AddBoard()
        {
            try
            {
                var usb = WindowsHid.Open(0x048D, 0x8297, 0xFF89, 0xCC);
                handles.Add(usb);
                Devices.Add(new LightingTarget(Board, 5, (colors, first) =>
                {
                    if (first)
                        usb.SetFeature(BoardCommand(0x31)); // use software colors, not hardware beat sync
                    for (int i = 0; i < colors.Length; i++)
                        usb.SetFeature(BoardColor(i + 1, colors[i]));
                    var apply = BoardCommand(0x28);
                    apply[2] = 0x3E; // zones LED1 through LED5
                    usb.SetFeature(apply);
                }, DeviceKind.Motherboard, null, "Motherboard"));
            }
            catch (IOException error) { Problems.Add("Motherboard: " + error.Message); }
        }

        void AddKraken()
        {
            try
            {
                var usb = WindowsHid.Open(0x1E71, 0x2007, 0xFF00, 1);
                handles.Add(usb);
                Devices.Add(new LightingTarget(Kraken, 9, (colors, first) =>
                {
                    usb.Write(KrakenColors(1, colors, 0, 8));
                    usb.Write(KrakenApply(1));
                    usb.Write(KrakenColors(2, colors, 8, 1));
                    usb.Write(KrakenApply(2));
                }, DeviceKind.Cooler, null, "Liquid cooler"));
            }
            catch (IOException error) { Problems.Add("Kraken: " + error.Message); }
        }

        // RGB Fusion 2 USB packets adapted from OpenRGB's IT8297 controller.
        static byte[] BoardCommand(byte command)
        {
            var report = new byte[64];
            report[0] = 0xCC;
            report[1] = command;
            return report;
        }

        internal static byte[] BoardColor(int zone, Color color)
        {
            var report = BoardCommand((byte)(0x20 + zone));
            report[2] = (byte)(1 << zone);
            report[11] = 1; // static, with zero transition time for software animation
            report[12] = 255;
            report[14] = color.B;
            report[15] = color.G;
            report[16] = color.R;
            return report;
        }

        // Kraken X3 uses the NZXT Hue 2 reports in OpenRGB, with a GRB payload.
        internal static byte[] KrakenColors(int channel, Color[] colors, int start, int count)
        {
            var report = new byte[64];
            report[0] = 0x22;
            report[1] = 0x10;
            report[2] = (byte)(1 << channel);
            for (int i = 0; i < count; i++)
            {
                Color color = colors[start + i];
                int pos = 4 + i * 3;
                report[pos] = color.G;
                report[pos + 1] = color.R;
                report[pos + 2] = color.B;
            }
            return report;
        }

        internal static byte[] KrakenApply(int channel)
        {
            var report = new byte[64];
            report[0] = 0x22;
            report[1] = 0xA0;
            report[2] = (byte)(1 << channel);
            report[4] = 0x01;
            report[7] = 0x28;
            report[10] = 0x80;
            report[12] = 0x32;
            report[15] = 0x01;
            return report;
        }

        public void Dispose()
        {
            foreach (IDisposable handle in handles)
                handle.Dispose();
            handles.Clear();
        }
    }
}
