using GHelper.Input;
using GHelper.UI;
using System.Drawing.Drawing2D;

namespace GHelper
{
    /// <summary>
    /// ASUS Dial settings for the ZenBook Pro 16X: what turning and pressing do (dial modes or simple bindings), the extra gestures,
    /// and the options - with a live indicator that follows the real dial. Built in code, like the Lighting Studio.
    /// </summary>
    public class Zenbook16XDial : RForm
    {
        float DpiScale => Math.Max(1f, DeviceDpi / 96f);
        int D(int px) => (int)Math.Round(px * DpiScale);

        static readonly Color Muted = Color.FromArgb(150, 150, 150);

        readonly ToolTip tips = new();
        readonly DialIndicator indicator = new();
        readonly Label status = new() { AutoSize = true };
        readonly Label explain = new() { AutoSize = true, ForeColor = Muted };
        readonly RButton tabModes = new(), tabBindings = new();
        readonly FlowLayoutPanel modesPanel = new(), bindingsPanel = new();
        readonly CheckedListBox modeList = new();
        readonly Label currentMode = new() { AutoSize = true, ForeColor = Muted };
        readonly System.Windows.Forms.Timer statusTimer = new() { Interval = 500 };
        Control? holdBinding;
        Label? holdWheelNote;
        bool loading;

        public Zenbook16XDial()
        {
            Text = "ASUS Dial — ZenBook Pro 16X";
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            MaximizeBox = false;

            BuildLayout();
            InitTheme(true);

            var listener = Program.dialListener;
            if (listener is not null) listener.Activity += OnActivity;
            statusTimer.Tick += (_, _) => UpdateStatus();
            statusTimer.Start();
            FormClosed += (_, _) =>
            {
                if (listener is not null) listener.Activity -= OnActivity;
                statusTimer.Dispose();
            };

            ShowSection(AppConfig.Is("dial_modes_on"));
            UpdateStatus();
        }

        // ------------------------------------------------------------ layout

        void BuildLayout()
        {
            var root = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(D(16), D(12), D(16), D(12)),
            };

            root.Controls.Add(BuildHeader());

