using GHelper.Helpers;
using HidSharp;

namespace GHelper.Input
{
    /// <summary>
    /// Listens to the ASUS Dial on the ZenBook Pro 16X (UX7602): ACPI ASUS2020, VID 0B05 / PID 0220.
    /// The dial is a touch ring with a centre button. In ASUS "interface 2" it sends events on the
    /// vendor collection as input report 0x0A:
    ///   0A &lt;button&gt; &lt;delta_lo&gt; &lt;delta_hi&gt;   (delta = int16, +1 clockwise / -1 counter-clockwise per detent)
    /// On connect we read register 3 (interface) with feature report 0x0A and set it to 2 if needed
    /// (ASUS's agent normally does this; without it the dial may report on the Windows radial path instead).
    /// Events are mapped through the usual KeyProcess config keys:
    ///   dial_cw, dial_ccw, dial_press, dial_double, dial_long
    /// ASUS's own dial software reacts to the same events; see DialTakeover.
    /// </summary>
    public class DialListener : IDisposable
    {
        const int VID = 0x0B05, PID = 0x0220;
        const byte REPORT_ID = 0x0A;
        const int FEATURE_LEN = 13;
        const int RETRY_MS = 5000;
        const int BURST_GAP_MS = 400;
        const int DOUBLE_PRESS_MS = 350;

        // actions that make sense to repeat on every detent; everything else fires once per rotation burst
        static readonly HashSet<string> Repeatable = new()
        {
            "volume_up", "volume_down", "brightness_up", "brightness_down",
            "backlight_up", "backlight_down", "screenpad_up", "screenpad_down",
            "next_track", "prev_track"
        };

        public static readonly string[] ConfigKeys = ["dial_cw", "dial_ccw", "dial_press", "dial_double", "dial_long"];

        // (config key, resolved action or null = run the key's configured action)
        readonly Action<string, string?> dispatch;
        readonly CancellationTokenSource cts = new();
        readonly object pressLock = new();
        Thread? thread;
        HidStream? stream;

        bool pressed = false;
        long pressedAt = 0;
        System.Threading.Timer? pendingPress;
        readonly Dictionary<string, long> lastFired = new();

        public DialListener(Action<string, string?>? dispatch = null)
        {
            this.dispatch = dispatch ?? DefaultDispatch;
        }

        static void DefaultDispatch(string key, string? action)
        {
            if (action is null) InputDispatcher.KeyProcess(key);
            else if (action == DialModes.NextModeAction) DialModes.Next();
            else InputDispatcher.RunAction(action);
        }

        public static bool IsSupported() => AppConfig.IsZenbookPro16X();

        public void Start()
        {
            if (thread is not null) return;
            thread = new Thread(Run) { IsBackground = true, Name = "AsusDialListener" };
            thread.Start();
        }

        static HidDevice? FindDevice()
        {
            foreach (var device in DeviceList.Local.GetHidDevices(VID, PID))
            {
                try
                {
                    if (device.GetMaxInputReportLength() != 4) continue;
                    if (device.GetReportDescriptor().TryGetReport(HidSharp.Reports.ReportType.Input, REPORT_ID, out _))
                        return device;
                }
                catch { }
            }
            return null;
        }

        // register 3 = interface: 2 = vendor stream on this collection (what we read), 1 = Windows radial controller path
        static void EnsureInterface(HidStream s)
        {
            try
            {
                var query = new byte[FEATURE_LEN];
                query[0] = REPORT_ID; query[1] = 0xC1; query[2] = 0x03;
                s.SetFeature(query);

                var reply = new byte[FEATURE_LEN];
                reply[0] = REPORT_ID;
                s.GetFeature(reply);

                if (reply[1] == 0xC1 && reply[2] == 0x03 && reply[3] != 2)
                {
                    Logger.WriteLine($"Dial: interface was {reply[3]}, setting 2");
                    var set = new byte[FEATURE_LEN];
                    set[0] = REPORT_ID; set[1] = 0xC0; set[2] = 0x03; set[3] = 0x02;
                    s.SetFeature(set);
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Dial: interface check failed: " + ex.Message);
            }
        }

        void Run()
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    var device = FindDevice();
                    if (device is not null && device.TryOpen(out HidStream s))
                    {
                        stream = s;
                        stream.ReadTimeout = Timeout.Infinite;
                        pressed = false;
                        Logger.WriteLine("Dial: listening on " + device.DevicePath);
                        EnsureInterface(stream);
                        var buf = new byte[device.GetMaxInputReportLength()];
                        while (!cts.IsCancellationRequested)
                        {
                            int n = stream.Read(buf, 0, buf.Length);
                            if (n >= 4 && buf[0] == REPORT_ID) Handle(buf[1], (short)(buf[2] | buf[3] << 8));
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!cts.IsCancellationRequested) Logger.WriteLine("Dial: " + ex.Message);
                }
                finally
                {
                    try { stream?.Dispose(); } catch { }
                    stream = null;
                }

                cts.Token.WaitHandle.WaitOne(RETRY_MS);
            }
        }

