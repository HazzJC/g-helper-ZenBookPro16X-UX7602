using GHelper.UI;
using GHelper.USB;
using System.Drawing.Drawing2D;

namespace GHelper
{
    /// <summary>
    /// Per-key editor for the ZenBook Pro 16X OLED. Paint individual keys, the two side lightbars
    /// and the lid logo, pick an animation and brightness, and keep as many named profiles as you
    /// like. Built in code rather than with a designer file so the whole layout stays in one place.
    /// </summary>
    public class Zenbook16XEditor : RForm
    {
        /// <summary>Scale for the fixed pixel sizes below; buttons auto-size instead.</summary>
        float DpiScale => Math.Max(1f, DeviceDpi / 96f);

        static readonly Color[] Palette =
        {
            Color.Black, Color.White,
            Color.FromArgb(255, 0, 0), Color.FromArgb(255, 128, 0), Color.FromArgb(255, 255, 0),
            Color.FromArgb(128, 255, 0), Color.FromArgb(0, 255, 0), Color.FromArgb(0, 255, 128),
            Color.FromArgb(0, 255, 255), Color.FromArgb(0, 128, 255), Color.FromArgb(0, 0, 255),
            Color.FromArgb(128, 0, 255), Color.FromArgb(255, 0, 255), Color.FromArgb(255, 0, 128),
        };

        readonly KeyboardCanvas canvas = new();
        readonly RComboBox profileCombo = new();
        readonly RComboBox effectCombo = new();
        readonly StepSelector speedSteps = new();
        readonly StepSelector brightnessSteps = new();
        readonly CheckBox liveApply = new();
        readonly Label status = new();
        readonly ToolTip tips = new();
        readonly List<Swatch> swatches = new();
        Swatch? customSwatch;

        Zenbook16XProfile profile = new();
        bool loading;

        /// <summary>
        /// Set by Save and close, which has just made this profile the keyboard's lighting. Any
        /// other close restores whatever the keyboard was configured to show, so browsing a
        /// profile in the editor never leaves it on the keyboard by accident.
        /// </summary>
        bool keepPreviewOnClose;

        // Streaming a frame is 11 USB feature reports; doing that inline on every mouse-move
        // during a drag stalls the UI thread. Changes are snapshotted and a background timer
        // coalesces them into at most one stream per tick.
        readonly System.Timers.Timer applyTimer = new(60) { AutoReset = true };
        readonly object frameLock = new();
        Color[]? pendingFrame;
        CustomFrameMode pendingEffect;
        double pendingSpeed;

        public Zenbook16XEditor()
        {
            Text = "Per-Key Lighting — ZenBook Pro 16X";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(760, 520);

            BuildLayout();
            InitTheme(true);

            // Size after InitTheme: it runs ControlHelper.Resize, which scales the form for DPI,
            // so anything set beforehand gets multiplied and can end up wider than the screen.
            var work = Screen.FromPoint(Cursor.Position).WorkingArea;
            int width = Math.Min((int)(work.Width * 0.86), 1440);
            int height = Math.Min((int)(width / 2.35) + 230, (int)(work.Height * 0.92));
            ClientSize = new Size(width, height);
            Location = new Point(work.X + (work.Width - Width) / 2, work.Y + (work.Height - Height) / 2);

            // The Aura layer must have detected the backlight before ApplyBrightness or ApplyAura
            // will do anything, and the editor can be opened before that has happened.
            if (!Aura.IsBacklightDetected)
            {
                try { Aura.Init(); } catch (Exception ex) { Logger.WriteLine($"PerKeyEditor init: {ex.Message}"); }
            }

            RefreshProfileList();
            LoadProfile(Zenbook16XProfiles.LastEditedName);

            brightnessSteps.SetValueSilently(AppConfig.Get(BrightnessKey, 3));
            SelectPaintColor(Palette[8]);

            canvas.CellsChanged += (_, _) => { Store("Saved"); QueueApply(); };
            effectCombo.SelectedIndexChanged += (_, _) => { if (!loading) { Store("Saved"); QueueApply(); } };
            speedSteps.ValueChanged += (_, _) => { if (!loading) { Store("Saved"); QueueApply(); } };
            brightnessSteps.ValueChanged += BrightnessSteps_ValueChanged;
            profileCombo.SelectedIndexChanged += ProfileCombo_SelectedIndexChanged;

            applyTimer.Elapsed += ApplyTimer_Elapsed;
            applyTimer.Start();

            FormClosed += (_, _) =>
            {
                applyTimer.Stop();
                applyTimer.Dispose();
                StoreProfile();

                if (!keepPreviewOnClose)
                    Task.Run(() => { try { Aura.ApplyAura(); } catch { } });
            };
        }

