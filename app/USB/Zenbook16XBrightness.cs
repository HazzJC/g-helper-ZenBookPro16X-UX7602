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
        public static double Gain(int percent) => GainAt(Math.Max(percent, MinPercent));

        /// <summary>
        /// The same curve for any value, including the fractional ones a fade passes through. Below 1% it falls linearly to 0, so a
        /// fade can reach black; at 1% and above it is the visible-floor curve.
        /// </summary>
        public static double GainAt(double percent)
        {
            if (percent >= 100) return 1.0;
            if (percent <= 0) return 0.0;
            double atMin = FloorValue / 255.0;
            if (percent < MinPercent) return atMin * percent / MinPercent;
            return (FloorValue + (Math.Exp(K * (percent - 1)) - 1) / K) / 255.0;
        }

        /// <summary>The gain for what is being shown right now (follows fades), and whether the visible floor applies.</summary>
        public static (double gain, bool floor) Snapshot()
        {
            double shown = Shown;
            return (GainAt(shown), shown >= MinPercent);
        }

        public static Color Apply(Color c)
        {
            var (gain, floor) = Snapshot();
            return Apply(c, gain, floor);
        }

        public static Color Apply(Color c, int percent) => Apply(c, Gain(percent), true);

        public static Color Apply(Color c, double gain, bool floor)
        {
            if (gain >= 1.0) return c;

            double r = c.R * gain, g = c.G * gain, b = c.B * gain;

            // Never let a clearly lit colour fall below the visible floor: lift it, keeping its hue, rather than letting it vanish.
            // Not during a fade to off, which has to be allowed to reach black.
            int origMax = Math.Max(c.R, Math.Max(c.G, c.B));
            double max = Math.Max(r, Math.Max(g, b));
            if (floor && origMax >= FloorAppliesFrom && max > 0 && max < FloorValue)
            {
                double lift = FloorValue / max;
                r *= lift; g *= lift; b *= lift;
            }

            return Color.FromArgb(c.A, (int)Math.Round(Math.Min(255, r)), (int)Math.Round(Math.Min(255, g)), (int)Math.Round(Math.Min(255, b)));
        }

        // ---- smooth fades ----
        // The percentage the user chose is the target; what is shown eases towards it along the same perceptual curve, re-sending
        // through the no-blank refresh path. Interactive changes are short (they start moving at once, so they never feel late);
        // the idle timeout fades out and back in more slowly.

        static readonly object fadeGate = new();
        static double shownPercent = -1;   // -1 = not faded yet: show the target
        static double fadeFrom, fadeTo;
        static long fadeStart;
        static int fadeMs, fadeGeneration;
        static Action? fadeDone;
        static System.Threading.Timer? fadeTimer;

        static double Shown
        {
            get { lock (fadeGate) return shownPercent < 0 ? Percent : shownPercent; }
        }

        public static int InteractiveFadeMs => Math.Max(0, AppConfig.Get("kb_fade_ms", 120));
        public static int IdleFadeMs => Math.Max(0, AppConfig.Get("kb_fade_idle_ms", 400));

        /// <summary>Fades are only possible where the colour gain is what dims the keyboard.</summary>
        public static bool CanFade => AppConfig.IsZenbookPro16X() && IsColourDriven(Aura.Mode);

        /// <summary>Eases what is shown to <paramref name="target"/> percent over <paramref name="ms"/>; then runs done.</summary>
        public static void FadeTo(double target, int ms, Action? done = null)
        {
            lock (fadeGate)
            {
                fadeGeneration++;
                fadeFrom = shownPercent < 0 ? Percent : shownPercent;
                fadeTo = target;
                fadeStart = Environment.TickCount64;
                fadeMs = ms;
                fadeDone = done;

                if (ms <= 0 || !CanFade)
                {
                    shownPercent = target;
                    fadeTimer?.Dispose();
                    fadeTimer = null;
                    fadeDone = null;
                }
                else
                {
                    fadeTimer ??= new System.Threading.Timer(_ => FadeTick(), null, 0, 16);
                    return;
                }
            }

            Refresh();
            done?.Invoke();
        }

        static void FadeTick()
        {
            Action? done = null;
            lock (fadeGate)
            {
                double t = fadeMs <= 0 ? 1 : (Environment.TickCount64 - fadeStart) / (double)fadeMs;
                if (t >= 1)
                {
                    shownPercent = fadeTo;
                    fadeTimer?.Dispose();
                    fadeTimer = null;
                    done = fadeDone;
                    fadeDone = null;
                }
                else
                {
                    double s = t * t * (3 - 2 * t);   // smoothstep: gentle start and finish
                    shownPercent = fadeFrom + (fadeTo - fadeFrom) * s;
                }
            }

            Refresh();
            try { done?.Invoke(); }
            catch (Exception ex) { Logger.WriteLine("Fade completion failed: " + ex.Message); }
        }

        /// <summary>Idle timeout: fade to black, then switch the backlight off. Instant where a fade isn't possible.</summary>
        public static void FadeOutThen(Action off)
        {
            if (!CanFade || IdleFadeMs == 0) { off(); return; }
            int generation;
            lock (fadeGate) generation = fadeGeneration + 1;
            FadeTo(0, IdleFadeMs, () =>
            {
                // a newer fade (the user came back mid-fade) cancels switching off
                lock (fadeGate) if (fadeGeneration != generation) return;
                off();
            });
        }

        /// <summary>Wake from the idle timeout: start from black, switch the backlight on, then fade up to the chosen brightness.</summary>
        public static void FadeInWith(Action on)
        {
            if (!CanFade || IdleFadeMs == 0)
            {
                lock (fadeGate) shownPercent = -1;
                on();
                return;
            }
            lock (fadeGate) { fadeGeneration++; shownPercent = 0; fadeTimer?.Dispose(); fadeTimer = null; fadeDone = null; }
            on();
            FadeTo(Percent, IdleFadeMs);
        }

        /// <summary>
        /// Firmware effects that pick their own colours (the colour in the mode-set packet is ignored) can only be dimmed by the
        /// four hardware steps.
        /// </summary>
        public static bool IsColourDriven(AuraMode mode) => mode switch
        {
            AuraMode.Rain => false,   // firmware Raindrop: its own red / white / blue
            AuraMode.AuraColorCycle or AuraMode.AuraRainbow => Zenbook16XEffects.SoftwareHueEffects,
            _ => true,
        };

        static readonly object refreshGate = new();
        static bool refreshPending, refreshRunning;

        /// <summary>Sets the percentage and re-sends whatever is showing.</summary>
        public static void Set(int percent)
        {
            int p = Math.Clamp(percent, MinPercent, 100);
            // pin what is showing now before the stored value changes, so the fade starts from it rather than from the new value
            lock (fadeGate) if (shownPercent < 0) shownPercent = Percent;
            AppConfig.Set(KEY, p);
            FadeTo(p, InteractiveFadeMs);   // eases there, re-sending as it goes (instant where fades are off or impossible)
            RaiseChanged();
        }

        /// <summary>Raised whenever the brightness the user sees may have changed (level, gain, on/off), so every control can follow.</summary>
        public static event Action? Changed;

        public static void RaiseChanged()
        {
            try { Changed?.Invoke(); }
            catch (Exception ex) { Logger.WriteLine("Brightness change handler failed: " + ex.Message); }
        }

        /// <summary>
        /// The one brightness the UI shows: 0 = off (hardware level 0), 1-100 = hardware level 3 with the colour gain.
        /// The hardware level is otherwise managed automatically, because level 3 gives the finest range.
        /// </summary>
        public static int Unified => GHelper.Input.InputDispatcher.GetBacklight() == 0 ? 0 : Percent;

        public static void SetUnified(int value)
        {
            if (value <= 0)
            {
                int level = GHelper.Input.InputDispatcher.GetBacklight();
                if (level > 0) GHelper.Input.InputDispatcher.SetBacklight(-level, true, toast: false);
                return;
            }

            // Firmware effects that choose their own colours ignore the colour gain, so the slider drives the four hardware steps
            // instead (thirds of the range). The percentage is still remembered for colour-driven modes.
            if (!IsColourDriven(Aura.Mode))
            {
                int target = value <= 33 ? 1 : value <= 66 ? 2 : 3;
                int current = GHelper.Input.InputDispatcher.GetBacklight();
                if (target != current) GHelper.Input.InputDispatcher.SetBacklight(target - current, true, toast: false);
                Set(value);
                return;
            }

            GHelper.Input.InputDispatcher.EnsureFineHardwareLevel();
            Set(value);
        }

        // Dial stops: the step grows with the level (about 1/8 of it) so the dim end - where one percent is a visible change - is stepped
        // finely, peaks at 4% in the middle, then eases back to 2% towards the top so full brightness is approached smoothly:
        // 1, 2 ... 12, 14 ... 33, 37, 41 ... 73, 76, 79 ... 85, 87, 89 ... 99, 100 (42 stops).
        public static readonly int[] Stops = BuildStops();

        static int[] BuildStops()
        {
            var stops = new List<int> { MinPercent };
            int p = MinPercent;
            while (p < 100)
            {
                int rise = (int)Math.Round(p / 8.0, MidpointRounding.AwayFromZero);
                int fall = Math.Max(2, (int)Math.Round((112 - p) / 12.0, MidpointRounding.AwayFromZero));
                p = Math.Min(100, p + Math.Clamp(Math.Min(rise, fall), 1, 4));
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
