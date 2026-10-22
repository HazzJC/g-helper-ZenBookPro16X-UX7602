using System.Drawing;
using System.Runtime.InteropServices;

namespace GHelper.USB
{
    /// <summary>
    /// Per-effect settings for the ZenBook Pro 16X presets: each effect owns its palette and
    /// speed (edited in the Lighting Studio) instead of borrowing the Aura colour pickers, plus
    /// the list of modes the user has hidden from the keyboard dropdown.
    /// </summary>
    public static class Zenbook16XSettings
    {
        public const int MAX_STOPS = 6;

        public static readonly (string Name, string[] Hex)[] PalettePresets =
        {
            ("Fire",    new[] { "FF2A00", "FF8000", "FFD000" }),
            ("Ocean",   new[] { "0040FF", "00B0FF", "00FFD0" }),
            ("Neon",    new[] { "FF00C8", "7A00FF", "00E5FF" }),
            ("Sunset",  new[] { "FF3C00", "FF0070", "7000FF" }),
            ("Aurora",  new[] { "00FF80", "00C8FF", "9000FF" }),
            ("Forest",  new[] { "00A020", "80E000", "FFE040" }),
            ("Ice",     new[] { "FFFFFF", "70D0FF", "2060FF" }),
            ("Candy",   new[] { "FF60B0", "B070FF", "60E0FF" }),
            ("Rainbow", new[] { "FF0000", "FFD000", "00FF40", "00C8FF", "8000FF" }),
        };

        static string[] DefaultHex(AuraMode mode) => mode switch
        {
            AuraMode.AURORA => PalettePresets[4].Hex,
            AuraMode.LAVA => PalettePresets[3].Hex,
            AuraMode.SONAR => PalettePresets[1].Hex,
            AuraMode.FIREFLIES => PalettePresets[5].Hex,
            AuraMode.EMBER => PalettePresets[2].Hex,
            _ => PalettePresets[8].Hex, // Colour Rain
        };

        /// <summary>Modes that are colour-driven software presets, and so have a palette.</summary>
        public static bool HasPalette(AuraMode mode) =>
            mode == AuraMode.RAIN_COLOR || Array.IndexOf(Zenbook16XEffects.All, mode) >= 0;

        public static Color[] GetPalette(AuraMode mode)
        {
            string stored = AppConfig.GetString("zb_pal_" + (int)mode, "") ?? "";
            var stops = Parse(stored);
            return stops.Length >= 2 ? stops : Parse(string.Join(',', DefaultHex(mode)));
        }

        public static void SetPalette(AuraMode mode, Color[] stops) =>
            AppConfig.Set("zb_pal_" + (int)mode, string.Join(',', stops.Select(c => $"{c.R:X2}{c.G:X2}{c.B:X2}")));

        public static void ResetPalette(AuraMode mode) => AppConfig.Set("zb_pal_" + (int)mode, "");

