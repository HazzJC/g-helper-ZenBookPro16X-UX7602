using GHelper.Display;
using GHelper.Gpu;
using GHelper.Gpu.AMD;
using GHelper.Helpers;
using GHelper.Input;
using GHelper.Mode;
using GHelper.UI;
using GHelper.USB;
using System.Diagnostics;

namespace GHelper
{
    public partial class Extra : RForm
    {

        ClamshellModeControl clamshellControl = new ClamshellModeControl();

        int[] vramOptions = [];

        int coresMinP = AsusACPI.PCoreMin;
        int coresMinE = AsusACPI.ECoreMin;

        const string EMPTY = "--------------";


        private void SetKeyCombo(ComboBox combo, TextBox txbox, string name)
        {
            if (combo is RComboBox rcombo) rcombo.NativeHeight = true;

            Dictionary<string, string> customActions = new Dictionary<string, string>
            {
              {"", EMPTY},
              {"volume_down", Properties.Strings.VolumeDown},
              {"volume_up", Properties.Strings.VolumeUp},
              {"backlight_down", Properties.Strings.BacklightDown},
              {"backlight_up", Properties.Strings.BacklightUp},
              {"mute", Properties.Strings.VolumeMute},
              {"screenshot", Properties.Strings.PrintScreen},
              {"play", Properties.Strings.PlayPause},
              {"next_track", "Next track"},
              {"prev_track", "Previous track"},
              {"aura", Properties.Strings.ToggleAura},
              {"performance", Properties.Strings.PerformanceMode},
              {"screen", Properties.Strings.ToggleScreen},
              {"lock", Properties.Strings.LockScreen},
              {"miniled", Properties.Strings.ToggleMiniled},
              {"fnlock", Properties.Strings.ToggleFnLock},
              {"brightness_down", Properties.Strings.BrightnessDown},
              {"brightness_up", Properties.Strings.BrightnessUp},
              {"visual", Properties.Strings.VisualMode},
              {"touchpad", Properties.Strings.ToggleTouchpad},
              {"touchscreen", Properties.Strings.ToggleTouchscreen },
              {"micmute", Properties.Strings.MuteMic},
              {"ghelper", Properties.Strings.OpenGHelper},
              {"overlay", Properties.Strings.Overlay},
              {"custom", Properties.Strings.Custom}
            };

            if (AppConfig.IsZenbookPro16X())
            {
                customActions.Add("backlight_fine_down", "Backlight dimmer (fine)");
                customActions.Add("backlight_fine_up", "Backlight brighter (fine)");
            }

            if (AppConfig.IsDUO())
            {
                customActions.Add("screenpad_down", Properties.Strings.ScreenPadDown);
                customActions.Add("screenpad_up", Properties.Strings.ScreenPadUp);
            }

            if (AppConfig.IsAlly())
            {
                customActions.Add("controller", "Controller Mode");
            }

            switch (name)
            {
                case "m1":
                    customActions[""] = Properties.Strings.VolumeDown;
                    customActions.Remove("volume_down");
                    break;
                case "m2":
                    customActions[""] = Properties.Strings.VolumeUp;
                    customActions.Remove("volume_up");
                    break;
                case "m3":
                    customActions[""] = Properties.Strings.MuteMic;
                    customActions.Remove("micmute");
                    break;
                case "m4":
                    customActions[""] = Properties.Strings.OpenGHelper;
                    customActions.Remove("ghelper");
                    break;
                case "m5":
                    customActions[""] = Properties.Strings.PerformanceMode;
                    customActions.Remove("performance");
                    break;
                case "fnf4":
                    customActions[""] = Properties.Strings.ToggleAura;
                    customActions.Remove("aura");
                    break;
                case "fnc":
                    customActions[""] = Properties.Strings.ToggleFnLock;
                    customActions.Remove("fnlock");
                    break;
                case "fnv":
                    customActions[""] = Properties.Strings.VisualMode;
                    customActions.Remove("visual");
                    break;
                case "fne":
                    customActions[""] = "Calculator";
                    break;
                case "paddle":
                    customActions[""] = EMPTY;
                    break;
                case "cc":
                    customActions[""] = EMPTY;
                    break;
            }

            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.DataSource = new BindingSource(customActions, null);
            combo.DisplayMember = "Value";
            combo.ValueMember = "Key";

            string action = AppConfig.GetString(name);

            combo.SelectedValue = (action is not null) ? action : "";
            if (combo.SelectedValue is null) combo.SelectedValue = "";

            combo.SelectedValueChanged += delegate
            {
                if (combo.SelectedValue is not null)
                    AppConfig.Set(name, combo.SelectedValue.ToString());

                if (name == "m1" || name == "m2" || name == "m3" || name == "m4" || name == "m5")
                {
                    MKeyControl.ApplyAll();
                    Program.inputDispatcher.RegisterKeys();
                }

            };

            txbox.Text = AppConfig.GetString(name + "_custom");
            txbox.TextChanged += delegate
            {
                AppConfig.Set(name + "_custom", txbox.Text);
            };
        }

        // ZenBook Pro 16X: smooth brightness (multiplies with the four hardware levels above) and the MyASUS lighting agent guard
        Slider? fineSlider;
        Label? fineValueLabel;

        public void VisualiseFineBrightness(int percent)
        {
            if (fineSlider is null) return;
            if (InvokeRequired) { BeginInvoke(() => VisualiseFineBrightness(percent)); return; }   // async: never block the dial thread
            fineSlider.Value = percent;   // its ValueChanged re-applies the same value, which is harmless
            fineValueLabel!.Text = percent + "%";
        }

        private void AddFineBrightness()
        {
            int row = tableBacklight.RowCount;
            tableBacklight.RowCount += 2;
            tableBacklight.RowStyles.Add(new RowStyle());
            tableBacklight.RowStyles.Add(new RowStyle());

            var label = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(Dpx(5), Dpx(6), Dpx(5), 0), Padding = new Padding(Dpx(9), Dpx(5), Dpx(7), Dpx(5)), Text = "Fine brightness" };
            fineSlider = new Slider { Min = KeyboardBrightness.MinPercent, Max = 100, Step = 1, Value = KeyboardBrightness.Percent, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(Dpx(5), Dpx(6), Dpx(5), 0), Height = Dpx(40), MinimumSize = new Size(Dpx(300), Dpx(40)), TabStop = true, AccessibleName = "Fine keyboard brightness" };
            fineValueLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(Dpx(5), Dpx(6), Dpx(5), 0), Padding = new Padding(0, Dpx(5), 0, Dpx(5)), Text = KeyboardBrightness.Percent + "%" };