        // ------------------------------------------------------------ layout

        void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(16, 12, 16, 14),
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            root.Controls.Add(BuildProfileBar(), 0, 0);

            canvas.Dock = DockStyle.Fill;
            canvas.Margin = new Padding(0, 10, 0, 10);
            root.Controls.Add(canvas, 0, 1);

            root.Controls.Add(BuildPaletteBar(), 0, 2);
            root.Controls.Add(BuildControlBar(), 0, 3);

            Controls.Add(root);
        }

        Control BuildProfileBar()
        {
            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };

            bar.Controls.Add(Caption("Profile", 7));

            profileCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            profileCombo.Width = (int)(190 * DpiScale);
            profileCombo.Margin = new Padding(0, 3, 10, 0);
            bar.Controls.Add(profileCombo);

            bar.Controls.Add(Action("New…", (_, _) => NewProfile(),
                "Start a new, blank profile"));
            bar.Controls.Add(Action("Save as profile…", (_, _) => SaveAsProfile(),
                "Save the current layout under a new name, and optionally make it the default"));
            bar.Controls.Add(Action("Rename…", (_, _) => RenameProfile()));
            bar.Controls.Add(Action("Delete", (_, _) => DeleteProfile()));

            status.AutoSize = true;
            status.Margin = new Padding(16, 10, 0, 0);
            status.ForeColor = Color.FromArgb(150, 150, 150);
            bar.Controls.Add(status);

