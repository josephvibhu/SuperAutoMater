using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ITAS_QC_Tool
{
    /// <summary>
    /// AutoMater QC Diagnostic Studio — Avionics HUD Interface v6.3.
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
        private TelemetryCardsView telemetryCards;
        private PerformanceGraphControl perfGraphs;
        private SlidingQcDrawer slidingDrawer;
        private NavRailPanel navRail;

        private Panel keyboardPanel;
        private Panel camPanel;
        private HudVuMeter micVuMeter;
        private Label lblMicState;
        private Label lblMouseTest;
        private TableLayoutPanel rootSplit;
        private Panel centerArea;

        private bool tpLeft = false;
        private bool tpRight = false;
        private bool tpMiddle = false;

        private readonly UsbPortTracker _usbTracker = new UsbPortTracker();
        private Label lblUsbTest;
        private System.Windows.Forms.Timer _powerTimer;

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
            this.Size = new Size(1440, 920);
            this.MinimumSize = new Size(1024, 700);
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
            this.FormClosing += Form1_FormClosing;
            this.KeyDown += Form1_KeyDown;
            this.KeyUp += Form1_KeyUp;
            this.Resize += Form1_Resize;
            this.DpiChanged += (s, e) => HandleDpiOrSizeChange();
        }

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
            // Root 3-Column Split: Column 0 (44px Left Rail) | Column 1 (100% Canvas) | Column 2 (0-350px QC Drawer)
            rootSplit = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0),
                BackColor = HudTheme.BgGlass
            };
            rootSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44F));
            rootSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            rootSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0F));
            rootSplit.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            // 1. Navigation Rail (Left Column 0: Test Status Indicators)
            navRail = new NavRailPanel();
            navRail.MenuButtonClicked += () => slidingDrawer.ToggleDrawer();
            rootSplit.Controls.Add(navRail, 0, 0);

            // 2. Sliding QC Drawer (Right Column 2)
            slidingDrawer = new SlidingQcDrawer();
            slidingDrawer.DrawerWidthChanged += (w) =>
            {
                rootSplit.ColumnStyles[2].Width = w;
                rootSplit.PerformLayout();
            };
            rootSplit.Controls.Add(slidingDrawer, 2, 0);

            // 3. Central Canvas Panel (Center Column 1)
            centerArea = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 4, 6, 4), BackColor = HudTheme.BgGlass };

            // --- TOP BRANDING HEADER BANNER ---
            Panel headerBanner = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = Color.FromArgb(14, 20, 24), Margin = new Padding(0, 0, 0, 4) };
            headerBanner.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.None;
                using (Pen p = new Pen(HudTheme.Bezel, 1))
                    e.Graphics.DrawRectangle(p, 0, 0, headerBanner.Width - 1, headerBanner.Height - 1);
                using (Pen p = new Pen(HudTheme.HudAccent, 1))
                {
                    e.Graphics.DrawLine(p, 0, 0, 8, 0);
                    e.Graphics.DrawLine(p, 0, 0, 0, 8);
                    e.Graphics.DrawLine(p, headerBanner.Width - 9, 0, headerBanner.Width - 1, 0);
                    e.Graphics.DrawLine(p, headerBanner.Width - 1, 0, headerBanner.Width - 1, 8);
                }
            };

            Label logoTitle = new Label
            {
                Text = AppVersion.Display,
                Dock = DockStyle.Left,
                AutoSize = true,
                Font = HudTheme.FontTitle13Bold,
                ForeColor = HudTheme.HudAccent,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 6, 0, 0)
            };

            HudButton btnOpenSign = new HudButton
            {
                Text = "🔏 QC SIGN-OFF ❯",
                Dock = DockStyle.Right,
                Width = 200,
                HudAccentColor = HudTheme.PassNominal,
                Font = HudTheme.FontMono11Bold
            };
            btnOpenSign.Click += (s, e) => slidingDrawer.ToggleDrawer();

            Label authorTag = new Label
            {
                Text = AppVersion.StationTag,
                Dock = DockStyle.Right,
                AutoSize = true,
                Font = HudTheme.FontMono11Bold,
                ForeColor = HudTheme.PassNominal,
                TextAlign = ContentAlignment.MiddleRight,
                Padding = new Padding(0, 8, 12, 0)
            };

            headerBanner.Controls.Add(logoTitle);
            headerBanner.Controls.Add(authorTag);
            headerBanner.Controls.Add(btnOpenSign);
            centerArea.Controls.Add(headerBanner);

            // --- RESPONSIVE 2-ROW MAIN GRID ---
            TableLayoutPanel mainGrid = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(0, 4, 0, 0) };
            mainGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 55F)); // Top Section: Telemetry, Controls, Camera & Mic
            mainGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 45F)); // Bottom Section: Perf Graphs & 104-Key Matrix

            // --- TOP ROW: 3-COLUMN SPLIT (38% Telemetry Cards, 34% Controls, 28% Camera & Sensors) ---
            TableLayoutPanel topGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(0, 0, 0, 4) };
            topGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F)); // System Telemetry Cards
            topGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F)); // Diagnostic Controls
            topGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F)); // Camera & Sensors

            // 1. System Telemetry Panel (Structured 5-Card View)
            HudBracketPanel grpSpecs = new HudBracketPanel
            {
                Dock = DockStyle.Fill,
                BezelTitle = "SYSTEM TELEMETRY",
                SubtitleBadge = "SPECIFICATIONS",
                AccentColor = HudTheme.HudAccent
            };

            telemetryCards = new TelemetryCardsView { Dock = DockStyle.Fill };

            lblAdminWarning = new Label
            {
                Dock = DockStyle.Top,
                Height = 20,
                Text = "⚠ CAUTION: Run as Administrator for deep NVMe SMART & battery wear analytics",
                BackColor = Color.FromArgb(40, 30, 15),
                ForeColor = HudTheme.WarnCaution,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = HudTheme.FontMono11Bold,
                Visible = false
            };

            grpSpecs.Controls.Add(telemetryCards);
            grpSpecs.Controls.Add(lblAdminWarning);
            topGrid.Controls.Add(grpSpecs, 0, 0);

            // 2. Diagnostic Controls Panel (Avionics Launchpad)
            HudBracketPanel grpTools = new HudBracketPanel
            {
                Dock = DockStyle.Fill,
                BezelTitle = "DIAGNOSTIC CONTROLS",
                SubtitleBadge = "POST LABS",
                AccentColor = HudTheme.WarnCaution
            };

            TableLayoutPanel toolGrid = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 10, ColumnCount = 1 };
            for (int i = 0; i < 10; i++) toolGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 10F));

            // Row 0: Hero 10-Second Express QC Button
            HudButton btnExpress = new HudButton
            {
                Text = "⚡ RUN 10-SECOND EXPRESS QC (POST)",
                Dock = DockStyle.Fill,
                HudAccentColor = HudTheme.HudAccent,
                Font = HudTheme.FontMono11Bold
            };
            btnExpress.Click += async (s, e) => await RunExpressQCSequenceAsync();
            toolGrid.Controls.Add(btnExpress, 0, 0);

            // Row 1: Screen Tests (Quick Colors | Advanced Lab)
            TableLayoutPanel screenGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            screenGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            screenGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            HudButton btnScreenQuick = new HudButton { Text = "SCREEN COLORS", Dock = DockStyle.Fill, HudAccentColor = HudTheme.HudAccent };
            btnScreenQuick.Click += (s, e) => LaunchDisplayTest();

            HudButton btnScreenAdv = new HudButton { Text = "ADV DISPLAY LAB", Dock = DockStyle.Fill, HudAccentColor = HudTheme.StorageAux };
            btnScreenAdv.Click += (s, e) =>
            {
                using (var adf = new AdvancedDisplayTestForm()) { adf.ShowDialog(); }
            };

            screenGrid.Controls.Add(btnScreenQuick, 0, 0);
            screenGrid.Controls.Add(btnScreenAdv, 1, 0);
            toolGrid.Controls.Add(screenGrid, 0, 1);

            // Row 2: Touchscreen & Storage Benchmark
            TableLayoutPanel touchStorageGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            touchStorageGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            touchStorageGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            HudButton btnTouch = new HudButton { Text = "TOUCH DIGITIZER", Dock = DockStyle.Fill, HudAccentColor = HudTheme.PassNominal };
            btnTouch.Click += (s, e) =>
            {
                using (var ttf = new TouchscreenTestForm()) { ttf.ShowDialog(); }
            };

            HudButton btnStorage = new HudButton { Text = "STORAGE SPEED BENCH", Dock = DockStyle.Fill, HudAccentColor = HudTheme.HudAccent };
            btnStorage.Click += (s, e) => LaunchStorageBenchmark();

            touchStorageGrid.Controls.Add(btnTouch, 0, 0);
            touchStorageGrid.Controls.Add(btnStorage, 1, 0);
            toolGrid.Controls.Add(touchStorageGrid, 0, 2);

            // Row 3: 3-Way Split Speaker Test Row (Left | Both | Right)
            TableLayoutPanel speakerGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0) };
            speakerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32F));
            speakerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36F));
            speakerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32F));

            HudButton btnSpkLeft = new HudButton { Text = "◄ LEFT", Dock = DockStyle.Fill, HudAccentColor = HudTheme.HudAccent };
            btnSpkLeft.Click += async (s, e) => await PlayAudioTest(true, false, AudioTestMode.Left, btnSpkLeft);

            HudButton btnSpkBoth = new HudButton { Text = "STEREO BOTH", Dock = DockStyle.Fill, HudAccentColor = HudTheme.WarnCaution };
            btnSpkBoth.Click += async (s, e) => await PlayAudioTest(true, true, AudioTestMode.Both, btnSpkBoth);

            HudButton btnSpkRight = new HudButton { Text = "RIGHT ►", Dock = DockStyle.Fill, HudAccentColor = HudTheme.StorageAux };
            btnSpkRight.Click += async (s, e) => await PlayAudioTest(false, true, AudioTestMode.Right, btnSpkRight);

            speakerGrid.Controls.Add(btnSpkLeft, 0, 0);
            speakerGrid.Controls.Add(btnSpkBoth, 1, 0);
            speakerGrid.Controls.Add(btnSpkRight, 2, 0);
            toolGrid.Controls.Add(speakerGrid, 0, 3);

            // Row 4: WiFi, Bluetooth & Fingerprint
            TableLayoutPanel netBioGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0) };
            netBioGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3F));
            netBioGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3F));
            netBioGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.4F));

            HudButton btnWifi = new HudButton { Text = "WI-FI RADAR", Dock = DockStyle.Fill, HudAccentColor = HudTheme.HudAccent };
            btnWifi.Click += (s, e) => LaunchWifiRadar();

            HudButton btnBluetooth = new HudButton { Text = "BLUETOOTH RADAR", Dock = DockStyle.Fill, HudAccentColor = HudTheme.PassNominal };
            btnBluetooth.Click += (s, e) => LaunchBluetoothTest();

            HudButton btnFingerprint = new HudButton { Text = "FINGERPRINT BIO", Dock = DockStyle.Fill, HudAccentColor = HudTheme.StorageAux };
            btnFingerprint.Click += (s, e) => LaunchFingerprintTest();

            netBioGrid.Controls.Add(btnWifi, 0, 0);
            netBioGrid.Controls.Add(btnBluetooth, 1, 0);
            netBioGrid.Controls.Add(btnFingerprint, 2, 0);
            toolGrid.Controls.Add(netBioGrid, 0, 4);

            // Row 5: CPU & RAM | GPU Stress
            TableLayoutPanel cpuGpuGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            cpuGpuGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            cpuGpuGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            HudButton btnCpuRam = new HudButton { Text = "CPU / RAM BURN", Dock = DockStyle.Fill, HudAccentColor = HudTheme.HudAccent };
            btnCpuRam.Click += (s, e) => LaunchCpuBurn();

            HudButton btnGpu = new HudButton { Text = "GPU 3D RENDER", Dock = DockStyle.Fill, HudAccentColor = HudTheme.StorageAux };
            btnGpu.Click += (s, e) =>
            {
                using (var gpuForm = new GpuBenchmarkForm(gpuName: lastModel, vram: "Dedicated / Dynamic")) { gpuForm.ShowDialog(); }
                MarkTestComplete("GPU");
            };

            cpuGpuGrid.Controls.Add(btnCpuRam, 0, 0);
            cpuGpuGrid.Controls.Add(btnGpu, 1, 0);
            toolGrid.Controls.Add(cpuGpuGrid, 0, 5);

            // Row 6: Export & QR
            TableLayoutPanel copyQrGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            copyQrGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            copyQrGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            HudButton btnCopy = new HudButton { Text = "COPY TELEMETRY", Dock = DockStyle.Fill, HudAccentColor = HudTheme.HudAccent };
            btnCopy.Click += (s, e) => CopyTelemetryToClipboard();

            HudButton btnQR = new HudButton { Text = "ASSET & QC RECORD", Dock = DockStyle.Fill, HudAccentColor = HudTheme.HudAccent };
            btnQR.Click += (s, e) => ShowQRCode();

            copyQrGrid.Controls.Add(btnCopy, 0, 0);
            copyQrGrid.Controls.Add(btnQR, 1, 0);
            toolGrid.Controls.Add(copyQrGrid, 0, 6);

            // Row 7: Trackpad Test & Live USB Port Plug Tracker
            TableLayoutPanel tpUsbGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            tpUsbGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48F));
            tpUsbGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52F));

            lblMouseTest = new Label
            {
                Dock = DockStyle.Fill,
                Text = "⟨ TRACKPAD : [ L ] [ M ] [ R ] ⟩",
                TextAlign = ContentAlignment.MiddleCenter,
                Font = HudTheme.FontMono11Bold,
                BackColor = Color.FromArgb(14, 20, 24),
                ForeColor = HudTheme.HudAccent,
                Cursor = Cursors.Cross,
                Margin = new Padding(2)
            };
            lblMouseTest.Paint += (s, pe) =>
            {
                pe.Graphics.SmoothingMode = SmoothingMode.None;
                bool isPassed = (tpLeft && tpRight);
                using (Pen p = new Pen(isPassed ? HudTheme.PassNominal : HudTheme.Bezel, 1))
                    pe.Graphics.DrawRectangle(p, 0, 0, lblMouseTest.Width - 1, lblMouseTest.Height - 1);
            };
            lblMouseTest.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) tpLeft = true;
                else if (e.Button == MouseButtons.Right) tpRight = true;
                else if (e.Button == MouseButtons.Middle) tpMiddle = true;

                lblMouseTest.BackColor = Color.FromArgb(28, 65, 52);
                lblMouseTest.ForeColor = HudTheme.TextBright;
                lblMouseTest.Text = $"⟨ TRACKPAD : [ {(tpLeft ? "✓ L" : "L")} ] [ {(tpMiddle ? "✓ M" : "M")} ] [ {(tpRight ? "✓ R" : "R")} ] ⟩";
                lblMouseTest.Invalidate();

                MarkTestComplete("Trackpad");
            };
            lblMouseTest.MouseUp += (s, e) =>
            {
                lblMouseTest.BackColor = Color.FromArgb(16, 38, 30);
                lblMouseTest.ForeColor = HudTheme.PassNominal;
                lblMouseTest.Text = $"⟨ TRACKPAD : [ {(tpLeft ? "✓ L" : "L")} ] [ {(tpMiddle ? "✓ M" : "M")} ] [ {(tpRight ? "✓ R" : "R")} ] ⟩";
                lblMouseTest.Invalidate();
            };

            lblUsbTest = new Label
            {
                Dock = DockStyle.Fill,
                Text = _usbTracker.GetHudStatusText(),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = HudTheme.FontMono11Bold,
                BackColor = Color.FromArgb(14, 20, 24),
                ForeColor = HudTheme.HudAccent,
                Cursor = Cursors.Hand,
                Margin = new Padding(2)
            };
            lblUsbTest.Paint += (s, pe) =>
            {
                pe.Graphics.SmoothingMode = SmoothingMode.None;
                bool isPassed = _usbTracker.TestedPortsCount > 0;
                using (Pen p = new Pen(isPassed ? HudTheme.PassNominal : HudTheme.Bezel, 1))
                    pe.Graphics.DrawRectangle(p, 0, 0, lblUsbTest.Width - 1, lblUsbTest.Height - 1);
            };
            lblUsbTest.Click += (s, e) =>
            {
                _usbTracker.Reset();
                lblUsbTest.Text = _usbTracker.GetHudStatusText();
                lblUsbTest.ForeColor = HudTheme.HudAccent;
                lblUsbTest.Invalidate();
            };

            tpUsbGrid.Controls.Add(lblMouseTest, 0, 0);
            tpUsbGrid.Controls.Add(lblUsbTest, 1, 0);
            toolGrid.Controls.Add(tpUsbGrid, 0, 7);

            // Row 8: Reset Matrix Highlights
            HudButton btnReset = new HudButton { Text = "RESET KEYBOARD & TRACKPAD", Dock = DockStyle.Fill, HudAccentColor = HudTheme.Muted };
            btnReset.Click += (s, e) => ResetKeyboardUI();
            toolGrid.Controls.Add(btnReset, 0, 8);

            // Row 9: Finish & Exit
            HudButton btnExit = new HudButton { Text = "EXIT DIAGNOSTIC STUDIO", Dock = DockStyle.Fill, HudAccentColor = HudTheme.FailWarning };
            btnExit.Click += (s, e) => this.Close();
            toolGrid.Controls.Add(btnExit, 0, 9);

            grpTools.Controls.Add(toolGrid);
            topGrid.Controls.Add(grpTools, 1, 0);

            // 3. Camera & Sensors Panel (Full-View Live Video + Dedicated Mic Section)
            HudBracketPanel grpMedia = new HudBracketPanel
            {
                Dock = DockStyle.Fill,
                BezelTitle = "CAMERA & SENSORS",
                SubtitleBadge = "LIVE STREAM",
                AccentColor = HudTheme.HudAccent
            };

            TableLayoutPanel mediaGrid = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
            mediaGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 65F)); // Full-sized Camera Viewport
            mediaGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));  // Camera Action Buttons
            mediaGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));  // Dedicated Mic Container

            camPanel = BuildCameraPanel();
            mediaGrid.Controls.Add(camPanel, 0, 0);

            // Single Row of Camera Action Buttons
            TableLayoutPanel camBtnRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 2, 0, 2) };
            camBtnRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68F));
            camBtnRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32F));

            btnCameraToggle = new HudButton { Text = "START CAMERA TEST", Dock = DockStyle.Fill, HudAccentColor = HudTheme.HudAccent };
            btnCameraToggle.Click += (s, e) => ToggleWebcam(btnCameraToggle, EventArgs.Empty);

            btnCameraSwitch = new HudButton { Text = "⇄ CAM", Dock = DockStyle.Fill, HudAccentColor = HudTheme.StorageAux };
            btnCameraSwitch.Click += (s, e) => SwitchCamera();

            camBtnRow.Controls.Add(btnCameraToggle, 0, 0);
            camBtnRow.Controls.Add(btnCameraSwitch, 1, 0);
            mediaGrid.Controls.Add(camBtnRow, 0, 1);

            // Dedicated Mic Section Container (Never Hidden or Squeezed)
            Panel micPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(14, 20, 24), Padding = new Padding(6, 3, 6, 3) };
            micPanel.Paint += (s, e) =>
            {
                using (Pen p = new Pen(HudTheme.Bezel, 1))
                    e.Graphics.DrawRectangle(p, 0, 0, micPanel.Width - 1, micPanel.Height - 1);
            };

            TableLayoutPanel micGrid = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            micGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 16F));
            micGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            lblMicState = new Label
            {
                Text = "MIC SENSOR : ACTIVE [44.1 kHz / 16-BIT]",
                ForeColor = HudTheme.HudAccent,
                Font = HudTheme.FontMono11Bold,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            micVuMeter = new HudVuMeter { Dock = DockStyle.Fill };
            micGrid.Controls.Add(lblMicState, 0, 0);
            micGrid.Controls.Add(micVuMeter, 0, 1);
            micPanel.Controls.Add(micGrid);

            mediaGrid.Controls.Add(micPanel, 0, 2);
            grpMedia.Controls.Add(mediaGrid);
            topGrid.Controls.Add(grpMedia, 2, 0);

            mainGrid.Controls.Add(topGrid, 0, 0);

            // --- BOTTOM ROW: 2-COLUMN SPLIT (30% Performance Graphs, 70% 104-Key Matrix) ---
            TableLayoutPanel bottomGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 4, 0, 0) };
            bottomGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F)); // System Performance & Utilization Graphs
            bottomGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70F)); // 104-Key Matrix

            // 4. System Performance & Utilization Graphs Panel
            HudBracketPanel grpPerf = new HudBracketPanel
            {
                Dock = DockStyle.Fill,
                BezelTitle = "SYSTEM PERFORMANCE & UTILIZATION",
                SubtitleBadge = "OSCILLOSCOPE",
                AccentColor = HudTheme.HudAccent
            };
            perfGraphs = new PerformanceGraphControl { Dock = DockStyle.Fill };
            grpPerf.Controls.Add(perfGraphs);
            bottomGrid.Controls.Add(grpPerf, 0, 0);

            // 5. 104-Key Virtual Keyboard Matrix Panel
            HudBracketPanel grpKeyboard = new HudBracketPanel
            {
                Dock = DockStyle.Fill,
                BezelTitle = "INPUT // 104-KEY MATRIX",
                SubtitleBadge = "REAL-TIME INTERRUPT",
                AccentColor = HudTheme.PassNominal
            };

            keyboardPanel = new BufferedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(14, 20, 24),
                Margin = new Padding(0)
            };
            keyboardPanel.Paint += KeyboardPanel_Paint;
            grpKeyboard.Controls.Add(keyboardPanel);
            bottomGrid.Controls.Add(grpKeyboard, 1, 0);

            mainGrid.Controls.Add(bottomGrid, 0, 1);
            centerArea.Controls.Add(mainGrid);

            rootSplit.Controls.Add(centerArea, 1, 0);
            this.Controls.Add(rootSplit);

            Build104Keyboard();
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

        private async void Form1_Load(object sender, EventArgs e)
        {
            try
            {
                lblAdminWarning.Visible = !IsAdministrator();
                _hookID = SetHook(_proc);

                // ── Background startup tasks (non-blocking) ──────────────────
                // 1. Auto-connect to GTW WiFi silently
                _ = StartupServices.ConnectToGtwWifiAsync();

                // 2. Bundle Sheets code.gs + guide on first run
                StartupServices.EnsureSheetsSetupFiles();

                StartMicVisualizer();
                await GenerateHardwareReport();

                // 3. Passive background network status test (marks WiFi nav icon green)
                _ = System.Threading.Tasks.Task.Run(async () =>
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

                // 4. Live background battery power wattage monitor (updates Card 5 every 2.5s)
                _powerTimer = new System.Windows.Forms.Timer { Interval = 2500 };
                _powerTimer.Tick += (s, ev) => UpdateLiveBatteryTelemetryCard();
                _powerTimer.Start();
            }
            catch (Exception ex)
            {
                DarkMessageBox.Show($"Startup Error: {ex.Message}", "System Failure");
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Stop background timers
            try { _powerTimer?.Stop(); _powerTimer?.Dispose(); } catch { }

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
