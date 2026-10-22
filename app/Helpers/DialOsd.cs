using GHelper.Display;
using GHelper.Input;
using GHelper.Mode;
using GHelper.UI;
using GHelper.USB;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace GHelper.Helpers
{
    /// <summary>What the dial ring shows: a label, a big value, and either a 0-1 ratio (arc) or N segments with one lit.</summary>
    public record DialReading(string Label, string Value, double? Ratio, ToastIcon? Icon, int Segments = 0, int Index = -1);

    /// <summary>
    /// On-screen ring for the ASUS Dial (ZenBook Pro 16X): the current dial mode's icon inside a ring that shows its value (or its
    /// position among a few states), with the name and value beside it. Replaces the plain text toast for changes made with the dial
    /// in dial modes. Same layered-window approach and DPI scaling as <see cref="ToastForm"/>.
    /// </summary>
    public class DialOsd : OSDNativeForm
    {
        const int HEIGHT = 104, MIN_WIDTH = 300;   // 96-dpi units, scaled at show time
        const int HIDE_MS = 1300;

        static DialOsd? instance;
        static readonly System.Windows.Forms.Timer hideTimer = new() { Interval = HIDE_MS };

        DialReading reading = new("", "", null, null);
        WheelState? wheel;   // when set, the mode wheel is shown instead of the ring

        /// <summary>The hold-to-pick mode wheel: every enabled mode around a circle, the highlighted one lit.</summary>
        public record WheelState(IReadOnlyList<(ToastIcon? Icon, string Name)> Items, int Index);

        const int WHEEL = 190;            // wheel panel height (96-dpi units)
        const int WHEEL_SAFETY_MS = 6000; // the listener closes it; this only stops a stray wheel from lingering

        public static void ShowWheel(IReadOnlyList<DialMode> modes, int index)
        {
            var form = Program.settingsForm;
            if (form is null || form.IsDisposed || !form.IsHandleCreated) return;
            ToastForm.SuppressUntil = Environment.TickCount64 + 600;
            var state = new WheelState(modes.Select(m => (m.Icon, m.ToastName ?? m.Name)).ToList(), index);
            form.BeginInvoke(() =>
            {
                try
                {
                    instance ??= new DialOsd();
                    instance.DisplayWheel(state);
                }
                catch (Exception ex) { Logger.WriteLine("Dial wheel failed: " + ex.Message); }
            });
        }

        public static void HideNow()
        {
            var form = Program.settingsForm;
            if (form is null || form.IsDisposed || !form.IsHandleCreated) return;
            form.BeginInvoke(() => { hideTimer.Stop(); instance?.Hide(); });
        }
        float scale = 1f;

        static readonly Font labelFont = new("Segoe UI", 17f, FontStyle.Regular, GraphicsUnit.Pixel);
        static readonly Font valueFont = new("Segoe UI", 34f, FontStyle.Bold, GraphicsUnit.Pixel);
        static readonly Bitmap measureBitmap = new(1, 1);

        static DialOsd()
        {
            hideTimer.Tick += (_, _) => { hideTimer.Stop(); instance?.Hide(); };
        }

        public static bool Enabled => AppConfig.IsNotFalse("dial_osd") && !AppConfig.Is("disable_osd");

        /// <summary>
        /// Shows the ring for a dial mode after its action ran. direction is +1 / -1 for a turn (used for Tracks / Scroll wording), 0 for
        /// a mode switch. Safe to call from any thread.
        /// </summary>
        public static void Show(DialMode mode, int direction)
        {
            var form = Program.settingsForm;
            if (!Enabled || form is null || form.IsDisposed || !form.IsHandleCreated) return;

            ToastForm.SuppressUntil = Environment.TickCount64 + 600;   // the ring replaces the action's own toast

            // slow readings (audio, WMI) are taken here, off the UI thread; the lighting mode is read on the UI thread, after the
            // queued mode change has run there
            DialReading? early = mode.Id == "aura" ? null : Read(mode, direction);
            form.BeginInvoke(() =>
            {
                try
                {
                    instance ??= new DialOsd();
                    instance.Display(early ?? Read(mode, direction));
                }
                catch (Exception ex) { Logger.WriteLine("Dial ring failed: " + ex.Message); }
            });
        }

        public static DialReading Read(DialMode mode, int direction)
        {
            string label = mode.ToastName ?? mode.Name;
            switch (mode.Id)
            {
                case "volume":
                    {
                        int v = Audio.GetVolume();
                        return new(label, v < 0 ? "--" : v + "%", v < 0 ? null : v / 100.0, mode.Icon);
                    }
                case "brightness":
                    {
                        int b = -1;
                        try { b = ScreenBrightness.Get(); } catch { }
                        return new(label, b < 0 ? "--" : b + "%", b < 0 ? null : b / 100.0, mode.Icon);
                    }
                case "backlight":
                    {
                        int u = KeyboardBrightness.Unified;
                        return new(label, u == 0 ? "Off" : u + "%", u / 100.0, mode.Icon);
                    }
                case "performance":
                    {
                        var list = Modes.GetList();
                        int index = list.IndexOf(Modes.GetCurrent());
                        return new(label, Modes.GetCurrentName(), null, mode.Icon, list.Count, index);
                    }
                case "aura":
                    {
                        var all = Aura.GetModes();
                        var modes = AppConfig.IsZenbookPro16X() ? Zenbook16XSettings.VisibleModes(all, Aura.Mode) : all;
                        var keys = modes.Keys.ToList();
                        int index = keys.IndexOf(Aura.Mode);
                        string name = modes.TryGetValue(Aura.Mode, out var n) ? n : Aura.Mode.ToString();
                        // a handful of states read best as segments; a long list as a position along the ring
                        return keys.Count <= 10
                            ? new(label, name, null, mode.Icon, keys.Count, index)
                            : new(label, name, keys.Count > 1 && index >= 0 ? index / (double)(keys.Count - 1) : null, mode.Icon);
                    }
                case "media":
                    return new(label, direction > 0 ? "Next" : direction < 0 ? "Previous" : "Ready", null, mode.Icon);
                case "scroll":
                    return new(label, direction > 0 ? "Down" : direction < 0 ? "Up" : "Ready", null, mode.Icon);
                default:
                    return new(label, "", null, mode.Icon);
            }
        }

        void Place(int width, int height)
        {
            nint dpiContext = User32.SetThreadDpiAwarenessContext(User32.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
            Screen screen = Screen.FromPoint(Cursor.Position);
            scale = Math.Max(1f, ControlHelper.GetDpiScale(Program.settingsForm).Value);
            Width = (int)(width * scale);
            Height = (int)(height * scale);
            X = screen.Bounds.X + (screen.Bounds.Width - Width) / 2;
            Y = screen.Bounds.Y + screen.Bounds.Height - (int)(300 * scale) - Height;
            Show();
            Invalidate();   // repaint when it is already showing
            User32.SetThreadDpiAwarenessContext(dpiContext);
        }

        void DisplayWheel(WheelState s)
        {
            hideTimer.Stop();
            wheel = s;
            Place(MeasureWheelWidth(s), WHEEL);
            hideTimer.Interval = WHEEL_SAFETY_MS;
            hideTimer.Start();
        }

        void Display(DialReading r)
        {
            hideTimer.Stop();
            wheel = null;
            reading = r;
            Place(MeasureWidth(r), HEIGHT);
            hideTimer.Interval = HIDE_MS;
            hideTimer.Start();

            Program.settingsForm.AccessibilityObject.RaiseAutomationNotification(
                System.Windows.Forms.Automation.AutomationNotificationKind.ActionCompleted,
                System.Windows.Forms.Automation.AutomationNotificationProcessing.MostRecent,
                $"{r.Label} {r.Value}");
        }

        protected override void PerformPaint(PaintEventArgs e)
        {
            e.Graphics.ScaleTransform(scale, scale);
            if (wheel is not null) DrawWheel(e.Graphics, wheel, MeasureWheelWidth(wheel));
            else Draw(e.Graphics, reading, MeasureWidth(reading));
        }

        static readonly Font hintFont = new("Segoe UI", 14f, FontStyle.Regular, GraphicsUnit.Pixel);
        const string WHEEL_LABEL = "Choose a dial mode";
        const string WHEEL_HINT = "Turn to choose, press to pick";

        public static int MeasureWheelWidth(WheelState s)
        {
            using var g = Graphics.FromImage(measureBitmap);
            float text = new[]
            {
                g.MeasureString(WHEEL_LABEL, labelFont).Width,
                g.MeasureString(WHEEL_HINT, hintFont).Width,
                s.Items.Max(i => g.MeasureString(i.Name, valueFont).Width),   // widest name, so the panel doesn't jump while turning
            }.Max();
            return (int)Math.Ceiling(WHEEL + text + 24);
        }

        /// <summary>Draws the mode wheel in 96-dpi units (also used by the preview tool).</summary>
        public static void DrawWheel(Graphics g, WheelState s, int width)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;

            using (var back = new SolidBrush(Color.FromArgb(215, 20, 20, 20)))
                g.FillRoundedRectangle(back, new Rectangle(0, 0, width, WHEEL), 16);

            float cx = WHEEL / 2f, cy = WHEEL / 2f, radius = 64, bubble = 44;
            using (var track = new Pen(Color.FromArgb(60, 60, 60), 2)) g.DrawEllipse(track, cx - radius, cy - radius, radius * 2, radius * 2);

            int n = s.Items.Count;
            using var normal = new SolidBrush(Color.FromArgb(52, 52, 52));
            using var lit = new SolidBrush(RForm.colorStandard);
            for (int i = 0; i < n; i++)
            {
                double a = (-90 + i * 360.0 / n) * Math.PI / 180;
                float x = cx + (float)(Math.Cos(a) * radius), y = cy + (float)(Math.Sin(a) * radius);
                g.FillEllipse(i == s.Index ? lit : normal, x - bubble / 2, y - bubble / 2, bubble, bubble);
                ToastForm.DrawIcon(g, s.Items[i].Icon, new RectangleF(x - 13, y - 13, 26, 26));
            }

            using var labelBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
            using var hintBrush = new SolidBrush(Color.FromArgb(150, 150, 150));
            float tx = WHEEL - 2;
            g.DrawString(WHEEL_LABEL, labelFont, labelBrush, new PointF(tx + 2, 48));
            g.DrawString(s.Index >= 0 && s.Index < n ? s.Items[s.Index].Name : "", valueFont, Brushes.White, new PointF(tx, 70));
            g.DrawString(WHEEL_HINT, hintFont, hintBrush, new PointF(tx + 2, 120));
        }

        public static int MeasureWidth(DialReading r)
        {
            using var g = Graphics.FromImage(measureBitmap);
            float text = Math.Max(g.MeasureString(r.Label, labelFont).Width, g.MeasureString(r.Value, valueFont).Width);
            return Math.Max(MIN_WIDTH, (int)Math.Ceiling(108 + text + 24));
        }

        /// <summary>Draws the ring panel in 96-dpi units (also used by the preview tool).</summary>
        public static void Draw(Graphics g, DialReading r, int width)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;

            using (var back = new SolidBrush(Color.FromArgb(200, 20, 20, 20)))
                g.FillRoundedRectangle(back, new Rectangle(0, 0, width, HEIGHT), 14);

            const float stroke = 7f;
            var ring = new RectangleF(16 + stroke / 2, 14 + stroke / 2, 76 - stroke, 76 - stroke);
            var accent = RForm.colorStandard;

            using (var track = new Pen(Color.FromArgb(70, 70, 70), stroke))
            using (var lit = new Pen(accent, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                if (r.Segments > 1)
                {
                    float gap = r.Segments > 6 ? 6f : 10f;
                    float sweep = 360f / r.Segments;
                    for (int i = 0; i < r.Segments; i++)
                        g.DrawArc(i == r.Index ? lit : track, ring, -90 + i * sweep + gap / 2, sweep - gap);
                }
                else
                {
                    g.DrawEllipse(track, ring);
                    if (r.Ratio is double ratio && ratio > 0.001)
                        g.DrawArc(lit, ring, -90, (float)(360 * Math.Min(1, ratio)));
                }
            }

            ToastForm.DrawIcon(g, r.Icon, new RectangleF(ring.X + ring.Width / 2 - 17, ring.Y + ring.Height / 2 - 17, 34, 34));

            using var labelBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
            g.DrawString(r.Label, labelFont, labelBrush, new PointF(108, 16));
            g.DrawString(r.Value, valueFont, Brushes.White, new PointF(105, 38));
        }
    }
}
