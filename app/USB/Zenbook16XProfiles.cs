using System.Drawing;
using System.Text;

namespace GHelper.USB
{
    /// <summary>A saved per-key layout: the frame plus how it animates.</summary>
    public class Zenbook16XProfile
    {
        public const int SPEED_MIN = 1;
        public const int SPEED_MAX = 3;

        public string Name = "Default";
        public Color[] Frame = NewFrame();
        public CustomFrameMode Effect = CustomFrameMode.Static;

        /// <summary>1-3, matching the hardware's own slow/normal/fast steps.</summary>
        public int Speed = 2;

        public static Color[] NewFrame()
        {
            var frame = new Color[Zenbook16X.SLOTS];
            Array.Fill(frame, Color.Black);
            return frame;
        }

        public double SpeedFactor => FactorFor(Speed);

        /// <summary>Animation rate multiplier for a 1-3 speed step.</summary>
        public static double FactorFor(int speed) => Math.Clamp(speed, SPEED_MIN, SPEED_MAX) * 0.6;

        public Zenbook16XProfile Clone() => new()
        {
            Name = Name,
            Frame = (Color[])Frame.Clone(),
            Effect = Effect,
            Speed = Speed,
        };
    }

    /// <summary>
    /// Named per-key profiles, stored in the app config. Each profile is one key holding the
    /// frame as hex triples plus its effect and speed; an index key keeps the order.
    ///
    /// Two different profiles are tracked, deliberately kept apart:
    /// - the <b>default</b> - what the keyboard plays in Custom (Per-Key) mode. Only changes when
    ///   the user explicitly makes something the default.
    /// - the <b>last edited</b> - what the editor reopens on. Browsing or editing a profile in the
    ///   editor must not silently change what the keyboard shows.
    /// </summary>
    public static class Zenbook16XProfiles
    {
        const string INDEX_KEY = "zenbook_profiles";
        const string DEFAULT_KEY = "zenbook_profile_default";
        const string EDITING_KEY = "zenbook_profile_active";
        const string PROFILE_PREFIX = "zenbook_profile_";

        // v1.0 stored a single unnamed frame under these keys; migrated on first load.
        const string LEGACY_FRAME = "zenbook_custom_frame";
        const string LEGACY_EFFECT = "zenbook_custom_effect";
        const string LEGACY_SPEED = "zenbook_custom_speed";

        public static List<string> Names()
        {
            string index = AppConfig.GetString(INDEX_KEY, "") ?? "";
            var names = index.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (names.Count == 0) names.Add("Default");
            return names;
        }

        static void SaveNames(IEnumerable<string> names)
            => AppConfig.Set(INDEX_KEY, string.Join(',', names));

        /// <summary>The profile Custom (Per-Key) mode plays.</summary>
        public static string DefaultName
        {
            get
            {
                var names = Names();
                string stored = AppConfig.GetString(DEFAULT_KEY, "") ?? "";
                if (names.Contains(stored)) return stored;

                // v1.0 had no separate default: the last-edited profile was also the one played.
                string legacy = AppConfig.GetString(EDITING_KEY, "") ?? "";
                return names.Contains(legacy) ? legacy : names[0];
            }
            set => AppConfig.Set(DEFAULT_KEY, value);
        }

        /// <summary>The profile the editor reopens on.</summary>
        public static string LastEditedName
        {
            get
            {
                var names = Names();
                string stored = AppConfig.GetString(EDITING_KEY, "") ?? "";
                return names.Contains(stored) ? stored : DefaultName;
            }
            set => AppConfig.Set(EDITING_KEY, value);
        }

        /// <summary>Names are used as config keys, so keep them to something safe and short.</summary>
        public static string Sanitize(string name)
        {
            var clean = new string((name ?? "").Trim()
                .Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_')
                .ToArray());
            if (clean.Length > 32) clean = clean.Substring(0, 32);
            return string.IsNullOrWhiteSpace(clean) ? "Profile" : clean;
        }

        /// <summary>Appends " 2", " 3"... until the name isn't taken.</summary>
        public static string Unique(string name)
        {
            var names = Names();
            if (!names.Contains(name)) return name;

            for (int i = 2; i < 100; i++)
                if (!names.Contains($"{name} {i}")) return $"{name} {i}";
            return name + " " + Guid.NewGuid().ToString("N").Substring(0, 4);
        }

