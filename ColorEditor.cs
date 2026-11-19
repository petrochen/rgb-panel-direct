using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace RgbPanel
{
    sealed class ColorEditor : Form
    {
        readonly TextBox hex = new TextBox();
        readonly StudioSlider[] channels = { new StudioSlider(0, 255, 0), new StudioSlider(0, 255, 0), new StudioSlider(0, 255, 0) };
        readonly Label[] values = new Label[3];
        readonly Label error;
        readonly Panel sample = new Panel();
        readonly StudioButton confirm;
        bool updating;

        public Color Color { get; private set; }

        public ColorEditor(string name, Color initial)
        {
            Text = "Edit " + name.ToLowerInvariant();
            BackColor = Palette.Background;
            ForeColor = Palette.Ink;
            Font = Palette.Body;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(429, 333);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = MinimizeBox = MaximizeBox = ShowInTaskbar = false;

            DirectPanel.LabelAt(this, name, 20, 10, 210, 36, Palette.Ink, Palette.Subhead);
            sample.SetBounds(20, 55, 172, 31);
            Controls.Add(sample);
            DirectPanel.LabelAt(this, "HEX", 224, 55, 39, 31, Palette.Muted, Palette.Meta);
            hex.SetBounds(275, 57, 134, 28);
            hex.Font = new Font("Consolas", 11F);
            hex.BackColor = Palette.Surface;
            hex.ForeColor = Palette.Ink;
            hex.BorderStyle = BorderStyle.FixedSingle;
            hex.MaxLength = 7;
            hex.AccessibleName = "Hex color";
            Controls.Add(hex);

            Color[] presets =
            {
                Color.FromArgb(134, 103, 255), Color.FromArgb(74, 142, 255), Color.FromArgb(43, 226, 204),
                Color.FromArgb(89, 215, 129), Color.FromArgb(247, 179, 82), Color.FromArgb(252, 100, 128), Color.White
            };
            for (int i = 0; i < presets.Length; i++)
            {
                Color choice = presets[i];
                var button = DirectPanel.ButtonAt(this, Hex(choice), 20 + i*57, 103, 47, 32, ButtonAppearance.Swatch);
                button.Swatch = choice;
                button.Click += (sender, args) => SelectColor(choice, true);
            }

            string[] names = { "Red", "Green", "Blue" };
            for (int i = 0; i < channels.Length; i++)
            {
                int y = 153 + i*33;
                DirectPanel.LabelAt(this, names[i], 20, y, 60, 29, Palette.Muted, Palette.Meta);
                channels[i].SetBounds(80, y, 263, 29);
                channels[i].AccessibleName = names[i];
                channels[i].BackColor = Palette.Background;
                channels[i].ValueChanged += (sender, args) =>
                {
                    if (!updating)
                        SelectColor(Color.FromArgb(channels[0].Value, channels[1].Value, channels[2].Value), true);
                };
                Controls.Add(channels[i]);
                values[i] = DirectPanel.LabelAt(this, "", 357, y, 52, 29, Palette.Ink, Palette.Meta);
                values[i].TextAlign = ContentAlignment.MiddleRight;
            }

            error = DirectPanel.LabelAt(this, "", 20, 251, 389, 25, Palette.Error, Palette.Meta);
            var cancel = DirectPanel.ButtonAt(this, "Cancel", 197, 279, 90, 36, ButtonAppearance.Secondary);
            confirm = DirectPanel.ButtonAt(this, "Use color", 297, 279, 112, 36, ButtonAppearance.Primary);
            cancel.DialogResult = DialogResult.Cancel;
            confirm.DialogResult = DialogResult.OK;
            CancelButton = cancel;
            AcceptButton = confirm;
            hex.TextChanged += OnHexChanged;
            SelectColor(initial, true);
            Shown += (sender, args) => { hex.Focus(); hex.SelectAll(); };
        }

        public static string Hex(Color value)
        {
            return String.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", value.R, value.G, value.B);
        }

        void OnHexChanged(object sender, EventArgs args)
        {
            if (updating)
                return;
            string text = hex.Text.Trim();
            if (text.StartsWith("#", StringComparison.Ordinal))
                text = text.Substring(1);
            int rgb;
            if (text.Length == 6 && Int32.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out rgb))
            {
                SelectColor(Color.FromArgb(255, Color.FromArgb(rgb)), false);
            }
            else
            {
                error.Text = "Enter a six-digit hex color.";
                confirm.Enabled = false;
            }
        }

        void SelectColor(Color value, bool changeHex)
        {
            updating = true;
            try
            {
                Color = value;
                sample.BackColor = value;
                if (changeHex) hex.Text = Hex(value);
                channels[0].Value = value.R;
                channels[1].Value = value.G;
                channels[2].Value = value.B;
                for (int i = 0; i < channels.Length; i++)
                    values[i].Text = channels[i].Value.ToString(CultureInfo.InvariantCulture);
                error.Text = "";
                confirm.Enabled = true;
            }
            finally { updating = false; }
        }

        protected override void OnHandleCreated(EventArgs args)
        {
            base.OnHandleCreated(args);
            Palette.Caption(Handle);
        }

        protected override void OnLoad(EventArgs args)
        {
            Palette.ScaleWindow(this);
            base.OnLoad(args);
        }
    }
}
