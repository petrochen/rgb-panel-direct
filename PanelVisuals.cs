using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RgbPanel
{
    static class Palette
    {
        internal static readonly Color Background = Color.FromArgb(13, 17, 27);
        internal static readonly Color Surface = Color.FromArgb(22, 28, 42);
        internal static readonly Color Inset = Color.FromArgb(16, 22, 35);
        internal static readonly Color Border = Color.FromArgb(49, 61, 82);
        internal static readonly Color Ink = Color.FromArgb(242, 246, 255);
        internal static readonly Color Muted = Color.FromArgb(162, 177, 203);
        internal static readonly Color Accent = Color.FromArgb(128, 232, 212);
        internal static readonly Color AccentInk = Color.FromArgb(12, 43, 42);
        internal static readonly Color Error = Color.FromArgb(255, 163, 156);
        internal static readonly Font Body = new Font("Segoe UI", 10F);
        internal static readonly Font Strong = new Font("Segoe UI", 10F, FontStyle.Bold);
        internal static readonly Font Meta = new Font("Segoe UI", 9F);
        internal static readonly Font Subhead = new Font("Segoe UI", 13F, FontStyle.Bold);

        internal static float Scale(Graphics graphics) { return graphics.DpiX / 96F; }

        internal static float ScaleWindow(Form form)
        {
            float scale;
            using (Graphics graphics = form.CreateGraphics())
                scale = Scale(graphics);
            if (Math.Abs(scale - 1) < 0.01F)
                return 1;

            form.SuspendLayout();
            try
            {
                form.ClientSize = new Size(Scaled(form.ClientSize.Width, scale), Scaled(form.ClientSize.Height, scale));
                foreach (Control child in form.Controls)
                    ScaleBounds(child, scale);
            }
            finally { form.ResumeLayout(); }
            return scale;
        }

        static void ScaleBounds(Control control, float scale)
        {
            control.SetBounds(Scaled(control.Left, scale), Scaled(control.Top, scale),
                Scaled(control.Width, scale), Scaled(control.Height, scale));
            foreach (Control child in control.Controls)
                ScaleBounds(child, scale);
        }

        internal static int Scaled(int value, float scale) { return (int)Math.Round(value * scale); }

        internal static void Rounded(Graphics graphics, RectangleF bounds, float radius, Color fill, Color border)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;
            float diameter = Math.Min(2 * radius, Math.Min(bounds.Width, bounds.Height));
            using (var path = new GraphicsPath())
            {
                path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
                path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
                path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
                path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
                path.CloseFigure();
                if (fill.A != 0)
                    using (var brush = new SolidBrush(fill)) graphics.FillPath(brush, path);
                if (border.A != 0)
                    using (var pen = new Pen(border)) graphics.DrawPath(pen, path);
            }
        }

        internal static void Caption(IntPtr handle)
        {
            int dark = 1;
            int round = 2;
            int bg = Background.R | (Background.G << 8) | (Background.B << 16);
            DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
            DwmSetWindowAttribute(handle, 33, ref round, sizeof(int));
            DwmSetWindowAttribute(handle, 35, ref bg, sizeof(int));
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
    }

    sealed class SurfaceCard : Panel
    {
        public SurfaceCard()
        {
            BackColor = Palette.Background;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Palette.Rounded(args.Graphics, new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F),
                14F * Palette.Scale(args.Graphics), Palette.Surface, Palette.Border);
            base.OnPaint(args);
        }
    }

    enum ButtonAppearance { Secondary, Primary, Selector, Color, Swatch }

    sealed class StudioButton : Button
    {
        readonly ButtonAppearance appearance;
        bool hover;
        Color swatch;

        public StudioButton(ButtonAppearance appearance)
        {
            this.appearance = appearance;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            UseVisualStyleBackColor = false;
            Font = Palette.Strong;
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        }

        public Color Swatch
        {
            get { return swatch; }
            set { swatch = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            Graphics g = args.Graphics;
            float s = Palette.Scale(g);
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool primary = appearance == ButtonAppearance.Primary;
            Color fill = primary ? Palette.Accent : Palette.Inset;
            if (appearance == ButtonAppearance.Swatch)
                fill = swatch;
            else if (!Enabled)
                fill = Palette.Surface;
            else if (hover)
                fill = primary ? Color.FromArgb(168, 246, 232) : Color.FromArgb(35, 46, 66);
            Color border = primary ? fill : Palette.Border;
            Palette.Rounded(g, new RectangleF(1, 1, Width - 2, Height - 2), 8 * s, fill, Focused ? Palette.Accent : border);

            if (appearance == ButtonAppearance.Swatch)
                return;
            Color text = primary && Enabled ? Palette.AccentInk : Enabled ? Palette.Ink : Palette.Muted;
            int inset = (int)(13 * s);
            var area = Rectangle.FromLTRB(inset, 0, Width - inset, Height);
            const TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
            if (appearance == ButtonAppearance.Selector)
            {
                area.Width -= (int)(22 * s);
                TextRenderer.DrawText(g, Text, Font, area, text, flags);
                using (var pen = new Pen(Palette.Muted, 1.6F * s))
                    g.DrawLines(pen, new[] { new PointF(Width - 26*s, Height/2F - 2*s), new PointF(Width - 22*s, Height/2F + 2*s), new PointF(Width - 18*s, Height/2F - 2*s) });
            }
            else if (appearance == ButtonAppearance.Color)
            {
                Palette.Rounded(g, new RectangleF(12 * s, Height / 2F - 9 * s, 18 * s, 18 * s), 4 * s, swatch, Color.FromArgb(90, Color.White));
                area.X += (int)(24 * s);
                area.Width -= (int)(24 * s);
                TextRenderer.DrawText(g, Text, Font, area, text, flags);
            }
            else
                TextRenderer.DrawText(g, Text, Font, area, text, flags | TextFormatFlags.HorizontalCenter);
        }

        protected override void OnMouseEnter(EventArgs args) { hover = true; Invalidate(); base.OnMouseEnter(args); }
        protected override void OnMouseLeave(EventArgs args) { hover = false; Invalidate(); base.OnMouseLeave(args); }
        protected override void OnGotFocus(EventArgs args) { Invalidate(); base.OnGotFocus(args); }
        protected override void OnLostFocus(EventArgs args) { Invalidate(); base.OnLostFocus(args); }
    }

    sealed class DeviceTile : CheckBox
    {
        public LightingTarget Target { get; private set; }
        bool hover;

        public DeviceTile(LightingTarget target)
        {
            Target = target;
            Text = target.DisplayName;
            AccessibleName = target.Name;
            AccessibleDescription = target.Category + ", " + target.LedCount + " LEDs";
            Cursor = Cursors.Hand;
            BackColor = Palette.Surface;
            AutoSize = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            Graphics g = args.Graphics;
            float s = Palette.Scale(g);
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = Checked ? Color.FromArgb(28, 45, 56) : hover ? Color.FromArgb(32, 42, 61) : Palette.Inset;
            Color border = Checked ? Color.FromArgb(57, 101, 112) : Palette.Border;
            Palette.Rounded(g, new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F), 9*s, fill, Focused ? Palette.Accent : border);

            var icon = new RectangleF(11*s, Height/2F - 17*s, 34*s, 34*s);
            Palette.Rounded(g, icon, 7*s, Color.FromArgb(39, 60, 76), Color.Transparent);
            DrawGlyph(g, icon, s);

            int textLeft = (int)(55*s);
            int textWidth = Math.Max(1, Width - textLeft - (int)(38*s));
            var heading = new Rectangle(textLeft, (int)(10*s), textWidth, (int)(23*s));
            var subtitle = new Rectangle(textLeft, (int)(32*s), textWidth, (int)(20*s));
            const TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
            TextRenderer.DrawText(g, Text, Palette.Strong, heading, Enabled ? Palette.Ink : Palette.Muted, flags);
            TextRenderer.DrawText(g, Target.Category + "  ·  " + Target.LedCount + " LEDs", Palette.Meta, subtitle, Palette.Muted, flags);

            var box = new RectangleF(Width - 30*s, Height/2F - 8*s, 16*s, 16*s);
            Palette.Rounded(g, box, 4*s, Checked ? Palette.Accent : Palette.Inset, Checked ? Palette.Accent : Palette.Border);
            if (Checked)
            {
                using (var pen = new Pen(Palette.AccentInk, 1.6F * s))
                    g.DrawLines(pen, new[] { new PointF(box.X + 4*s, box.Y + 8*s), new PointF(box.X + 7*s, box.Y + 11*s), new PointF(box.X + 12*s, box.Y + 5*s) });
            }
        }

        void DrawGlyph(Graphics g, RectangleF icon, float s)
        {
            float x = icon.X + 8*s;
            float y = icon.Y + 8*s;
            using (var pen = new Pen(Palette.Accent, 1.35F * s))
            {
                if (Target.Kind == DeviceKind.Cooler)
                {
                    g.DrawEllipse(pen, x + 1*s, y + 1*s, 16*s, 16*s);
                    g.DrawEllipse(pen, x + 5*s, y + 5*s, 8*s, 8*s);
                }
                else if (Target.Kind == DeviceKind.Motherboard)
                {
                    g.DrawRectangle(pen, x + 2*s, y + 1*s, 14*s, 16*s);
                    g.DrawRectangle(pen, x + 5*s, y + 4*s, 5*s, 5*s);
                    g.DrawLine(pen, x + 5*s, y + 13*s, x + 13*s, y + 13*s);
                }
                else
                {
                    g.DrawRectangle(pen, x, y + 5*s, 18*s, 8*s);
                    for (int i = 0; i < 3; i++)
                        g.DrawLine(pen, x + (4 + 5*i)*s, y + 8*s, x + (4 + 5*i)*s, y + 15*s);
                }
            }
        }

        protected override void OnCheckedChanged(EventArgs args) { Invalidate(); base.OnCheckedChanged(args); }
        protected override void OnMouseEnter(EventArgs args) { hover = true; Invalidate(); base.OnMouseEnter(args); }
        protected override void OnMouseLeave(EventArgs args) { hover = false; Invalidate(); base.OnMouseLeave(args); }
        protected override void OnGotFocus(EventArgs args) { Invalidate(); base.OnGotFocus(args); }
        protected override void OnLostFocus(EventArgs args) { Invalidate(); base.OnLostFocus(args); }
    }

    sealed class StudioSlider : Control
    {
        readonly int minimum;
        readonly int maximum;
        int value;

        public event EventHandler ValueChanged;

        public StudioSlider(int minimum, int maximum, int value)
        {
            this.minimum = minimum;
            this.maximum = maximum;
            this.value = value;
            AccessibleRole = AccessibleRole.Slider;
            TabStop = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
        }

        public int Value
        {
            get { return value; }
            set
            {
                int next = Math.Max(minimum, Math.Min(maximum, value));
                if (next == this.value)
                    return;
                this.value = next;
                Invalidate();
                AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            Graphics g = args.Graphics;
            float s = Palette.Scale(g);
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float y = Height / 2F;
            float margin = 9*s;
            float rail = Math.Max(0, Width - 2*margin);
            float filled = (value - minimum) * rail / (maximum - minimum);
            Palette.Rounded(g, new RectangleF(margin, y - 2*s, rail, 4*s), 2*s, Palette.Border, Color.Transparent);
            Palette.Rounded(g, new RectangleF(margin, y - 2*s, filled, 4*s), 2*s, Enabled ? Palette.Accent : Palette.Muted, Color.Transparent);
            using (var brush = new SolidBrush(Enabled ? Palette.Accent : Palette.Muted))
                g.FillEllipse(brush, margin + filled - 6*s, y - 6*s, 12*s, 12*s);
            if (Focused)
                using (var pen = new Pen(Palette.Ink)) g.DrawEllipse(pen, margin + filled - 8*s, y - 8*s, 16*s, 16*s);
        }

        void MoveTo(int x)
        {
            float margin;
            using (Graphics graphics = CreateGraphics())
                margin = 9F * Palette.Scale(graphics);
            Value = minimum + (int)Math.Round((x - margin) / Math.Max(1, Width - 2*margin) * (maximum - minimum));
        }

        protected override void OnMouseDown(MouseEventArgs args)
        {
            base.OnMouseDown(args);
            if (args.Button != MouseButtons.Left) return;
            Focus();
            Capture = true;
            MoveTo(args.X);
        }

        protected override void OnMouseMove(MouseEventArgs args)
        {
            base.OnMouseMove(args);
            if (Capture && args.Button == MouseButtons.Left) MoveTo(args.X);
        }

        protected override void OnMouseUp(MouseEventArgs args)
        {
            if (args.Button == MouseButtons.Left) Capture = false;
            base.OnMouseUp(args);
        }

        protected override bool IsInputKey(Keys key)
        {
            switch (key & Keys.KeyCode)
            {
                case Keys.Left: case Keys.Right: case Keys.Down: case Keys.Up:
                case Keys.Home: case Keys.End: case Keys.PageUp: case Keys.PageDown: return true;
                default: return base.IsInputKey(key);
            }
        }

        protected override void OnKeyDown(KeyEventArgs args)
        {
            switch (args.KeyCode)
            {
                case Keys.Left: case Keys.Down: Value--; break;
                case Keys.Right: case Keys.Up: Value++; break;
                case Keys.PageDown: Value -= 10; break;
                case Keys.PageUp: Value += 10; break;
                case Keys.Home: Value = minimum; break;
                case Keys.End: Value = maximum; break;
                default: base.OnKeyDown(args); return;
            }
            args.Handled = true;
        }

        protected override void OnGotFocus(EventArgs args) { Invalidate(); base.OnGotFocus(args); }
        protected override void OnLostFocus(EventArgs args) { Invalidate(); base.OnLostFocus(args); }
        protected override AccessibleObject CreateAccessibilityInstance() { return new SliderAccessibility(this); }

        sealed class SliderAccessibility : ControlAccessibleObject
        {
            readonly StudioSlider slider;
            public SliderAccessibility(StudioSlider slider) : base(slider) { this.slider = slider; }
            public override string Value
            {
                get { return slider.Value.ToString(); }
                set { int next; if (Int32.TryParse(value, out next)) slider.Value = next; }
            }
        }
    }

    sealed class DirectionToggle : CheckBox
    {
        public DirectionToggle()
        {
            BackColor = Palette.Surface;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            var g = args.Graphics;
            float s = Palette.Scale(g);
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float y = (Height - 18*s) / 2;
            Palette.Rounded(g, new RectangleF(1, y, 33*s, 18*s), 9*s,
                Checked ? Palette.Accent : Palette.Inset, Focused ? Palette.Accent : Palette.Border);
            using (var pen = new SolidBrush(Checked ? Palette.AccentInk : Palette.Muted))
                g.FillEllipse(pen, Checked ? 18*s : 4*s, y + 3*s, 12*s, 12*s);
            TextRenderer.DrawText(g, Text, Palette.Body, new Rectangle((int)(44*s), 0, Width - (int)(44*s), Height),
                Palette.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        protected override void OnCheckedChanged(EventArgs args) { Invalidate(); base.OnCheckedChanged(args); }
        protected override void OnGotFocus(EventArgs args) { Invalidate(); base.OnGotFocus(args); }
        protected override void OnLostFocus(EventArgs args) { Invalidate(); base.OnLostFocus(args); }
    }

    sealed class StatusBadge : Control
    {
        bool ready;

        public void SetStatus(string status, bool ready)
        {
            this.ready = ready;
            Text = status;
            AccessibleName = status;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            var g = args.Graphics;
            float s = Palette.Scale(g);
            g.Clear(Palette.Background);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Palette.Rounded(g, new RectangleF(0.5F, 0.5F, Width-1F, Height-1F), Height/2F,
                Palette.Surface, Palette.Border);
            int textWidth = TextRenderer.MeasureText(g, Text, Palette.Meta, Size.Empty, TextFormatFlags.NoPadding).Width;
            float start = Math.Max(12*s, (Width - textWidth - 18*s) / 2F);
            using (var brush = new SolidBrush(ready ? Palette.Accent : Palette.Muted))
                g.FillEllipse(brush, start, Height/2F - 3.5F*s, 7*s, 7*s);
            int offset = (int)(start + 17*s);
            TextRenderer.DrawText(g, Text, Palette.Meta, new Rectangle(offset, 0, Width - offset - (int)(8*s), Height),
                Palette.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    sealed class PanelMark : Control
    {
        protected override void OnPaint(PaintEventArgs args)
        {
            var g = args.Graphics;
            float s = Palette.Scale(g);
            g.Clear(Palette.Background);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Palette.Rounded(g, new RectangleF(0.5F, 0.5F, Width-1F, Height-1F), 10*s,
                Palette.Surface, Palette.Border);
            Color[] colors = { Color.FromArgb(155, 126, 255), Color.FromArgb(119, 183, 255), Palette.Accent };
            int[] heights = { 12, 20, 15 };
            for (int i = 0; i < colors.Length; i++)
                Palette.Rounded(g, new RectangleF((11 + 7*i)*s, (28 - heights[i])*s, 4*s, heights[i]*s),
                    2*s, colors[i], Color.Transparent);
        }
    }

    sealed class StudioMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Palette.Surface; } }
        public override Color MenuBorder { get { return Palette.Border; } }
        public override Color MenuItemSelected { get { return Color.FromArgb(40, 56, 77); } }
        public override Color MenuItemBorder { get { return Palette.Border; } }
        public override Color CheckBackground { get { return Palette.Surface; } }
        public override Color CheckSelectedBackground { get { return Palette.Surface; } }
        public override Color ImageMarginGradientBegin { get { return Palette.Surface; } }
        public override Color ImageMarginGradientMiddle { get { return Palette.Surface; } }
        public override Color ImageMarginGradientEnd { get { return Palette.Surface; } }
    }
}
