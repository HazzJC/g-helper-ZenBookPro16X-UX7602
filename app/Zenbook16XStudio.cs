using GHelper.UI;
using GHelper.USB;
using System.Drawing.Drawing2D;

namespace GHelper
{
    /// <summary>
    /// Lighting Studio for the ZenBook Pro 16X OLED: every keyboard mode in one grouped list,
    /// with a tick to show or hide each one in the main keyboard dropdown, and - for the colour
    /// driven presets - their own palette and speed. Built in code, like the per-key editor.
    /// </summary>
    public class Zenbook16XStudio : RForm
    {
        float DpiScale => Math.Max(1f, DeviceDpi / 96f);

        readonly FlowLayoutPanel list = new();
        readonly Panel detail = new();
        readonly Label title = new();
        readonly Label blurb = new();
        readonly Label activeLabel = new();
        readonly FlowLayoutPanel colourSection = new();
        readonly FlowLayoutPanel presetChips = new();
        readonly FlowLayoutPanel stopRow = new();
        readonly GradientChip preview = new();
        readonly StepSelector speed = new();
        readonly Label speedCaption = new();
        readonly RButton useButton = new();
        Button resetButton = null!;
        readonly ToolTip tips = new();
        readonly System.Windows.Forms.Timer debounce = new() { Interval = 180 };

        readonly Dictionary<AuraMode, Panel> rows = new();
        Dictionary<AuraMode, string> allModes = new();
        AuraMode selected;
        List<Color> stops = new();
        bool loading;

        public Zenbook16XStudio()
        {
            Text = "Lighting Studio — ZenBook Pro 16X";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(820, 560);

            allModes = Aura.GetModes();
            BuildLayout();
            InitTheme(true);

            var work = Screen.FromPoint(Cursor.Position).WorkingArea;
            ClientSize = new Size(Math.Min((int)(work.Width * 0.82), 1180), Math.Min((int)(work.Height * 0.9), 900));
            Location = new Point(work.X + (work.Width - Width) / 2, work.Y + (work.Height - Height) / 2);

            debounce.Tick += (_, _) => { debounce.Stop(); ApplyIfActive(); };
            FormClosed += (_, _) => debounce.Dispose();

            AuraMode active = ActiveMode;
            Select(allModes.ContainsKey(active) ? active : allModes.Keys.First());
        }

        static AuraMode ActiveMode => (AuraMode)AppConfig.Get("aura_mode");

        // ------------------------------------------------------------ layout