        void Handle(byte button, short delta)
        {
            if (delta != 0)
            {
                // normally +-1 per report; accept bigger deltas defensively
                string key = delta > 0 ? "dial_cw" : "dial_ccw";
                for (int i = 0; i < Math.Min(Math.Abs((int)delta), 5); i++) Fire(key);
            }

            bool down = (button & 1) != 0;
            if (down && !pressed)
            {
                pressed = true;
                pressedAt = Environment.TickCount64;
            }
            else if (!down && pressed)
            {
                pressed = false;
                long held = Environment.TickCount64 - pressedAt;
                if (held >= AppConfig.Get("dial_long_ms", 600)) Fire("dial_long");
                else ShortPress();
            }
        }

        static bool IsBound(string key) => AppConfig.GetString(key)?.Length > 1;

        // a short press fires immediately, unless a double-press action is bound: then wait to see if a second press follows
        void ShortPress()
        {
            if (!IsBound("dial_double"))
            {
                Fire("dial_press");
                return;
            }

            lock (pressLock)
            {
                if (pendingPress is not null)
                {
                    pendingPress.Dispose();
                    pendingPress = null;
                    Fire("dial_double");
                }
                else
                {
                    pendingPress = new System.Threading.Timer(_ =>
                    {
                        lock (pressLock)
                        {
                            pendingPress?.Dispose();
                            pendingPress = null;
                        }
                        Fire("dial_press");
                    }, null, DOUBLE_PRESS_MS, Timeout.Infinite);
                }
            }
        }

        void Fire(string key)
        {
            // dial modes (press = next mode, turn = adjust current mode) replace the plain turn/press bindings
            bool fromMode = DialModes.IsActive && key is "dial_cw" or "dial_ccw" or "dial_press";
            string? action;
            if (!fromMode) action = AppConfig.GetString(key);
            else if (key == "dial_press") action = DialModes.NextModeAction;
            else
            {
                var mode = DialModes.Current();
                action = key == "dial_cw" ? mode?.Up : mode?.Down;
            }
            if (string.IsNullOrEmpty(action) || action.Length <= 1) return;

            long now = Environment.TickCount64;
            if (!Repeatable.Contains(action) && key.EndsWith("cw"))
            {
                // rotation toggle-type action: once per burst
                if (lastFired.TryGetValue(key, out long last) && now - last < BURST_GAP_MS)
                {
                    lastFired[key] = now;
                    return;
                }
            }
            lastFired[key] = now;

            try
            {
                Logger.WriteLine($"Dial: {key} -> {action}");
                dispatch(key, fromMode ? action : null);
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"Dial: action {action} failed: {ex.Message}");
            }
        }

        public void Dispose()
        {
            cts.Cancel();
            lock (pressLock) { pendingPress?.Dispose(); pendingPress = null; }
            try { stream?.Dispose(); } catch { }
        }
    }

    /// <summary>
    /// ASUS's dial agent (child of the ASUSDialService, runs as SYSTEM) also acts on every turn/press, which
    /// double-fires G-Helper's actions. "Take over" stops and disables only that service; un-ticking restores it.
    /// Needs administrator rights (same as the existing "Stop ASUS services" feature).
    /// </summary>
    public static class DialTakeover
    {
        const string SERVICE = "ASUSDialService";

        public static bool IsEnabled() => AppConfig.Is("dial_takeover");

        public static bool Apply(bool enable)
        {
            if (!ProcessHelper.IsUserAdministrator())
            {
                Logger.WriteLine("Dial takeover needs administrator rights");
                return false;
            }

            AppConfig.Set("dial_takeover", enable ? 1 : 0);
            if (enable) ProcessHelper.StopDisableService(SERVICE);
            else ProcessHelper.StartEnableService(SERVICE);
            return true;
        }

        // the service is set to Disabled, but make sure it stays down after updates/repairs re-enable it
        public static void EnsureOnStartup()
        {
            if (!IsEnabled() || !ProcessHelper.IsUserAdministrator()) return;
            if (System.Diagnostics.Process.GetProcessesByName("AsusDialService").Length == 0) return;
            Logger.WriteLine("ASUS Dial service revived, re-stopping on startup");
            Task.Run(() => ProcessHelper.StopDisableService(SERVICE));
        }
    }
}
