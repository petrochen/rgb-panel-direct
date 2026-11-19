using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RgbPanel
{
    static class DirectProgram
    {
        [STAThread]
        static void Main()
        {
            bool first;
            using (var instance = new Mutex(true, @"Local\RgbPanel.X570I.Direct", out first))
            {
                if (!first)
                {
                    MessageBox.Show("RGB is already open.", "RGB");
                    return;
                }
                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    using (Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                    using (var panel = new DirectPanel(LightingHardware.Open))
                    {
                        if (icon != null)
                        {
                            panel.Icon = icon;
                            panel.ShowIcon = true;
                        }
                        Application.Run(panel);
                    }
                }
                finally { instance.ReleaseMutex(); }
            }
        }
    }

    sealed class DirectPanel : Form
    {
        readonly Func<LightingHardware> openHardware;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Dictionary<LightingTarget, ActiveEffect> active = new Dictionary<LightingTarget, ActiveEffect>();
        readonly Panel devices = new Panel { BackColor = Palette.Surface };
        readonly StatusBadge connection = new StatusBadge();
        readonly Label selectionCount;
        readonly Label feedback = new Label();
        readonly Label brightnessValue = new Label();
        readonly Label speedValue = new Label();
        readonly StudioSlider brightness = new StudioSlider(0, 100, 72);
        readonly StudioSlider speed = new StudioSlider(8, 120, 40);
        readonly DirectionToggle reverse = new DirectionToggle();
        readonly EffectPreview preview = new EffectPreview();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 45 };
        readonly StudioButton mode, firstColor, secondColor, refresh, apply, turnOff;
        readonly ContextMenuStrip effectMenu = new ContextMenuStrip();
        readonly ToolTip statusTip = new ToolTip();
        EffectKind selectedEffect = EffectKind.GradientWave;

        LightingHardware hardware;
        LightingEffect previewEffect;
        Color first = Color.FromArgb(134, 103, 255);
        Color second = Color.FromArgb(43, 226, 204);
        Task<FrameFailure[]> frameWrite;
        bool busy;
        bool closing;
        float layoutScale = 1;

        public DirectPanel(Func<LightingHardware> openHardware)
        {
            this.openHardware = openHardware;
            Text = "RGB";
            BackColor = Palette.Background;
            ForeColor = Palette.Ink;
            Font = Palette.Body;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(904, 602);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterScreen;

            var mark = new PanelMark();
            mark.SetBounds(24, 22, 40, 40);
            Controls.Add(mark);
            LabelAt(this, "Lighting", 78, 18, 430, 45, Palette.Ink, new Font("Segoe UI", 23F, FontStyle.Bold));
            connection.SetBounds(689, 24, 191, 36);
            Controls.Add(connection);

            var deviceCard = Card(24, 84, 314, 443);
            LabelAt(deviceCard, "Devices", 18, 15, 173, 30, Palette.Ink, Palette.Subhead);
            selectionCount = LabelAt(deviceCard, "0 selected", 18, 45, 263, 22, Palette.Muted, Palette.Meta);
            refresh = ButtonAt(deviceCard, "Refresh", 195, 17, 100, 31, ButtonAppearance.Secondary);
            refresh.AccessibleName = "Refresh devices";
            refresh.Click += async (sender, args) => await RefreshDevices();
            devices.SetBounds(17, 80, 280, 343);
            devices.AutoScroll = true;
            devices.AccessibleName = "Lighting devices";
            deviceCard.Controls.Add(devices);

            var effectCard = Card(352, 84, 528, 443);
            LabelAt(effectCard, "Lighting effect", 20, 15, 480, 30, Palette.Ink, Palette.Subhead);
            mode = ButtonAt(effectCard, "", 20, 53, 488, 42, ButtonAppearance.Selector);
            ConfigureEffectMenu();
            mode.Click += (sender, args) => effectMenu.Show(mode, new Point(0, mode.Height + 4));

            preview.SetBounds(20, 109, 488, 125);
            preview.AccessibleName = "Live preview of the selected effect";
            effectCard.Controls.Add(preview);

            firstColor = ButtonAt(effectCard, "", 20, 248, 228, 38, ButtonAppearance.Color);
            secondColor = ButtonAt(effectCard, "", 280, 248, 228, 38, ButtonAppearance.Color);
            firstColor.Click += (sender, args) => PickColor(true);
            secondColor.Click += (sender, args) => PickColor(false);
            LabelAt(effectCard, "Brightness", 20, 305, 110, 30, Palette.Ink, Palette.Body);
            ConfigureSlider(effectCard, brightness, brightnessValue, 305, "Brightness");
            LabelAt(effectCard, "Cycle length", 20, 343, 110, 30, Palette.Ink, Palette.Body);
            ConfigureSlider(effectCard, speed, speedValue, 343, "Cycle length in tenths of a second");
            reverse.Text = "Reverse direction";
            reverse.SetBounds(20, 390, 245, 31);
            effectCard.Controls.Add(reverse);

            brightness.ValueChanged += (sender, args) => UpdatePreview();
            speed.ValueChanged += (sender, args) => UpdatePreview();
            reverse.CheckedChanged += (sender, args) => UpdatePreview();

            turnOff = ButtonAt(this, "Turn off selected", 558, 544, 155, 40, ButtonAppearance.Secondary);
            apply = ButtonAt(this, "Apply effect", 729, 544, 151, 40, ButtonAppearance.Primary);
            turnOff.Click += async (sender, args) => await ApplyEffect(new LightingEffect(EffectKind.Off, Color.Black, Color.Black, 0, 1, false));
            apply.Click += async (sender, args) => await ApplyEffect(previewEffect);

            feedback.SetBounds(24, 538, 510, 53);
            feedback.TextAlign = ContentAlignment.MiddleLeft;
            feedback.ForeColor = Palette.Muted;
            Controls.Add(feedback);
            UpdatePreview();
            UpdateButtons();

            timer.Tick += async (sender, args) => await DrawFrame();
            Shown += async (sender, args) =>
            {
                timer.Start();
                await RefreshDevices();
            };
        }

        protected override void OnHandleCreated(EventArgs args)
        {
            base.OnHandleCreated(args);
            Palette.Caption(Handle);
        }

        protected override void OnLoad(EventArgs args)
        {
            layoutScale = Palette.ScaleWindow(this);
            base.OnLoad(args);
        }

        void ConfigureEffectMenu()
        {
            effectMenu.BackColor = Palette.Surface;
            effectMenu.ForeColor = Palette.Ink;
            effectMenu.Renderer = new ToolStripProfessionalRenderer(new StudioMenuColors());
            effectMenu.ShowImageMargin = false;
            effectMenu.ShowCheckMargin = true;
            for (int i = 0; i < LightingEffect.Names.Length; i++)
            {
                var kind = (EffectKind)i;
                var item = new ToolStripMenuItem(LightingEffect.Names[i]) { Font = Palette.Body, ForeColor = Palette.Ink };
                item.Click += (sender, args) => { selectedEffect = kind; UpdatePreview(); };
                effectMenu.Items.Add(item);
            }
        }

        IEnumerable<DeviceTile> Tiles { get { return devices.Controls.OfType<DeviceTile>(); } }

        async Task RefreshDevices()
        {
            if (busy)
                return;

            SetBusy(true, "Looking for lighting controllers…");
            connection.SetStatus("Detecting devices", false);
            try
            {
                await WaitForFrame();
                var selection = new HashSet<string>(Tiles.Where(tile => tile.Checked).Select(tile => tile.Target.Name));
                bool firstScan = hardware == null;
                if (hardware != null)
                    hardware.Dispose();
                hardware = null;
                active.Clear();
                foreach (DeviceTile tile in Tiles.ToArray())
                    tile.Dispose();
                hardware = await Task.Run(openHardware);
                int row = 0;
                foreach (LightingTarget device in hardware.Devices)
                {
                    var tile = new DeviceTile(device) { Checked = firstScan || selection.Contains(device.Name) };
                    tile.SetBounds(0, ScalePx(row * 86), devices.ClientSize.Width - 2, ScalePx(74));
                    tile.CheckedChanged += (sender, args) => UpdateButtons();
                    devices.Controls.Add(tile);
                    row++;
                }
                int count = hardware.Devices.Count;
                connection.SetStatus(DeviceCount(count) + " ready", count > 0);
                SetFeedback(hardware.Problems.Count == 0 ? "Ready to apply an effect" : String.Join(" ", hardware.Problems),
                    hardware.Problems.Count > 0);
            }
            catch (Exception error)
            {
                connection.SetStatus("Lighting unavailable", false);
                SetFeedback(error.Message, true);
            }
            finally { SetBusy(false); }
        }

        async Task ApplyEffect(LightingEffect effect)
        {
            var selected = Tiles.Where(tile => tile.Checked).Select(tile => tile.Target).ToArray();
            if (busy || selected.Length == 0)
                return;

            SetBusy(true, "Applying effect…");
            try
            {
                await WaitForFrame();
                double begin = clock.Elapsed.TotalSeconds;
                var next = selected.Select(target => new ActiveEffect(target, effect, begin)).ToArray();
                FrameFailure[] failures = await Task.Run(() => WriteFrames(next, begin, true));
                foreach (ActiveEffect item in next)
                {
                    active.Remove(item.Target);
                    if (item.Effect.Animated && !failures.Any(fail => fail.Effect == item))
                        active.Add(item.Target, item);
                }
                if (failures.Length == 0)
                    SetFeedback(effect.Name + " applied to " + DeviceCount(next.Length), false);
                else
                    ReportFailures(failures);
            }
            finally { SetBusy(false); }
        }

        async Task DrawFrame()
        {
            double elapsed = clock.Elapsed.TotalSeconds;
            if (previewEffect.Animated)
                preview.ShowFrame(previewEffect, elapsed);
            if (busy || closing || active.Count == 0 || frameWrite != null)
                return;

            ActiveEffect[] copy = active.Values.ToArray();
            frameWrite = Task.Run(() => WriteFrames(copy, elapsed, false));
            try
            {
                FrameFailure[] failures = await frameWrite;
                foreach (FrameFailure failure in failures)
                {
                    ActiveEffect current;
                    if (active.TryGetValue(failure.Effect.Target, out current) && current == failure.Effect)
                        active.Remove(failure.Effect.Target);
                }
                if (failures.Length > 0 && !busy)
                    ReportFailures(failures);
            }
            finally { frameWrite = null; }
        }

        Task WaitForFrame()
        {
            return frameWrite ?? (Task)Task.FromResult(false);
        }

        static FrameFailure[] WriteFrames(ActiveEffect[] items, double elapsed, bool firstFrame)
        {
            var failures = new List<FrameFailure>();
            foreach (ActiveEffect item in items)
            {
                try
                {
                    item.Effect.Render(item.Pending, elapsed - item.Start);
                    if (firstFrame || !item.Pending.SequenceEqual(item.Previous))
                    {
                        item.Target.Write(item.Pending, firstFrame);
                        Array.Copy(item.Pending, item.Previous, item.Pending.Length);
                    }
                }
                catch (Exception error) { failures.Add(new FrameFailure(item, error)); }
            }
            return failures.ToArray();
        }

        void ReportFailures(FrameFailure[] failures)
        {
            FrameFailure failure = failures[0];
            SetFeedback(failure.Effect.Target.Name + " — " + failure.Error.Message, true);
        }

        void UpdatePreview()
        {
            previewEffect = new LightingEffect(selectedEffect, first, second,
                brightness.Value, speed.Value / 10.0, reverse.Checked);
            bool timed = previewEffect.Kind != EffectKind.Solid && previewEffect.Kind != EffectKind.Gradient;
            mode.Text = previewEffect.Name;
            mode.AccessibleName = "Lighting effect, " + previewEffect.Name;
            for (int i = 0; i < effectMenu.Items.Count; i++)
                ((ToolStripMenuItem)effectMenu.Items[i]).Checked = i == (int)selectedEffect;
            speed.Enabled = timed;
            reverse.Visible = previewEffect.UsesDirection;
            brightnessValue.Text = brightness.Value + "%";
            speedValue.Text = timed ? (speed.Value / 10.0).ToString("0.0", CultureInfo.InvariantCulture) + " s" : "—";
            ShowColorButton(firstColor, "Color 1", first, previewEffect.UsesFirst);
            ShowColorButton(secondColor, "Color 2", second, previewEffect.UsesSecond);
            preview.ShowFrame(previewEffect, clock.Elapsed.TotalSeconds);
        }

        static void ShowColorButton(StudioButton button, string label, Color color, bool enabled)
        {
            button.Text = label + "   " + ColorEditor.Hex(color);
            button.Swatch = color;
            button.Visible = enabled;
        }

        void PickColor(bool primary)
        {
            using (var dialog = new ColorEditor(primary ? "Color 1" : "Color 2", primary ? first : second))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                if (primary)
                    first = dialog.Color;
                else
                    second = dialog.Color;
                UpdatePreview();
            }
        }

        void SetBusy(bool value, string message = null)
        {
            busy = value;
            if (message != null)
                SetFeedback(message, false);
            UpdateButtons();
        }

        void UpdateButtons()
        {
            if (IsDisposed)
                return;
            refresh.Enabled = !busy && !closing;
            devices.Enabled = !busy && !closing;
            int count = Tiles.Count(tile => tile.Checked);
            selectionCount.Text = count + " selected";
            apply.Enabled = turnOff.Enabled = !busy && !closing && count > 0;
        }

        void SetFeedback(string text, bool failed)
        {
            feedback.ForeColor = failed ? Palette.Error : Palette.Muted;
            feedback.Text = text;
            statusTip.SetToolTip(feedback, failed ? text : null);
        }

        static string DeviceCount(int count)
        {
            return count + (count == 1 ? " device" : " devices");
        }

        int ScalePx(int pixels) { return Palette.Scaled(pixels, layoutScale); }

        protected override async void OnFormClosing(FormClosingEventArgs args)
        {
            base.OnFormClosing(args);
            if (!busy && frameWrite == null)
                return;

            args.Cancel = true;
            if (closing)
                return;
            closing = true;
            UpdateButtons();
            while (busy || frameWrite != null)
                await Task.Delay(25);
            Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs args)
        {
            timer.Dispose();
            effectMenu.Dispose();
            statusTip.Dispose();
            if (hardware != null)
                hardware.Dispose();
            base.OnFormClosed(args);
        }

        SurfaceCard Card(int x, int y, int width, int height)
        {
            var panel = new SurfaceCard();
            panel.SetBounds(x, y, width, height);
            Controls.Add(panel);
            return panel;
        }

        static void ConfigureSlider(Control parent, StudioSlider slider, Label value, int y, string label)
        {
            slider.SetBounds(133, y, 307, 29);
            slider.BackColor = Palette.Surface;
            slider.AccessibleName = label;
            parent.Controls.Add(slider);
            value.SetBounds(448, y, 60, 28);
            value.ForeColor = Palette.Muted;
            value.BackColor = Palette.Surface;
            value.TextAlign = ContentAlignment.MiddleRight;
            parent.Controls.Add(value);
        }

        internal static Label LabelAt(Control parent, string text, int x, int y, int width, int height, Color color, Font font)
        {
            var label = new Label
            {
                Text = text, ForeColor = color, TextAlign = ContentAlignment.MiddleLeft, Font = font,
                BackColor = parent is SurfaceCard ? Palette.Surface : parent.BackColor
            };
            label.SetBounds(x, y, width, height);
            parent.Controls.Add(label);
            return label;
        }

        internal static StudioButton ButtonAt(Control parent, string text, int x, int y, int width, int height, ButtonAppearance appearance)
        {
            var button = new StudioButton(appearance) { Text = text, BackColor = parent is SurfaceCard ? Palette.Surface : parent.BackColor };
            button.SetBounds(x, y, width, height);
            parent.Controls.Add(button);
            return button;
        }

        sealed class ActiveEffect
        {
            public readonly LightingTarget Target;
            public readonly LightingEffect Effect;
            public readonly Color[] Pending;
            public readonly Color[] Previous;
            public readonly double Start;

            public ActiveEffect(LightingTarget target, LightingEffect effect, double start)
            {
                Target = target;
                Effect = effect;
                Start = start;
                Pending = new Color[target.LedCount];
                Previous = new Color[target.LedCount];
            }
        }

        sealed class FrameFailure
        {
            public readonly ActiveEffect Effect;
            public readonly Exception Error;
            public FrameFailure(ActiveEffect effect, Exception error) { Effect = effect; Error = error; }
        }
    }

    sealed class EffectPreview : Control
    {
        readonly Color[] pixels = new Color[26];

        public EffectPreview()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
        }

        public void ShowFrame(LightingEffect effect, double elapsed)
        {
            effect.Render(pixels, elapsed);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            var g = args.Graphics;
            float s = Palette.Scale(g);
            g.Clear(Palette.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new RectangleF(1, 1, Width - 2, Height - 2);
            Palette.Rounded(g, bounds, 11*s, Palette.Background, Palette.Border);
            var heading = new Rectangle((int)(17*s), (int)(11*s), Width - (int)(34*s), (int)(23*s));
            TextRenderer.DrawText(g, "Live preview", Palette.Meta, heading, Palette.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            float margin = 16*s;
            float cell = (Width - margin*2) / pixels.Length;
            float y = Height * 0.47F;
            float h = Height * 0.26F;
            for (int i = 0; i < pixels.Length; i++)
            {
                float x = margin + i * cell;
                var glow = new RectangleF(x, y - 12*s, cell, h + 24*s);
                using (var halo = new LinearGradientBrush(glow, Color.Transparent, Color.Transparent, LinearGradientMode.Vertical))
                {
                    halo.InterpolationColors = new ColorBlend
                    {
                        Colors = new[] { Color.FromArgb(0, pixels[i]), Color.FromArgb(42, pixels[i]), Color.FromArgb(0, pixels[i]) },
                        Positions = new[] { 0F, 0.48F, 1F }
                    };
                    g.FillRectangle(halo, glow);
                }
                Palette.Rounded(g, new RectangleF(x + s, y, cell - 2*s, h), 3*s, pixels[i], Color.FromArgb(38, Palette.Ink));
            }
        }
    }
}