            return bar;
        }

        Control BuildPaletteBar()
        {
            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };

            foreach (var color in Palette)
            {
                var swatch = new Swatch(color) { Size = new Size((int)(28 * DpiScale), (int)(28 * DpiScale)) };
                swatch.Click += (_, _) => SelectPaintColor(swatch.Color);
                swatches.Add(swatch);
                bar.Controls.Add(swatch);
            }

            customSwatch = new Swatch(Palette[8]) { Margin = new Padding(14, 4, 6, 4), Size = new Size((int)(28 * DpiScale), (int)(28 * DpiScale)) };
            customSwatch.Click += (_, _) =>
            {
                var picker = new RColorPicker(canvas.PaintColor);
                picker.ColorChanged += SelectPaintColor;
                picker.ShowDialog(this);
            };
            swatches.Add(customSwatch);
            bar.Controls.Add(customSwatch);

            bar.Controls.Add(new Label
            {
                Text = "pick…",
                AutoSize = true,
                Margin = new Padding(0, 11, 20, 0),
                ForeColor = Color.FromArgb(150, 150, 150),
            });

            bar.Controls.Add(Action("Fill all", (_, _) => canvas.FillAll(canvas.PaintColor)));
            bar.Controls.Add(Action("Clear", (_, _) => canvas.FillAll(Color.Black)));

            return bar;
        }

        Control BuildControlBar()
        {
            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = true,
                Padding = new Padding(0, 8, 0, 0),
            };

            bar.Controls.Add(Caption("Effect"));
            effectCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            effectCombo.Width = (int)(120 * DpiScale);
            effectCombo.Margin = new Padding(0, 3, 18, 0);
            effectCombo.Items.AddRange(new object[] { "Static", "Breathe", "Strobe", "Sweep" });
            bar.Controls.Add(effectCombo);

            bar.Controls.Add(Caption("Speed"));
            SetupSteps(speedSteps, Zenbook16XProfile.SPEED_MIN, Zenbook16XProfile.SPEED_MAX,
                "Animation speed: 1 slow, 2 normal, 3 fast (Static ignores it)");
            bar.Controls.Add(speedSteps);

            bar.Controls.Add(Caption("Brightness"));
            SetupSteps(brightnessSteps, 0, 3, "Keyboard brightness: 0 is off, 3 is maximum");
            bar.Controls.Add(brightnessSteps);

            liveApply.Text = "Live preview";
            liveApply.Checked = true;
            liveApply.AutoSize = true;
            liveApply.Margin = new Padding(4, 9, 18, 0);
            bar.Controls.Add(liveApply);

            bar.Controls.Add(Action("Apply now", (_, _) => QueueApply(true)));
            bar.Controls.Add(Action("Set as default", (_, _) => SetAsDefault(),
                "Make this profile the keyboard's lighting (Custom (Per-Key) mode)"));
            bar.Controls.Add(Action("Save and close", (_, _) => SaveAndClose(),
                "Save, make this profile the keyboard's lighting, and close"));

            return bar;
        }

        static Label Caption(string text, int topMargin = 9) => new()
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(0, topMargin, 8, 0),
        };

        void SetupSteps(StepSelector steps, int min, int max, string tip)
        {
            steps.Maximum = max;
            steps.Minimum = min;
            int count = max - min + 1;
            steps.Size = new Size((int)(count * 32 * DpiScale), (int)(28 * DpiScale));
            steps.Margin = new Padding(0, 3, 18, 0);
            tips.SetToolTip(steps, tip);
        }

        /// <summary>
        /// Buttons size to their own text. Fixed pixel widths clip once the form is DPI-scaled -
        /// the label grows with the font but the width doesn't.
        /// </summary>
        Button Action(string text, EventHandler onClick, string? tip = null)
        {
            var button = new RButton
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(12, 5, 12, 5),
                Margin = new Padding(0, 2, 8, 0),
            };
            button.Click += onClick;
            if (tip is not null) tips.SetToolTip(button, tip);
            return button;
        }

        void SelectPaintColor(Color color)
        {
            canvas.PaintColor = color;
            if (customSwatch is not null && !Palette.Contains(color)) customSwatch.Color = color;
            foreach (var s in swatches) s.Selected = s.Color == color;
        }

        // ------------------------------------------------------------ profiles

        /// <summary>A dropdown entry; the default profile is marked so it's visible at a glance.</summary>
        sealed record ProfileItem(string Name, bool IsDefault)
        {
            public override string ToString() => IsDefault ? $"{Name}   ★ default" : Name;
        }

        void RefreshProfileList()
        {
            loading = true;

            string defaultName = Zenbook16XProfiles.DefaultName;
            profileCombo.Items.Clear();
            foreach (var name in Zenbook16XProfiles.Names())
                profileCombo.Items.Add(new ProfileItem(name, name == defaultName));

            SelectInList(profile.Name);
            loading = false;
        }

        void SelectInList(string name)
        {
            for (int i = 0; i < profileCombo.Items.Count; i++)
            {
                if (profileCombo.Items[i] is ProfileItem item && item.Name == name)
                {
                    profileCombo.SelectedIndex = i;
                    return;
                }
            }
        }

        void LoadProfile(string name)
        {
            loading = true;

            profile = Zenbook16XProfiles.Load(name);
            canvas.LoadColors(profile.Frame);
            effectCombo.SelectedIndex = (int)profile.Effect;
            speedSteps.SetValueSilently(profile.Speed);
            SelectInList(name);
            Zenbook16XProfiles.LastEditedName = name;

            loading = false;
            SetStatus($"Loaded “{name}”");
            QueueApply();
        }

        /// <summary>
        /// Copies the current UI state into the profile object and persists it. Deliberately does
        /// not touch the default - editing a profile shouldn't change what the keyboard shows.
        /// </summary>
        void StoreProfile()
        {
            Array.Copy(canvas.Colors, profile.Frame, Math.Min(canvas.Colors.Length, profile.Frame.Length));
            profile.Effect = (CustomFrameMode)Math.Clamp(effectCombo.SelectedIndex, 0, 3);
            profile.Speed = speedSteps.Value;
            Zenbook16XProfiles.Save(profile);
            Zenbook16XProfiles.LastEditedName = profile.Name;
        }

        void Store(string message)
        {
            StoreProfile();
            SetStatus($"{message} to “{profile.Name}”");
        }

        void ProfileCombo_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (loading || profileCombo.SelectedItem is not ProfileItem item || item.Name == profile.Name) return;
            StoreProfile();
            LoadProfile(item.Name);
        }

        void NewProfile()
        {
            var result = ProfileDialog("New profile", "Profile " + (Zenbook16XProfiles.Names().Count + 1), offerDefault: false);
            if (result is null) return;

            StoreProfile();
            profile = new Zenbook16XProfile { Name = Zenbook16XProfiles.Unique(Zenbook16XProfiles.Sanitize(result.Value.Name)) };
            Zenbook16XProfiles.Save(profile);
            RefreshProfileList();
            LoadProfile(profile.Name);
        }

        /// <summary>
        /// Saves what's on the canvas under a new name - the current profile is left as it was -
        /// and optionally makes the new one the keyboard's default lighting.
        /// </summary>
        void SaveAsProfile()
        {
            var result = ProfileDialog("Save as profile", profile.Name + " copy", offerDefault: true);
            if (result is null) return;

            StoreProfile();

            var copy = profile.Clone();
            copy.Name = Zenbook16XProfiles.Unique(Zenbook16XProfiles.Sanitize(result.Value.Name));
            Zenbook16XProfiles.Save(copy);

            profile = copy;
            if (result.Value.SetDefault) MakeDefault();

            RefreshProfileList();
            LoadProfile(copy.Name);
            SetStatus(result.Value.SetDefault
                ? $"Saved “{copy.Name}” and set it as the default"
                : $"Saved as “{copy.Name}”");
        }

        void RenameProfile()
        {
            var result = ProfileDialog("Rename profile", profile.Name, offerDefault: false);
            if (result is null) return;

            string clean = Zenbook16XProfiles.Sanitize(result.Value.Name);
            if (clean == profile.Name) return;
            clean = Zenbook16XProfiles.Unique(clean);

            StoreProfile();
            Zenbook16XProfiles.Rename(profile.Name, clean);
            profile.Name = clean;
            RefreshProfileList();
            LoadProfile(clean);
        }

        void DeleteProfile()
        {
            if (Zenbook16XProfiles.Names().Count <= 1)
            {
                SetStatus("Can't delete the only profile");
                return;
            }

            if (MessageBox.Show($"Delete profile “{profile.Name}”?", "Delete profile",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            Zenbook16XProfiles.Delete(profile.Name);
            RefreshProfileList();
            LoadProfile(Zenbook16XProfiles.Names()[0]);
        }

        /// <summary>
        /// Name prompt, optionally with a "Set as default" box. Laid out with auto-sizing panels
        /// rather than fixed coordinates so it survives DPI scaling the same way the editor does.
        /// </summary>
        (string Name, bool SetDefault)? ProfileDialog(string title, string initial, bool offerDefault)
        {
            using var dialog = new RForm
            {
                Text = title,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(16),
            };

            var layout = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                WrapContents = false,
                Dock = DockStyle.Fill,
            };

            layout.Controls.Add(new Label { Text = "Name", AutoSize = true, Margin = new Padding(0, 0, 0, 4) });

            var input = new RTextBox
            {
                Text = initial,
                Width = (int)(320 * DpiScale),
                Margin = new Padding(0, 0, 0, 12),
            };
            layout.Controls.Add(input);

            var setDefault = new CheckBox
            {
                Text = "Set as default — make this the keyboard's lighting",
                AutoSize = true,
                Checked = false,
                Margin = new Padding(0, 0, 0, 14),
                Visible = offerDefault,
            };
            if (offerDefault) layout.Controls.Add(setDefault);

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                WrapContents = false,
                Width = input.Width,
                Margin = Padding.Empty,
            };
            var cancel = new RButton { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, Padding = new Padding(12, 5, 12, 5), Margin = Padding.Empty };
            var ok = new RButton { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true, Padding = new Padding(12, 5, 12, 5), Margin = new Padding(0, 0, 8, 0) };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            layout.Controls.Add(buttons);

            dialog.Controls.Add(layout);
            dialog.AcceptButton = ok;
            dialog.CancelButton = cancel;
            dialog.TopMost = TopMost;
            dialog.InitTheme();

            dialog.Shown += (_, _) => { input.Focus(); input.SelectAll(); };

            if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(input.Text)) return null;
            return (input.Text, offerDefault && setDefault.Checked);
        }

        void SetStatus(string text)
        {
            if (status.InvokeRequired) status.BeginInvoke(() => status.Text = text);
            else status.Text = text;
        }

        // ------------------------------------------------------------ applying

        static string BrightnessKey => SystemInformation.PowerStatus.PowerLineStatus != PowerLineStatus.Online
            ? "keyboard_brightness_ac"
            : "keyboard_brightness";

        void BrightnessSteps_ValueChanged(object? sender, EventArgs e)
        {
            int level = brightnessSteps.Value;
            AppConfig.Set(BrightnessKey, level);

            Task.Run(() =>
            {
                try { Aura.ApplyBrightness(level, "PerKeyEditor"); } catch { }
            });

            // ApplyBrightness stops the effect engine when it hits zero, so repaint after it.
            QueueApply(true);
        }

        /// <param name="force">
        /// True for an explicit "Apply now"; false for the automatic calls, which only stream
        /// when live preview is on.
        /// </param>
        void QueueApply(bool force = false)
        {
            if (!force && !liveApply.Checked) return;

            // Snapshot everything the timer needs on the UI thread, so the timer thread never has
            // to marshal back and can't deadlock against a UI thread waiting on frameLock.
            var snapshot = new Color[Zenbook16X.SLOTS];
            Array.Copy(canvas.Colors, snapshot, Math.Min(canvas.Colors.Length, snapshot.Length));

            lock (frameLock)
            {
                pendingFrame = snapshot;
                pendingEffect = (CustomFrameMode)Math.Clamp(effectCombo.SelectedIndex, 0, 3);
                pendingSpeed = Zenbook16XProfile.FactorFor(speedSteps.Value);
            }
        }

        void ApplyTimer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            Color[] frame;
            CustomFrameMode mode;
            double speed;

            lock (frameLock)
            {
                if (pendingFrame is null) return;
                frame = pendingFrame;
                mode = pendingEffect;
                speed = pendingSpeed;
                pendingFrame = null;
            }

            try
            {
                if (mode == CustomFrameMode.Static)
                {
                    PerKeyEngine.Stop();
                    Zenbook16X.DirectEnable();
                    Zenbook16X.Stream(frame);
                }
                else
                {
                    PerKeyEngine.Start(new CustomFrameEffect(frame, mode, speed));
                }
            }
            catch (Exception ex) { Logger.WriteLine($"PerKeyEditor apply failed: {ex.Message}"); }
        }

        /// <summary>
        /// Makes the current profile the default and switches the keyboard to Custom (Per-Key)
        /// mode, so it's what the keyboard shows from now on - including after a restart.
        /// </summary>
        void MakeDefault()
        {
            Zenbook16XProfiles.DefaultName = profile.Name;
            AppConfig.Set("aura_mode", (int)AuraMode.CUSTOM_PERKEY);
            try { Aura.Mode = AuraMode.CUSTOM_PERKEY; } catch { }

            // Keep the main window's mode dropdown in step, rather than showing a stale mode.
            try { Program.settingsForm?.SyncAuraMode(); } catch { }
        }

        /// <summary>
        /// Streams directly rather than relying on ApplyAura, which is a no-op while the backlight
        /// is off and would otherwise silently do nothing.
        /// </summary>
        void SetAsDefault()
        {
            StoreProfile();
            MakeDefault();
            RefreshProfileList();
            QueueApply(true);

            SetStatus($"“{profile.Name}” is now the default keyboard lighting");
        }

        void SaveAndClose()
        {
            SetAsDefault();
            keepPreviewOnClose = true;
            Close();
        }
    }

    /// <summary>A round colour swatch with a selection ring.</summary>
    class Swatch : Control
    {
        Color color;
        bool selected;

        public Color Color
        {
            get => color;
            set { color = value; Invalidate(); }
        }

        public bool Selected
        {
            get => selected;
            set { if (selected != value) { selected = value; Invalidate(); } }
        }

        public Swatch(Color color)
        {
            this.color = color;
            Size = new Size(28, 28);
            Margin = new Padding(0, 4, 6, 4);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? RForm.formBack);

            var box = new Rectangle(3, 3, Width - 7, Height - 7);

            using (var brush = new SolidBrush(color)) g.FillEllipse(brush, box);
            using (var edge = new Pen(Color.FromArgb(110, 110, 116))) g.DrawEllipse(edge, box);

            if (selected)
            {
                using var ring = new Pen(RForm.foreMain, 2f);
                g.DrawEllipse(ring, 1, 1, Width - 3, Height - 3);
            }
        }
    }
}