        public static Zenbook16XProfile Load(string name)
        {
            var profile = new Zenbook16XProfile { Name = name };

            string stored = AppConfig.GetString(PROFILE_PREFIX + name, "") ?? "";
            if (string.IsNullOrEmpty(stored))
            {
                MigrateLegacy(profile);
                return profile;
            }

            // "<effect>|<speed>|<hex triples>"
            var parts = stored.Split('|');
            if (parts.Length == 3)
            {
                if (int.TryParse(parts[0], out int effect)) profile.Effect = (CustomFrameMode)Math.Clamp(effect, 0, 3);
                // v1.0 stored speed as 1-5; anything above the new maximum clamps to fastest.
                if (int.TryParse(parts[1], out int speed)) profile.Speed = Math.Clamp(speed, Zenbook16XProfile.SPEED_MIN, Zenbook16XProfile.SPEED_MAX);
                ParseFrame(parts[2], profile.Frame);
            }
            else ParseFrame(stored, profile.Frame);

            return profile;
        }

        static void MigrateLegacy(Zenbook16XProfile profile)
        {
            string legacy = AppConfig.GetString(LEGACY_FRAME, "") ?? "";
            if (string.IsNullOrEmpty(legacy)) return;

            ParseFrame(legacy, profile.Frame);
            profile.Effect = (CustomFrameMode)Math.Clamp(AppConfig.Get(LEGACY_EFFECT, 0), 0, 3);
            profile.Speed = Math.Clamp(AppConfig.Get(LEGACY_SPEED, 2), Zenbook16XProfile.SPEED_MIN, Zenbook16XProfile.SPEED_MAX);
            Save(profile);
        }

        public static void Save(Zenbook16XProfile profile)
        {
            var hex = new StringBuilder(Zenbook16X.SLOTS * 6);
            for (int i = 0; i < Zenbook16X.SLOTS; i++)
            {
                Color c = i < profile.Frame.Length ? profile.Frame[i] : Color.Black;
                hex.Append($"{c.R:X2}{c.G:X2}{c.B:X2}");
            }

            AppConfig.Set(PROFILE_PREFIX + profile.Name, $"{(int)profile.Effect}|{profile.Speed}|{hex}");

            var names = Names();
            if (!names.Contains(profile.Name))
            {
                names.Add(profile.Name);
                SaveNames(names);
            }
        }

        /// <summary>Renames in place, keeping the profile's position and its default status.</summary>
        public static void Rename(string oldName, string newName)
        {
            if (oldName == newName) return;

            bool wasDefault = DefaultName == oldName;
            bool wasEditing = LastEditedName == oldName;

            var profile = Load(oldName);
            profile.Name = newName;

            var names = Names();
            int index = names.IndexOf(oldName);
            if (index >= 0) names[index] = newName;
            else names.Add(newName);
            SaveNames(names);

            Save(profile);
            AppConfig.Remove(PROFILE_PREFIX + oldName);

            if (wasDefault) DefaultName = newName;
            if (wasEditing) LastEditedName = newName;
        }

        public static void Delete(string name)
        {
            var names = Names();
            if (names.Count <= 1) return; // never leave the list empty

            names.Remove(name);
            SaveNames(names);
            AppConfig.Remove(PROFILE_PREFIX + name);

            if (AppConfig.GetString(DEFAULT_KEY, "") == name) DefaultName = names[0];
            if (AppConfig.GetString(EDITING_KEY, "") == name) LastEditedName = names[0];
        }

        /// <summary>The profile the Custom (Per-Key) aura mode plays back.</summary>
        public static Zenbook16XProfile Default() => Load(DefaultName);

        static void ParseFrame(string hex, Color[] into)
        {
            if (hex.Length < Zenbook16X.SLOTS * 6) return;
            for (int i = 0; i < Zenbook16X.SLOTS; i++)
            {
                try
                {
                    into[i] = Color.FromArgb(
                        Convert.ToByte(hex.Substring(i * 6, 2), 16),
                        Convert.ToByte(hex.Substring(i * 6 + 2, 2), 16),
                        Convert.ToByte(hex.Substring(i * 6 + 4, 2), 16));
                }
                catch { into[i] = Color.Black; }
            }
        }
    }
}