            root.Controls.Add(Section("What turning and pressing do"));
            var tabs = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, D(4)) };
            SetupTab(tabModes, "Dial modes", true);
            SetupTab(tabBindings, "Simple bindings", false);
            tabs.Controls.Add(tabModes);
            tabs.Controls.Add(tabBindings);
            root.Controls.Add(tabs);

            explain.MaximumSize = new Size(D(470), 0);
            explain.Margin = new Padding(D(2), 0, 0, D(6));
            root.Controls.Add(explain);

            BuildModesPanel();
            BuildBindingsPanel();
            root.Controls.Add(modesPanel);
            root.Controls.Add(bindingsPanel);

            root.Controls.Add(Section("More gestures"));
            root.Controls.Add(BuildGestures());

            root.Controls.Add(Section("Options"));
            root.Controls.Add(BuildOptions());

            var close = new FlowLayoutPanel { AutoSize = false, Size = new Size(D(470), D(40)), FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0, D(10), 0, 0) };
            close.Controls.Add(Button("Close", (_, _) => Close()));
            root.Controls.Add(close);

            Controls.Add(root);
        }

        Control BuildHeader()
        {
            var header = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, AutoSize = false, Size = new Size(D(470), D(76)), Margin = new Padding(0, 0, 0, D(4)) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left };
            var title = new Label { Text = "ASUS Dial", AutoSize = true, Font = new Font(Font.FontFamily, Font.Size * 1.5f, FontStyle.Bold), Margin = new Padding(0, 0, 0, D(2)) };
            titles.Controls.Add(title);
            status.Margin = new Padding(D(2), 0, 0, 0);
            titles.Controls.Add(status);
            var hint = new Label { Text = "Turn or press the dial: the ring follows it.", AutoSize = true, ForeColor = Muted, Margin = new Padding(D(2), D(2), 0, 0) };
            titles.Controls.Add(hint);

            indicator.Size = new Size(D(64), D(64));
            indicator.Margin = new Padding(D(8), 0, 0, 0);
            tips.SetToolTip(indicator, "Live: the dot turns with the dial, the centre lights up while pressed");

            header.Controls.Add(titles, 0, 0);
            header.Controls.Add(indicator, 1, 0);
            return header;
        }

        void BuildModesPanel()
        {
            modesPanel.FlowDirection = FlowDirection.TopDown;
            modesPanel.WrapContents = false;
            modesPanel.AutoSize = true;
            modesPanel.Margin = new Padding(0);

            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            modeList.CheckOnClick = true;
            modeList.IntegralHeight = false;
            modeList.Width = D(240);
            modeList.Margin = new Padding(D(2), D(2), D(8), D(2));
            modeList.AccessibleName = "Dial modes in order";
            modeList.DisplayMember = nameof(DialMode.Name);

            loading = true;
            foreach (var (mode, enabled) in DialModes.ForPicker())
                modeList.SetItemChecked(modeList.Items.Add(mode), enabled);
            loading = false;

            // every mode visible, no scrollbar; item height is only final once the control has its handle and scaled font
            void FitList() => modeList.Height = modeList.ItemHeight * modeList.Items.Count + D(6);
            FitList();
            modeList.HandleCreated += delegate { FitList(); };
            modeList.FontChanged += delegate { FitList(); };
            modeList.ItemCheck += delegate { BeginInvoke(SaveModes); };   // checked state is applied after the event

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
            var up = Button("Move up", (_, _) => MoveMode(-1));
            var down = Button("Move down", (_, _) => MoveMode(1));
            up.MinimumSize = down.MinimumSize = new Size(D(100), 0);
            buttons.Controls.Add(up);
            buttons.Controls.Add(down);

            row.Controls.Add(modeList);
            row.Controls.Add(buttons);
            modesPanel.Controls.Add(row);

            currentMode.Margin = new Padding(D(2), D(4), 0, 0);
            modesPanel.Controls.Add(currentMode);
        }

        void SaveModes()
        {
            if (loading) return;
            DialModes.Save(modeList.Items.Cast<DialMode>().Where((m, i) => modeList.GetItemChecked(i)).Select(m => m.Id));
            UpdateCurrentMode();
        }

        void MoveMode(int delta)
        {
            int i = modeList.SelectedIndex, j = i + delta;
            if (i < 0 || j < 0 || j >= modeList.Items.Count) return;
            var item = modeList.Items[i];
            bool isChecked = modeList.GetItemChecked(i);
            loading = true;
            modeList.Items.RemoveAt(i);
            modeList.Items.Insert(j, item);
            modeList.SetItemChecked(j, isChecked);
            modeList.SelectedIndex = j;
            loading = false;
            SaveModes();
        }

        void BuildBindingsPanel()
        {
            bindingsPanel.FlowDirection = FlowDirection.TopDown;
            bindingsPanel.WrapContents = false;
            bindingsPanel.AutoSize = true;
            bindingsPanel.Margin = new Padding(0);
            bindingsPanel.Controls.Add(BindingRows(null, ("dial_cw", "Turn right"), ("dial_ccw", "Turn left"), ("dial_press", "Press")));
        }

        Control BuildGestures()
        {
            var cells = new Dictionary<string, FlowLayoutPanel>();
            var table = BindingRows(cells, ("dial_double", "Double press"), ("dial_long", "Hold"));

            // with the mode wheel on, hold opens the wheel instead of running a binding: the note takes the dropdown's place
            var holdCell = cells["dial_long"];
            holdBinding = holdCell;
            holdWheelNote = new Label { Text = "Opens the mode wheel (see Options)", AutoSize = true, ForeColor = Muted, Anchor = AnchorStyles.Left, Margin = new Padding(D(4), D(6), 0, D(6)) };
            int row = table.GetRow(holdCell);
            var swap = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            table.Controls.Remove(holdCell);
            swap.Controls.Add(holdCell);
            swap.Controls.Add(holdWheelNote);
            table.Controls.Add(swap, 1, row);
            return table;
        }

        /// <summary>One row per gesture: a label, then the action dropdown and the custom-command box together in one cell.</summary>
        TableLayoutPanel BindingRows(Dictionary<string, FlowLayoutPanel>? cells, params (string key, string title)[] rows)
        {
            var table = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 0, 0, D(4)) };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, D(110)));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            for (int r = 0; r < rows.Length; r++)
            {
                var (key, title) = rows[r];
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                var label = new Label { Text = title, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(D(2), 0, 0, 0) };
                var combo = new RComboBox { Width = D(200), FormattingEnabled = true, Margin = new Padding(D(2), D(2), D(4), D(2)), BorderColor = Color.White, ButtonColor = Color.FromArgb(255, 255, 255), AccessibleName = "Dial " + title };
                var text = new RTextBox { Width = D(150), Margin = new Padding(D(2), D(2), 0, D(2)), PlaceholderText = "action", TabStop = false };
                var cell = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
                cell.Controls.Add(combo);
                cell.Controls.Add(text);
                table.Controls.Add(label, 0, r);
                table.Controls.Add(cell, 1, r);
                Extra.SetKeyCombo(combo, text, key);
                cells?.Add(key, cell);
            }
            return table;
        }

        Control BuildOptions()
        {
            var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0) };

            var checkWheel = new CheckBox { AutoSize = true, Text = "Hold opens a mode wheel (with dial modes)", Checked = AppConfig.IsNotFalse("dial_wheel"), Margin = new Padding(D(2), D(2), 0, D(2)) };
            tips.SetToolTip(checkWheel, "Hold the dial to see every mode at once, turn to choose, press to pick");
            checkWheel.CheckedChanged += delegate
            {
                AppConfig.Set("dial_wheel", checkWheel.Checked ? 1 : 0);
                UpdateHoldRow();
            };
            flow.Controls.Add(checkWheel);
            flow.Controls.Add(new Label
            {
                Text = "Off: hold runs its own binding (above) and a press simply cycles to the next mode, as before.",
                AutoSize = true,
                MaximumSize = new Size(D(450), 0),
                ForeColor = Muted,
                Margin = new Padding(D(20), 0, 0, D(4)),
            });

            var checkRing = new CheckBox { AutoSize = true, Text = "Show the on-screen ring for dial modes", Checked = AppConfig.IsNotFalse("dial_osd"), Margin = new Padding(D(2), D(2), 0, D(2)) };
            tips.SetToolTip(checkRing, "A ring with the mode's icon and value; off shows the plain text toasts instead");
            checkRing.CheckedChanged += delegate { AppConfig.Set("dial_osd", checkRing.Checked ? 1 : 0); };
            flow.Controls.Add(checkRing);
            flow.Controls.Add(new Label
            {
                Text = "Off: plain text notifications, as before.",
                AutoSize = true,
                MaximumSize = new Size(D(450), 0),
                ForeColor = Muted,
                Margin = new Padding(D(20), 0, 0, D(4)),
            });

            var holdRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(D(2), D(2), 0, D(2)) };
            holdRow.Controls.Add(new Label { Text = "Hold time", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, D(6), D(8), 0) });
            var holdTime = new RComboBox { Width = D(150), DropDownStyle = ComboBoxStyle.DropDownList, BorderColor = Color.White, ButtonColor = Color.FromArgb(255, 255, 255), AccessibleName = "Hold time" };
            var holdValues = new[] { (400, "Short (0.4 s)"), (600, "Normal (0.6 s)"), (900, "Long (0.9 s)") };
            foreach (var (_, name) in holdValues) holdTime.Items.Add(name);
            int currentHold = AppConfig.Get("dial_long_ms", 600);
            holdTime.SelectedIndex = Array.FindIndex(holdValues, v => v.Item1 >= currentHold) is int idx && idx >= 0 ? idx : 1;
            holdTime.SelectedIndexChanged += delegate { AppConfig.Set("dial_long_ms", holdValues[holdTime.SelectedIndex].Item1); };
            holdRow.Controls.Add(holdTime);
            flow.Controls.Add(holdRow);

            var checkTakeover = new CheckBox { AutoSize = true, Text = "Take over the dial from ASUS software (needs admin)", Checked = DialTakeover.IsEnabled(), Margin = new Padding(D(2), D(2), 0, 0) };
            tips.SetToolTip(checkTakeover, "Stops ASUS's own Dial service, which otherwise reacts to the same turns and presses");
            bool reverting = false;
            checkTakeover.CheckedChanged += delegate
            {
                if (reverting) return;
                if (!DialTakeover.Apply(checkTakeover.Checked))
                {
                    MessageBox.Show("Stopping the ASUS Dial service needs administrator rights. Restart G-Helper as administrator and try again.", "ASUS Dial", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    reverting = true;
                    checkTakeover.Checked = DialTakeover.IsEnabled();
                    reverting = false;
                }
            };
            flow.Controls.Add(checkTakeover);
            flow.Controls.Add(new Label
            {
                Text = "ASUS's own Dial service reacts to the same turns and presses, so with both running every action fires twice.",
                AutoSize = true,
                MaximumSize = new Size(D(450), 0),
                ForeColor = Muted,
                Margin = new Padding(D(20), 0, 0, 0),
            });

            return flow;
        }

        // ------------------------------------------------------------ behaviour

        void SetupTab(RButton tab, string text, bool modes)
        {
            tab.Text = text;
            tab.BorderColor = RForm.colorStandard;
            tab.AutoSize = true;
            tab.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            tab.Padding = new Padding(D(10), D(4), D(10), D(4));
            tab.Margin = new Padding(0, 0, D(6), 0);
            tab.Click += (_, _) =>
            {
                AppConfig.Set("dial_modes_on", modes ? 1 : 0);
                ShowSection(modes);
            };
        }

        void ShowSection(bool modes)
        {
            tabModes.Activated = modes;
            tabBindings.Activated = !modes;
            modesPanel.Visible = modes;
            bindingsPanel.Visible = !modes;
            explain.Text = modes
                ? "Press the dial to switch between the ticked modes, turn it to adjust the current one. Put them in the order you want."
                : "Turning and pressing run the actions you choose here.";
            UpdateHoldRow();
            UpdateCurrentMode();
        }

        void UpdateHoldRow()
        {
            bool wheel = AppConfig.Is("dial_modes_on") && AppConfig.IsNotFalse("dial_wheel");
            if (holdBinding is not null) holdBinding.Visible = !wheel;
            if (holdWheelNote is not null) holdWheelNote.Visible = wheel;
        }

        void UpdateCurrentMode()
        {
            var mode = DialModes.Current();
            currentMode.Text = mode is null ? "Tick at least one mode." : "Now on: " + mode.Name;
        }

        void UpdateStatus()
        {
            var listener = Program.dialListener;
            if (listener is null)
            {
                status.Text = "Dial support is not running";
                status.ForeColor = Muted;
            }
            else if (listener.IsConnected)
            {
                status.Text = "● Connected";
                status.ForeColor = RForm.colorEco;
            }
            else
            {
                status.Text = "○ Looking for the dial…";
                status.ForeColor = Muted;
            }
            if (modesPanel.Visible) UpdateCurrentMode();
        }

        void OnActivity(string what, int delta)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(() =>
            {
                switch (what)
                {
                    case "turn": indicator.Turn(delta); break;
                    case "down": indicator.SetPressed(true); break;
                    case "up": indicator.SetPressed(false); break;
                }
            });
        }

        Label Section(string text) => new()
        {
            Text = text.ToUpperInvariant(),
            AutoSize = true,
            ForeColor = Muted,
            Margin = new Padding(D(2), D(12), 0, D(4)),
        };

        Button Button(string text, EventHandler onClick)
        {
            var button = new RButton
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(D(10), D(3), D(10), D(3)),
                Margin = new Padding(0, D(2), D(6), D(2)),
            };
            button.Click += onClick;
            return button;
        }
    }

    /// <summary>A small picture of the dial: a ring with a dot that turns with it, and a centre that lights while pressed.</summary>
    public class DialIndicator : Control
    {
        double angle;
        bool pressed;

        public DialIndicator()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public void Turn(int delta)
        {
            angle = (angle + delta * 24) % 360;
            Invalidate();
        }

        public void SetPressed(bool down)
        {
            pressed = down;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? BackColor);

            float size = Math.Min(Width, Height) - 2;
            float stroke = size * 0.08f;
            var ring = new RectangleF((Width - size) / 2 + stroke / 2, (Height - size) / 2 + stroke / 2, size - stroke, size - stroke);

            using (var pen = new Pen(Color.FromArgb(110, 110, 110), stroke)) g.DrawEllipse(pen, ring);

            // the marker rides on the ring
            double a = (angle - 90) * Math.PI / 180;
            float cx = Width / 2f, cy = Height / 2f, r = ring.Width / 2;
            float dot = stroke * 1.9f;
            using (var accent = new SolidBrush(RForm.colorStandard))
                g.FillEllipse(accent, cx + (float)(Math.Cos(a) * r) - dot / 2, cy + (float)(Math.Sin(a) * r) - dot / 2, dot, dot);

            // centre button
            float centre = size * 0.36f;
            var centreRect = new RectangleF(cx - centre / 2, cy - centre / 2, centre, centre);
            if (pressed)
                using (var fill = new SolidBrush(RForm.colorStandard)) g.FillEllipse(fill, centreRect);
            else
                using (var pen = new Pen(Color.FromArgb(110, 110, 110), stroke * 0.6f)) g.DrawEllipse(pen, centreRect);
        }
    }
}
