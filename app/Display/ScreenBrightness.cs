namespace GHelper.Display
{
    using System;
    using System.Diagnostics;
    using System.Management;

    public static class ScreenBrightness
    {
        public static int Get()
        {
            using var mclass = new ManagementClass("WmiMonitorBrightness")
            {
                Scope = new ManagementScope(@"\\.\root\wmi")
            };
            using var instances = mclass.GetInstances();
            foreach (ManagementObject instance in instances)
            {
                return (byte)instance.GetPropertyValue("CurrentBrightness");
            }
            return 0;
        }

        public static void Set(int brightness)
        {
            using var mclass = new ManagementClass("WmiMonitorBrightnessMethods")
            {
                Scope = new ManagementScope(@"\\.\root\wmi")
            };
            using var instances = mclass.GetInstances();
            var args = new object[] { 1, brightness };
            foreach (ManagementObject instance in instances)
            {
                instance.InvokeMethod("WmiSetBrightness", args);
            }
        }

        // CurrentBrightness lags behind a Set (the panel ramps), so reading it back between fast dial detents
        // returns stale values and the level jumps around. While turning, step from the last target instead.
        const int TARGET_VALID_MS = 1500;
        static readonly object adjustLock = new();
        static int lastTarget = -1;
        static long lastTargetAt;

        public static int Adjust(int delta)
        {
            lock (adjustLock)
            {
                long now = Environment.TickCount64;
                int brightness = lastTarget >= 0 && now - lastTargetAt < TARGET_VALID_MS ? lastTarget : Get();
                brightness = Math.Min(100, Math.Max(0, brightness + delta));
                Set(brightness);
                lastTarget = brightness;
                lastTargetAt = Environment.TickCount64;
                return brightness;
            }
        }

    }
}
