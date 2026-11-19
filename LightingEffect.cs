using System;
using System.Drawing;

namespace RgbPanel
{
    enum EffectKind
    {
        Solid, Gradient, Fade, Breathing, GradientWave, RainbowWave, Spectrum, Comet, Off
    }

    sealed class LightingEffect
    {
        public static readonly string[] Names =
        {
            "Solid Color", "Gradient", "Color Fade", "Breathing",
            "Gradient Wave", "Rainbow Wave", "Spectrum Cycle", "Comet"
        };

        public readonly EffectKind Kind;
        public readonly Color First;
        public readonly Color Second;
        public readonly int Brightness;
        public readonly double Period;
        public readonly bool Reverse;

        public LightingEffect(EffectKind kind, Color first, Color second, int brightness, double period, bool reverse)
        {
            Kind = kind;
            First = first;
            Second = second;
            Brightness = brightness;
            Period = period;
            Reverse = reverse;
        }

        public bool Animated { get { return Kind != EffectKind.Solid && Kind != EffectKind.Gradient && Kind != EffectKind.Off && Brightness > 0; } }
        public bool UsesFirst { get { return Kind != EffectKind.RainbowWave && Kind != EffectKind.Spectrum && Kind != EffectKind.Off; } }
        public bool UsesSecond { get { return Kind == EffectKind.Gradient || Kind == EffectKind.Fade || Kind == EffectKind.GradientWave || Kind == EffectKind.Comet; } }
        public bool UsesDirection { get { return Kind == EffectKind.GradientWave || Kind == EffectKind.RainbowWave || Kind == EffectKind.Comet; } }
        public string Name { get { return Kind == EffectKind.Off ? "Off" : Names[(int)Kind]; } }

        public void Render(Color[] pixels, double seconds)
        {
            double phase = (seconds / Period) % 1.0;
            double brightness = Brightness / 100.0;
            for (int i = 0; i < pixels.Length; i++)
            {
                double line = pixels.Length > 1 ? i / (double)(pixels.Length - 1) : 0;
                double offset = i / (double)pixels.Length;
                double travel = offset + (Reverse ? phase : -phase);
                Color color;
                switch (Kind)
                {
                    case EffectKind.Gradient:
                        color = Blend(First, Second, line);
                        break;
                    case EffectKind.Fade:
                        color = Blend(First, Second, Wave(phase));
                        break;
                    case EffectKind.Breathing:
                        color = Scale(First, 0.08 + 0.92 * (1.0 - Wave(phase)));
                        break;
                    case EffectKind.GradientWave:
                        color = Blend(First, Second, Wave(travel));
                        break;
                    case EffectKind.RainbowWave:
                        color = Hue(travel);
                        break;
                    case EffectKind.Spectrum:
                        color = Hue(phase);
                        break;
                    case EffectKind.Comet:
                        double tail = Math.Pow(1 - Fraction(Reverse ? travel : -travel), 3);
                        color = Blend(Scale(Second, 0.25), First, tail);
                        break;
                    case EffectKind.Off:
                        color = Color.Black;
                        break;
                    default:
                        color = First;
                        break;
                }
                pixels[i] = Scale(color, brightness);
            }
        }

        static double Wave(double phase) { return (1 - Math.Cos(phase * Math.PI * 2)) / 2; }
        static double Fraction(double value) { return value - Math.Floor(value); }

        static Color Hue(double phase)
        {
            double h = Fraction(phase) * 6;
            double cross = 1 - Math.Abs(h % 2 - 1);
            switch ((int)h)
            {
                case 0: return Color.FromArgb(255, Byte(cross * 255), 0);
                case 1: return Color.FromArgb(Byte(cross * 255), 255, 0);
                case 2: return Color.FromArgb(0, 255, Byte(cross * 255));
                case 3: return Color.FromArgb(0, Byte(cross * 255), 255);
                case 4: return Color.FromArgb(Byte(cross * 255), 0, 255);
                default: return Color.FromArgb(255, 0, Byte(cross * 255));
            }
        }

        static Color Blend(Color a, Color b, double t)
        {
            return Color.FromArgb(Byte(a.R + (b.R - a.R) * t), Byte(a.G + (b.G - a.G) * t), Byte(a.B + (b.B - a.B) * t));
        }

        static Color Scale(Color c, double scale)
        {
            return Color.FromArgb(Byte(c.R * scale), Byte(c.G * scale), Byte(c.B * scale));
        }

        static int Byte(double value) { return (int)Math.Round(value); }
    }
}
