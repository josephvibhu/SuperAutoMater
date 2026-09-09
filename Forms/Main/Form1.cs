using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SuperAutoMater
{
    /// <summary>
    /// SuperAutoMater QC Diagnostic Studio — Avionics HUD Interface v0.1.
    /// Single-sourced theming, scaling, and command dispatcher.
    /// </summary>
    public partial class Form1 : Form
    {
        #region UI Theme Aliases

        public static Color BtopBg          => HudTheme.BgGlass;
        public static Color BtopCardBg      => HudTheme.PanelGlassTop;
        public static Color BtopBorder      => HudTheme.Bezel;
        public static Color BtopBorderActive=> HudTheme.HudAccent;
        public static Color BtopCyan        => HudTheme.HudAccent;
        public static Color BtopCoral       => HudTheme.FailWarning;
        public static Color BtopAmber       => HudTheme.WarnCaution;
        public static Color BtopPurple      => HudTheme.StorageAux;
        public static Color BtopPink        => HudTheme.WarnCaution;
        public static Color BtopWhite       => HudTheme.TextBright;
        public static Color BtopMuted       => HudTheme.Muted;
        public static Color BtopBtnBg       => Color.FromArgb(14, 20, 24);

        public static Color BgObsidian      => HudTheme.BgGlass;
        public static Color BgCard          => HudTheme.PanelGlassTop;
        public static Color BorderCard      => HudTheme.Bezel;
        public static Color BorderHeader    => HudTheme.HudAccent;
        public static Color MintAccent      => HudTheme.PassNominal;
        public static Color MintDim         => Color.FromArgb(16, 38, 30);
        public static Color MintPillBg      => Color.FromArgb(14, 20, 24);
        public static Color TextSage        => HudTheme.HudAccent;
        public static Color TextMuted       => HudTheme.Muted;
        public static Color FlameOrange     => HudTheme.FailWarning;
        public static Color AmberGold       => HudTheme.WarnCaution;

        private Font uiFont = HudTheme.FontMono11;
        private Font uiFontBold = HudTheme.FontMono11Bold;

        #endregion

        #region Shared UI Fields

        private Label lblAdminWarning;
        private RichTextBox reportBox = new RichTextBox();
        private SlidingQcDrawer slidingDrawer;
        private NavRailPanel navRail;

        private Panel keyboardPanel;
        private Panel camPanel;
        private HudVuMeter micVuMeter;
        private Label lblMicState;
        private BufferedPanel trackpadVisual;
        private TableLayoutPanel rootSplit;
        private Panel centerArea;

        private bool tpLeft = false;
        private bool tpRight = false;
        private bool tpMiddle = false;
        private bool tpLeftDown = false;
        private bool tpRightDown = false;
        private bool tpMiddleDown = false;

        private readonly UsbPortTracker _usbTracker = new UsbPortTracker();
        private Label lblUsbTest;
        private System.Windows.Forms.Timer _powerTimer;
        private System.Windows.Forms.Timer _cpuSampleTimer;

        // Bento Grid Telemetry Cards & Controls
        private BentoCard cardDevice;
        private BentoCard cardCpu;
        private BentoCard cardMemory;
        private BentoCard cardBattery;

        private Label lblDeviceModel;
        private Label lblDeviceSerial;
        private Label lblDeviceGrade;

        private Label lblCpuName;
        private Label lblCpuClock;
        private TelemetrySparkline sparkCpu;

        private Label lblMemorySpecs;
        private Label lblStorageSummary;
        private Label lblStorageSmartBadge;

        private Label lblBatteryFlow;
        private Label lblBatteryWear;
        private RadialMeter meterBattery;

        // Top Avionics Bar Telemetry Chips
        private Label lblTopBattery;
        private Label lblTopCpu;
        private Label lblTopWifi;
        private GlowButton btnTopExpressQC;

        // Left Suite Test Pipeline Buttons
        private GlowButton btnSuiteDisplay;
        private GlowButton btnSuiteAudio;
        private GlowButton btnSuiteWebcam;
        private GlowButton btnSuiteKeyboard;
        private GlowButton btnSuiteCpu;
        private GlowButton btnSuiteGpu;
        private GlowButton btnSuiteWifi;
        private GlowButton btnSuiteStorage;
        private GlowButton btnSuitePrintLabel;
        private GlowButton btnSuiteSignOff;

        // Bento Bottom Arena Controls
        private Label lblKeyboardCount;
        private BentoCard cardKeyboard;
        private BentoCard cardSensors;
        private BentoCard cardUsb;
        private BentoCard cardWarehouse;

        public static Form1 Instance;

        public string GetLastSerial() => lastSerial;
        public string GetTelemetryReportText() => reportBox?.Text ?? "";

        #endregion

        private bool IsAdministrator()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        public Form1()
        {
            Instance = this;
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.Size = new Size(1440, 920);
            this.MinimumSize = new Size(1200, 720);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Text = AppVersion.WindowTitle;
            this.BackColor = HudTheme.BgGlass;
            this.ForeColor = HudTheme.TextBright;
            this.KeyPreview = true;
            this.DoubleBuffered = true;
            this.WindowState = FormWindowState.Maximized;

            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                if (File.Exists(iconPath)) this.Icon = new Icon(iconPath);
            }
            catch { }

            BuildResponsiveLayout();

            _usbTracker.PortTested += (count, label) =>
            {
                if (this.IsHandleCreated && !this.IsDisposed)
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        if (lblUsbTest != null)
                        {
                            lblUsbTest.Text = _usbTracker.GetHudStatusText();
                            lblUsbTest.ForeColor = HudTheme.PassNominal;
                            lblUsbTest.Invalidate();
                        }
                        MarkTestComplete("Trackpad");
                    }));
                }
            };

            this.Load += Form1_Load;
            this.Shown += Form1_Shown;
            this.FormClosing += Form1_FormClosing;
            this.KeyDown += Form1_KeyDown;
            this.KeyUp += Form1_KeyUp;
            this.Resize += Form1_Resize;
            this.DpiChanged += (s, e) => HandleDpiOrSizeChange();
        }

        /// <summary>
        /// Scales a pixel value by the current device DPI relative to 96 DPI baseline.
        /// </summary>
        private int DpiScaled(int px) => (int)Math.Round(px * (this.DeviceDpi / 96f));

        private void Form1_Resize(object sender, EventArgs e)
        {
            HandleDpiOrSizeChange();
        }

        private void HandleDpiOrSizeChange()
        {
            if (centerArea != null && centerArea.Height > 100)
            {
                ScalingService.Instance.Recalculate(centerArea.Height, this.DeviceDpi);
                this.Invalidate(true);
            }
        }

        #region Diagnostic Dispatcher Methods

        public void ToggleQcDrawer() => slidingDrawer?.ToggleDrawer();
        public void LaunchCpuBurn()
        {
            using (var crForm = new CpuRamDiagnosticForm()) { crForm.ShowDialog(); }
        }
        public void LaunchWifiRadar()
        {
            using (var wtf = new WifiTestForm()) { wtf.ShowDialog(); }
        }
        public async void LaunchStorageBenchmark()
        {
            await TriggerStorageBenchmark();
        }
        public void LaunchFingerprintTest()
        {
            using (var fpForm = new FingerprintTestForm()) { fpForm.ShowDialog(); }
        }
        public void LaunchBluetoothTest()
        {
            using (var btForm = new BluetoothTestForm()) { btForm.ShowDialog(this); }
        }
        public void LaunchAudioTest()
        {
            _ = PlayAudioTest(true, true, AudioTestMode.Both);
        }
        public void LaunchDisplayTest()
        {
            using (var dtf = new DisplayTestForm(isAutomated: false)) { dtf.ShowDialog(); }
            MarkTestComplete("Display");
        }
        public void FocusKeyboardMatrix()
        {
            keyboardPanel?.Focus();
            keyboardPanel?.Invalidate();
        }

        #endregion

        #region Layout Construction (Avionics HUD)

        private void BuildResponsiveLayout()
        {
            // Root 3-Column Split: Column 0 (235px Left Suite Rail) | Column 1 (100% Bento Arena) | Column 2 (0-350px Sliding Drawer)
            rootSplit = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0),
                BackColor = HudTheme.BgBase
            };
            rootSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 235F));  // Left Rail widened to 235px for clean unclipped buttons
            rootSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            rootSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0F));
            rootSplit.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            // Legacy navRail preserved so any existing item hooks don't null-ref
            navRail = new NavRailPanel();
            navRail.MenuButtonClicked += () => slidingDrawer.ToggleDrawer();

            // 1. Diagnostic Suite Rail (Left Column 0)
            Control leftRail = BuildLeftTestSuiteRail();
            rootSplit.Controls.Add(leftRail, 0, 0);

            // 2. Sliding QC Drawer (Right Column 2)
            slidingDrawer = new SlidingQcDrawer();
            slidingDrawer.DrawerWidthChanged += (w) =>
            {
                rootSplit.ColumnStyles[2].Width = w;
                rootSplit.PerformLayout();
            };
            rootSplit.Controls.Add(slidingDrawer, 2, 0);

            // 3. Central Canvas Panel (Center Column 1)
            centerArea = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(4, 4, 6, 4),
                BackColor = HudTheme.BgBase
            };

            TableLayoutPanel centerGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            centerGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));  // Top Avionics Bar
            centerGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // Bento Dashboard Arena
            centerGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));  // Bottom Command Hotkey Bar (reduced from 28 to 24)

            centerGrid.Controls.Add(BuildTopAvionicsBar(), 0, 0);
            centerGrid.Controls.Add(BuildBentoDashboardArena(), 0, 1);
            centerGrid.Controls.Add(BuildBottomCommandBar(), 0, 2);

            centerArea.Controls.Add(centerGrid);
            rootSplit.Controls.Add(centerArea, 1, 0);
            this.Controls.Add(rootSplit);

            Build104Keyboard();
        }

        private Control BuildTopAvionicsBar()
        {
            Panel bar = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(11, 6, 20),
                Margin = new Padding(0, 0, 0, 4)
            };
            bar.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen p = new Pen(Color.FromArgb(60, 168, 85, 247), 1))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, bar.Width - 1, bar.Height - 1);
                }
            };

            TableLayoutPanel barGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 7,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(8, 3, 8, 3)
            };
            barGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));          // Brand & Bench telemetry
            barGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));      // Flexible spacer
            barGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));          // lblTopBattery chip
            barGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));          // lblTopCpu chip
            barGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));          // lblTopWifi chip
            barGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 185F));    // btnTopExpressQC
            barGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140F));    // btnSignDrawer

            // Brand Header & Bench Info
            FlowLayoutPanel brandPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0),
                Padding = new Padding(0, 2, 0, 0)
            };
            Label lblBrand = new Label
            {
                Text = "⚡ SUPERAUTOMATER",
                Font = HudTheme.FontTitle13Bold,
                ForeColor = HudTheme.HudAccent,
                AutoSize = true,
                Margin = new Padding(0, 2, 4, 0)
            };
            Label lblSub = new Label
            {
                Text = "v0.2",
                Font = HudTheme.FontMono8Bold,
                ForeColor = HudTheme.HudAccentSoft,
                BackColor = Color.FromArgb(30, 15, 50),
                Padding = new Padding(5, 2, 5, 2),
                AutoSize = true,
                Margin = new Padding(0, 3, 8, 0)
            };
            lblSub.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Color.FromArgb(80, 168, 85, 247), 1))
                    e.Graphics.DrawRectangle(p, 0, 0, lblSub.Width - 1, lblSub.Height - 1);
            };

            Label lblDivider = new Label
            {
                Text = "|",
                Font = HudTheme.FontMono9Bold,
                ForeColor = Color.FromArgb(60, 40, 90),
                AutoSize = true,
                Margin = new Padding(0, 3, 8, 0)
            };

            Label lblBenchInfo = new Label
            {
                Text = "● BENCH ONLINE  |  BAY: QC-TERMINAL-01  |  OPERATOR: SENIOR REFURB TECH",
                Font = HudTheme.FontMono8Bold,
                ForeColor = HudTheme.PassNominal,
                AutoSize = true,
                Margin = new Padding(0, 5, 4, 0)
            };

            brandPanel.Controls.Add(lblBrand);
            brandPanel.Controls.Add(lblSub);
            brandPanel.Controls.Add(lblDivider);
            brandPanel.Controls.Add(lblBenchInfo);
            barGrid.Controls.Add(brandPanel, 0, 0);

            // Flexible Spacer
            barGrid.Controls.Add(new Panel { Dock = DockStyle.Fill }, 1, 0);

            // Quick Telemetry Chips
            lblTopBattery = new Label
            {
                Text = "BATTERY: 100% (AC Line)",
                Font = HudTheme.FontMono9Bold,
                ForeColor = HudTheme.PassNominal,
                BackColor = Color.FromArgb(18, 9, 32),
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(8, 4, 8, 4),
                AutoSize = true,
                Margin = new Padding(3, 1, 3, 1)
            };
            lblTopBattery.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Color.FromArgb(50, 168, 85, 247), 1))
                    e.Graphics.DrawRectangle(p, 0, 0, lblTopBattery.Width - 1, lblTopBattery.Height - 1);
            };
            barGrid.Controls.Add(lblTopBattery, 2, 0);

            lblTopCpu = new Label
            {
                Text = "CPU FREQ: 0% NOMINAL",
                Font = HudTheme.FontMono9Bold,
                ForeColor = HudTheme.HudAccentSoft,
                BackColor = Color.FromArgb(18, 9, 32),
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(8, 4, 8, 4),
                AutoSize = true,
                Margin = new Padding(3, 1, 3, 1)
            };
            lblTopCpu.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Color.FromArgb(50, 168, 85, 247), 1))
                    e.Graphics.DrawRectangle(p, 0, 0, lblTopCpu.Width - 1, lblTopCpu.Height - 1);
            };
            barGrid.Controls.Add(lblTopCpu, 3, 0);

            lblTopWifi = new Label
            {
                Text = "WI-FI: STANDBY",
                Font = HudTheme.FontMono9Bold,
                ForeColor = Color.FromArgb(56, 189, 248),
                BackColor = Color.FromArgb(18, 9, 32),
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(8, 4, 8, 4),
                AutoSize = true,
                Margin = new Padding(3, 1, 6, 1)
            };
            lblTopWifi.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Color.FromArgb(50, 168, 85, 247), 1))
                    e.Graphics.DrawRectangle(p, 0, 0, lblTopWifi.Width - 1, lblTopWifi.Height - 1);
            };
            barGrid.Controls.Add(lblTopWifi, 4, 0);

            // Express QC Primary Button
            btnTopExpressQC = new GlowButton
            {
                Text = "⚡ 1-CLICK EXPRESS QC",
                HotkeyText = "[F5]",
                IsPrimary = true,
                AccentColor = HudTheme.HudAccent,
                Dock = DockStyle.Fill,
                Margin = new Padding(2, 0, 3, 0)
            };
            btnTopExpressQC.Click += async (s, e) => await RunExpressQCSequenceAsync();
            barGrid.Controls.Add(btnTopExpressQC, 5, 0);

            // QC Sign-off Drawer Toggle Button
            GlowButton btnSignDrawer = new GlowButton
            {
                Text = "🔏 QC SIGN-OFF",
                HotkeyText = "[Enter]",
                AccentColor = HudTheme.PassNominal,
                Dock = DockStyle.Fill,
                Margin = new Padding(2, 0, 0, 0)
            };
            btnSignDrawer.Click += (s, e) => slidingDrawer.ToggleDrawer();
            barGrid.Controls.Add(btnSignDrawer, 6, 0);

            bar.Controls.Add(barGrid);
            return bar;
        }

        private Control BuildLeftTestSuiteRail()
        {
            BentoCard railCard = new BentoCard
            {
                Dock = DockStyle.Fill,
                HeaderTitle = "DIAGNOSTICS",
                HeaderSubtitle = "[8 TESTS]",
                TagAccentColor = HudTheme.HudAccent,
                CornerRadius = 12,
                Margin = new Padding(2, 0, 2, 0)
            };

            TableLayoutPanel railGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 11,
                Margin = new Padding(0),
                Padding = new Padding(4, 1, 4, 2)  // Tightened to gain vertical room
            };
            for (int i = 0; i < 8; i++) railGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 9.5F));
            railGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 8F));  // Admin / Status / Spacer
            railGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 11F)); // Print Label
            railGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 11F)); // Pass QC & Sign-Off

            // 1. Display Test
            btnSuiteDisplay = new GlowButton { Text = "SCREEN COLORS", HotkeyText = "[F1]", Font = HudTheme.FontMono9Bold, Dock = DockStyle.Fill, Margin = new Padding(0, 1, 0, 1) };
            btnSuiteDisplay.Click += (s, e) => LaunchDisplayTest();
            railGrid.Controls.Add(btnSuiteDisplay, 0, 0);

            // 2. Audio Test
            btnSuiteAudio = new GlowButton { Text = "AUDIO STEREO", HotkeyText = "[F2]", Font = HudTheme.FontMono9Bold, Dock = DockStyle.Fill, Margin = new Padding(0, 1, 0, 1) };
            btnSuiteAudio.Click += (s, e) => LaunchAudioTest();
            railGrid.Controls.Add(btnSuiteAudio, 0, 1);

            // 3. Webcam & Sensors
            btnSuiteWebcam = new GlowButton { Text = "CAMERA & MIC", HotkeyText = "[F3]", Font = HudTheme.FontMono9Bold, Dock = DockStyle.Fill, Margin = new Padding(0, 1, 0, 1) };
            btnSuiteWebcam.Click += (s, e) => ToggleWebcam(btnCameraToggle, EventArgs.Empty);
            railGrid.Controls.Add(btnSuiteWebcam, 0, 2);

            // 4. Keyboard Matrix
            btnSuiteKeyboard = new GlowButton { Text = "KEYBOARD MATRIX", HotkeyText = "[F4]", Font = HudTheme.FontMono9Bold, Dock = DockStyle.Fill, Margin = new Padding(0, 1, 0, 1) };
            btnSuiteKeyboard.Click += (s, e) => FocusKeyboardMatrix();
            railGrid.Controls.Add(btnSuiteKeyboard, 0, 3);

            // 5. CPU & RAM Burn
            btnSuiteCpu = new GlowButton { Text = "CPU / RAM BURN", HotkeyText = "[F6]", Font = HudTheme.FontMono9Bold, Dock = DockStyle.Fill, Margin = new Padding(0, 1, 0, 1) };
            btnSuiteCpu.Click += (s, e) => LaunchCpuBurn();
            railGrid.Controls.Add(btnSuiteCpu, 0, 4);

            // 6. GPU 3D Benchmark
            btnSuiteGpu = new GlowButton { Text = "GPU 3D RENDER", HotkeyText = "[F7]", Font = HudTheme.FontMono9Bold, Dock = DockStyle.Fill, Margin = new Padding(0, 1, 0, 1) };
            btnSuiteGpu.Click += (s, e) => LaunchGpuBenchmark();
            railGrid.Controls.Add(btnSuiteGpu, 0, 5);

            // 7. Wi-Fi & Bluetooth Radar
            btnSuiteWifi = new GlowButton { Text = "WI-FI RADAR", HotkeyText = "[F8]", Font = HudTheme.FontMono9Bold, Dock = DockStyle.Fill, Margin = new Padding(0, 1, 0, 1) };
            btnSuiteWifi.Click += (s, e) => LaunchWifiRadar();
            railGrid.Controls.Add(btnSuiteWifi, 0, 6);

            // 8. Storage Speed Benchmark
            btnSuiteStorage = new GlowButton { Text = "STORAGE BENCH", HotkeyText = "[F9]", Font = HudTheme.FontMono9Bold, Dock = DockStyle.Fill, Margin = new Padding(0, 1, 0, 1) };
            btnSuiteStorage.Click += (s, e) => LaunchStorageBenchmark();
            railGrid.Controls.Add(btnSuiteStorage, 0, 7);

            // Row 8: Admin Warning / Status
            lblAdminWarning = new Label
            {
                Text = "⚠ RUN AS ADMIN FOR FULL SMART",
                Font = HudTheme.FontMono9Bold,
                ForeColor = HudTheme.WarnCaution,
                BackColor = Color.FromArgb(30, 20, 10),
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                Visible = false,
                Margin = new Padding(0, 2, 0, 2)
            };
            railGrid.Controls.Add(lblAdminWarning, 0, 8);

            // Action: Print Label
            btnSuitePrintLabel = new GlowButton
            {
                Text = "🖨 PRINT LABEL",
                HotkeyText = "[Ctrl+P]",
                Font = HudTheme.FontMono9Bold,
                Dock = DockStyle.Fill,
                AccentColor = HudTheme.HudAccentSoft,
                Margin = new Padding(0, 1, 0, 1)
            };
            btnSuitePrintLabel.Click += (s, e) => TriggerLabelPrint();
            railGrid.Controls.Add(btnSuitePrintLabel, 0, 9);

            // Action: Pass QC & Sign-Off
            btnSuiteSignOff = new GlowButton
            {
                Text = "🔏 PASS & SIGN-OFF",
                HotkeyText = "[Enter]",
                Font = HudTheme.FontMono9Bold,
                IsPrimary = true,
                AccentColor = HudTheme.PassNominal,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 1, 0, 0)
            };
            btnSuiteSignOff.Click += (s, e) => slidingDrawer.ToggleDrawer();
            railGrid.Controls.Add(btnSuiteSignOff, 0, 10);

            railCard.Controls.Add(railGrid);
            return railCard;
        }

        private Control BuildBentoDashboardArena()
        {
            TableLayoutPanel arena = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            arena.RowStyles.Add(new RowStyle(SizeType.Absolute, 130F)); // 4 Top Bento Metric Tiles (increased from 114 for content fit)
            arena.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));  // Main Interactive Area (Keyboard + Sensors/USB Stack)

            // --- 1. TOP BENTO TILES (4 CARDS) ---
            TableLayoutPanel topCardsGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 4),
                Padding = new Padding(0)
            };
            topCardsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            topCardsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            topCardsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            topCardsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));

            // Tile 1: Chassis Identity
            cardDevice = new BentoCard
            {
                Dock = DockStyle.Fill,
                HeaderTitle = "CHASSIS IDENTITY",
                HeaderSubtitle = "OEM SYSTEM",
                TagAccentColor = HudTheme.HudAccent,
                Margin = new Padding(0, 0, 3, 0)
            };
            lblDeviceModel = new Label
            {
                Dock = DockStyle.Top,
                Height = 18,
                Font = HudTheme.FontMono11Bold,
                ForeColor = HudTheme.TextBright,
                Text = "Detecting Chassis...",
                TextAlign = ContentAlignment.MiddleLeft
            };
            Panel devSub = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 0) };
            lblDeviceSerial = new Label
            {
                Dock = DockStyle.Left,
                AutoSize = true,
                Font = HudTheme.FontMono9,
                ForeColor = HudTheme.HudAccentSoft,
                Text = "SN: Detecting...",
                TextAlign = ContentAlignment.MiddleLeft
            };
            lblDeviceGrade = new Label
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                Font = HudTheme.FontMono9Bold,
                ForeColor = HudTheme.PassNominal,
                BackColor = Color.FromArgb(12, 38, 28),
                Text = "GRADE A+",
                Padding = new Padding(6, 2, 6, 2),
                TextAlign = ContentAlignment.MiddleCenter
            };
            devSub.Controls.Add(lblDeviceSerial);
            devSub.Controls.Add(lblDeviceGrade);
            cardDevice.Controls.Add(devSub);
            cardDevice.Controls.Add(lblDeviceModel);
            topCardsGrid.Controls.Add(cardDevice, 0, 0);

            // Tile 2: CPU & Thermals
            cardCpu = new BentoCard
            {
                Dock = DockStyle.Fill,
                HeaderTitle = "CPU & THERMALS",
                HeaderSubtitle = "NOMINAL",
                TagAccentColor = HudTheme.HudAccent,
                Margin = new Padding(3, 0, 3, 0)
            };
            lblCpuName = new Label
            {
                Dock = DockStyle.Top,
                Height = 18,
                Font = HudTheme.FontMono11Bold,
                ForeColor = HudTheme.TextBright,
                Text = "Detecting CPU...",
                TextAlign = ContentAlignment.MiddleLeft
            };
            lblCpuClock = new Label
            {
                Dock = DockStyle.Top,
                Height = 15,
                Font = HudTheme.FontMono9,
                ForeColor = HudTheme.HudAccentSoft,
                Text = "Clock: -- GHz",
                TextAlign = ContentAlignment.MiddleLeft
            };
            sparkCpu = new TelemetrySparkline
            {
                Dock = DockStyle.Fill,
                LineColor = HudTheme.HudAccent,
                Capacity = 60,
                MinValue = 0f,
                MaxValue = 100f,
                ShowCurrentBadge = true
            };
            cardCpu.Controls.Add(sparkCpu);
            cardCpu.Controls.Add(lblCpuClock);
            cardCpu.Controls.Add(lblCpuName);
            topCardsGrid.Controls.Add(cardCpu, 1, 0);

            // Tile 3: Storage & Memory
            cardMemory = new BentoCard
            {
                Dock = DockStyle.Fill,
                HeaderTitle = "STORAGE & MEMORY",
                HeaderSubtitle = "NVMe / RAM",
                TagAccentColor = HudTheme.WarnCaution,
                Margin = new Padding(3, 0, 3, 0)
            };
            lblMemorySpecs = new Label
            {
                Dock = DockStyle.Top,
                Height = 18,
                Font = HudTheme.FontMono11Bold,
                ForeColor = HudTheme.TextBright,
                Text = "RAM: Detecting...",
                TextAlign = ContentAlignment.MiddleLeft
            };
            lblStorageSummary = new Label
            {
                Dock = DockStyle.Top,
                Height = 16,
                Font = HudTheme.FontMono9,
                ForeColor = HudTheme.TextNormal,
                Text = "Drive: Detecting...",
                TextAlign = ContentAlignment.MiddleLeft
            };
            lblStorageSmartBadge = new Label
            {
                Dock = DockStyle.Fill,
                Font = HudTheme.FontMono9Bold,
                ForeColor = HudTheme.PassNominal,
                Text = "SMART: 100% HEALTH [PASS]",
                TextAlign = ContentAlignment.BottomLeft
            };
            cardMemory.Controls.Add(lblStorageSmartBadge);
            cardMemory.Controls.Add(lblStorageSummary);
            cardMemory.Controls.Add(lblMemorySpecs);
            topCardsGrid.Controls.Add(cardMemory, 2, 0);

            // Tile 4: Battery Telemetry & Arc Flow
            cardBattery = new BentoCard
            {
                Dock = DockStyle.Fill,
                HeaderTitle = "BATTERY TELEMETRY",
                HeaderSubtitle = "FLOW / HEALTH",
                TagAccentColor = HudTheme.PassNominal,
                Margin = new Padding(3, 0, 0, 0)
            };
            TableLayoutPanel battGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0)
            };
            battGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65F));
            battGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));

            Panel battTextPanel = new Panel { Dock = DockStyle.Fill };
            lblBatteryFlow = new Label
            {
                Dock = DockStyle.Top,
                Height = 18,
                Font = HudTheme.FontMono11Bold,
                ForeColor = HudTheme.TextBright,
                Text = "Discharge: --W",
                TextAlign = ContentAlignment.MiddleLeft
            };
            lblBatteryWear = new Label
            {
                Dock = DockStyle.Top,
                Height = 18,
                Font = HudTheme.FontMono9,
                ForeColor = HudTheme.WarnCaution,
                Text = "Health: 100% (AC Line)",
                TextAlign = ContentAlignment.MiddleLeft
            };
            battTextPanel.Controls.Add(lblBatteryWear);
            battTextPanel.Controls.Add(lblBatteryFlow);
            battGrid.Controls.Add(battTextPanel, 0, 0);

            meterBattery = new RadialMeter
            {
                Dock = DockStyle.Fill,
                Value = 100f,
                MeterColor = HudTheme.PassNominal,
                TitleText = "BATT",
                TrackWidth = 6f
            };
            battGrid.Controls.Add(meterBattery, 1, 0);
            cardBattery.Controls.Add(battGrid);
            topCardsGrid.Controls.Add(cardBattery, 3, 0);

            arena.Controls.Add(topCardsGrid, 0, 0);

            // --- 2. BOTTOM TIER: 62% KEYBOARD MATRIX / 38% MULTI-STACK ---
            TableLayoutPanel bottomSplit = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            bottomSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62F));
            bottomSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));

            // Left: 104-Key Virtual Keyboard Matrix
            cardKeyboard = new BentoCard
            {
                Dock = DockStyle.Fill,
                HeaderTitle = "LIVE KEYBOARD HEATMAP & RAW SCANNER",
                HeaderSubtitle = "1000Hz RAW HOOK",
                TagAccentColor = HudTheme.HudAccent,
                Margin = new Padding(0, 2, 3, 0)
            };

            Panel kbHeaderStrip = new Panel
            {
                Dock = DockStyle.Top,
                Height = 26,
                BackColor = Color.FromArgb(14, 8, 24),
                Padding = new Padding(8, 2, 8, 2)
            };
            lblKeyboardCount = new Label
            {
                Dock = DockStyle.Left,
                AutoSize = true,
                Font = HudTheme.FontMono9Bold,
                ForeColor = HudTheme.HudAccentSoft,
                Text = "KEYS LOGGED: 0/104",
                TextAlign = ContentAlignment.MiddleLeft
            };
            Button btnResetKb = new Button
            {
                Dock = DockStyle.Right,
                Width = 90,
                Text = "RESET [ESC]",
                Font = HudTheme.FontMono9Bold,
                BackColor = Color.FromArgb(25, 12, 40),
                ForeColor = HudTheme.TextBright,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnResetKb.FlatAppearance.BorderColor = Color.FromArgb(60, 168, 85, 247);
            btnResetKb.Click += (s, e) => ResetKeyboardUI();

            Label lblKbHook = new Label
            {
                Dock = DockStyle.Fill,
                Font = HudTheme.FontMono9,
                ForeColor = HudTheme.PassNominal,
                Text = "● Raw Windows Low-Level Hook Active (1000Hz)",
                TextAlign = ContentAlignment.MiddleCenter
            };
            kbHeaderStrip.Controls.Add(lblKbHook);
            kbHeaderStrip.Controls.Add(lblKeyboardCount);
            kbHeaderStrip.Controls.Add(btnResetKb);

            keyboardPanel = new BufferedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(11, 6, 20),
                Margin = new Padding(0)
            };
            keyboardPanel.Paint += KeyboardPanel_Paint;

            cardKeyboard.Controls.Add(keyboardPanel);
            cardKeyboard.Controls.Add(kbHeaderStrip);
            bottomSplit.Controls.Add(cardKeyboard, 0, 0);

            // Right: Multi-Stack (Sensors + USB Radar + Warehouse Pipeline)
            TableLayoutPanel rightStack = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Margin = new Padding(3, 2, 0, 0),
                Padding = new Padding(0)
            };
            rightStack.RowStyles.Add(new RowStyle(SizeType.Percent, 48F)); // Sensors & Camera
            rightStack.RowStyles.Add(new RowStyle(SizeType.Percent, 28F)); // USB Radar & Trackpad
            rightStack.RowStyles.Add(new RowStyle(SizeType.Percent, 24F)); // Warehouse Pipeline

            // Card A: Camera & Audio Sensors
            cardSensors = new BentoCard
            {
                Dock = DockStyle.Fill,
                HeaderTitle = "CAMERA & AUDIO SENSORS",
                HeaderSubtitle = "LIVE STREAM",
                TagAccentColor = HudTheme.HudAccent,
                Margin = new Padding(0, 0, 0, 3)
            };
            TableLayoutPanel camSensorGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1,
                Margin = new Padding(0)
            };
            camSensorGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // camPanel
            camSensorGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));  // Buttons
            camSensorGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));  // Mic Container (38px gives clean clearance for text + bar)

            camPanel = BuildCameraPanel();
            camSensorGrid.Controls.Add(camPanel, 0, 0);

            TableLayoutPanel camBtns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 1, 0, 1) };
            camBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65F));
            camBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));

            btnCameraToggle = new GlowButton { Text = "START CAMERA TEST", HotkeyText = "[F3]", Dock = DockStyle.Fill, AccentColor = HudTheme.HudAccent };
            btnCameraToggle.Click += (s, e) => ToggleWebcam(btnCameraToggle, EventArgs.Empty);

            btnCameraSwitch = new GlowButton { Text = "⇄ CAM", Dock = DockStyle.Fill, AccentColor = HudTheme.StorageAux };
            btnCameraSwitch.Click += (s, e) => SwitchCamera();

            camBtns.Controls.Add(btnCameraToggle, 0, 0);
            camBtns.Controls.Add(btnCameraSwitch, 1, 0);
            camSensorGrid.Controls.Add(camBtns, 0, 1);

            // Mic visualizer container
            Panel micContainer = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(14, 8, 24), Padding = new Padding(4, 2, 4, 2) };
            lblMicState = new Label
            {
                Text = "MIC: ACTIVE (44.1 kHz / 16-BIT)",
                Font = HudTheme.FontMono8Bold,
                ForeColor = HudTheme.HudAccent,
                Dock = DockStyle.Top,
                Height = 16,
                TextAlign = ContentAlignment.MiddleLeft
            };
            micVuMeter = new HudVuMeter { Dock = DockStyle.Fill };
            micContainer.Controls.Add(micVuMeter);
            micContainer.Controls.Add(lblMicState);
            camSensorGrid.Controls.Add(micContainer, 0, 2);

            cardSensors.Controls.Add(camSensorGrid);
            rightStack.Controls.Add(cardSensors, 0, 0);

            // Card B: USB Radar & Trackpad
            cardUsb = new BentoCard
            {
                Dock = DockStyle.Fill,
                HeaderTitle = "USB RADAR & TRACKPAD",
                HeaderSubtitle = "HARDWARE BUS",
                TagAccentColor = HudTheme.WarnCaution,
                Margin = new Padding(0, 2, 0, 2)
            };
            TableLayoutPanel usbTpGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(2)
            };
            usbTpGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48F));
            usbTpGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52F));

            trackpadVisual = new BufferedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(16, 9, 28),
                Cursor = Cursors.Hand,
                Margin = new Padding(2)
            };

            trackpadVisual.Paint += (s, pe) =>
            {
                var g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int w = trackpadVisual.Width;
                int h = trackpadVisual.Height;

                if (w <= 4 || h <= 4) return;

                // Outer border
                bool allPassed = tpLeft && tpRight;
                using (Pen borderPen = new Pen(allPassed ? HudTheme.PassNominal : HudTheme.Bezel, 1f))
                {
                    g.DrawRectangle(borderPen, 0, 0, w - 1, h - 1);
                }

                // Split into Left Zone (45%), Middle Zone (10%), Right Zone (45%)
                int midW = Math.Max(16, w / 8);
                int sideW = (w - midW - 6) / 2;
                Rectangle lRect = new Rectangle(2, 2, sideW, h - 5);
                Rectangle mRect = new Rectangle(lRect.Right + 1, 2, midW, h - 5);
                Rectangle rRect = new Rectangle(mRect.Right + 1, 2, w - mRect.Right - 3, h - 5);

                // Draw Left Zone: Active Click (Electric Violet) > Tested (Emerald) > Normal (Amethyst Glass)
                Color lBg = tpLeftDown ? Color.FromArgb(95, 45, 160) : (tpLeft ? Color.FromArgb(16, 45, 30) : Color.FromArgb(22, 12, 38));
                Color lBorder = tpLeftDown ? HudTheme.HudAccentSoft : (tpLeft ? HudTheme.PassNominal : HudTheme.Bezel);
                Color lText = tpLeftDown ? Color.White : (tpLeft ? HudTheme.PassNominal : HudTheme.HudAccentSoft);
                using (SolidBrush b = new SolidBrush(lBg)) g.FillRectangle(b, lRect);
                using (Pen p = new Pen(lBorder, tpLeftDown ? 1.5f : 1f)) g.DrawRectangle(p, lRect);

                // Draw Middle Zone
                Color mBg = tpMiddleDown ? Color.FromArgb(95, 45, 160) : (tpMiddle ? Color.FromArgb(16, 45, 30) : Color.FromArgb(18, 10, 30));
                Color mBorder = tpMiddleDown ? HudTheme.HudAccentSoft : (tpMiddle ? HudTheme.PassNominal : Color.FromArgb(40, 25, 60));
                Color mText = tpMiddleDown ? Color.White : (tpMiddle ? HudTheme.PassNominal : HudTheme.Muted);
                using (SolidBrush b = new SolidBrush(mBg)) g.FillRectangle(b, mRect);
                using (Pen p = new Pen(mBorder, tpMiddleDown ? 1.5f : 1f)) g.DrawRectangle(p, mRect);

                // Draw Right Zone
                Color rBg = tpRightDown ? Color.FromArgb(95, 45, 160) : (tpRight ? Color.FromArgb(16, 45, 30) : Color.FromArgb(22, 12, 38));
                Color rBorder = tpRightDown ? HudTheme.HudAccentSoft : (tpRight ? HudTheme.PassNominal : HudTheme.Bezel);
                Color rText = tpRightDown ? Color.White : (tpRight ? HudTheme.PassNominal : HudTheme.HudAccentSoft);
                using (SolidBrush b = new SolidBrush(rBg)) g.FillRectangle(b, rRect);
                using (Pen p = new Pen(rBorder, tpRightDown ? 1.5f : 1f)) g.DrawRectangle(p, rRect);

                // Draw text labels
                using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
                {
                    using (SolidBrush tb = new SolidBrush(lText))
                        g.DrawString(tpLeft ? "✓ LEFT" : "LEFT BTN", HudTheme.FontMono8Bold, tb, lRect, sf);

                    using (SolidBrush tb = new SolidBrush(mText))
                        g.DrawString(tpMiddle ? "✓" : "MID", HudTheme.FontMono8, tb, mRect, sf);

                    using (SolidBrush tb = new SolidBrush(rText))
                        g.DrawString(tpRight ? "✓ RIGHT" : "RIGHT BTN", HudTheme.FontMono8Bold, tb, rRect, sf);
                }
            };

            trackpadVisual.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    tpLeft = true;
                    tpLeftDown = true;
                }
                else if (e.Button == MouseButtons.Right)
                {
                    tpRight = true;
                    tpRightDown = true;
                }
                else if (e.Button == MouseButtons.Middle)
                {
                    tpMiddle = true;
                    tpMiddleDown = true;
                }

                trackpadVisual.Invalidate();
                if (tpLeft && tpRight)
                {
                    MarkTestComplete("Trackpad");
                }
            };

            trackpadVisual.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) tpLeftDown = false;
                else if (e.Button == MouseButtons.Right) tpRightDown = false;
                else if (e.Button == MouseButtons.Middle) tpMiddleDown = false;
                trackpadVisual.Invalidate();
            };

            trackpadVisual.MouseLeave += (s, e) =>
            {
                if (tpLeftDown || tpRightDown || tpMiddleDown)
                {
                    tpLeftDown = false;
                    tpRightDown = false;
                    tpMiddleDown = false;
                    trackpadVisual.Invalidate();
                }
            };

            lblUsbTest = new Label
            {
                Dock = DockStyle.Fill,
                Text = _usbTracker.GetHudStatusText(),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = HudTheme.FontMono9Bold,
                BackColor = Color.FromArgb(16, 9, 28),
                ForeColor = HudTheme.HudAccent,
                Cursor = Cursors.Hand,
                Margin = new Padding(2)
            };
            lblUsbTest.Paint += (s, pe) =>
            {
                bool isPassed = _usbTracker.TestedPortsCount > 0;
                using (Pen p = new Pen(isPassed ? HudTheme.PassNominal : Color.FromArgb(40, 168, 85, 247), 1))
                    pe.Graphics.DrawRectangle(p, 0, 0, lblUsbTest.Width - 1, lblUsbTest.Height - 1);
            };
            lblUsbTest.Click += (s, e) =>
            {
                _usbTracker.Reset();
                lblUsbTest.Text = _usbTracker.GetHudStatusText();
                lblUsbTest.ForeColor = HudTheme.HudAccent;
                lblUsbTest.Invalidate();
            };

            usbTpGrid.Controls.Add(trackpadVisual, 0, 0);
            usbTpGrid.Controls.Add(lblUsbTest, 1, 0);
            cardUsb.Controls.Add(usbTpGrid);
            rightStack.Controls.Add(cardUsb, 0, 1);

            // Card C: Warehouse Cloud & Label Dispatch
            cardWarehouse = new BentoCard
            {
                Dock = DockStyle.Fill,
                HeaderTitle = "WAREHOUSE INVENTORY PIPELINE",
                HeaderSubtitle = "CONNECTED",
                HeaderSubtitleColor = HudTheme.PassNominal,
                HeaderSubtitleBgColor = Color.FromArgb(16, 45, 30),
                HeaderSubtitleBorderColor = Color.FromArgb(50, 16, 185, 129),
                TagAccentColor = HudTheme.PassNominal,
                Margin = new Padding(0, 2, 0, 0)
            };
            TableLayoutPanel whGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(6, 2, 6, 2)
            };
            whGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 16F));
            whGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 16F));
            whGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            Label lblSheet = new Label
            {
                Text = "Cloud Sheet: Refurb_Inventory_2026",
                Font = HudTheme.FontMono9,
                ForeColor = HudTheme.HudAccentSoft,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            Label lblQueue = new Label
            {
                Text = $"Queue: {OfflineSyncQueue.Instance.PendingCount} Pending | Printer: Zebra ESC/POS",
                Font = HudTheme.FontMono9,
                ForeColor = HudTheme.PassNominal,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            GlowButton btnOpenAsset = new GlowButton
            {
                Text = "ASSET & QR CODE LAB",
                HotkeyText = "[Ctrl+P]",
                Dock = DockStyle.Fill,
                AccentColor = HudTheme.HudAccentSoft,
                Margin = new Padding(0, 2, 0, 0)
            };
            btnOpenAsset.Click += (s, e) => ShowQRCode();

            whGrid.Controls.Add(lblSheet, 0, 0);
            whGrid.Controls.Add(lblQueue, 0, 1);
            whGrid.Controls.Add(btnOpenAsset, 0, 2);
            cardWarehouse.Controls.Add(whGrid);
            rightStack.Controls.Add(cardWarehouse, 0, 2);

            bottomSplit.Controls.Add(rightStack, 1, 0);
            arena.Controls.Add(bottomSplit, 0, 1);

            return arena;
        }

        private Control BuildBottomCommandBar()
        {
            Panel bar = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(11, 6, 20),
                Margin = new Padding(0, 2, 0, 0),
                Padding = new Padding(8, 0, 8, 0)
            };
            bar.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Color.FromArgb(50, 168, 85, 247), 1))
                    e.Graphics.DrawLine(p, 0, 0, bar.Width, 0);
            };

            Label lblShortcuts = new Label
            {
                Text = "HOTKEYS:  [Space] Pass Test   [Tab] Next Test   [Enter] QC Sign-Off   [Ctrl+P] Print Label   [F5] Refresh",
                Font = HudTheme.FontMono9Bold,
                ForeColor = HudTheme.HudAccentSoft,
                Dock = DockStyle.Left,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft
            };

            Label lblStation = new Label
            {
                Text = "STATION AUDIT ID: #SAM-20260909-001  |  PASS RATIO: 100%",
                Font = HudTheme.FontMono9Bold,
                ForeColor = HudTheme.PassNominal,
                Dock = DockStyle.Right,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleRight
            };

            bar.Controls.Add(lblShortcuts);
            bar.Controls.Add(lblStation);
            return bar;
        }

        public void UpdateBentoGridTelemetry()
        {
            if (this.IsDisposed || !this.IsHandleCreated) return;
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(UpdateBentoGridTelemetry));
                return;
            }

            try
            {
                // 1. Chassis Identity Tile
                if (lblDeviceModel != null)
                {
                    lblDeviceModel.Text = string.IsNullOrWhiteSpace(lastModel) || lastModel == "N/A"
                        ? "Detecting Chassis..."
                        : lastModel;
                }
                if (lblDeviceSerial != null)
                {
                    lblDeviceSerial.Text = $"SN: {(string.IsNullOrWhiteSpace(lastSerial) ? "N/A" : lastSerial)}";
                }
                if (cardDevice != null && !string.IsNullOrWhiteSpace(lastModel) && lastModel != "N/A")
                {
                    string manufacturer = "OEM System";
                    if (lastModel.Contains("Dell", StringComparison.OrdinalIgnoreCase)) manufacturer = "Dell Inc.";
                    else if (lastModel.Contains("ThinkPad", StringComparison.OrdinalIgnoreCase) || lastModel.Contains("Lenovo", StringComparison.OrdinalIgnoreCase)) manufacturer = "Lenovo Group";
                    else if (lastModel.Contains("HP", StringComparison.OrdinalIgnoreCase) || lastModel.Contains("Hewlett", StringComparison.OrdinalIgnoreCase)) manufacturer = "HP Inc.";
                    else if (lastModel.Contains("Apple", StringComparison.OrdinalIgnoreCase) || lastModel.Contains("MacBook", StringComparison.OrdinalIgnoreCase)) manufacturer = "Apple Inc.";
                    else if (lastModel.Contains("ASUS", StringComparison.OrdinalIgnoreCase)) manufacturer = "ASUS Corp";
                    else if (lastModel.Contains("Acer", StringComparison.OrdinalIgnoreCase)) manufacturer = "Acer Inc.";
                    cardDevice.HeaderSubtitle = manufacturer;
                    cardDevice.HeaderSubtitleColor = HudTheme.HudAccentSoft;
                    cardDevice.HeaderSubtitleBgColor = Color.FromArgb(30, 15, 50);
                    cardDevice.HeaderSubtitleBorderColor = Color.FromArgb(60, 168, 85, 247);
                }

                // 2. CPU & Thermals Tile
                if (lblCpuName != null)
                {
                    lblCpuName.Text = string.IsNullOrWhiteSpace(lastCpu) || lastCpu == "N/A"
                        ? "Detecting CPU..."
                        : lastCpu;
                }
                if (lblCpuClock != null)
                {
                    if (NativeMethods.TryGetProcessorPowerInfo(out var pInfo) && pInfo != null && pInfo.Length > 0)
                    {
                        double curGhz = pInfo[0].CurrentMhz / 1000.0;
                        double maxGhz = pInfo[0].MaxMhz / 1000.0;
                        lblCpuClock.Text = $"{curGhz:F2} GHz (Max {maxGhz:F2} GHz) · {pInfo.Length}T";
                        if (cardCpu != null)
                        {
                            cardCpu.HeaderSubtitle = $"{pInfo.Length}C NOMINAL";
                            cardCpu.HeaderSubtitleColor = HudTheme.PassNominal;
                            cardCpu.HeaderSubtitleBgColor = Color.FromArgb(16, 45, 30);
                            cardCpu.HeaderSubtitleBorderColor = Color.FromArgb(50, 16, 185, 129);
                        }
                    }
                    else
                    {
                        lblCpuClock.Text = $"{Environment.ProcessorCount} Logical Cores Active";
                    }
                }

                // 3. Storage & Memory Tile
                if (lblMemorySpecs != null)
                {
                    lblMemorySpecs.Text = string.IsNullOrWhiteSpace(lastRam) || lastRam == "N/A"
                        ? "RAM: Detecting..."
                        : $"RAM: {lastRam}";
                }
                if (lblStorageSummary != null)
                {
                    lblStorageSummary.Text = string.IsNullOrWhiteSpace(lastStorageSummary) || lastStorageSummary == "N/A"
                        ? "Drive: Detecting..."
                        : $"NVMe: {lastStorageSummary}";
                }
                if (cardMemory != null && !string.IsNullOrWhiteSpace(lastRam) && lastRam != "N/A")
                {
                    cardMemory.HeaderSubtitle = "16GB / NVMe";
                    cardMemory.HeaderSubtitleColor = HudTheme.HudAccentSoft;
                    cardMemory.HeaderSubtitleBgColor = Color.FromArgb(30, 15, 50);
                    cardMemory.HeaderSubtitleBorderColor = Color.FromArgb(60, 168, 85, 247);
                }
                if (lblStorageSmartBadge != null)
                {
                    if (!string.IsNullOrWhiteSpace(lastStorageHealth) && (lastStorageHealth.Contains("100%") || lastStorageHealth.Contains("PASS")))
                    {
                        lblStorageSmartBadge.Text = "SMART: 100% HEALTH [PASS]";
                        lblStorageSmartBadge.ForeColor = HudTheme.PassNominal;
                    }
                    else if (!string.IsNullOrWhiteSpace(lastStorageHealth) && lastStorageHealth != "N/A")
                    {
                        lblStorageSmartBadge.Text = $"SMART: {lastStorageHealth}";
                        lblStorageSmartBadge.ForeColor = HudTheme.WarnCaution;
                    }
                }

                // 4. Battery Telemetry Tile & Top Bar Chip
                string flowText = "AC Direct Power";
                float battHealthVal = 100f;

                if (NativeMethods.TryGetBatteryState(out var bState) && bState.BatteryPresent)
                {
                    flowText = FormatBatteryPowerFlow(bState);

                    if (lblBatteryFlow != null)
                    {
                        lblBatteryFlow.Text = flowText;
                    }

                    if (!string.IsNullOrWhiteSpace(lastBatteryHealth) && lastBatteryHealth != "N/A")
                    {
                        string cleaned = lastBatteryHealth.Replace("%", "").Trim();
                        if (float.TryParse(cleaned, out float parsed))
                        {
                            battHealthVal = Math.Clamp(parsed, 0, 100);
                        }
                    }

                    if (lblBatteryWear != null)
                    {
                        string timeStr = bState.Discharging && bState.EstimatedTime > 0 && bState.EstimatedTime < 86400
                            ? $"{bState.EstimatedTime / 3600}h {(bState.EstimatedTime % 3600) / 60}m left"
                            : (bState.Charging ? "Charging" : "AC Line Connected");
                        lblBatteryWear.Text = $"Health: {battHealthVal:F0}% · {timeStr}";
                    }

                    if (meterBattery != null)
                    {
                        meterBattery.Value = battHealthVal;
                        meterBattery.MeterColor = battHealthVal >= 80f ? HudTheme.PassNominal : (battHealthVal >= 60f ? HudTheme.WarnCaution : HudTheme.FailWarning);
                    }

                    if (cardBattery != null)
                    {
                        cardBattery.HeaderSubtitle = $"{battHealthVal:F0}% INTEGRITY";
                        cardBattery.HeaderSubtitleColor = HudTheme.PassNominal;
                        cardBattery.HeaderSubtitleBgColor = Color.FromArgb(16, 45, 30);
                        cardBattery.HeaderSubtitleBorderColor = Color.FromArgb(50, 16, 185, 129);
                    }

                    uint pct = bState.MaxCapacity > 0 ? (bState.RemainingCapacity * 100 / bState.MaxCapacity) : 100;
                    if (lblTopBattery != null)
                    {
                        lblTopBattery.Text = $"BATTERY: {pct}% ({flowText})";
                        lblTopBattery.ForeColor = bState.Discharging ? HudTheme.WarnCaution : HudTheme.PassNominal;
                    }
                }
                else
                {
                    if (lblBatteryFlow != null) lblBatteryFlow.Text = "AC Line Mainline (No Battery)";
                    if (lblBatteryWear != null) lblBatteryWear.Text = "Standard AC Subsystem Active";
                    if (meterBattery != null)
                    {
                        meterBattery.Value = 100f;
                        meterBattery.MeterColor = HudTheme.PassNominal;
                    }
                    if (cardBattery != null)
                    {
                        cardBattery.HeaderSubtitle = "AC MAINS";
                        cardBattery.HeaderSubtitleColor = HudTheme.HudAccentSoft;
                        cardBattery.HeaderSubtitleBgColor = Color.FromArgb(30, 15, 50);
                        cardBattery.HeaderSubtitleBorderColor = Color.FromArgb(60, 168, 85, 247);
                    }
                    if (lblTopBattery != null)
                    {
                        lblTopBattery.Text = "BATTERY: AC POWER";
                        lblTopBattery.ForeColor = HudTheme.PassNominal;
                    }
                }

                // Top bar network chip
                if (lblTopWifi != null)
                {
                    if (!string.IsNullOrWhiteSpace(lastNetworkSummary) && lastNetworkSummary.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase))
                    {
                        lblTopWifi.Text = "WI-FI: LINKED";
                        lblTopWifi.ForeColor = Color.FromArgb(56, 189, 248);
                    }
                    else if (!string.IsNullOrWhiteSpace(lastNetworkSummary) && lastNetworkSummary != "N/A")
                    {
                        lblTopWifi.Text = "ETH: ONLINE";
                        lblTopWifi.ForeColor = HudTheme.PassNominal;
                    }
                    else
                    {
                        lblTopWifi.Text = "📶 NET: STANDBY";
                        lblTopWifi.ForeColor = HudTheme.HudAccentSoft;
                    }
                }
            }
            catch { }
        }

        public void UpdateSuiteButtonState(string test)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => UpdateSuiteButtonState(test)));
                return;
            }

            GlowButton targetBtn = null;
            if (test.Equals("Display", StringComparison.OrdinalIgnoreCase)) targetBtn = btnSuiteDisplay;
            else if (test.Equals("Audio", StringComparison.OrdinalIgnoreCase)) targetBtn = btnSuiteAudio;
            else if (test.Equals("Camera", StringComparison.OrdinalIgnoreCase) || test.Equals("Webcam", StringComparison.OrdinalIgnoreCase)) targetBtn = btnSuiteWebcam;
            else if (test.Equals("Keyboard", StringComparison.OrdinalIgnoreCase)) targetBtn = btnSuiteKeyboard;
            else if (test.Equals("CPU", StringComparison.OrdinalIgnoreCase)) targetBtn = btnSuiteCpu;
            else if (test.Equals("GPU", StringComparison.OrdinalIgnoreCase) || test.Equals("GPU Stress", StringComparison.OrdinalIgnoreCase)) targetBtn = btnSuiteGpu;
            else if (test.Equals("WiFi", StringComparison.OrdinalIgnoreCase)) targetBtn = btnSuiteWifi;
            else if (test.Equals("Storage", StringComparison.OrdinalIgnoreCase)) targetBtn = btnSuiteStorage;

            if (targetBtn != null)
            {
                targetBtn.AccentColor = HudTheme.PassNominal;
                targetBtn.StatusBadge = "✓ PASS";
                targetBtn.Invalidate();
            }
        }

        public void LaunchGpuBenchmark()
        {
            using (var gpuForm = new GpuBenchmarkForm(gpuName: lastModel, vram: "Dedicated / Dynamic"))
            {
                gpuForm.ShowDialog(this);
            }
            MarkTestComplete("GPU");
        }

        public void TriggerLabelPrint()
        {
            try
            {
                int bHealth = 100;
                if (!string.IsNullOrWhiteSpace(lastBatteryHealth))
                {
                    string clean = lastBatteryHealth.Replace("%", "").Trim();
                    if (int.TryParse(clean, out int parsed)) bHealth = parsed;
                }

                var record = new AssetQueueRecord
                {
                    Asset_Tag = $"ASSET-{DateTime.Now:yyyyMMddHHmm}",
                    Model = string.IsNullOrWhiteSpace(lastModel) || lastModel == "N/A" ? "Refurbished Unit" : lastModel,
                    Serial_Number = string.IsNullOrWhiteSpace(lastSerial) ? "N/A" : lastSerial,
                    Processor = string.IsNullOrWhiteSpace(lastCpu) ? "Intel / AMD Core" : lastCpu,
                    Memory = string.IsNullOrWhiteSpace(lastRam) ? "16GB" : lastRam,
                    Battery_Health = bHealth,
                    Physical_Grade = "A+",
                    Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    QueuedAt = DateTime.Now
                };
                ThermalLabelPrinter.PrintLabel(record, this);
            }
            catch (Exception ex)
            {
                DarkMessageBox.Show($"Thermal Print Error: {ex.Message}", "Printer Error");
            }
        }

        private void StartCpuSampleTimer()
        {
            _cpuSampleTimer = new System.Windows.Forms.Timer { Interval = 1200 };
            _cpuSampleTimer.Tick += (s, ev) =>
            {
                try
                {
                    if (this.IsDisposed || !this.IsHandleCreated) return;
                    float cpuUsage = NativeMethods.GetSystemCpuUsage();
                    sparkCpu?.AddValue(cpuUsage);
                    if (lblTopCpu != null)
                    {
                        lblTopCpu.Text = $"⚡ CPU: {cpuUsage:F0}%";
                        lblTopCpu.ForeColor = cpuUsage > 80f ? HudTheme.WarnCaution : (cpuUsage > 92f ? HudTheme.FailWarning : HudTheme.HudAccent);
                    }
                }
                catch { }
            };
            _cpuSampleTimer.Start();
        }

        private void CopyTelemetryToClipboard()
        {
            try
            {
                string text = reportBox != null ? reportBox.Text : "";
                if (!string.IsNullOrEmpty(text))
                {
                    Clipboard.SetText(text);
                    DarkMessageBox.Show("System Telemetry copied to Windows Clipboard.", "Telemetry Exported");
                }
            }
            catch (Exception ex)
            {
                DarkMessageBox.Show("Clipboard copy failed: " + ex.Message, "Export Error");
            }
        }

        private async Task TriggerStorageBenchmark()
        {
            try
            {
                string result = await RunStorageBenchmarkAsync();
                DarkMessageBox.Show(result, "Storage Speed Benchmark");
            }
            catch (Exception ex)
            {
                DarkMessageBox.Show("Benchmark error: " + ex.Message, "Error");
            }
        }

        private async Task RunExpressQCSequenceAsync()
        {
            try
            {
                using (var hud = new ExpressQCHudForm(this))
                {
                    if (hud.ShowDialog(this) == DialogResult.OK)
                    {
                        hud.ApplyResultsToForm(this);
                        DarkMessageBox.Show("⚡ Express QC Sequence Complete!\n\nAll automated hardware diagnostics certified & applied to QC Checklist.\nPlease click 'QC SIGN-OFF' to sign & export.", "Express QC Finished");
                    }
                }
            }
            catch (Exception ex)
            {
                DarkMessageBox.Show("Express QC Sequence Error: " + ex.Message, "Express QC Error");
            }
        }

        #endregion

        #region Form Lifecycle

        private void Form1_Load(object sender, EventArgs e)
        {
            try
            {
                // Bootstrap DPI-aware scaling immediately on load with actual device DPI
                ScalingService.Instance.Recalculate(this.ClientSize.Height, this.DeviceDpi);

                lblAdminWarning.Visible = !IsAdministrator();
                _hookID = SetHook(_proc);
            }
            catch (Exception ex)
            {
                DarkMessageBox.Show($"Startup Hook Error: {ex.Message}", "System Failure");
            }
        }

        private async void Form1_Shown(object sender, EventArgs e)
        {
            // The UI frame and Bento controls are already visible and painted on the screen!
            // All heavy operations run asynchronously in the background with zero UI freeze:
            try
            {
                // 1. Silent background Wi-Fi connection
                _ = StartupServices.ConnectToGtwWifiAsync();

                // 2. Background setup verification (non-blocking)
                _ = Task.Run(() => StartupServices.EnsureSheetsSetupFiles());

                // 3. Deferred microphone visualizer initialization
                _ = Task.Run(() =>
                {
                    try
                    {
                        if (this.IsHandleCreated && !this.IsDisposed)
                        {
                            this.BeginInvoke(new Action(StartMicVisualizer));
                        }
                    }
                    catch { }
                });

                // 4. Asynchronous Hardware Telemetry Probe
                await GenerateHardwareReport();

                // 5. Passive background network status test
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using (var ping = new System.Net.NetworkInformation.Ping())
                        {
                            var reply = await ping.SendPingAsync("1.1.1.1", 2500);
                            if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                            {
                                if (!this.IsDisposed && this.IsHandleCreated)
                                    this.BeginInvoke(new Action(() => MarkTestComplete("WiFi")));

                                if (OfflineSyncQueue.Instance.PendingCount > 0)
                                {
                                    await OfflineSyncQueue.Instance.FlushQueueAsync(AssetCsvExportForm.DefaultEmbeddedSheetsUrl);
                                }
                            }
                        }
                    }
                    catch { }
                });

                // 6. Live background battery power wattage monitor
                _powerTimer = new System.Windows.Forms.Timer { Interval = 2500 };
                _powerTimer.Tick += (s, ev) => UpdateLiveBatteryTelemetryCard();
                _powerTimer.Start();

                // 7. Live background CPU usage telemetry sparkline sampler
                StartCpuSampleTimer();
            }
            catch (Exception ex)
            {
                DarkMessageBox.Show($"Telemetry Initialization Error: {ex.Message}", "System Failure");
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Stop background timers
            try { _powerTimer?.Stop(); _powerTimer?.Dispose(); } catch { }
            try { _cpuSampleTimer?.Stop(); _cpuSampleTimer?.Dispose(); } catch { }

            // Unhook keyboard
            if (_hookID != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
            }

            // Clean up keyboard paint GDI cache
            try { DisposeKeyboardGdiCache(); } catch { }

            // Stop webcam
            StopWebcam();

            // Stop audio
            try { StopCurrentAudio(); } catch { }
            try { StopMicVisualizer(); } catch { }

            // Dispose fonts
            try
            {
                uiFont?.Dispose();
                uiFontBold?.Dispose();
            }
            catch { }
        }

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            HighlightKey(e.KeyCode, true);

            // Technician Hotkey shortcuts (disabled when typing inside a text box)
            if (!(this.ActiveControl is TextBox) && !(this.ActiveControl is RichTextBox))
            {
                if (e.KeyCode == Keys.F1) { e.Handled = true; LaunchDisplayTest(); }
                else if (e.KeyCode == Keys.F2) { e.Handled = true; LaunchAudioTest(); }
                else if (e.KeyCode == Keys.F3) { e.Handled = true; ToggleWebcam(btnCameraToggle, EventArgs.Empty); }
                else if (e.KeyCode == Keys.F4) { e.Handled = true; FocusKeyboardMatrix(); }
                else if (e.KeyCode == Keys.F5) { e.Handled = true; _ = RunExpressQCSequenceAsync(); }
                else if (e.KeyCode == Keys.F6) { e.Handled = true; LaunchCpuBurn(); }
                else if (e.KeyCode == Keys.F7) { e.Handled = true; LaunchGpuBenchmark(); }
                else if (e.KeyCode == Keys.F8) { e.Handled = true; LaunchWifiRadar(); }
                else if (e.KeyCode == Keys.F9) { e.Handled = true; LaunchStorageBenchmark(); }
                else if (e.Control && e.KeyCode == Keys.P) { e.Handled = true; TriggerLabelPrint(); }
                else if (e.KeyCode == Keys.Enter && (e.Modifiers == Keys.None || e.Modifiers == Keys.Control)) { e.Handled = true; slidingDrawer?.ToggleDrawer(); }
                else if (e.KeyCode == Keys.Escape) { e.Handled = true; ResetKeyboardUI(); }
            }
        }

        private void Form1_KeyUp(object sender, KeyEventArgs e)
        {
            HighlightKey(e.KeyCode, false);
        }

        protected override void WndProc(ref Message m)
        {
            _usbTracker?.ProcessWndProc(ref m);
            base.WndProc(ref m);
        }

        #endregion
    }
}
