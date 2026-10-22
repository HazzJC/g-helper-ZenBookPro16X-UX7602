using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace GHelper.Helpers
{

    static class Drawing
    {

        public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            Size size = new Size(diameter, diameter);
            Rectangle arc = new Rectangle(bounds.Location, size);
            GraphicsPath path = new GraphicsPath();

            if (radius == 0)
            {
                path.AddRectangle(bounds);
                return path;
            }

            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle bounds, int cornerRadius)
        {
            using (GraphicsPath path = RoundedRect(bounds, cornerRadius))
            {
                graphics.FillPath(brush, path);
            }
        }
    }

    public enum ToastIcon
    {
        BrightnessUp,
        BrightnessDown,
        BacklightUp,
        BacklightDown,
        Touchpad,
        Microphone,
        MicrophoneMute,
        FnLock,
        Battery,
        Charger,
        Controller,
        Volume,
        Media,
        Scroll,
        Performance,
        Lighting
    }

    public class ToastForm : OSDNativeForm
    {

        protected static string toastText = "Balanced";
        protected static ToastIcon? toastIcon = null;

        protected static System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();

        public ToastForm()
        {
            timer.Tick += timer_Tick;
            timer.Enabled = false;
            timer.Interval = 2000;
        }

        private static readonly Font _toastFont = new Font("Segoe UI", 36f, FontStyle.Bold, GraphicsUnit.Pixel);
        private static readonly SolidBrush _toastBrush = new SolidBrush(Color.FromArgb(150, Color.Black));
        private static readonly SolidBrush _toastTextBrush = new SolidBrush(Color.White);
        private static readonly StringFormat _toastFormat = new StringFormat
        {
            LineAlignment = StringAlignment.Center,
            Alignment = StringAlignment.Center
        };

        protected override void PerformPaint(PaintEventArgs e)
        {
            // the window is sized in physical pixels (see RunToast); draw in 96-dpi units scaled up
            float scale = ToastScale;
            e.Graphics.ScaleTransform(scale, scale);
            DrawToast(e.Graphics, new Rectangle(0, 0, (int)(Bound.Width / scale), (int)(Bound.Height / scale)), toastText, toastIcon);
        }

        // font-based icons for toasts without a bitmap (crisp at any scale); Segoe Fluent Icons on Windows 11, MDL2 before that
        static readonly Dictionary<ToastIcon, string> glyphIcons = new()
        {
            [ToastIcon.Volume] = "\uE767",
            [ToastIcon.Media] = "\uE893",
            [ToastIcon.Scroll] = "\uE8CB",
            [ToastIcon.Performance] = "\uE945",
            [ToastIcon.Lighting] = "\uE790",
        };

        static readonly string glyphFont = FontFamily.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

        static readonly Bitmap measureBitmap = new Bitmap(1, 1);

        // width follows the measured text (plus the icon when there is one); 300 is the old minimum
        public static Size ToastSize(string text, bool hasIcon = false)
        {
            using var g = Graphics.FromImage(measureBitmap);
            int textWidth = (int)Math.Ceiling(g.MeasureString(text, _toastFont, 4000, _toastFormat).Width);
            return new Size(Math.Max(300, textWidth + (hasIcon ? 18 + 64 + 16 + 36 : 80)), 100);
        }

        protected static float ToastScale = 1f;

        /// <summary>Draws a toast icon (bitmap, or a font glyph for the newer ones) into the box. Returns false if there is none.</summary>
        public static bool DrawIcon(Graphics g, ToastIcon? toastIcon, RectangleF box)
        {
            using Bitmap? icon = toastIcon switch
            {
                ToastIcon.BrightnessUp   => Properties.Resources.brightness_up,
                ToastIcon.BrightnessDown => Properties.Resources.brightness_down,
                ToastIcon.BacklightUp    => Properties.Resources.backlight_up,
                ToastIcon.BacklightDown  => Properties.Resources.backlight_down,
                ToastIcon.Microphone     => Properties.Resources.icons8_microphone_96,
                ToastIcon.MicrophoneMute => Properties.Resources.icons8_mute_unmute_96,
                ToastIcon.Touchpad       => Properties.Resources.icons8_touchpad_96,
                ToastIcon.FnLock         => Properties.Resources.icons8_function,
                ToastIcon.Battery        => Properties.Resources.icons8_charged_battery_96,
                ToastIcon.Charger        => Properties.Resources.icons8_charging_battery_96,
                ToastIcon.Controller     => Properties.Resources.icons8_controller_96,
                _                        => null
            };

            if (icon is not null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(icon, box);
                return true;
            }

            if (toastIcon is not null && glyphIcons.TryGetValue(toastIcon.Value, out var glyph))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                using var font = new Font(glyphFont, box.Height * 0.9f, FontStyle.Regular, GraphicsUnit.Pixel);
                g.DrawString(glyph, font, _toastTextBrush, box, _toastFormat);
                return true;
            }

            return false;
        }

        public static void DrawToast(Graphics g, Rectangle bound, string text, ToastIcon? toastIcon)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.FillRoundedRectangle(_toastBrush, bound, 10);

            bool hasIcon = DrawIcon(g, toastIcon, new RectangleF(18, 18, 64, 64));

            // text is centred in the space right of the icon (or in the whole toast without one)
            float textCentre = hasIcon ? (18 + 64 + 16 + bound.Width - 20) / 2f : bound.Width / 2f;
            g.DrawString(text, _toastFont, _toastTextBrush, new PointF(textCentre, bound.Height / 2f), _toastFormat);
        }

        public static void ReadText(string text)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = "PowerShell.exe";
            startInfo.Arguments = $"-Command \"Add-Type -AssemblyName System.Speech; (New-Object System.Speech.Synthesis.SpeechSynthesizer).Speak('{text}')\"";
            startInfo.CreateNoWindow = true;
            Process.Start(startInfo);
        }


        /// <summary>Toasts are skipped until this time (Environment.TickCount64): the dial's ring is showing the same change.</summary>
        public static long SuppressUntil;

        public void RunToast(string text, ToastIcon? icon = null)
        {
            if (Environment.TickCount64 < SuppressUntil) return;

            if (AppConfig.Is("disable_osd")) return;

            Program.settingsForm.Invoke(delegate
            {
                //Hide();
                timer.Stop();

                toastText = text;
                toastIcon = icon;

                nint dpiContext = User32.SetThreadDpiAwarenessContext(User32.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

                Screen screen1 = Screen.FromHandle(Handle);

                ToastScale = Math.Max(1f, ControlHelper.GetDpiScale(Program.settingsForm).Value);
                var toastSize = ToastSize(toastText, icon is not null);
                Width = (int)(toastSize.Width * ToastScale);
                Height = (int)(toastSize.Height * ToastScale);
                X = (screen1.Bounds.Width - Width) / 2;
                Y = screen1.Bounds.Height - (int)(300 * ToastScale) - Height;

                Show();
                User32.SetThreadDpiAwarenessContext(dpiContext);

                timer.Start();

                //if (AppConfig.Is("narrator")) ReadText(text);

                Program.settingsForm.AccessibilityObject.RaiseAutomationNotification(
                    System.Windows.Forms.Automation.AutomationNotificationKind.ActionCompleted,
                    System.Windows.Forms.Automation.AutomationNotificationProcessing.MostRecent,
                    text);

            });

        }

        private void timer_Tick(object? sender, EventArgs e)
        {
            //Debug.WriteLine("Toast end");
            Hide();
            timer.Stop();
        }
    }
}