        void BuildLayout()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(14, 12, 14, 12) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(300 * DpiScale)));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            list.Dock = DockStyle.Fill;
            list.FlowDirection = FlowDirection.TopDown;
            list.WrapContents = false;
            list.AutoScroll = true;
            list.HorizontalScroll.Enabled = false;
            list.Margin = new Padding(0, 0, 12, 0);
            BuildList();
            root.Controls.Add(list, 0, 0);

            BuildDetail();
            detail.Dock = DockStyle.Fill;
            root.Controls.Add(detail, 1, 0);

            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
            footer.Controls.Add(Action("Show all", (_, _) => SetAllHidden(false), "Show every mode in the keyboard dropdown"));
            root.Controls.Add(footer, 0, 1);

            var close = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 8, 0, 0) };
            close.Controls.Add(Action("Close", (_, _) => Close()));
            root.Controls.Add(close, 1, 1);

            Controls.Add(root);
        }

        void BuildList()
        {
            list.SuspendLayout();
            list.Controls.Clear();
            rows.Clear();

            var hardware = allModes.Keys.Where(m => !Zenbook16XSettings.HasPalette(m) && m != AuraMode.CUSTOM_PERKEY).ToList();
            var presets = allModes.Keys.Where(Zenbook16XSettings.HasPalette).ToList();
            var custom = allModes.Keys.Where(m => m == AuraMode.CUSTOM_PERKEY).ToList();

            list.Controls.Add(new Label
            {
                Text = "Tick = show in the keyboard dropdown",
                AutoSize = true,
                Margin = new Padding(4, 0, 0, 2),
                ForeColor = Color.FromArgb(150, 150, 150),
            });

            AddGroup("Lighting presets", presets);
            AddGroup("Your layouts", custom);
            AddGroup("Built-in effects", hardware);

            list.ResumeLayout();
        }

        void AddGroup(string name, List<AuraMode> modes)
        {
            if (modes.Count == 0) return;

            list.Controls.Add(new Label
            {
                Text = name.ToUpperInvariant(),
                AutoSize = true,
                Margin = new Padding(4, 8, 0, 2),
                ForeColor = Color.FromArgb(150, 150, 150),
            });

            foreach (var mode in modes) list.Controls.Add(MakeRow(mode));
        }

        Panel MakeRow(AuraMode mode)
        {
            var row = new Panel
            {
                Height = (int)(27 * DpiScale),
                Width = (int)(264 * DpiScale),
                Margin = new Padding(0, 0, 0, 1),
                Cursor = Cursors.Hand,
            };

            var check = new CheckBox
            {
                Checked = !Zenbook16XSettings.IsHidden(mode),
                AutoSize = true,
                Location = new Point((int)(8 * DpiScale), 0),
            };
            tips.SetToolTip(check, "Show this mode in the keyboard dropdown");
            check.CheckedChanged += (_, _) =>
            {
                if (loading) return;
                if (!check.Checked && mode == ActiveMode)
                {
                    // Hiding the mode that's currently on would leave the dropdown pointing at nothing.
                    loading = true; check.Checked = true; loading = false;
                    MessageBox.Show("This is the mode the keyboard is using right now. Switch to another mode first, then you can hide it.",
                        "Lighting Studio", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Zenbook16XSettings.SetHidden(mode, !check.Checked);
                Program.settingsForm?.RefreshKeyboardModes();
            };

            var label = new Label
            {
                Text = allModes[mode],
                AutoSize = true,
                Location = new Point((int)(36 * DpiScale), 0),
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand,
            };

            row.Controls.Add(check);
            row.HandleCreated += (_, _) => { check.Top = (row.Height - check.Height) / 2; label.Top = (row.Height - label.Height) / 2; };
            row.Controls.Add(label);
            row.Click += (_, _) => Select(mode);
            label.Click += (_, _) => Select(mode);

            rows[mode] = row;
            return row;
        }

        void BuildDetail()
        {
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
            };

            title.AutoSize = true;
            title.Font = new Font(Font.FontFamily, Font.Size * 1.5f, FontStyle.Bold);
            title.Margin = new Padding(0, 0, 0, 2);
            flow.Controls.Add(title);

            activeLabel.AutoSize = true;
            activeLabel.Margin = new Padding(0, 0, 0, 6);
            flow.Controls.Add(activeLabel);

            blurb.AutoSize = true;
            blurb.MaximumSize = new Size((int)(520 * DpiScale), 0);
            blurb.Margin = new Padding(0, 0, 0, 14);
            blurb.ForeColor = Color.FromArgb(150, 150, 150);
            flow.Controls.Add(blurb);

            // Colours -----------------------------------------------------
            colourSection.FlowDirection = FlowDirection.TopDown;
            colourSection.WrapContents = false;
            colourSection.AutoSize = true;
            colourSection.Margin = new Padding(0);

            colourSection.Controls.Add(Caption("Palette"));

            preview.Size = new Size((int)(420 * DpiScale), (int)(30 * DpiScale));
            preview.Margin = new Padding(0, 2, 0, 8);
            colourSection.Controls.Add(preview);

            stopRow.AutoSize = true;
            stopRow.WrapContents = false;
            stopRow.Margin = new Padding(0, 0, 0, 8);
            colourSection.Controls.Add(stopRow);

            colourSection.Controls.Add(Caption("Start from"));
            presetChips.AutoSize = true;
            presetChips.MaximumSize = new Size((int)(520 * DpiScale), 0);
            presetChips.Margin = new Padding(0, 2, 0, 8);
            foreach (var (name, hex) in Zenbook16XSettings.PalettePresets)
            {
                var chip = new GradientChip(Zenbook16XSettings.FromHex(hex))
                {
                    Size = new Size((int)(56 * DpiScale), (int)(26 * DpiScale)),
                    Margin = new Padding(0, 0, 8, 8),
                    Cursor = Cursors.Hand,
                };
                tips.SetToolTip(chip, name);
                var captured = hex;
                chip.Click += (_, _) =>
                {
                    stops = Zenbook16XSettings.FromHex(captured).ToList();
                    PaletteChanged();
                };
                presetChips.Controls.Add(chip);
            }
            colourSection.Controls.Add(presetChips);

            speedCaption.Text = "Speed";
            speedCaption.AutoSize = true;
            speedCaption.Margin = new Padding(0, 4, 8, 0);
            colourSection.Controls.Add(speedCaption);
            speed.Minimum = 1;
            speed.Maximum = 3;
            speed.Size = new Size((int)(96 * DpiScale), (int)(28 * DpiScale));
            speed.Margin = new Padding(0, 2, 0, 8);
            speed.ValueChanged += (_, _) =>
            {
                if (loading) return;
                Zenbook16XSettings.SetSpeed(selected, speed.Value);
                debounce.Stop(); debounce.Start();
            };
            colourSection.Controls.Add(speed);

            flow.Controls.Add(colourSection);

            // Buttons -----------------------------------------------------
            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 14) };
            useButton.Text = "Use this mode";
            useButton.AutoSize = true;
            useButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            useButton.Padding = new Padding(14, 6, 14, 6);
            useButton.Margin = new Padding(0, 0, 8, 0);
            useButton.Click += (_, _) => UseMode();
            buttons.Controls.Add(useButton);
            resetButton = Action("Reset palette", (_, _) =>
            {
                Zenbook16XSettings.ResetPalette(selected);
                stops = Zenbook16XSettings.GetPalette(selected).ToList();
                PaletteChanged(save: false);
            }, "Back to this effect's default colours");
            buttons.Controls.Add(resetButton);
            flow.Controls.Add(buttons);
            flow.Controls.SetChildIndex(buttons, 3); // directly under the description, always visible

            detail.Controls.Add(flow);
            flow.HorizontalScroll.Visible = false;
            flow.Resize += (_, _) => FitDetail(flow.ClientSize.Width);
        }

        /// <summary>Keeps wrapped text and the palette preview matched to the available width.</summary>
        void FitDetail(int width)
        {
            int usable = Math.Max((int)(240 * DpiScale), width - (int)(30 * DpiScale));
            blurb.MaximumSize = new Size(usable, 0);
            presetChips.MaximumSize = new Size(usable, 0);
            preview.Width = Math.Min(usable, (int)(560 * DpiScale));
        }

        [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string? pszSubIdList);

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // Scrollable panels keep the light native scrollbars unless themed explicitly.
            string theme = darkTheme ? "DarkMode_Explorer" : "Explorer";
            SetWindowTheme(list.Handle, theme, null);
            foreach (var flow in detail.Controls.OfType<FlowLayoutPanel>()) SetWindowTheme(flow.Handle, theme, null);
        }

        // ------------------------------------------------------------ behaviour

        void Select(AuraMode mode)
        {
            selected = mode;
            foreach (var (m, row) in rows)
                row.BackColor = m == mode ? Color.FromArgb(50, 128, 128, 140) : Color.Transparent;

            title.Text = allModes[mode];
            blurb.Text = Describe(mode);

            bool palette = Zenbook16XSettings.HasPalette(mode);
            colourSection.Visible = palette;
            resetButton.Visible = palette;

            loading = true;
            if (palette)
            {
                stops = Zenbook16XSettings.GetPalette(mode).ToList();
                speed.SetValueSilently(Zenbook16XSettings.GetSpeed(mode));
                RebuildStops();
            }
            loading = false;

            RefreshActive();
        }

        void RefreshActive()
        {
            bool active = selected == ActiveMode;
            activeLabel.Text = active ? "● Active on the keyboard now" : "";
            activeLabel.ForeColor = Color.FromArgb(70, 200, 120);
            activeLabel.Visible = active;
            useButton.Enabled = !active;
        }

        static string Describe(AuraMode mode) => mode switch
        {
            AuraMode.AURORA => "Slow curtains of colour drifting across the keys and lightbars.",
            AuraMode.LAVA => "Soft blobs wander the keyboard, merging and splitting like a lava lamp.",
            AuraMode.SONAR => "Rings ping out from the middle of the keyboard. The logo blinks as each leaves and the lightbars flash at the edges.",
            AuraMode.FIREFLIES => "Soft lights wander across the keys, each pulsing in its own rhythm.",
            AuraMode.EMBER => "Typing reactive. Each key you press flares white-hot in a colour from your palette, then cools as the glow spreads to its neighbours.",
            AuraMode.RAIN_COLOR => "Multi-coloured drops fall down the keys with fading tails and splash the lightbars.",
            AuraMode.CUSTOM_PERKEY => "Your hand-painted per-key layout. Edit it in the per-key editor.",
            _ => "Built-in keyboard effect, driven by the keyboard's own controller. Colours come from the main window's colour buttons.",
        };

        void UseMode()
        {
            AppConfig.Set("aura_mode", (int)selected);
            Program.settingsForm?.SyncAuraMode();
            Task.Run(() => { try { Aura.ApplyAura(); } catch (Exception ex) { Logger.WriteLine($"Studio apply: {ex.Message}"); } });
            RefreshActive();
        }

        void ApplyIfActive()
        {
            if (selected != ActiveMode) return;
            Task.Run(() => { try { Aura.ApplyAura(); } catch (Exception ex) { Logger.WriteLine($"Studio apply: {ex.Message}"); } });
        }

        void SetAllHidden(bool hidden)
        {
            foreach (var mode in allModes.Keys.ToList())
                Zenbook16XSettings.SetHidden(mode, hidden && mode != ActiveMode);

            loading = true;
            foreach (var (m, row) in rows)
                foreach (var c in row.Controls.OfType<CheckBox>()) c.Checked = !Zenbook16XSettings.IsHidden(m);
            loading = false;

            Program.settingsForm?.RefreshKeyboardModes();
        }

        // ------------------------------------------------------------ palette editing

        void PaletteChanged(bool save = true)
        {
            if (save) Zenbook16XSettings.SetPalette(selected, stops.ToArray());
            RebuildStops();
            debounce.Stop(); debounce.Start();
        }

        void RebuildStops()
        {
            preview.Stops = stops.ToArray();
            stopRow.SuspendLayout();
            stopRow.Controls.Clear();

            for (int i = 0; i < stops.Count; i++)
            {
                int index = i;
                var swatch = new Swatch(stops[i]) { Size = new Size((int)(34 * DpiScale), (int)(34 * DpiScale)) };
                tips.SetToolTip(swatch, "Click to change this colour");
                swatch.Click += (_, _) =>
                {
                    var picker = new RColorPicker(stops[index]);
                    picker.ColorChanged += c =>
                    {
                        stops[index] = Color.FromArgb(255, c.R, c.G, c.B);
                        PaletteChanged();
                    };
                    picker.ShowDialog(this);
                };
                stopRow.Controls.Add(swatch);
            }

            if (stops.Count < Zenbook16XSettings.MAX_STOPS)
                stopRow.Controls.Add(Action("＋", (_, _) =>
                {
                    stops.Add(stops.Count > 0 ? stops[^1] : Color.White);
                    PaletteChanged();
                }, "Add a colour"));
            if (stops.Count > 2)
                stopRow.Controls.Add(Action("−", (_, _) =>
                {
                    stops.RemoveAt(stops.Count - 1);
                    PaletteChanged();
                }, "Remove the last colour"));

            stopRow.ResumeLayout();
        }

        // ------------------------------------------------------------ helpers

        static Label Caption(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(0, 0, 8, 0) };

        Button Action(string text, EventHandler? onClick, string? tip = null)
        {
            var button = new RButton
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(12, 5, 12, 5),
                Margin = new Padding(0, 2, 8, 0),
            };
            if (onClick is not null) button.Click += onClick;
            if (tip is not null) tips.SetToolTip(button, tip);
            return button;
        }
    }

    /// <summary>A horizontal gradient swatch; used for palette previews and the preset chips.</summary>
    class GradientChip : Control
    {
        Color[] stops;

        public Color[] Stops
        {
            get => stops;
            set { stops = value; Invalidate(); }
        }

        public GradientChip(Color[]? stops = null)
        {
            this.stops = stops ?? new[] { Color.White, Color.Black };
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? RForm.formBack);

            var box = new Rectangle(0, 0, Width - 1, Height - 1);
            if (stops.Length >= 2 && box.Width > 1)
            {
                using var brush = new LinearGradientBrush(box, stops[0], stops[^1], LinearGradientMode.Horizontal);
                var blend = new ColorBlend(stops.Length)
                {
                    Colors = stops,
                    Positions = Enumerable.Range(0, stops.Length).Select(i => i / (float)(stops.Length - 1)).ToArray(),
                };
                brush.InterpolationColors = blend;
                g.FillRectangle(brush, box);
            }
            using var edge = new Pen(Color.FromArgb(110, 110, 116));
            g.DrawRectangle(edge, box);
        }
    }
}