        static Color[] Parse(string csv)
        {
            var list = new List<Color>();
            foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (part.Length == 6 && int.TryParse(part, System.Globalization.NumberStyles.HexNumber, null, out int rgb))
                    list.Add(Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF));
            }
            return list.Take(MAX_STOPS).ToArray();
        }

        public static Color[] FromHex(string[] hex) => Parse(string.Join(',', hex));

        /// <summary>Speed step 1 (slow) to 3 (fast); 2 by default.</summary>
        public static int GetSpeed(AuraMode mode) => Math.Clamp(AppConfig.Get("zb_speed_" + (int)mode, 2), 1, 3);
        public static void SetSpeed(AuraMode mode, int step) => AppConfig.Set("zb_speed_" + (int)mode, Math.Clamp(step, 1, 3));
        public static double SpeedFactor(AuraMode mode) => GetSpeed(mode) switch { 1 => 0.6, 3 => 1.7, _ => 1.0 };

        /// <summary>Linear RGB interpolation along the stops, t in 0..1.</summary>
        public static Color Sample(Color[] stops, double t)
        {
            if (stops.Length == 0) return Color.White;
            if (stops.Length == 1) return stops[0];
            t = Math.Clamp(t, 0, 1) * (stops.Length - 1);
            int i = Math.Min((int)t, stops.Length - 2);
            double f = t - i;
            Color a = stops[i], b = stops[i + 1];
            return Color.FromArgb((int)(a.R + (b.R - a.R) * f), (int)(a.G + (b.G - a.G) * f), (int)(a.B + (b.B - a.B) * f));
        }

        /// <summary>Resamples the stops into <paramref name="count"/> evenly spaced colours.</summary>
        public static Color[] Expand(Color[] stops, int count)
        {
            var result = new Color[count];
            for (int i = 0; i < count; i++) result[i] = Sample(stops, count == 1 ? 0 : (double)i / (count - 1));
            return result;
        }

        // ---- hidden modes

        public static bool IsHidden(AuraMode mode) => HiddenSet().Contains((int)mode);

        public static void SetHidden(AuraMode mode, bool hidden)
        {
            var set = HiddenSet();
            if (hidden) set.Add((int)mode); else set.Remove((int)mode);
            AppConfig.Set("zb_hidden_modes", string.Join(',', set));
        }

        static HashSet<int> HiddenSet()
        {
            string s = AppConfig.GetString("zb_hidden_modes", "") ?? "";
            var set = new HashSet<int>();
            foreach (var p in s.Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(p, out int v)) set.Add(v);
            return set;
        }

        /// <summary>The modes for the keyboard dropdown: everything not hidden, plus the active mode.</summary>
        public static Dictionary<AuraMode, string> VisibleModes(Dictionary<AuraMode, string> all, AuraMode active)
        {
            var hidden = HiddenSet();
            return all.Where(kv => kv.Key == active || !hidden.Contains((int)kv.Key))
                      .ToDictionary(kv => kv.Key, kv => kv.Value);
        }
    }

    /// <summary>
    /// Original software-rendered presets for the ZenBook Pro 16X OLED. Each one is a
    /// <see cref="PerKeyEffect"/> that draws onto the confirmed 17-column LED matrix
    /// (see <see cref="Zenbook16X"/>), the two lightbars and the lid logo.
    ///
    /// Each has its own palette and speed, kept in <see cref="Zenbook16XSettings"/> and edited in
    /// the Lighting Studio - they deliberately do not borrow the Aura colour pickers.
    /// </summary>
    public static class Zenbook16XEffects
    {
        /// <summary>
        /// Rainbow and Colour Cycle run in the keyboard firmware by default (they keep going when G-Helper is closed, but only dim in the
        /// four hardware steps). kb_software_effects = 1 renders them here instead so fine brightness dims them (Extra settings option).
        /// </summary>
        public static bool SoftwareHueEffects => AppConfig.Is("kb_software_effects");

        static bool IsHueEffect(AuraMode mode) => mode is AuraMode.AuraRainbow or AuraMode.AuraColorCycle;

        public static bool IsPreset(AuraMode mode) => mode switch
        {
            AuraMode.RAIN_COLOR or AuraMode.CUSTOM_PERKEY => true,
            AuraMode.AURORA or AuraMode.LAVA or AuraMode.SONAR
                or AuraMode.FIREFLIES or AuraMode.EMBER => true,
            AuraMode.AuraRainbow or AuraMode.AuraColorCycle => SoftwareHueEffects,
            _ => false,
        };

        public static PerKeyEffect? Create(AuraMode mode)
        {
            if (IsHueEffect(mode))
            {
                if (!SoftwareHueEffects) return null;
                double hueSpeed = HueEffect.SpeedFactor(Aura.Speed);
                return mode == AuraMode.AuraRainbow ? new RainbowEffect(hueSpeed) : new ColorCycleEffect(hueSpeed);
            }

            if (!Zenbook16XSettings.HasPalette(mode)) return null;

            var palette = Zenbook16XSettings.GetPalette(mode);
            double s = Zenbook16XSettings.SpeedFactor(mode);

            return mode switch
            {
                AuraMode.AURORA => new AuroraEffect(palette, s),
                AuraMode.LAVA => new LavaEffect(palette, s),
                AuraMode.SONAR => new SonarEffect(palette, s),
                AuraMode.FIREFLIES => new FirefliesEffect(palette, s),
                AuraMode.EMBER => new EmberEffect(palette, s),
                AuraMode.RAIN_COLOR => new RainEffect(Zenbook16XSettings.Expand(palette, 8),
                    6.0 * s, 3.0 * s, 5.5 * s),
                _ => null,
            };
        }

        public static string Name(AuraMode mode) => mode switch
        {
            AuraMode.RAIN_COLOR => "Colour Rain",
            AuraMode.AURORA => "Aurora",
            AuraMode.LAVA => "Lava Lamp",
            AuraMode.SONAR => "Sonar",
            AuraMode.FIREFLIES => "Fireflies",
            AuraMode.EMBER => "Ember (Typing)",
            _ => mode.ToString(),
        };

        public static readonly AuraMode[] All =
        {
            AuraMode.AURORA, AuraMode.LAVA, AuraMode.SONAR, AuraMode.FIREFLIES, AuraMode.EMBER,
        };
    }

    /// <summary>Shared helpers: matrix geometry and palette sampling.</summary>
    public abstract class MatrixEffect : PerKeyEffect
    {
        /// Matrix size in keys. Row 0 is the Esc/F-key row.
        protected const int W = Zenbook16X.COLS;
        protected const int H = Zenbook16X.ROW_BOTTOM - Zenbook16X.ROW_TOP + 1;

        protected readonly Color[] palette;
        protected readonly double speed;

        protected MatrixEffect(Color[] palette, double speed)
        {
            this.palette = palette.Length > 0 ? palette : new[] { Color.White };
            this.speed = speed;
        }

        protected static int SlotAt(int x, int y) => Zenbook16X.Slot(y + Zenbook16X.ROW_TOP, x);

        /// <summary>Smoothly samples the palette, t in 0..1.</summary>
        protected Color Pal(double t) => Zenbook16XSettings.Sample(palette, t);

        protected static Color Lerp(Color a, Color b, double t) => Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));

        protected static Color Scale(Color c, double k)
        {
            k = Math.Clamp(k, 0, 1);
            return Color.FromArgb((int)(c.R * k), (int)(c.G * k), (int)(c.B * k));
        }

        protected static double Smooth(double e0, double e1, double x)
        {
            double t = Math.Clamp((x - e0) / (e1 - e0), 0, 1);
            return t * t * (3 - 2 * t);
        }

        protected static void Bars(Color[] frame, Color left, Color right)
        {
            Zenbook16X.Blend(frame, Zenbook16X.SLOT_LIGHTBAR_LEFT, left);
            Zenbook16X.Blend(frame, Zenbook16X.SLOT_LIGHTBAR_RIGHT, right);
        }
    }

    /// <summary>
    /// Slow curtains of colour drifting across the keys, like an aurora: three interfering
    /// waves pick the hue, and a travelling band of light picks where it shows.
    /// </summary>
    public class AuroraEffect : MatrixEffect
    {
        public AuroraEffect(Color[] palette, double speed) : base(palette, speed) { }

        double Hue(double x, double y, double t)
        {
            double v = Math.Sin(x * 0.33 + t * 0.55)
                     + Math.Sin(y * 0.85 - t * 0.40)
                     + Math.Sin((x + y * 1.5) * 0.22 + t * 0.30);
            return (v + 3) / 6;
        }

        double Curtain(double x, double y, double t)
        {
            double wobble = Math.Sin(t * 0.35 + y * 0.9) * 2.0;
            double band = 0.5 + 0.5 * Math.Sin((x + wobble) * 0.45 - t * 0.75);
            return 0.12 + 0.88 * Math.Pow(band, 1.6);
        }

        public override void Render(Color[] frame, double time, double dt)
        {
            double t = time * speed;

            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    frame[SlotAt(x, y)] = Scale(Pal(Hue(x, y, t)), Curtain(x, y, t));

            // The bars sit just off either edge of the matrix.
            Bars(frame,
                Scale(Pal(Hue(-2, H / 2.0, t)), Curtain(-2, H / 2.0, t)),
                Scale(Pal(Hue(W + 1, H / 2.0, t)), Curtain(W + 1, H / 2.0, t)));

            frame[Zenbook16X.SLOT_LOGO] = Scale(Pal(Hue(W / 2.0, -1, t)), 0.7);
        }
    }

    /// <summary>
    /// A lava lamp: soft metaballs wander the keyboard, merging and splitting. The colour of
    /// each pixel is blended from whichever blobs are closest.
    /// </summary>
    public class LavaEffect : MatrixEffect
    {
        const int BLOBS = 4;
        readonly double[] phaseX = new double[BLOBS];
        readonly double[] phaseY = new double[BLOBS];
        readonly double[] rateX = new double[BLOBS];
        readonly double[] rateY = new double[BLOBS];
        readonly double[] radius = new double[BLOBS];

        public LavaEffect(Color[] palette, double speed) : base(palette, speed)
        {
            var rng = new Random();
            for (int i = 0; i < BLOBS; i++)
            {
                phaseX[i] = rng.NextDouble() * Math.PI * 2;
                phaseY[i] = rng.NextDouble() * Math.PI * 2;
                rateX[i] = 0.18 + rng.NextDouble() * 0.22;
                rateY[i] = 0.25 + rng.NextDouble() * 0.30;
                radius[i] = 2.0 + rng.NextDouble() * 1.4;
            }
        }

        (double x, double y) BlobPos(int i, double t) => (
            (W - 1) / 2.0 + Math.Sin(t * rateX[i] + phaseX[i]) * (W / 2.0 + 1),
            (H - 1) / 2.0 + Math.Sin(t * rateY[i] + phaseY[i]) * (H / 2.0 + 0.6));

        Color Sample(double px, double py, double t)
        {
            double field = 0, hue = 0;
            for (int i = 0; i < BLOBS; i++)
            {
                var (bx, by) = BlobPos(i, t);
                double dx = px - bx, dy = (py - by) * 1.6; // rows are tight, stretch them
                double f = radius[i] * radius[i] / (dx * dx + dy * dy + 0.3);
                field += f;
                hue += f * i / (BLOBS - 1);
            }

            double glow = Smooth(0.55, 1.5, field);
            double core = Smooth(1.6, 3.2, field); // hot centres go pale
            Color c = Lerp(Pal(hue / Math.Max(field, 1e-6)), Color.White, core * 0.55);
            return Scale(c, glow);
        }

        public override void Render(Color[] frame, double time, double dt)
        {
            double t = time * speed * 2.2;

            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    frame[SlotAt(x, y)] = Sample(x, y, t);

            Bars(frame, Sample(-2, H / 2.0, t), Sample(W + 1, H / 2.0, t));
            frame[Zenbook16X.SLOT_LOGO] = Sample(W / 2.0, -1.5, t);
        }
    }

    /// <summary>
    /// Sonar pings: a ring expands from the middle of the keyboard (the lid logo blinks as it
    /// leaves), and the lightbars flash when it reaches the edges.
    /// </summary>
    public class SonarEffect : MatrixEffect
    {
        struct Ping { public double Age; public Color Color; public bool Hit; }

        readonly List<Ping> pings = new();
        readonly Random rng = new();
        double untilNext;
        double barLeft, barRight;
        double logo;
        Color logoColor = Color.White;

        const double CX = (W - 1) / 2.0, CY = (H - 1) / 2.0;

        public SonarEffect(Color[] palette, double speed) : base(palette, speed) { }

        public override void Reset()
        {
            pings.Clear();
            untilNext = 0;
            barLeft = barRight = logo = 0;
        }

        public override void Render(Color[] frame, double time, double dt)
        {
            untilNext -= dt;
            if (untilNext <= 0)
            {
                untilNext = (1.5 + rng.NextDouble() * 0.8) / speed;
                Color col = Pal(rng.NextDouble());
                pings.Add(new Ping { Color = col });
                logo = 1;
                logoColor = col;
            }

            double rate = 7.5 * speed;
            for (int i = pings.Count - 1; i >= 0; i--)
            {
                Ping p = pings[i];
                p.Age += dt;
                double r = p.Age * rate;

                if (r > W * 0.65 && !p.Hit)
                {
                    p.Hit = true;
                    barLeft = barRight = 1;
                }
                if (r > W + 3) { pings.RemoveAt(i); continue; }
                pings[i] = p;

                double life = 1 - r / (W + 3);
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        double d = Math.Sqrt((x - CX) * (x - CX) + ((y - CY) * 1.7) * ((y - CY) * 1.7));
                        double ring = Math.Exp(-Math.Pow(d - r, 2) / 1.3);
                        if (ring < 0.02) continue;
                        // A pale leading edge over a coloured wake.
                        Color c = Lerp(p.Color, Color.White, ring * 0.45);
                        Zenbook16X.Blend(frame, SlotAt(x, y), c, ring * life);
                    }
            }

            barLeft = Math.Max(0, barLeft - dt * 1.6);
            barRight = Math.Max(0, barRight - dt * 1.6);
            logo = Math.Max(0, logo - dt * 2.5);

            Color barColor = pings.Count > 0 ? pings[^1].Color : Pal(0.5);
            Bars(frame, Scale(barColor, barLeft), Scale(barColor, barRight));
            frame[Zenbook16X.SLOT_LOGO] = Scale(logoColor, logo);
        }
    }

    /// <summary>
    /// Fireflies: a handful of soft lights wander the keys, each pulsing on its own rhythm and
    /// splashing a little light onto the neighbouring keys.
    /// </summary>
    public class FirefliesEffect : MatrixEffect
    {
        class Fly
        {
            public double X, Y, Heading, Phase, Rate, T;
            public Color Color;
        }

        const int COUNT = 14;
        readonly List<Fly> flies = new();
        readonly Random rng = new();

        public FirefliesEffect(Color[] palette, double speed) : base(palette, speed) { }

        Fly Spawn(Fly? f = null)
        {
            f ??= new Fly();
            f.X = rng.NextDouble() * (W - 1);
            f.Y = rng.NextDouble() * (H - 1);
            f.Heading = rng.NextDouble() * Math.PI * 2;
            f.Phase = rng.NextDouble() * Math.PI * 2;
            f.Rate = 0.9 + rng.NextDouble() * 1.4;
            f.Color = Pal(rng.NextDouble());
            return f;
        }

        public override void Reset()
        {
            flies.Clear();
            for (int i = 0; i < COUNT; i++) flies.Add(Spawn());
        }

        public override void Render(Color[] frame, double time, double dt)
        {
            double barL = 0, barR = 0;

            foreach (var f in flies)
            {
                f.Heading += (rng.NextDouble() - 0.5) * 3.0 * dt;
                f.X += Math.Cos(f.Heading) * 1.1 * speed * dt;
                f.Y += Math.Sin(f.Heading) * 0.5 * speed * dt;
                f.Phase += f.Rate * speed * dt;

                if (f.X < -1 || f.X > W || f.Y < -1 || f.Y > H) { Spawn(f); continue; }

                // Mostly dark, a slow bright swell: sin^3 on the positive half only.
                double glow = Math.Max(0, Math.Sin(f.Phase));
                glow = glow * glow * glow;
                if (glow < 0.01) continue;

                // Splat across the four nearest keys.
                int x0 = (int)Math.Floor(f.X), y0 = (int)Math.Floor(f.Y);
                double fx = f.X - x0, fy = f.Y - y0;
                for (int dy = 0; dy <= 1; dy++)
                    for (int dx = 0; dx <= 1; dx++)
                    {
                        int x = x0 + dx, y = y0 + dy;
                        if (x < 0 || x >= W || y < 0 || y >= H) continue;
                        double w = (dx == 0 ? 1 - fx : fx) * (dy == 0 ? 1 - fy : fy);
                        Zenbook16X.Blend(frame, SlotAt(x, y), f.Color, glow * (0.25 + 0.75 * w));
                    }

                if (f.X < 3) barL = Math.Max(barL, glow * (1 - f.X / 3));
                if (f.X > W - 4) barR = Math.Max(barR, glow * (1 - (W - 1 - f.X) / 3));
            }

            // A faint dusk glow so the keyboard never goes fully black.
            double dusk = 0.04 + 0.03 * Math.Sin(time * 0.5 * speed);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    Zenbook16X.Blend(frame, SlotAt(x, y), Pal(0.5), dusk);

            Bars(frame, Scale(Pal(0.2), barL + 0.03), Scale(Pal(0.8), barR + 0.03));
            frame[Zenbook16X.SLOT_LOGO] = Scale(Pal(0.5), 0.2 + 0.2 * Math.Sin(time * 0.7 * speed));
        }
    }

    /// <summary>
    /// Typing reactive. Each key you press flares white-hot, then cools through the palette as
    /// the heat bleeds to neighbouring keys and fades - so a burst of typing leaves a glowing,
    /// slowly-dimming trail. The lightbars glow with how much you're typing.
    ///
    /// Key state is polled with GetAsyncKeyState; nothing is hooked, stored or recorded - only
    /// "is this key down right now" is read, to light the matching LED.
    /// </summary>
    public class EmberEffect : MatrixEffect
    {
        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vKey);

        // Rows are y 0..6 (row 6 holds the arrow cluster's lower keys), columns 0..16.
        const int ROWS = H + 1;
        readonly double[,] heat = new double[ROWS, W];
        readonly double[,] next = new double[ROWS, W];
        // Heat-weighted palette position per key, so each press keeps its own colour as it spreads.
        readonly double[,] tint = new double[ROWS, W];
        readonly double[,] nextTint = new double[ROWS, W];
        readonly Random rng = new();
        double lastHue = 0.5;
        readonly bool[] down = new bool[256];
        double bars;

        /// <summary>
        /// Virtual key -> (y, x). Positions are approximate for the wide keys (Enter, shifts,
        /// space): one representative LED is enough, the diffusion spreads it.
        /// </summary>
        static readonly Dictionary<int, (int y, int x)> KeyMap = BuildKeyMap();

        public EmberEffect(Color[] palette, double speed) : base(palette, speed) { }

        static Dictionary<int, (int, int)> BuildKeyMap()
        {
            var m = new Dictionary<int, (int, int)>();

            m[0x1B] = (0, 0);                                             // Esc
            for (int i = 0; i < 12; i++) m[0x70 + i] = (0, 1 + i);        // F1-F12
            m[0x2C] = (0, 13); m[0x2D] = (0, 14); m[0x2E] = (0, 15);      // PrtSc Ins Del

            m[0xDF] = (1, 0);                                             // ` (UK)
            for (int i = 1; i <= 9; i++) m[0x30 + i] = (1, i);            // 1-9
            m[0x30] = (1, 10);                                            // 0
            m[0xBD] = (1, 11); m[0xBB] = (1, 12);                         // - =
            m[0x08] = (1, 14);                                            // Backspace
            m[0x24] = (1, 16);                                            // Home

            m[0x09] = (2, 0);                                             // Tab
            int[] qRow = { 'Q', 'W', 'E', 'R', 'T', 'Y', 'U', 'I', 'O', 'P' };
            for (int i = 0; i < qRow.Length; i++) m[qRow[i]] = (2, 1 + i);
            m[0xDB] = (2, 11); m[0xDD] = (2, 12); m[0xDE] = (2, 13);      // [ ] #
            m[0x21] = (2, 16);                                            // PgUp

            m[0x14] = (3, 0);                                             // Caps
            int[] aRow = { 'A', 'S', 'D', 'F', 'G', 'H', 'J', 'K', 'L' };
            for (int i = 0; i < aRow.Length; i++) m[aRow[i]] = (3, 1 + i);
            m[0xBA] = (3, 10); m[0xC0] = (3, 11);                        // ; ' (UK)
            m[0x0D] = (3, 14);                                            // Enter
            m[0x22] = (3, 16);                                            // PgDn

            m[0xA0] = (4, 0); m[0xE2] = (4, 1);                           // LShift \
            int[] zRow = { 'Z', 'X', 'C', 'V', 'B', 'N', 'M' };
            for (int i = 0; i < zRow.Length; i++) m[zRow[i]] = (4, 2 + i);
            m[0xBC] = (4, 9); m[0xBE] = (4, 10); m[0xBF] = (4, 11);       // , . /
            m[0xA1] = (4, 14);                                            // RShift
            m[0x23] = (4, 16);                                            // End

            m[0xA2] = (5, 0); m[0x5B] = (5, 2); m[0xA4] = (5, 3);         // LCtrl Win LAlt
            m[0x20] = (5, 6);                                             // Space
            m[0xA5] = (5, 9); m[0xA3] = (5, 11);                          // RAlt RCtrl
            m[0x26] = (5, 13);                                            // Up
            m[0x25] = (6, 12); m[0x28] = (6, 13); m[0x27] = (6, 14);      // Left Down Right

            return m;
        }

        public override void Reset()
        {
            Array.Clear(heat);
            Array.Clear(tint);
            Array.Clear(down);
            bars = 0;
        }

        static int SlotAtRow(int y, int x) => Zenbook16X.Slot(y + Zenbook16X.ROW_TOP, x);

        public override void Render(Color[] frame, double time, double dt)
        {
            double total = 0;

            // Ignite newly pressed keys.
            foreach (var (vk, pos) in KeyMap)
            {
                bool isDown = (GetAsyncKeyState(vk) & 0x8000) != 0;
                if (isDown && !down[vk & 0xFF])
                {
                    lastHue = rng.NextDouble();
                    heat[pos.y, pos.x] = 1.0;
                    tint[pos.y, pos.x] = lastHue;
                    bars = Math.Min(1.0, bars + 0.25);
                }
                down[vk & 0xFF] = isDown;
            }

            // Diffuse to neighbours, then cool.
            double k = Math.Min(0.2, 5.0 * dt * speed);
            double cool = Math.Exp(-dt * 1.35 * speed);
            for (int y = 0; y < ROWS; y++)
                for (int x = 0; x < W; x++)
                {
                    double h = heat[y, x];
                    double sum =
                        (x > 0 ? heat[y, x - 1] : h) + (x < W - 1 ? heat[y, x + 1] : h) +
                        (y > 0 ? heat[y - 1, x] : h) + (y < ROWS - 1 ? heat[y + 1, x] : h);
                    next[y, x] = Math.Max(0, (h + k * (sum - 4 * h)) * cool);
                    double t0 = tint[y, x];
                    double tsum =
                        (x > 0 ? tint[y, x - 1] : t0) + (x < W - 1 ? tint[y, x + 1] : t0) +
                        (y > 0 ? tint[y - 1, x] : t0) + (y < ROWS - 1 ? tint[y + 1, x] : t0);
                    nextTint[y, x] = Math.Max(0, (t0 + k * (tsum - 4 * t0)) * cool);
                }

            for (int y = 0; y < ROWS; y++)
                for (int x = 0; x < W; x++)
                {
                    heat[y, x] = next[y, x];
                    tint[y, x] = nextTint[y, x];
                    total += heat[y, x];

                    // Row 6 only has the three arrow keys; the rest isn't a key.
                    if (y == ROWS - 1 && (x < 12 || x > 14)) continue;

                    frame[SlotAtRow(y, x)] = Ramp(heat[y, x], heat[y, x] > 1e-4 ? tint[y, x] / heat[y, x] : lastHue);
                }

            // Idle: a barely-there breathing so the board isn't dead before you type.
            double idle = 0.05 + 0.03 * Math.Sin(time * 0.9);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    Zenbook16X.Blend(frame, SlotAt(x, y), Pal(0.0), idle);

            bars = Math.Max(0, bars - dt * 0.9);
            Color barColor = Ramp(0.35 + bars * 0.65, lastHue);
            Bars(frame, Scale(barColor, 0.12 + bars), Scale(barColor, 0.12 + bars));
            frame[Zenbook16X.SLOT_LOGO] = Scale(Ramp(0.4 + Math.Min(total / 6, 0.6), lastHue), 0.25 + Math.Min(total / 8, 0.75));
        }

        /// <summary>Heat -> colour: white-hot at 1, settling into the key's own colour, then drifting along the palette as it dies.</summary>
        Color Ramp(double h, double hue)
        {
            h = Math.Clamp(h, 0, 1);
            Color main = Pal(hue);
            Color tail = Pal(hue > 0.7 ? hue - 0.3 : hue + 0.3);
            if (h > 0.72) return Lerp(main, Color.White, (h - 0.72) / 0.28);
            if (h > 0.30) return Lerp(tail, main, (h - 0.30) / 0.42);
            return Scale(tail, h / 0.30);
        }
    }

    /// <summary>
    /// Software versions of the firmware Rainbow and Colour Cycle. The firmware effects pick their own colours, so the fine
    /// brightness gain (applied to every streamed colour) could not dim them; rendered here they dim like everything else.
    /// Speed follows the keyboard's Slow / Normal / Fast setting. Used only when kb_software_effects = 1 (off by default).
    /// </summary>
    public abstract class HueEffect : MatrixEffect
    {
        protected HueEffect(double speed) : base(new[] { Color.White }, speed) { }

        public static double SpeedFactor(AuraSpeed speed) => speed switch
        {
            AuraSpeed.Slow => 0.5,
            AuraSpeed.Fast => 2.0,
            _ => 1.0,
        };

        /// <summary>Fully saturated hue, h in turns (0..1 wraps).</summary>
        protected static Color Hue(double h)
        {
            h = (h % 1 + 1) % 1 * 6;
            int i = (int)h;
            double f = h - i;
            int up = (int)(255 * f), down = (int)(255 * (1 - f));
            return i switch
            {
                0 => Color.FromArgb(255, up, 0),
                1 => Color.FromArgb(down, 255, 0),
                2 => Color.FromArgb(0, 255, up),
                3 => Color.FromArgb(0, down, 255),
                4 => Color.FromArgb(up, 0, 255),
                _ => Color.FromArgb(255, 0, down),
            };
        }
    }

    /// <summary>A rainbow travelling across the keyboard, with a slight lean so rows don't move in lockstep.</summary>
    public class RainbowEffect : HueEffect
    {
        const double CyclesPerSecond = 0.12;   // one full hue turn every ~8 s at Normal
        const double Spread = 0.85;            // how much of the rainbow is on the keyboard at once

        public RainbowEffect(double speed) : base(speed) { }

        Color At(double x, double y, double t) => Hue((x + y * 0.35) / W * Spread - t);

        public override void Render(Color[] frame, double time, double dt)
        {
            double t = time * CyclesPerSecond * speed;

            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    frame[SlotAt(x, y)] = At(x, y, t);

            Bars(frame, At(-1, H / 2.0, t), At(W, H / 2.0, t));
            frame[Zenbook16X.SLOT_LOGO] = At(W / 2.0, -1, t);
        }
    }

    /// <summary>The whole keyboard, bars and logo stepping smoothly through the hues together.</summary>
    public class ColorCycleEffect : HueEffect
    {
        const double CyclesPerSecond = 0.08;   // one full turn every ~12 s at Normal

        public ColorCycleEffect(double speed) : base(speed) { }

        public override void Render(Color[] frame, double time, double dt)
        {
            Color c = Hue(time * CyclesPerSecond * speed);

            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    frame[SlotAt(x, y)] = c;

            Bars(frame, c, c);
            frame[Zenbook16X.SLOT_LOGO] = c;
        }
    }
}
