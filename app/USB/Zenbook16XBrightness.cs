using GHelper.Helpers;
using System.Diagnostics;

namespace GHelper.USB
{
    /// <summary>
    /// Fine keyboard brightness for the ZenBook Pro 16X.
    ///
    /// The hardware level (feature report 0x5A, BA C5 C4 &lt;0-3&gt;) only has four steps, and anything above 3 turns the keyboard
    /// off. The colour value of every LED is a full 0-255 control that multiplies with it, so a smooth brightness is a gain applied
    /// to the colours on their way out (see Aura.ApplyZenbook16XDirect / SendZenbook16XHardwareMode and Zenbook16X.Stream).
    /// Measured on the keyboard: white 2 at hardware level 3 is visible and dim, white 1 is not visible at all.
    /// </summary>
    public static class KeyboardBrightness
    {
        /// <summary>
        /// Lowest output for a full-scale colour (what 1% shows). Measured on this keyboard: white 1 is invisible, white 2 is clearly
        /// visible and dim.
        /// </summary>
        public const int FloorValue = 2;
        public const int MinPercent = 1;

        /// <summary>Colours dimmer than this on input are left alone by the floor (effect fade tails, intentional near-black).</summary>
        const int FloorAppliesFrom = 24;

        const string KEY = "kb_fine";

        public static int Percent => Math.Clamp(AppConfig.Get(KEY, 100), MinPercent, 100);

        // Output value for a full-scale colour is  V(p) = FloorValue + (e^(k(p-1)) - 1) / k  : it starts at exactly one colour step per
        // percent (so neighbouring percentages never collapse to the same value, which a plain exponential does at the low end) and
        // bends exponentially upward so the top still feels even. k is solved so that V(100) = 255.
        static readonly double K = SolveK();

        static double SolveK()
        {
            double lo = 1e-6, hi = 0.5, target = 255.0 - FloorValue;
            for (int i = 0; i < 80; i++)
            {
                double k = (lo + hi) / 2;
                if ((Math.Exp(99 * k) - 1) / k > target) hi = k; else lo = k;
            }
            return (lo + hi) / 2;
        }

        /// <summary>Colour gain (0-1) for a percentage; 100% is exactly 1.</summary>
        public static double Gain(int percent)
        {
            if (percent >= 100) return 1.0;
            percent = Math.Max(percent, MinPercent);
            return (FloorValue + (Math.Exp(K * (percent - 1)) - 1) / K) / 255.0;
        }

        public static Color Apply(Color c) => Apply(c, Percent);

        public static Color Apply(Color c, int percent)
        {
            if (percent >= 100) return c;

            double gain = Gain(percent);
            double r = c.R * gain, g = c.G * gain, b = c.B * gain;

            // Never let a clearly lit colour fall below the visible floor: lift it, keeping its hue, rather than letting it vanish.
            int origMax = Math.Max(c.R, Math.Max(c.G, c.B));
            double max = Math.Max(r, Math.Max(g, b));
            if (origMax >= FloorAppliesFrom && max < FloorValue)
            {
                double lift = FloorValue / max;
                r *= lift; g *= lift; b *= lift;
            }

            return Color.FromArgb(c.A, (int)Math.Round(Math.Min(255, r)), (int)Math.Round(Math.Min(255, g)), (int)Math.Round(Math.Min(255, b)));
        }

        /// <summary>
        /// Firmware effects that pick their own colours (the colour in the mode-set packet is ignored) can only be dimmed by the
        /// four hardware steps.
        /// </summary>
        public static bool IsColourDriven(AuraMode mode) => mode is not (AuraMode.AuraColorCycle or AuraMode.AuraRainbow or AuraMode.Rain);

        static readonly object refreshGate = new();
        static bool refreshPending, refreshRunning;

        /// <summary>Sets the percentage and re-sends whatever is showing.</summary>
        public static void Set(int percent)
        {
            AppConfig.Set(KEY, Math.Clamp(percent, MinPercent, 100));
            Refresh();
        }

