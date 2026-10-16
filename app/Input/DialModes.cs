using GHelper.Helpers;

namespace GHelper.Input
{
    /// <summary>One thing the dial can adjust: rotation runs Up/Down (any KeyProcess action), a press moves to the next mode.</summary>
    public record DialMode(string Id, string Name, string Up, string Down, ToastIcon? Icon = null, string? ToastName = null);

    public static class DialModes
    {
        public const string NextModeAction = "dial_next_mode";

        public static readonly DialMode[] All =
        [
            new("volume", "Volume", "volume_up", "volume_down", ToastIcon.Volume),
            new("brightness", "Screen brightness", "brightness_up", "brightness_down", ToastIcon.BrightnessUp, "Brightness"),
            new("backlight", "Keyboard backlight", "backlight_up", "backlight_down", ToastIcon.BacklightUp, "Backlight"),
            new("media", "Next / previous track", "next_track", "prev_track", ToastIcon.Media, "Tracks"),
            new("scroll", "Scroll", "scroll_down", "scroll_up", ToastIcon.Scroll),
            new("performance", "Performance mode", "performance", "performance_back", ToastIcon.Performance, "Performance"),
            new("aura", "Keyboard lighting mode", "aura", "aura_back", ToastIcon.Lighting, "Lighting"),
        ];

        static string[] DefaultOrder => ["volume", "brightness", "backlight"];

        /// <summary>When on, rotation adjusts the current mode and a press switches mode (replacing the dial_cw/ccw/press bindings).</summary>
        public static bool IsActive => AppConfig.Is("dial_modes_on") && Enabled().Count > 0;

        static DialMode? Find(string id) => All.FirstOrDefault(m => m.Id == id);

        /// <summary>Enabled modes in cycle order.</summary>
        public static List<DialMode> Enabled()
        {
            string? saved = AppConfig.GetString("dial_modes");
            var ids = saved is null ? DefaultOrder : saved.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return ids.Select(Find).Where(m => m is not null).Cast<DialMode>().Distinct().ToList();
        }

        /// <summary>All modes for the picker: enabled ones first in cycle order, then the rest.</summary>
        public static List<(DialMode mode, bool enabled)> ForPicker()
        {
            var enabled = Enabled();
            return enabled.Select(m => (m, true)).Concat(All.Except(enabled).Select(m => (m, false))).ToList();
        }

        public static void Save(IEnumerable<string> enabledIds) => AppConfig.Set("dial_modes", string.Join(",", enabledIds));

        public static DialMode? Current()
        {
            var list = Enabled();
            if (list.Count == 0) return null;
            return list.FirstOrDefault(m => m.Id == AppConfig.GetString("dial_mode_current")) ?? list[0];
        }

        /// <summary>Advance to the next enabled mode and show it on screen.</summary>
        public static DialMode? Next()
        {
            var list = Enabled();
            if (list.Count == 0) return null;
            var current = Current();
            var next = list[(list.IndexOf(current!) + 1) % list.Count];
            AppConfig.Set("dial_mode_current", next.Id);
            Program.toast?.RunToast(next.ToastName ?? next.Name, next.Icon);
            return next;
        }
    }
}
