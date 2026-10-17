using System.Diagnostics.Eventing.Reader;

namespace GHelper.Helpers
{
    /// <summary>
    /// ZenBook Pro 16X mitigation, gated to that model. On 2026-10-18 the machine bluescreened (0x10E, inside Windows' video memory
    /// manager) about two seconds after G-Helper started its NVIDIA GPU initialisation, a few minutes after waking from a long sleep.
    /// The dump shows no G-Helper code, so the cause is unproven; the working theory is that the first NVIDIA call woke a dGPU that was
    /// still settling after the resume. So when G-Helper is *started* shortly after a resume, the first contact with the GPU is delayed.
    ///
    /// Config (all optional): gpu_resume_guard (default on), gpu_resume_window_min (default 10), gpu_resume_delay (seconds, default 30).
    /// </summary>
    public static class ResumeGuard
    {
        /// <summary>How long ago the system last woke from sleep (Modern Standby or S3/S4), if that was within the window.</summary>
        public static TimeSpan? SinceLastResume(TimeSpan window)
        {
            try
            {
                string xpath =
                    "*[System[((Provider[@Name='Microsoft-Windows-Kernel-Power'] and (EventID=107 or EventID=507)) or " +
                    "(Provider[@Name='Microsoft-Windows-Power-Troubleshooter'] and EventID=1)) and " +
                    $"TimeCreated[timediff(@SystemTime) <= {(long)window.TotalMilliseconds}]]]";

                var query = new EventLogQuery("System", PathType.LogName, xpath) { ReverseDirection = true };
                using var reader = new EventLogReader(query);
                using var evt = reader.ReadEvent();
                if (evt?.TimeCreated is DateTime when) return DateTime.Now - when;
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Resume check failed: " + ex.Message);
            }
            return null;
        }

        /// <summary>Startup entry point: creates the GPU control now, or after a delay if the system woke up recently.</summary>
        public static void InitGpuControl()
        {
            if (!AppConfig.IsZenbookPro16X() || !AppConfig.IsNotFalse("gpu_resume_guard"))
            {
                HardwareControl.RecreateGpuControl();
                return;
            }

            var since = SinceLastResume(TimeSpan.FromMinutes(AppConfig.Get("gpu_resume_window_min", 10)));
            if (since is null)
            {
                HardwareControl.RecreateGpuControl();
                return;
            }

            int delay = AppConfig.Get("gpu_resume_delay", 30);
            Logger.WriteLine($"GPU init delayed {delay}s: the system woke from sleep {since.Value.TotalSeconds:F0}s ago");
            HardwareControl.RecreateGpuControlWithDelay(delay);
        }
    }
}