        // Dial stops: the step grows with the level (about 1/8 of it, 1-5%), so the dim end - where one percent is a visible change and
        // the eye is most sensitive - is stepped finely, and the bright end quickly. 1, 2, 3 ... 12, 14, 16 ... 100.
        public static readonly int[] Stops = BuildStops();

        static int[] BuildStops()
        {
            var stops = new List<int> { MinPercent };
            int p = MinPercent;
            while (p < 100)
            {
                p = Math.Min(100, p + Math.Clamp((int)Math.Round(p / 8.0, MidpointRounding.AwayFromZero), 1, 5));
                stops.Add(p);
            }
            return stops.ToArray();
        }

        /// <summary>Moves to the next dial stop (count stops for a fast spin) in the given direction. Returns the new percentage.</summary>
        public static int StepStops(int direction, int count = 1)
        {
            int current = Percent;
            int i;
            if (direction > 0)
            {
                i = Array.FindIndex(Stops, s => s > current);
                i = i < 0 ? Stops.Length - 1 : Math.Min(Stops.Length - 1, i + count - 1);
            }
            else
            {
                i = Array.FindLastIndex(Stops, s => s < current);
                i = i < 0 ? 0 : Math.Max(0, i - (count - 1));
            }
            Set(Stops[i]);
            return Stops[i];
        }

        /// <summary>Steps by delta percent. Returns the new percentage.</summary>
        public static int Step(int delta)
        {
            int p = Math.Clamp(Percent + delta, MinPercent, 100);
            Set(p);
            return p;
        }

        /// <summary>
        /// Applies the current percentage straight away on a background worker that always sends the newest value: a click is sent at
        /// once, and a fast spin that outpaces the keyboard skips intermediate values instead of falling behind.
        /// </summary>
        public static void Refresh()
        {
            // Animated / per-key effects pick the new gain up on their next frame; only static output has to be re-sent.
            if (PerKeyEngine.Running) return;

            lock (refreshGate)
            {
                refreshPending = true;
                if (refreshRunning) return;
                refreshRunning = true;
            }

            ThreadPool.QueueUserWorkItem(_ =>
            {
                while (true)
                {
                    lock (refreshGate)
                    {
                        if (!refreshPending) { refreshRunning = false; return; }
                        refreshPending = false;
                    }

                    try { Aura.RefreshZenbook16XBrightness(); }
                    catch (Exception ex) { Logger.WriteLine("Fine brightness refresh failed: " + ex.Message); }
                }
            });
        }
    }

    /// <summary>
    /// MyASUS's lighting agent (AsusExclusiveAgent.exe) re-applies ASUS's saved effect whenever the backlight changes
    /// (e.g. Fn+F7), overwriting what G-Helper set. When enabled this keeps that one process stopped. It is only a user-session helper:
    /// it comes back when MyASUS is opened or at next sign-in, which is why this checks periodically rather than once.
    /// </summary>
    public static class AsusLightingAgent
    {
        const string PROCESS = "AsusExclusiveAgent";
        const string KEY = "stop_lighting_agent";

        static System.Threading.Timer? watcher;

        public static bool IsEnabled => AppConfig.Is(KEY);

        public static void SetEnabled(bool enabled)
        {
            AppConfig.Set(KEY, enabled ? 1 : 0);
            if (enabled) Start(); else Stop();
        }

        public static void Start()
        {
            if (!IsEnabled || !AppConfig.IsZenbookPro16X()) return;
            watcher ??= new System.Threading.Timer(_ => StopAgent(), null, 0, 10000);
        }

        public static void Stop()
        {
            watcher?.Dispose();
            watcher = null;
        }

        static void StopAgent()
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(PROCESS))
                {
                    try
                    {
                        Logger.WriteLine("Stopping ASUS lighting agent (" + PROCESS + ")");
                        p.Kill();
                    }
                    catch (Exception ex) { Logger.WriteLine("Could not stop " + PROCESS + ": " + ex.Message); }
                    finally { p.Dispose(); }
                }
            }
            catch (Exception ex) { Logger.WriteLine("Lighting agent check failed: " + ex.Message); }
        }
    }
}
