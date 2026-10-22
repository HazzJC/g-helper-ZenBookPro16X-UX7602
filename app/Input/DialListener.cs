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
        const int STEP_GAP_MS = 300;   // minimum time between steps of cycle-type actions while the dial keeps turning
        const int DOUBLE_PRESS_MS = 350;

        // actions that make sense to repeat on every detent; everything else fires once per rotation burst
        static readonly HashSet<string> Repeatable = new()
        {
            "volume_up", "volume_down", "brightness_up", "brightness_down",
            "backlight_up", "backlight_down", "backlight_fine_up", "backlight_fine_down", "volume_fine_up", "volume_fine_down", "screen_fine_up", "screen_fine_down", "screenpad_up", "screenpad_down",
            "next_track", "prev_track"
        };

        public static readonly string[] ConfigKeys = ["dial_cw", "dial_ccw", "dial_press", "dial_double", "dial_long"];

        // (config key, resolved action or null = run the key's configured action)
        readonly Action<string, string?> dispatch;
        readonly CancellationTokenSource cts = new();
        readonly object pressLock = new();
        Thread? thread;
        readonly System.Collections.Concurrent.BlockingCollection<Action> queue = new();
        HidStream? stream;

        volatile bool pressed;
        long pressedAt = 0;
        System.Threading.Timer? pendingPress;
        readonly Dictionary<string, long> lastFired = new();

        public DialListener(Action<string, string?>? dispatch = null)
        {
            this.dispatch = dispatch ?? DefaultDispatch;
        }

        static void DefaultDispatch(string key, string? action)
        {
            if (action is null) { InputDispatcher.KeyProcess(key); return; }
            if (action == DialModes.NextModeAction) { DialModes.Next(); return; }

            // dial modes: run the action, then show the on-screen ring (which stands in for the action's own toast)
            if (DialOsd.Enabled) ToastForm.SuppressUntil = Environment.TickCount64 + 600;
            InputDispatcher.RunAction(action, "dial_mode");
            var mode = DialModes.Current();
            if (mode is not null) DialOsd.Show(mode, key == "dial_cw" ? 1 : -1);
        }

        public static bool IsSupported() => AppConfig.IsZenbookPro16X();

        public void Start()
        {
            if (thread is not null) return;
            new Thread(() =>
            {
                try { foreach (var work in queue.GetConsumingEnumerable(cts.Token)) work(); }
                catch (OperationCanceledException) { }
            }) { IsBackground = true, Name = "AsusDialActions" }.Start();
            thread = new Thread(Run) { IsBackground = true, Name = "AsusDialListener" };
            thread.Start();

            Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
            health = new System.Threading.Timer(_ => HealthCheck(), null, 10000, 10000);
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
        // Returns 0 = already 2, 1 = it had reset and was set back to 2, -1 = the device could not be talked to.
        // The dial drops back to interface 1 (Windows radial controller, a different collection) after sleep, so this has to be re-asserted.
        static int EnsureInterface(HidStream s)
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
                    return 1;
                }
                return 0;
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Dial: interface check failed: " + ex.Message);
                return -1;
            }
        }

        // ---- staying connected across sleep / device resets ----

        bool fastRetry;
        System.Threading.Timer? health;

        /// <summary>Drops the current stream so the read loop opens a fresh one (quickly) and re-asserts the interface.</summary>
        void Reconnect(string reason)
        {
            if (cts.IsCancellationRequested) return;
            Logger.WriteLine("Dial: reconnecting (" + reason + ")");
            fastRetry = true;
            try { stream?.Dispose(); } catch { }
        }

        void OnPowerModeChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
        {
            if (e.Mode != Microsoft.Win32.PowerModes.Resume) return;
            // give the I2C-HID device a moment to come back before re-opening it
            Task.Delay(2000).ContinueWith(_ => Reconnect("system resume"));
        }

        /// <summary>
        /// Every few seconds, from a separate short-lived handle (the read loop's own handle may be blocked in a read or stale): is the
        /// device still there, and is it still on interface 2? Fixes a reset in place and reopens the stream after it.
        /// </summary>
        void HealthCheck()
        {
            if (cts.IsCancellationRequested || stream is null) return;
            try
            {
                var device = FindDevice();
                if (device is null || !device.TryOpen(out HidStream probe))
                {
                    Reconnect("device not available");
                    return;
                }

                using (probe)
                {
                    int result = EnsureInterface(probe);
                    if (result != 0) Reconnect(result == 1 ? "interface was reset" : "device not answering");
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Dial: health check failed: " + ex.Message);
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

                // a deliberate reconnect (resume, interface reset) retries almost at once; a failed open waits longer
                int wait = fastRetry ? 500 : RETRY_MS;
                fastRetry = false;
                cts.Token.WaitHandle.WaitOne(wait);
            }
        }

        /// <summary>Test hook: processes a report as if the dial had sent it (no hardware involved).</summary>
        public void Simulate(byte button, short delta) => Handle(button, delta);

        /// <summary>True while a HID stream to the dial is open.</summary>
        public bool IsConnected => stream is not null;

        /// <summary>
        /// Raw dial activity for live displays (the ASUS Dial window): "turn" with the delta, "down" / "up" for the button.
        /// Raised on the listener thread; handlers must marshal to their UI thread.
        /// </summary>
        public event Action<string, int>? Activity;

        void Report(string what, int delta)
        {
            try { Activity?.Invoke(what, delta); }
            catch (Exception ex) { Logger.WriteLine("Dial activity handler failed: " + ex.Message); }
        }

        void Handle(byte button, short delta)
        {
            if (delta != 0) Report("turn", delta);
            bool isDown = (button & 1) != 0;
            if (isDown != pressed) Report(isDown ? "down" : "up", 0);

            if (delta != 0 && !WheelTurn(delta))
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
                if (WheelEnabled && !wheelOpen) StartHoldTimer();
            }
            else if (!down && pressed)
            {
                pressed = false;
                CancelHoldTimer();

                if (wheelJustOpened) { wheelJustOpened = false; return; }   // releasing the hold that opened the wheel
                if (wheelOpen) { WheelSelect(); return; }                    // a press while the wheel is open picks

                long held = Environment.TickCount64 - pressedAt;
                if (held >= AppConfig.Get("dial_long_ms", 600)) Fire("dial_long");
                else ShortPress();
            }
        }

        // ---- hold-to-pick mode wheel (dial modes only) ----
        // Hold: the wheel opens (once the hold time passes, while still held). Turn: move the highlight. Press: pick it.
        // 3 s without input: closes without changing anything. No press-and-turn is needed - the touch ring makes that awkward.

        readonly object wheelGate = new();
        volatile bool wheelOpen, wheelJustOpened;
        int wheelIndex;
        List<DialMode> wheelModes = new();
        System.Threading.Timer? holdTimer, wheelIdle;
        const int WHEEL_IDLE_MS = 3000;

        static bool WheelEnabled => DialModes.IsActive && AppConfig.IsNotFalse("dial_wheel") && DialModes.Enabled().Count > 1;

        void StartHoldTimer()
        {
            lock (wheelGate)
            {
                holdTimer?.Dispose();
                holdTimer = new System.Threading.Timer(_ => OpenWheel(), null, AppConfig.Get("dial_long_ms", 600), Timeout.Infinite);
            }
        }

        void CancelHoldTimer()
        {
            lock (wheelGate) { holdTimer?.Dispose(); holdTimer = null; }
        }

        void OpenWheel()
        {
            lock (wheelGate)
            {
                holdTimer?.Dispose();
                holdTimer = null;
                if (!pressed || wheelOpen) return;
                wheelModes = DialModes.Enabled();
                var current = DialModes.Current();
                wheelIndex = Math.Max(0, wheelModes.FindIndex(m => m.Id == current?.Id));
                wheelOpen = true;
                wheelJustOpened = true;
                RestartWheelIdle();
            }
            Logger.WriteLine("Dial: mode wheel opened");
            DialOsd.ShowWheel(wheelModes, wheelIndex);
        }

        bool WheelTurn(int delta)
        {
            lock (wheelGate)
            {
                if (!wheelOpen) return false;
                int n = wheelModes.Count;
                wheelIndex = ((wheelIndex + Math.Sign(delta)) % n + n) % n;
                RestartWheelIdle();
            }
            DialOsd.ShowWheel(wheelModes, wheelIndex);
            return true;
        }

        void WheelSelect()
        {
            DialMode chosen;
            lock (wheelGate)
            {
                if (!wheelOpen) return;
                wheelOpen = false;
                wheelIdle?.Dispose();
                wheelIdle = null;
                chosen = wheelModes[wheelIndex];
            }
            AppConfig.Set("dial_mode_current", chosen.Id);
            Logger.WriteLine("Dial: mode wheel -> " + chosen.Id);
            if (DialOsd.Enabled) DialOsd.Show(chosen, 0);
            else { DialOsd.HideNow(); Program.toast?.RunToast(chosen.ToastName ?? chosen.Name, chosen.Icon); }
        }

        void WheelCancel()
        {
            lock (wheelGate)
            {
                if (!wheelOpen) return;
                wheelOpen = false;
                wheelJustOpened = false;
                wheelIdle?.Dispose();
                wheelIdle = null;
            }
            Logger.WriteLine("Dial: mode wheel closed without a change");
            DialOsd.HideNow();
        }

        void RestartWheelIdle()
        {
            wheelIdle?.Dispose();
            wheelIdle = new System.Threading.Timer(_ => WheelCancel(), null, WHEEL_IDLE_MS, Timeout.Infinite);
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
                // cycle-type action (lighting / performance mode): one step per STEP_GAP_MS while spinning.
                // Only a step that actually fires restarts the clock, otherwise continuous spinning would never step again.
                if (lastFired.TryGetValue(key, out long last) && now - last < STEP_GAP_MS) return;
            }
            lastFired[key] = now;

            // Run on the action thread, in order: a toast or window update that blocks must never hold up reading the next click.
            string? resolved = fromMode ? action : null;
            queue.Add(() =>
            {
                try
                {
                    Logger.WriteLine($"Dial: {key} -> {action}");
                    dispatch(key, resolved);
                }
                catch (Exception ex)
                {
                    Logger.WriteLine($"Dial: action {action} failed: {ex.Message}");
                }
            });
        }

        public void Dispose()
        {
            cts.Cancel();
            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            health?.Dispose();
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