            tableBacklight.Controls.Add(label, 0, row);
            tableBacklight.Controls.Add(fineSlider, 1, row);
            tableBacklight.SetColumnSpan(fineSlider, 3);
            tableBacklight.Controls.Add(fineValueLabel, 4, row);

            fineSlider.ValueChanged += delegate
            {
                int percent = fineSlider.Value;
                fineValueLabel.Text = percent + "%";
                fineSlider.AccessibleName = "Fine keyboard brightness: " + percent + "%";
                if (percent != KeyboardBrightness.Percent)
                {
                    InputDispatcher.EnsureFineHardwareLevel();
                    KeyboardBrightness.Set(percent);
                }
            };

            var checkAgent = new CheckBox { AutoSize = true, Margin = new Padding(Dpx(14), Dpx(6), 0, Dpx(3)), Text = "Stop MyASUS lighting agent (it re-applies ASUS's effect on Fn+F7)", Checked = AsusLightingAgent.IsEnabled, AccessibleName = "Stop MyASUS lighting agent" };
            tableBacklight.Controls.Add(checkAgent, 0, row + 1);
            tableBacklight.SetColumnSpan(checkAgent, 5);
            checkAgent.CheckedChanged += delegate { AsusLightingAgent.SetEnabled(checkAgent.Checked); };
        }

        // The form is authored at 192 DPI and auto-scaled when it is shown, but controls created in code after
        // InitializeComponent are not, so size them from the current DPI (same baseline as the designer values).
        private int Dpx(int designerPx) => (int)Math.Round(designerPx * DeviceDpi / 192f);

        // Dial modes: press cycles through the ticked modes, turning adjusts the current one
        private void AddDialModePicker(List<ComboBox> modeBound)
        {
            int row = tableBindings.RowCount++;
            tableBindings.RowStyles.Add(new RowStyle());

            var panel = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(0, Dpx(8), 0, Dpx(3)) };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var checkModes = new CheckBox { AutoSize = true, Margin = new Padding(Dpx(5), Dpx(3), 0, Dpx(3)), Text = "Dial modes: press switches, turn adjusts", Checked = AppConfig.Is("dial_modes_on"), AccessibleName = "Dial modes" };
            var list = new CheckedListBox { CheckOnClick = true, IntegralHeight = false, Width = Dpx(420), Margin = new Padding(Dpx(25), Dpx(3), Dpx(5), Dpx(3)), AccessibleName = "Dial modes in order" };
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0, Dpx(3), 0, Dpx(3)) };
            var buttonUp = new RButton { Text = "Move up", AutoSize = false, Size = new Size(Dpx(170), Dpx(46)), Margin = new Padding(Dpx(5), Dpx(3), Dpx(5), Dpx(3)), AccessibleName = "Move dial mode up" };
            var buttonDown = new RButton { Text = "Move down", AutoSize = false, Size = buttonUp.Size, Margin = buttonUp.Margin, AccessibleName = "Move dial mode down" };
            buttons.Controls.Add(buttonUp);
            buttons.Controls.Add(buttonDown);

            panel.Controls.Add(checkModes, 0, 0);
            panel.SetColumnSpan(checkModes, 2);
            panel.Controls.Add(list, 0, 1);
            panel.Controls.Add(buttons, 1, 1);

            tableBindings.Controls.Add(panel, 0, row);
            tableBindings.SetColumnSpan(panel, 3);

            bool loading = true;

            void Save()
            {
                if (loading) return;
                var ids = list.Items.Cast<DialMode>().Where((m, i) => list.GetItemChecked(i)).Select(m => m.Id);
                DialModes.Save(ids);
            }

            void UpdateEnabled()
            {
                list.Enabled = buttonUp.Enabled = buttonDown.Enabled = checkModes.Checked;
                foreach (var combo in modeBound) combo.Enabled = !checkModes.Checked;
            }

            list.DisplayMember = nameof(DialMode.Name);
            foreach (var (mode, enabled) in DialModes.ForPicker())
                list.SetItemChecked(list.Items.Add(mode), enabled);
            // show every mode without a scrollbar; item height is only final once the control has its handle and scaled font
            void FitList() => list.Height = list.ItemHeight * list.Items.Count + Dpx(10);
            FitList();
            list.HandleCreated += delegate { FitList(); };
            list.FontChanged += delegate { FitList(); };
            loading = false;
            UpdateEnabled();

            list.ItemCheck += delegate { BeginInvoke(Save); };   // checked state is applied after the event
            checkModes.CheckedChanged += delegate
            {
                AppConfig.Set("dial_modes_on", checkModes.Checked ? 1 : 0);
                UpdateEnabled();
            };

            void Move(int delta)
            {
                int i = list.SelectedIndex, j = i + delta;
                if (i < 0 || j < 0 || j >= list.Items.Count) return;
                var item = list.Items[i];
                bool isChecked = list.GetItemChecked(i);
                loading = true;
                list.Items.RemoveAt(i);
                list.Items.Insert(j, item);
                list.SetItemChecked(j, isChecked);
                list.SelectedIndex = j;
                loading = false;
                Save();
            }

            buttonUp.Click += delegate { Move(-1); };
            buttonDown.Click += delegate { Move(1); };
        }

        // ASUS Dial (ZenBook Pro 16X): a heading, one binding row per dial gesture, dial modes, and the ASUS takeover option
        private void AddDialBindings()
        {
            var rows = new (string key, string title)[]
            {
                ("dial_cw", "Dial right:"),
                ("dial_ccw", "Dial left:"),
                ("dial_press", "Dial press:"),
                ("dial_double", "Dial double:"),
                ("dial_long", "Dial hold:"),
            };

            int headingRow = tableBindings.RowCount++;
            tableBindings.RowStyles.Add(new RowStyle());
            var heading = new Label { AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, Dpx(10), 0, 0), Padding = new Padding(Dpx(5), Dpx(11), 0, 0), Text = "ASUS Dial" };
            tableBindings.Controls.Add(heading, 0, headingRow);
            tableBindings.SetColumnSpan(heading, 3);

            var modeBound = new List<ComboBox>();   // bindings that dial modes take over while enabled

            foreach (var (key, title) in rows)
            {
                int row = tableBindings.RowCount++;
                tableBindings.RowStyles.Add(new RowStyle());

                var label = new Label { AutoSize = true, Margin = new Padding(0), Padding = new Padding(Dpx(5), Dpx(11), 0, 0), Text = title };
                var combo = new RComboBox { Dock = DockStyle.Top, FormattingEnabled = true, Margin = new Padding(Dpx(5), Dpx(3), Dpx(5), Dpx(3)), BorderColor = Color.White, ButtonColor = Color.FromArgb(255, 255, 255), AccessibleName = title };
                var text = new RTextBox { Dock = DockStyle.Top, Margin = new Padding(Dpx(5), Dpx(3), Dpx(5), Dpx(3)), PlaceholderText = "action", TabStop = false };

                tableBindings.Controls.Add(label, 0, row);
                tableBindings.Controls.Add(combo, 1, row);
                tableBindings.Controls.Add(text, 2, row);

                SetKeyCombo(combo, text, key);
                if (key is "dial_cw" or "dial_ccw" or "dial_press") modeBound.Add(combo);
            }

            AddDialModePicker(modeBound);

            int checkRow = tableBindings.RowCount++;
            tableBindings.RowStyles.Add(new RowStyle());
            var checkTakeover = new CheckBox { AutoSize = true, Margin = new Padding(Dpx(5), Dpx(3), 0, Dpx(3)), Text = "Take over dial from ASUS software (needs admin)", Checked = DialTakeover.IsEnabled(), AccessibleName = "Take over dial from ASUS software" };
            tableBindings.Controls.Add(checkTakeover, 0, checkRow);
            tableBindings.SetColumnSpan(checkTakeover, 3);
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
        }

        public Extra()
        {
            InitializeComponent();

            labelBindings.Text = Properties.Strings.KeyBindings;
            buttonResetBindings.Text = Properties.Strings.Reset;
            labelBacklightTitle.Text = Properties.Strings.LaptopBacklight;
            labelSettings.Text = Properties.Strings.Other;

            checkAwake.Text = Properties.Strings.Awake;
            checkSleep.Text = Properties.Strings.Sleep;
            checkBoot.Text = checkBootLogo.Text = checkBootBar.Text = checkBootLid.Text = Properties.Strings.Boot + "/" + Properties.Strings.Shutdown;
            checkBattery.Text = checkBatteryLogo.Text = checkBatteryBar.Text = checkBatteryLid.Text = Properties.Strings.Battery;
            checkBootSound.Text = Properties.Strings.BootSound;
            checkKeystoneSound.Text = Properties.Strings.KeystoneSound;
            checkStatusLed.Text = Properties.Strings.LEDStatusIndicators;

            labelSpeed.Text = Properties.Strings.AnimationSpeed;
            //labelBrightness.Text = Properties.Strings.Brightness;

            labelBacklightTimeout.Text = Properties.Strings.BacklightTimeout;
            //labelBacklightTimeoutPlugged.Text = Properties.Strings.BacklightTimeoutPlugged;

            checkNoOverdrive.Text = Properties.Strings.DisableOverdrive;
            checkELMB.Text = Properties.Strings.ExtremeLowMotionBlur;
            checkTopmost.Text = Properties.Strings.WindowTop;
            checkUSBC.Text = Properties.Strings.OptimizedUSBC;
            checkAutoToggleClamshellMode.Text = Properties.Strings.ToggleClamshellMode;

            labelBacklightKeyboard.Text = Properties.Strings.Keyboard;
            labelBacklightBar.Text = Properties.Strings.Lightbar;
            labelBacklightLid.Text = Properties.Strings.Lid;
            labelBacklightLogo.Text = Properties.Strings.Logo;

            checkGpuApps.Text = Properties.Strings.KillGpuApps;
            checkAspm.Text = Properties.Strings.DisablePCIeASPM;
            checkStandbyNetworking.Text = Properties.Strings.DisableStandbyNetworking;
            checkNVPlatform.Text = Properties.Strings.StopStartNVServices;
            labelHibernateAfter.Text = Properties.Strings.HibernateAfter;
            numericHibernateAfter.OffText = Properties.Strings.Off;
            numericBacklightTime.OffText = Properties.Strings.Off;
            numericBacklightPluggedTime.OffText = Properties.Strings.Off;

            labelAPUMem.Text = Properties.Strings.APUMemory;
            labelCores.Text = Properties.Strings.CPUCoresConfiguration;

            labelOptimalBrightness.Text = Properties.Strings.OptimalDisplayBrightness;
            comboOptimalBrightness.Items[0] = Properties.Strings.Off;
            comboOptimalBrightness.Items[1] = Properties.Strings.OnAlways;
            comboOptimalBrightness.Items[2] = Properties.Strings.OnBattery;

            Text = Properties.Strings.ExtraSettings;

            // Accessible Labels

            panelServices.AccessibleName = Properties.Strings.AsusServicesRunning;
            panelBindings.AccessibleName = Properties.Strings.KeyBindings;
            tableBindings.AccessibleName = Properties.Strings.KeyBindings;

            comboM1.AccessibleName = "M1 Action";
            comboM2.AccessibleName = "M2 Action";
            comboM3.AccessibleName = "M3 Action";
            comboM4.AccessibleName = "M4 Action";
            comboM5.AccessibleName = "M5 Action";

            labelM5.Visible = comboM5.Visible = textM5.Visible = false;
            comboFNF4.AccessibleName = "Fn+F4 Action";
            comboFNC.AccessibleName = "Fn+C Action";
            comboFNV.AccessibleName = "Fn+V Action";
            comboFNE.AccessibleName = "Fn+Numpad Action";

            numericBacklightPluggedTime.AccessibleName = Properties.Strings.BacklightTimeoutPlugged;
            numericBacklightTime.AccessibleName = Properties.Strings.BacklightTimeoutBattery;

            comboKeyboardSpeed.AccessibleName = Properties.Strings.LaptopBacklight + " " + Properties.Strings.AnimationSpeed;
            comboAPU.AccessibleName = Properties.Strings.LaptopBacklight + " " + Properties.Strings.AnimationSpeed;

            checkBoot.AccessibleName = Properties.Strings.Boot + "/" + Properties.Strings.Shutdown + " " + Properties.Strings.LaptopBacklight;
            checkAwake.AccessibleName = Properties.Strings.Awake + " " + Properties.Strings.LaptopBacklight;
            checkSleep.AccessibleName = Properties.Strings.Sleep + " " + Properties.Strings.LaptopBacklight;

            panelSettings.AccessibleName = Properties.Strings.ExtraSettings;
            numericHibernateAfter.AccessibleName = Properties.Strings.HibernateAfter;

            if (AppConfig.IsARCNM())
            {
                labelM3.Text = "FN+F6";
                labelM1.Visible = comboM1.Visible = textM1.Visible = false;
                labelM2.Visible = comboM2.Visible = textM2.Visible = false;
                labelM4.Visible = comboM4.Visible = textM4.Visible = false;
                labelFNF4.Visible = comboFNF4.Visible = textFNF4.Visible = false;
            }

            if (AppConfig.NoMKeys())
            {
                labelM1.Text = "FN+F2";
                labelM2.Text = "FN+F3";
                labelM3.Text = "FN+F4";
                labelM4.Visible = comboM4.Visible = textM4.Visible = AppConfig.IsM4Button();
                labelFNF4.Visible = comboFNF4.Visible = textFNF4.Visible = false;
            }

            if (AppConfig.IsVivoZenPro())
            {
                labelM1.Visible = comboM1.Visible = textM1.Visible = false;
                labelM2.Visible = comboM2.Visible = textM2.Visible = false;
                labelM3.Visible = comboM3.Visible = textM3.Visible = false;
                labelFNF4.Visible = comboFNF4.Visible = textFNF4.Visible = false;
                labelM4.Text = "FN+F12";
            }

            if (AppConfig.MediaKeys())
            {
                labelFNF4.Visible = comboFNF4.Visible = textFNF4.Visible = false;
            }

            if (!AppConfig.IsTUF())
            {
                labelFNE.Visible = comboFNE.Visible = textFNE.Visible = false;
            }

            if (AppConfig.IsNoFNV())
            {
                labelFNV.Visible = comboFNV.Visible = textFNV.Visible = false;
            }

            if (!Program.acpi.IsSupported(AsusACPI.GPUEco))
            {
                checkGpuApps.Visible = false;
                checkUSBC.Visible = false;
            }

            checkNoOverdrive.Visible = Program.acpi.IsOverdriveSupported();

            // Change text and hide irrelevant options on the ROG Ally,
            // which is a bit of a special case piece of hardware.
            if (AppConfig.IsAlly())
            {
                labelM1.Visible = comboM1.Visible = textM1.Visible = false;
                labelM2.Visible = comboM2.Visible = textM2.Visible = false;

                // Re-label M3 and M4 and FNF4 to match the front labels.
                labelM3.Text = "Cmd Center";
                labelM4.Text = "ROG";
                labelFNF4.Text = "Back Paddles";

                // Hide all of the FN options, as the Ally has no special keyboard FN key.
                labelFNC.Visible = false;
                comboFNC.Visible = false;
                textFNC.Visible = false;

                labelFNV.Visible = false;
                comboFNV.Visible = false;
                textFNV.Visible = false;

                SetKeyCombo(comboM3, textM3, "cc");
                SetKeyCombo(comboM4, textM4, "m4");
                SetKeyCombo(comboFNF4, textFNF4, "paddle");

                checkGpuApps.Visible = false;
                checkUSBC.Visible = false;
                checkAutoToggleClamshellMode.Visible = false;
            }
            else
            {
                SetKeyCombo(comboM1, textM1, "m1");
                SetKeyCombo(comboM2, textM2, "m2");

                SetKeyCombo(comboM3, textM3, "m3");
                SetKeyCombo(comboM4, textM4, "m4");
                SetKeyCombo(comboFNF4, textFNF4, "fnf4");

                SetKeyCombo(comboFNC, textFNC, "fnc");
                SetKeyCombo(comboFNV, textFNV, "fnv");
                SetKeyCombo(comboFNE, textFNE, "fne");
            }

            if (AppConfig.IsStrix())
            {
                labelM4.Text = "M5/ROG";
                labelM5.Visible = comboM5.Visible = textM5.Visible = true;
                SetKeyCombo(comboM5, textM5, "m5");
            }


            if (DialListener.IsSupported()) AddDialBindings();
            if (AppConfig.IsZenbookPro16X()) AddFineBrightness();

            InitTheme();
            Shown += Keyboard_Shown;

            comboKeyboardSpeed.DropDownStyle = ComboBoxStyle.DropDownList;
            comboKeyboardSpeed.DataSource = new BindingSource(Aura.GetSpeeds(), null);
            comboKeyboardSpeed.DisplayMember = "Value";
            comboKeyboardSpeed.ValueMember = "Key";
            comboKeyboardSpeed.SelectedValue = Aura.Speed;
            comboKeyboardSpeed.SelectedValueChanged += ComboKeyboardSpeed_SelectedValueChanged;

            // Keyboard
            checkAwake.Checked = AppConfig.IsNotFalse("keyboard_awake");
            checkBattery.Checked = AppConfig.IsOnBattery("keyboard_awake");
            checkBoot.Checked = AppConfig.IsNotFalse("keyboard_boot");
            checkSleep.Checked = AppConfig.IsNotFalse("keyboard_sleep");

            // Lightbar
            checkAwakeBar.Checked = AppConfig.IsNotFalse("keyboard_awake_bar");
            checkBatteryBar.Checked = AppConfig.IsOnBattery("keyboard_awake_bar");
            checkBootBar.Checked = AppConfig.IsNotFalse("keyboard_boot_bar");
            checkSleepBar.Checked = AppConfig.IsNotFalse("keyboard_sleep_bar");

            // Lid
            checkAwakeLid.Checked = AppConfig.IsNotFalse("keyboard_awake_lid");
            checkBatteryLid.Checked = AppConfig.IsOnBattery("keyboard_awake_lid");
            checkBootLid.Checked = AppConfig.IsNotFalse("keyboard_boot_lid");
            checkSleepLid.Checked = AppConfig.IsNotFalse("keyboard_sleep_lid");

            // Logo
            checkAwakeLogo.Checked = AppConfig.IsNotFalse("keyboard_awake_logo");
            checkBatteryLogo.Checked = AppConfig.IsOnBattery("keyboard_awake_logo");
            checkBootLogo.Checked = AppConfig.IsNotFalse("keyboard_boot_logo");
            checkSleepLogo.Checked = AppConfig.IsNotFalse("keyboard_sleep_logo");

            checkAwake.CheckedChanged += CheckPower_CheckedChanged;
            checkBattery.CheckedChanged += CheckPower_CheckedChanged;
            checkBoot.CheckedChanged += CheckPower_CheckedChanged;
            checkSleep.CheckedChanged += CheckPower_CheckedChanged;

            checkAwakeBar.CheckedChanged += CheckPower_CheckedChanged;
            checkBatteryBar.CheckedChanged += CheckPower_CheckedChanged;
            checkBootBar.CheckedChanged += CheckPower_CheckedChanged;
            checkSleepBar.CheckedChanged += CheckPower_CheckedChanged;

            checkAwakeLid.CheckedChanged += CheckPower_CheckedChanged;
            checkBatteryLid.CheckedChanged += CheckPower_CheckedChanged;
            checkBootLid.CheckedChanged += CheckPower_CheckedChanged;
            checkSleepLid.CheckedChanged += CheckPower_CheckedChanged;

            checkAwakeLogo.CheckedChanged += CheckPower_CheckedChanged;
            checkBatteryLogo.CheckedChanged += CheckPower_CheckedChanged;
            checkBootLogo.CheckedChanged += CheckPower_CheckedChanged;
            checkSleepLogo.CheckedChanged += CheckPower_CheckedChanged;

            if (!AppConfig.IsBacklightZones() || AppConfig.IsARCNM())
            {
                labelBacklightKeyboard.Visible = false;
                checkBattery.Visible = false;
            }

            if (!Aura.HasLightbar)
            {
                labelBacklightBar.Visible = false;
                checkAwakeBar.Visible = false;
                checkBatteryBar.Visible = false;
                checkBootBar.Visible = false;
                checkSleepBar.Visible = false;
            }

            if (!Aura.HasLogo)
            {
                labelBacklightLogo.Visible = false;
                checkAwakeLogo.Visible = false;
                checkBatteryLogo.Visible = false;
                checkBootLogo.Visible = false;
                checkSleepLogo.Visible = false;
            }

            if (!Aura.HasRearglow)
            {
                labelBacklightLid.Visible = false;
                checkAwakeLid.Visible = false;
                checkBatteryLid.Visible = false;
                checkBootLid.Visible = false;
                checkSleepLid.Visible = false;
            }

            //checkAutoToggleClamshellMode.Visible = clamshellControl.IsExternalDisplayConnected();
            checkAutoToggleClamshellMode.Checked = AppConfig.Is("toggle_clamshell_mode");
            checkAutoToggleClamshellMode.CheckedChanged += checkAutoToggleClamshellMode_CheckedChanged;

            checkTopmost.Checked = AppConfig.Is("topmost");
            checkTopmost.CheckedChanged += CheckTopmost_CheckedChanged; ;

            checkNoOverdrive.Checked = AppConfig.IsNoOverdrive();
            checkNoOverdrive.CheckedChanged += CheckNoOverdrive_CheckedChanged;

            int elmb = ScreenELMB.Get();
            checkELMB.Visible = elmb >= 0;
            checkELMB.Checked = elmb == 1;
            checkELMB.CheckedChanged += CheckELMB_CheckedChanged;

            checkUSBC.Checked = AppConfig.Is("optimized_usbc");
            checkUSBC.CheckedChanged += CheckUSBC_CheckedChanged;

            sliderBrightness.Value = InputDispatcher.GetBacklight();
            sliderBrightness.AccessibleName = Properties.Strings.LaptopBacklight + ": " + sliderBrightness.Value;
            sliderBrightness.ValueChanged += SliderBrightness_ValueChanged;

            panelXGM.Visible = XGM.IsConnected();
            checkXGM.Checked = !(AppConfig.Get("xmg_light") == 0);
            checkXGM.CheckedChanged += CheckXGM_CheckedChanged;

            numericBacklightTime.Value = AppConfig.Get("keyboard_timeout", 60);
            numericBacklightPluggedTime.Value = AppConfig.Get("keyboard_ac_timeout", 0);

            numericBacklightTime.ValueChanged += NumericBacklightTime_ValueChanged;
            numericBacklightPluggedTime.ValueChanged += NumericBacklightTime_ValueChanged;

            checkGpuApps.Checked = AppConfig.Is("kill_gpu_apps");
            checkGpuApps.CheckedChanged += CheckGpuApps_CheckedChanged;

            int bootSound = Program.acpi.DeviceGet(AsusACPI.BootSound);
            checkBootSound.Visible = bootSound >= 0;
            if (bootSound < 0 || bootSound > UInt16.MaxValue) bootSound = AppConfig.Get("boot_sound", 0);

            checkBootSound.Checked = (bootSound == 1);
            checkBootSound.CheckedChanged += CheckBootSound_CheckedChanged;

            var statusLed = Program.acpi.DeviceGet(AsusACPI.StatusLed);
            checkStatusLed.Visible = statusLed >= 0;
            checkStatusLed.Checked = (statusLed > 0);
            checkStatusLed.CheckedChanged += CheckLEDStatus_CheckedChanged;

            int numberPad = AppConfig.IsNumberPad() ? NumberPad.Get() : -1;
            checkNumberPad.Visible = numberPad >= 0;
            checkNumberPad.Checked = numberPad == 1;
            checkNumberPad.CheckedChanged += CheckNumberPad_CheckedChanged;

            var optimalBrightness = ScreenControl.GetOptimalBrightness();
            if (optimalBrightness >= 0)
            {
                panelOptimalBrightness.Visible = true;
                comboOptimalBrightness.DropDownStyle = ComboBoxStyle.DropDownList;
                comboOptimalBrightness.SelectedIndex = AppConfig.Get("optimal_brightness", optimalBrightness);
                comboOptimalBrightness.SelectedIndexChanged += OptimalBrightness_Changed;
            }

            pictureHelp.Click += PictureHelp_Click;
            buttonResetBindings.Click += ButtonResetBindings_Click;
            buttonServices.Click += ButtonServices_Click;

            pictureLog.Click += PictureLog_Click;

            checkNVPlatform.Visible = Program.acpi.IsNVidiaGPU();
            checkNVPlatform.Checked = AppConfig.IsNVPlatform();
            checkNVPlatform.CheckedChanged += CheckNVPlatform_CheckedChanged;

            checkAspm.Checked = AppConfig.IsAutoASPM();
            checkAspm.CheckedChanged += CheckAspm_CheckedChanged;

            checkStandbyNetworking.Checked = AppConfig.IsAutoStandbyNetworking();
            checkStandbyNetworking.CheckedChanged += CheckStandbyNetworking_CheckedChanged;

            checkKeystoneSound.Visible = AppConfig.IsKeystone();
            checkKeystoneSound.Checked = Keystone.IsEnabled();
            checkKeystoneSound.CheckedChanged += CheckKeystoneSoundCheckedChanged;

            toolTip.SetToolTip(checkAutoToggleClamshellMode, Properties.Strings.ClamshellModeTooltip);
            toolTip.SetToolTip(checkNVPlatform, Properties.Strings.NVPlatformTooltip);
            toolTip.SetToolTip(checkAspm, Properties.Strings.DisablePCIeASPMTooltip);
            toolTip.SetToolTip(checkStandbyNetworking, Properties.Strings.DisableStandbyNetworkingTooltip);

            InitCores();
            InitHibernate();
            InitVramMem();

            InitACPITesting();

        }

        private void CheckKeystoneSoundCheckedChanged(object? sender, EventArgs e)
        {
            Keystone.SetEnabled(checkKeystoneSound.Checked);
        }

        private void CheckAspm_CheckedChanged(object? sender, EventArgs e)
        {
            AppConfig.Set("aspm", (checkAspm.Checked ? 1 : 0));
            PowerNative.SetBalancedASPM(checkAspm.Checked ? 0 : 2);
        }

        private void CheckStandbyNetworking_CheckedChanged(object? sender, EventArgs e)
        {
            AppConfig.Set("standby_networking", (checkStandbyNetworking.Checked ? 1 : 0));
            if (checkStandbyNetworking.Checked) PowerNative.SetConnectivityInStandby(0, 0);
            else PowerNative.SetConnectivityInStandby(1, 2);
        }

        private void CheckNVPlatform_CheckedChanged(object? sender, EventArgs e)
        {
            AppConfig.Set("nv_platform", (checkNVPlatform.Checked ? 1 : 0));
        }

        private void OptimalBrightness_Changed(object? sender, EventArgs e)
        {
            ScreenControl.SetOptimalBrightness(comboOptimalBrightness.SelectedIndex);
        }

        private void CheckLEDStatus_CheckedChanged(object? sender, EventArgs e)
        {
            InputDispatcher.SetStatusLED(checkStatusLed.Checked);
        }

        private void CheckNumberPad_CheckedChanged(object? sender, EventArgs e)
        {
            NumberPad.Set(checkNumberPad.Checked);
        }

        private void InitACPITesting()
        {
            pictureScan.Visible = true;
            pictureScan.Click += PictureScan_Click;

            if (!AppConfig.Is("debug")) return;

            panelACPI.Visible = true;
            textACPICommand.Text = "110034";
            textACPIParam.Text = "0x0303";
            buttonACPISend.Click += ButtonACPISend_Click;
        }

        private void ButtonACPISend_Click(object? sender, EventArgs e)
        {
            try
            {
                int deviceID = Convert.ToInt32(textACPICommand.Text, 16);
                int status = Convert.ToInt32(textACPIParam.Text, textACPIParam.Text.Contains("x") ? 16 : 10);
                int result = Program.acpi.DeviceSet((uint)deviceID, status, "TestACPI " + deviceID.ToString("X8") + " " + status.ToString("X4"));
                labelACPITitle.Text = "ACPI DEVS Test : " + result.ToString();
            }
            catch (Exception ex)
            {
                Logger.WriteLine(ex.Message);
            }
        }

        private void InitCores()
        {
            (int eCores, int pCores) = Program.acpi.GetCores();
            (int eCoresMax, int pCoresMax) = Program.acpi.GetCores(AsusACPI.CORES_MAX);

            if (eCores < 0 || pCores < 0 || eCoresMax < 0 || pCoresMax < 0)
            {
                panelCores.Visible = false;
                return;
            }

            if (eCoresMax == 0) eCoresMax = 8;
            if (pCoresMax == 0) pCoresMax = 6;

            if (AppConfig.Is8Ecores()) eCoresMax = Math.Max(8, eCoresMax);

            eCoresMax = Math.Max(4, eCoresMax);

            (int eMin, int pMin) = Program.acpi.GetCores(AsusACPI.CORES_MIN);
            if (pMin >= 1)
            {
                coresMinP = Math.Min(pMin, pCoresMax);
                coresMinE = Math.Min(eMin, eCoresMax);
            }

            panelCores.Visible = true;

            comboCoresE.DropDownStyle = ComboBoxStyle.DropDownList;
            comboCoresP.DropDownStyle = ComboBoxStyle.DropDownList;

            for (int i = coresMinP; i <= pCoresMax; i++) comboCoresP.Items.Add(i.ToString() + " Pcores");
            for (int i = coresMinE; i <= eCoresMax; i++) comboCoresE.Items.Add(i.ToString() + " Ecores");

            comboCoresP.SelectedIndex = Math.Max(Math.Min(pCores - coresMinP, comboCoresP.Items.Count - 1), 0);
            comboCoresE.SelectedIndex = Math.Max(Math.Min(eCores - coresMinE, comboCoresE.Items.Count - 1), 0);

            buttonCores.Click += ButtonCores_Click;

        }

        private void ButtonCores_Click(object? sender, EventArgs e)
        {
            DialogResult dialogResult = MessageBox.Show(Properties.Strings.AlertAPUMemoryRestart, Properties.Strings.AlertAPUMemoryRestartTitle, MessageBoxButtons.YesNo);

            if (dialogResult == DialogResult.Yes)
            {
                Program.acpi.SetCores(coresMinE + comboCoresE.SelectedIndex, coresMinP + comboCoresP.SelectedIndex);
                Process.Start("shutdown", "/r /t 1");
            }
        }


        private void PictureScan_Click(object? sender, EventArgs e)
        {
            string logFile = Program.acpi.ScanRange();
            new Process
            {
                StartInfo = new ProcessStartInfo(logFile)
                {
                    UseShellExecute = true
                }
            }.Start();
        }

        private void InitVramMem()
        {
            int unitMb = 0;
            if (PawnIO.CpuInfo.IsAMD) vramOptions = Program.acpi.GetVramOptions(out unitMb);

            if (vramOptions.Length > 0)
            {
                comboAPU.Items.Clear();
                foreach (int option in vramOptions)
                    comboAPU.Items.Add(option == 0 ? Properties.Strings.AutoMode : ((double)option * unitMb / 1024).ToString("0.#") + "G");

                int current = Program.acpi.GetVramMem();
                if (current == 0) current = AppConfig.Get("vram_mem", 0);

                comboAPU.SelectedIndex = Math.Max(0, Array.IndexOf(vramOptions, current));
            }
            else if (IntelVram.Init())
            {
                comboAPU.Items.Clear();
                comboAPU.Items.Add(Properties.Strings.AutoMode);
                foreach (int size in IntelVram.Sizes) comboAPU.Items.Add(size + "G");

                comboAPU.SelectedIndex = IntelVram.Index;
            }
            else
            {
                if (!AppConfig.IsAlly()) return;

                int apuMem = Program.acpi.GetAPUMem();
                if (apuMem < 0) return;

                comboAPU.SelectedIndex = apuMem;
            }

            panelAPU.Visible = true;
            comboAPU.DropDownStyle = ComboBoxStyle.DropDownList;
            comboAPU.SelectedIndexChanged += ComboAPU_SelectedIndexChanged;
        }

        private void ComboAPU_SelectedIndexChanged(object? sender, EventArgs e)
        {
            int mem = comboAPU.SelectedIndex;

            if (IntelVram.Supported && !ProcessHelper.IsUserAdministrator())
            {
                ProcessHelper.RunAsAdmin();
                return;
            }

            if (IntelVram.Supported) IntelVram.SetIndex(mem);
            else if (vramOptions.Length == 0) Program.acpi.SetAPUMem(mem);
            else
            {
                Program.acpi.SetVramMem(vramOptions[mem]);
                AppConfig.Set("vram_mem", vramOptions[mem]);
            }

            DialogResult dialogResult = MessageBox.Show(Properties.Strings.AlertAPUMemoryRestart, Properties.Strings.AlertAPUMemoryRestartTitle, MessageBoxButtons.YesNo);
            if (dialogResult == DialogResult.Yes)
            {
                Process.Start("shutdown", "/r /t 1");
            }

        }

        private void CheckBootSound_CheckedChanged(object? sender, EventArgs e)
        {
            int bootSound = checkBootSound.Checked ? 1 : 0;
            Program.acpi.DeviceSet(AsusACPI.BootSound, bootSound, "BootSound");
            AppConfig.Set("boot_sound", bootSound);
        }

        private void InitHibernate()
        {
            try
            {
                int hibernate = PowerNative.GetHibernateAfter();
                if (hibernate < 0 || hibernate > numericHibernateAfter.Maximum) hibernate = 0;
                numericHibernateAfter.Value = hibernate;
                numericHibernateAfter.ValueChanged += NumericHibernateAfter_ValueChanged;

            }
            catch (Exception ex)
            {
                panelPower.Visible = false;
                Logger.WriteLine(ex.ToString());
            }

        }

        private void NumericHibernateAfter_ValueChanged(object? sender, EventArgs e)
        {
            PowerNative.SetHibernateAfter((int)numericHibernateAfter.Value);
        }

        private void PictureLog_Click(object? sender, EventArgs e)
        {
            new Process
            {
                StartInfo = new ProcessStartInfo(Logger.logFile)
                {
                    UseShellExecute = true
                }
            }.Start();
        }

        private void SliderBrightness_ValueChanged(object? sender, EventArgs e)
        {
            bool onBattery = SystemInformation.PowerStatus.PowerLineStatus != PowerLineStatus.Online;

            if (onBattery)
                AppConfig.Set("keyboard_brightness_ac", sliderBrightness.Value);
            else
                AppConfig.Set("keyboard_brightness", sliderBrightness.Value);

            Aura.ApplyBrightness(sliderBrightness.Value, "Slider");
            sliderBrightness.AccessibleName = Properties.Strings.LaptopBacklight + ": " + sliderBrightness.Value;
        }

        public void VisualiseBacklight(int backlight)
        {
            if (InvokeRequired) { Invoke(() => VisualiseBacklight(backlight)); return; }
            sliderBrightness.ValueChanged -= SliderBrightness_ValueChanged;
            sliderBrightness.Value = backlight;
            sliderBrightness.AccessibleName = Properties.Strings.LaptopBacklight + ": " + sliderBrightness.Value;
            sliderBrightness.ValueChanged += SliderBrightness_ValueChanged;
        }

        private void InitServices()
        {
            buttonServices.Enabled = false;
            Task.Run(() =>
            {
                int servicesCount = AsusService.GetRunningCount();
                if (!IsDisposed) Invoke(() => VisualiseServices(servicesCount));
            });
        }

        private void VisualiseServices(int servicesCount)
        {
            if (servicesCount > 0)
            {
                buttonServices.Text = Properties.Strings.Stop;
                labelServices.ForeColor = colorTurbo;
            }
            else
            {
                buttonServices.Text = Properties.Strings.Start;
                labelServices.ForeColor = colorStandard;
            }

            labelServices.Text = Properties.Strings.AsusServicesRunning + ":  " + servicesCount;
            buttonServices.Enabled = true;

        }

        public void ServiesToggle()
        {
            buttonServices.Enabled = false;

            if (AsusService.GetRunningCount() > 0)
            {
                labelServices.Text = Properties.Strings.StoppingServices + " ...";
                Task.Run(() =>
                {
                    AsusService.StopAsusServices();
                    Program.inputDispatcher.Init();
                    BeginInvoke(delegate
                    {
                        InitServices();
                    });
                });
            }
            else
            {
                labelServices.Text = Properties.Strings.StartingServices + " ...";
                Task.Run(() =>
                {
                    AsusService.StartAsusServices();
                    BeginInvoke(delegate
                    {
                        InitServices();
                    });
                });
            }
        }

        private void ButtonServices_Click(object? sender, EventArgs e)
        {
            if (ProcessHelper.IsUserAdministrator())
                ServiesToggle();
            else
                ProcessHelper.RunAsAdmin("services");
        }

        private void CheckGpuApps_CheckedChanged(object? sender, EventArgs e)
        {
            AppConfig.Set("kill_gpu_apps", (checkGpuApps.Checked ? 1 : 0));
        }

        private void NumericBacklightTime_ValueChanged(object? sender, EventArgs e)
        {
            AppConfig.Set("keyboard_timeout", (int)numericBacklightTime.Value);
            AppConfig.Set("keyboard_ac_timeout", (int)numericBacklightPluggedTime.Value);
            Program.inputDispatcher.InitBacklightTimer();
        }

        private void CheckXGM_CheckedChanged(object? sender, EventArgs e)
        {
            AppConfig.Set("xmg_light", (checkXGM.Checked ? 1 : 0));
            XGM.Light(checkXGM.Checked);
        }

        private void CheckELMB_CheckedChanged(object? sender, EventArgs e)
        {
            AppConfig.Set("elmb", checkELMB.Checked ? 1 : 0);
            ScreenELMB.Set(checkELMB.Checked ? 1 : 0);
        }

        private void CheckUSBC_CheckedChanged(object? sender, EventArgs e)
        {
            AppConfig.Set("optimized_usbc", (checkUSBC.Checked ? 1 : 0));
        }

        private void PictureHelp_Click(object? sender, EventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://github.com/seerge/g-helper/wiki/Power-user-settings#custom-hotkey-actions") { UseShellExecute = true });
        }

        private void ButtonResetBindings_Click(object? sender, EventArgs e)
        {
            comboM1.SelectedValue = "";
            comboM2.SelectedValue = "";
            comboM3.SelectedValue = "";
            comboM4.SelectedValue = "";
            comboM5.SelectedValue = "";

            textM1.Text = textM2.Text = textM3.Text = textM4.Text = textM5.Text = "";

            MKeyControl.Reset();
            Program.inputDispatcher.RegisterKeys();
        }

        private void CheckNoOverdrive_CheckedChanged(object? sender, EventArgs e)
        {
            AppConfig.Set("no_overdrive", (checkNoOverdrive.Checked ? 1 : 0));
            ScreenControl.AutoScreen(true);
        }


        private void CheckTopmost_CheckedChanged(object? sender, EventArgs e)
        {
            AppConfig.Set("topmost", (checkTopmost.Checked ? 1 : 0));
            Program.settingsForm.TopMost = checkTopmost.Checked;
        }

        private void CheckPower_CheckedChanged(object? sender, EventArgs e)
        {
            AppConfig.Set("keyboard_awake", (checkAwake.Checked ? 1 : 0));
            AppConfig.Set("keyboard_boot", (checkBoot.Checked ? 1 : 0));
            AppConfig.Set("keyboard_sleep", (checkSleep.Checked ? 1 : 0));
            AppConfig.Set("keyboard_shutdown", (checkBoot.Checked ? 1 : 0));

            AppConfig.Set("keyboard_awake_bar", (checkAwakeBar.Checked ? 1 : 0));
            AppConfig.Set("keyboard_boot_bar", (checkBootBar.Checked ? 1 : 0));
            AppConfig.Set("keyboard_sleep_bar", (checkSleepBar.Checked ? 1 : 0));
            AppConfig.Set("keyboard_shutdown_bar", (checkBootBar.Checked ? 1 : 0));

            AppConfig.Set("keyboard_awake_lid", (checkAwakeLid.Checked ? 1 : 0));
            AppConfig.Set("keyboard_boot_lid", (checkBootLid.Checked ? 1 : 0));
            AppConfig.Set("keyboard_sleep_lid", (checkSleepLid.Checked ? 1 : 0));
            AppConfig.Set("keyboard_shutdown_lid", (checkBootLid.Checked ? 1 : 0));

            AppConfig.Set("keyboard_awake_logo", (checkAwakeLogo.Checked ? 1 : 0));
            AppConfig.Set("keyboard_boot_logo", (checkBootLogo.Checked ? 1 : 0));
            AppConfig.Set("keyboard_sleep_logo", (checkSleepLogo.Checked ? 1 : 0));
            AppConfig.Set("keyboard_shutdown_logo", (checkBootLogo.Checked ? 1 : 0));

            if (AppConfig.IsBacklightZones())
            {
                AppConfig.Set("keyboard_awake_bat", (checkBattery.Checked ? 1 : 0));
                AppConfig.Set("keyboard_awake_bar_bat", (checkBatteryBar.Checked ? 1 : 0));
                AppConfig.Set("keyboard_awake_lid_bat", (checkBatteryLid.Checked ? 1 : 0));
                AppConfig.Set("keyboard_awake_logo_bat", (checkBatteryLogo.Checked ? 1 : 0));
            }

            Aura.ApplyPower();
            if (Aura.IsOldStrix) Aura.ApplyAura();

        }

        private void ComboKeyboardSpeed_SelectedValueChanged(object? sender, EventArgs e)
        {
            AppConfig.Set("aura_speed", (int)comboKeyboardSpeed.SelectedValue);
            Aura.ApplyAura();
        }


        private void Keyboard_Shown(object? sender, EventArgs e)
        {
            if (Height > Program.settingsForm.Height)
            {
                var top = Program.settingsForm.Top + Program.settingsForm.Height - Height;

                if (top < 0)
                {
                    MaximumSize = new Size(Width, Program.settingsForm.Height);
                    Top = Program.settingsForm.Top;
                }
                else
                {
                    Top = top;
                }

            }
            else
            {
                Top = Program.settingsForm.Top;
            }

            Left = Program.settingsForm.Left - Width - 5;
            InitServices();
        }


        private void checkAutoToggleClamshellMode_CheckedChanged(object? sender, EventArgs e)
        {
            AppConfig.Set("toggle_clamshell_mode", checkAutoToggleClamshellMode.Checked ? 1 : 0);

            if (checkAutoToggleClamshellMode.Checked)
            {
                clamshellControl.ToggleLidAction();
            }
            else
            {
                ClamshellModeControl.DisableClamshellMode();
            }

        }

        private void panelAPU_Paint(object sender, PaintEventArgs e)
        {

        }
    }
}
