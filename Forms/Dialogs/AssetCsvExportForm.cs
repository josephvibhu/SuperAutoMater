using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using QRCoder;

namespace SuperAutoMater
{
    /// <summary>
    /// Professional Asset Entry, PDF Certificate Generator, QR Code & Google Sheets Sync Dialog.
    /// Clean, modern UI with robust error handling, deduplication, and built-in Apps Script helper.
    /// </summary>
    public class AssetCsvExportForm : Form
    {
        // --- Input controls ---
        private TextBox txtAssetNo;
        private TextBox txtSerial;
        private TextBox txtModel;
        private TextBox txtCpu;
        private TextBox txtRamStorage;
        private TextBox txtBatteryHealth;
        private ComboBox cmbStatus;
        private ComboBox cmbWipIssue;
        private ComboBox cmbGrade;
        private TextBox txtShelf;
        private TextBox txtRemarks;
        private CheckBox chkAutoSavePdf;
        private TextBox txtSheetsUrl;
        private PictureBox picQrCode;
        private Label lblUploadStatus;
        private ModernButton btnUpload;
        private ModernButton btnSyncQueue;
        private ModernButton btnPrintLabel;
        private System.Windows.Forms.Timer qrTimer;
        private bool _isUploading = false;

        // Clean typography
        private static readonly Font FontTitle   = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        private static readonly Font FontLabel   = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        private static readonly Font FontInput   = new Font("Consolas", 9f, FontStyle.Regular);
        private static readonly Font FontMonoSm  = new Font("Consolas", 8.5f, FontStyle.Regular);
        private static readonly Font FontBtn     = new Font("Segoe UI", 9f, FontStyle.Bold);
        private static readonly Font FontHint    = new Font("Segoe UI", 8f, FontStyle.Regular);

        // Palette tokens
        private static readonly Color ClrWindowBg    = Color.FromArgb(12, 16, 20);
        private static readonly Color ClrCardBg      = Color.FromArgb(16, 22, 28);
        private static readonly Color ClrCardHeader  = Color.FromArgb(22, 30, 38);
        private static readonly Color ClrBorder      = Color.FromArgb(36, 48, 60);
        private static readonly Color ClrInputBg     = Color.FromArgb(10, 14, 18);
        private static readonly Color ClrInputFocus  = Color.FromArgb(14, 24, 34);
        private static readonly Color ClrCyanAccent  = Color.FromArgb(56, 189, 248);  // #38BDF8
        private static readonly Color ClrGreenPass   = Color.FromArgb(74, 222, 128);  // #4ADE80
        private static readonly Color ClrAmberWarn   = Color.FromArgb(251, 191, 36);  // #FBBF24
        private static readonly Color ClrRedFail     = Color.FromArgb(248, 113, 113); // #F87171
        private static readonly Color ClrTextBright  = Color.FromArgb(241, 245, 249);
        private static readonly Color ClrTextMuted   = Color.FromArgb(148, 163, 184);

        private readonly string detectedModel;
        private readonly string detectedSerial;
        private readonly string detectedCpu;
        private readonly string detectedRam;
        private readonly string detectedStorage;
        private readonly string detectedBattery;

        private static readonly HttpClient _http = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5
        })
        {
            Timeout = TimeSpan.FromSeconds(25)
        };

        private static readonly string SettingsFile =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sheets_url.txt");

        public AssetCsvExportForm(string model, string serial, string cpu, string ram, string storage, string batteryHealth = "100")
        {
            detectedModel   = Coerce(model);
            detectedSerial  = Coerce(serial);
            detectedCpu     = Coerce(cpu);
            detectedRam     = Coerce(ram);
            detectedStorage = Coerce(storage);
            detectedBattery = CleanBatteryHealth(batteryHealth);

            this.Text = "SuperAutoMater — Asset Record & Inventory Sync";
            this.Size = new Size(1020, 720);
            this.MinimumSize = new Size(940, 650);
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = ClrWindowBg;
            this.ForeColor = ClrTextBright;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.DoubleBuffered = true;

            try
            {
                string ico = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                if (File.Exists(ico)) this.Icon = new Icon(ico);
            }
            catch { }

            // Debounce: update QR 200ms after last keystroke
            qrTimer = new System.Windows.Forms.Timer { Interval = 200 };
            qrTimer.Tick += (_, __) => { qrTimer.Stop(); _ = RefreshQrAsync(); };

            BuildLayout();

            OfflineSyncQueue.Instance.QueueChanged += OnQueueChanged;

            this.Shown += (_, __) =>
            {
                txtAssetNo?.Focus();
                txtAssetNo?.SelectAll();
                _ = RefreshQrAsync();
            };
        }

        private static string Coerce(string s) =>
            string.IsNullOrWhiteSpace(s) || s.Trim() is "N/A" or "Unknown" ? "" : s.Trim();

        private static string CleanBatteryHealth(string s)
        {
            if (string.IsNullOrWhiteSpace(s) || s.Contains("N/A")) return "100";
            var m = Regex.Match(s, @"\d+");
            if (m.Success && int.TryParse(m.Value, out int v))
            {
                return Math.Clamp(v, 0, 100).ToString();
            }
            return "100";
        }

        // ── LAYOUT ──────────────────────────────────────────────────────────────

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(12),
                BackColor = ClrWindowBg
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            root.Controls.Add(BuildLeftCard(), 0, 0);
            root.Controls.Add(BuildRightCards(), 1, 0);
            this.Controls.Add(root);
        }

        private Control BuildLeftCard()
        {
            var card = new ModernCardPanel
            {
                Dock = DockStyle.Fill,
                Title = "ASSET RECORD & HARDWARE SPECIFICATIONS",
                BadgeText = "[ QC RECORD ]",
                BadgeColor = ClrCyanAccent,
                Margin = new Padding(0, 0, 8, 0)
            };

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 16,
                BackColor = Color.Transparent
            };

            // Explicit row sizing: 8 label/input pairs + PDF export bar + action buttons
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F)); // 0: Asset & Serial labels
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F)); // 1: Asset & Serial inputs
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F)); // 2: Model label
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F)); // 3: Model input
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F)); // 4: CPU label
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F)); // 5: CPU input
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F)); // 6: RAM/Storage & Battery labels
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F)); // 7: RAM/Storage & Battery inputs
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F)); // 8: Status & WIP Issue labels
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F)); // 9: Status & WIP Issue inputs
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F)); // 10: Grade & Shelf labels
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F)); // 11: Grade & Shelf inputs
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F)); // 12: Remarks label
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F)); // 13: Remarks input
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F)); // 14: PDF Checkbox options
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F)); // 15: Action buttons

            // Row 0 & 1: Asset Tag # & Serial Number
            var lblRow0 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            lblRow0.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
            lblRow0.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
            lblRow0.Controls.Add(MakeLabel("ASSET TAG # (e.g. AC002)"), 0, 0);
            lblRow0.Controls.Add(MakeLabel("SERIAL NUMBER (DETECTED / EDITABLE)"), 1, 0);
            grid.Controls.Add(lblRow0, 0, 0);

            var inpRow0 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 6) };
            inpRow0.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
            inpRow0.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));

            txtAssetNo = MakeTextBox("AC002", isAccent: true);
            txtSerial  = MakeTextBox(string.IsNullOrEmpty(detectedSerial) ? "" : detectedSerial);
            txtAssetNo.TextChanged += (_, __) => TriggerQrRefresh();
            txtSerial.TextChanged  += (_, __) => TriggerQrRefresh();

            inpRow0.Controls.Add(txtAssetNo, 0, 0);
            inpRow0.Controls.Add(txtSerial,  1, 0);
            grid.Controls.Add(inpRow0, 0, 1);

            // Row 2 & 3: Model Name
            grid.Controls.Add(MakeLabel("HARDWARE MODEL NAME"), 0, 2);
            txtModel = MakeTextBox(string.IsNullOrEmpty(detectedModel) ? "" : detectedModel);
            txtModel.TextChanged += (_, __) => TriggerQrRefresh();
            grid.Controls.Add(txtModel, 0, 3);

            // Row 4 & 5: CPU
            grid.Controls.Add(MakeLabel("PROCESSOR (CPU)"), 0, 4);
            txtCpu = MakeTextBox(string.IsNullOrEmpty(detectedCpu) ? "" : detectedCpu);
            txtCpu.TextChanged += (_, __) => TriggerQrRefresh();
            grid.Controls.Add(txtCpu, 0, 5);

            // Row 6 & 7: RAM / Storage & Battery Health
            var lblRow3 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            lblRow3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64F));
            lblRow3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36F));
            lblRow3.Controls.Add(MakeLabel("RAM / STORAGE GB  [ Format: 16/512 or 16/1000 ]"), 0, 0);
            lblRow3.Controls.Add(MakeLabel("BATTERY HEALTH (0-100)"), 1, 0);
            grid.Controls.Add(lblRow3, 0, 6);

            var inpRow3 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 6) };
            inpRow3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64F));
            inpRow3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36F));

            txtRamStorage = MakeTextBox(AutoRamStorage(detectedRam, detectedStorage));
            txtBatteryHealth = MakeTextBox(detectedBattery);
            txtRamStorage.TextChanged += (_, __) => TriggerQrRefresh();
            txtBatteryHealth.TextChanged += (_, __) => TriggerQrRefresh();

            inpRow3.Controls.Add(txtRamStorage, 0, 0);
            inpRow3.Controls.Add(txtBatteryHealth, 1, 0);
            grid.Controls.Add(inpRow3, 0, 7);

            // Row 8 & 9: Status & Work In Progress (WIP Issue)
            var lblRow4 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            lblRow4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
            lblRow4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
            lblRow4.Controls.Add(MakeLabel("QC STATUS"), 0, 0);
            lblRow4.Controls.Add(MakeLabel("WORK IN PROGRESS (DIAGNOSTIC ISSUE)"), 1, 0);
            grid.Controls.Add(lblRow4, 0, 8);

            var inpRow4 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 6) };
            inpRow4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
            inpRow4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));

            cmbStatus = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = ClrInputBg,
                ForeColor = ClrGreenPass,
                Font = FontInput,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 0, 6, 0)
            };
            cmbStatus.Items.AddRange(new object[]
            {
                "RTS",  // Ready To Sale
                "WIP",  // Work In Progress
                "RFR",  // Ready For Repair / Flagged
                "SOLD"  // Sold
            });
            cmbStatus.SelectedIndex = 0; // Default: RTS
            cmbStatus.SelectedIndexChanged += (_, __) =>
            {
                if (cmbStatus.Text == "WIP") cmbStatus.ForeColor = ClrAmberWarn;
                else if (cmbStatus.Text == "RFR") cmbStatus.ForeColor = ClrRedFail;
                else cmbStatus.ForeColor = ClrGreenPass;
                TriggerQrRefresh();
            };

            // WIP Issue ComboBox (DropDown so user can also type a new/custom issue)
            cmbWipIssue = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDown,
                BackColor = ClrInputBg,
                ForeColor = ClrCyanAccent,
                Font = FontInput,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 0, 4, 0)
            };
            cmbWipIssue.Items.AddRange(new object[]
            {
                "All Okay",
                "Power On Issue",
                "No OS",
                "Display Issue",
                "BIOS Issue",
                "Battery Issue",
                "Undefined"
            });
            cmbWipIssue.SelectedIndex = 0; // Default: All Okay
            cmbWipIssue.TextChanged += (_, __) => TriggerQrRefresh();

            inpRow4.Controls.Add(cmbStatus, 0, 0);
            inpRow4.Controls.Add(cmbWipIssue, 1, 0);
            grid.Controls.Add(inpRow4, 0, 9);

            // Row 10 & 11: Physical Grade & Shelf Location
            var lblRow5 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            lblRow5.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
            lblRow5.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
            lblRow5.Controls.Add(MakeLabel("PHYSICAL GRADE"), 0, 0);
            lblRow5.Controls.Add(MakeLabel("SHELF / BIN LOCATION"), 1, 0);
            grid.Controls.Add(lblRow5, 0, 10);

            var inpRow5 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 6) };
            inpRow5.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
            inpRow5.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));

            cmbGrade = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = ClrInputBg,
                ForeColor = ClrCyanAccent,
                Font = FontInput,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 0, 6, 0)
            };
            cmbGrade.Items.AddRange(new object[] { "A+", "A", "B", "C" });
            cmbGrade.SelectedIndex = 0; // Default: A+
            cmbGrade.SelectedIndexChanged += (_, __) => TriggerQrRefresh();

            txtShelf = MakeTextBox("Shelf A-1");
            txtShelf.TextChanged += (_, __) => TriggerQrRefresh();

            inpRow5.Controls.Add(cmbGrade, 0, 0);
            inpRow5.Controls.Add(txtShelf, 1, 0);
            grid.Controls.Add(inpRow5, 0, 11);

            // Row 12 & 13: Remarks / Notes
            grid.Controls.Add(MakeLabel("REMARKS / QC NOTES"), 0, 12);
            txtRemarks = MakeTextBox("Grade A+ (All POST Passed)");
            txtRemarks.TextChanged += (_, __) => TriggerQrRefresh();
            grid.Controls.Add(txtRemarks, 0, 13);

            // Row 14: PDF Auto-save Toggle (unchecked by default per user request)
            chkAutoSavePdf = new CheckBox
            {
                Text = "Auto-save PDF Diagnostic Certificate to Reports/ folder on Cloud Sync",
                Checked = false, // By default do not save
                Dock = DockStyle.Fill,
                Font = FontHint,
                ForeColor = ClrTextBright,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            grid.Controls.Add(chkAutoSavePdf, 0, 14);

            // Row 15: Action Buttons (Save PDF Now & Copy Summary)
            var btnRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                BackColor = Color.Transparent, Margin = new Padding(0, 2, 0, 0)
            };
            btnRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            btnRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            var btnPdf = new ModernButton
            {
                Text = "GENERATE PDF REPORT NOW",
                Dock = DockStyle.Fill,
                AccentColor = ClrCyanAccent,
                Margin = new Padding(0, 0, 4, 0)
            };
            btnPdf.Click += (_, __) => GeneratePdfReportManual();

            var btnCopySummary = new ModernButton
            {
                Text = "COPY SUMMARY TO CLIPBOARD",
                Dock = DockStyle.Fill,
                AccentColor = ClrGreenPass,
                Margin = new Padding(4, 0, 0, 0)
            };
            btnCopySummary.Click += (_, __) => CopyTextSummary();

            btnRow.Controls.Add(btnPdf, 0, 0);
            btnRow.Controls.Add(btnCopySummary, 1, 0);
            grid.Controls.Add(btnRow, 0, 15);

            card.Controls.Add(grid);
            return card;
        }

        private Control BuildRightCards()
        {
            var outer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
                BackColor = Color.Transparent, Margin = new Padding(4, 0, 0, 0)
            };
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 52F)); // QR section
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 48F)); // Sheets section

            // ── Card 1: 2D Barcode / QR Payload ────────────────────────────
            var qrCard = new ModernCardPanel
            {
                Dock = DockStyle.Fill,
                Title = "2D BARCODE / QR PAYLOAD",
                BadgeText = "[ SCAN READY ]",
                BadgeColor = ClrGreenPass,
                Margin = new Padding(0, 0, 0, 8)
            };

            var qrLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            qrLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            qrLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));

            picQrCode = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.White,
                Margin = new Padding(6)
            };

            var qrHint = new Label
            {
                Text = "Scan with 2D Barcode Scanner, Phone Camera, or AppSheet",
                Dock = DockStyle.Fill,
                Font = FontHint,
                ForeColor = ClrTextMuted,
                TextAlign = ContentAlignment.MiddleCenter
            };

            qrLayout.Controls.Add(picQrCode, 0, 0);
            qrLayout.Controls.Add(qrHint, 0, 1);
            qrCard.Controls.Add(qrLayout);
            outer.Controls.Add(qrCard, 0, 0);

            // ── Card 2: Google Sheets Live Sync ────────────────────────────
            var sheetsCard = new ModernCardPanel
            {
                Dock = DockStyle.Fill,
                Title = "GOOGLE SHEETS LIVE INVENTORY SYNC",
                BadgeText = "[ CLOUD SYNC ]",
                BadgeColor = ClrAmberWarn,
                Margin = new Padding(0)
            };

            var sheetsLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6,
                BackColor = Color.Transparent
            };
            sheetsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F)); // Label
            sheetsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F)); // URL Input
            sheetsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F)); // Upload Button
            sheetsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F)); // Print Label & Sync Queue Row
            sheetsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F)); // Script Helper Button
            sheetsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // Status readout

            sheetsLayout.Controls.Add(MakeLabel("APPS SCRIPT WEB APP DEPLOYMENT URL:"), 0, 0);

            txtSheetsUrl = new TextBox
            {
                Dock = DockStyle.Fill,
                BackColor = ClrInputBg,
                ForeColor = ClrAmberWarn,
                Font = FontMonoSm,
                BorderStyle = BorderStyle.FixedSingle,
                Text = LoadSheetsUrl(),
                Margin = new Padding(0, 0, 0, 4)
            };
            txtSheetsUrl.TextChanged += (_, __) => SaveSheetsUrl(txtSheetsUrl.Text);
            sheetsLayout.Controls.Add(txtSheetsUrl, 0, 1);

            btnUpload = new ModernButton
            {
                Text = "UPLOAD TO GOOGLE SHEETS",
                Dock = DockStyle.Fill,
                AccentColor = ClrGreenPass,
                Font = FontBtn,
                Margin = new Padding(0, 0, 0, 4)
            };
            btnUpload.Click += async (_, __) => await UploadToSheetsAsync();
            sheetsLayout.Controls.Add(btnUpload, 0, 2);

            var actionSplit = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 4)
            };
            actionSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            actionSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            btnPrintLabel = new ModernButton
            {
                Text = "🖨 PRINT CHASSIS LABEL",
                Dock = DockStyle.Fill,
                AccentColor = ClrAmberWarn,
                Font = FontBtn,
                Margin = new Padding(0, 0, 2, 0)
            };
            btnPrintLabel.Click += (_, __) => PrintChassisLabelManual();

            int pending = OfflineSyncQueue.Instance.PendingCount;
            btnSyncQueue = new ModernButton
            {
                Text = $"☁ SYNC QUEUE ({pending})",
                Dock = DockStyle.Fill,
                AccentColor = ClrCyanAccent,
                Font = FontBtn,
                Enabled = pending > 0,
                Margin = new Padding(2, 0, 0, 0)
            };
            btnSyncQueue.Click += async (_, __) => await FlushOfflineQueueManualAsync();

            actionSplit.Controls.Add(btnPrintLabel, 0, 0);
            actionSplit.Controls.Add(btnSyncQueue, 1, 0);
            sheetsLayout.Controls.Add(actionSplit, 0, 3);

            var btnSetupGuide = new ModernButton
            {
                Text = "VIEW & COPY code.gs SCRIPT",
                Dock = DockStyle.Fill,
                AccentColor = ClrCyanAccent,
                Font = FontBtn,
                Margin = new Padding(0, 0, 0, 4)
            };
            btnSetupGuide.Click += (_, __) => ShowScriptSetupDialog();
            sheetsLayout.Controls.Add(btnSetupGuide, 0, 4);

            lblUploadStatus = new Label
            {
                Dock = DockStyle.Fill,
                Font = FontHint,
                ForeColor = ClrTextMuted,
                Text = "Ready to upload. Supports offline queue buffering and 2D thermal chassis labels.",
                TextAlign = ContentAlignment.TopLeft,
                AutoSize = false,
                Margin = new Padding(2, 2, 2, 0)
            };
            sheetsLayout.Controls.Add(lblUploadStatus, 0, 5);

            sheetsCard.Controls.Add(sheetsLayout);
            outer.Controls.Add(sheetsCard, 0, 1);

            return outer;
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static Label MakeLabel(string text) => new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            Font = FontLabel,
            ForeColor = ClrCyanAccent,
            AutoSize = false,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = new Padding(0, 2, 0, 2)
        };

        private static TextBox MakeTextBox(string value, bool isAccent = false) => new TextBox
        {
            Dock = DockStyle.Fill,
            Font = FontInput,
            BackColor = isAccent ? ClrInputFocus : ClrInputBg,
            ForeColor = isAccent ? ClrCyanAccent : ClrTextBright,
            BorderStyle = BorderStyle.FixedSingle,
            Text = value,
            Margin = new Padding(0, 0, 4, 4),
            TabStop = true
        };

        public static string AutoFormatRamStorage(string ram, string storage)
        {
            string r = "16";
            var mr = Regex.Match(ram ?? "", @"(\d+)\s*GB", RegexOptions.IgnoreCase);
            if (mr.Success) r = mr.Groups[1].Value;

            string s = "512";
            var ms = Regex.Match(storage ?? "", @"(\d+)\s*GB", RegexOptions.IgnoreCase);
            if (ms.Success)
            {
                int v = int.Parse(ms.Groups[1].Value);
                s = v >= 900 ? "1000" : v >= 450 ? "512" : v >= 220 ? "256" : v >= 110 ? "128" : v.ToString();
            }
            else
            {
                var mt = Regex.Match(storage ?? "", @"(\d+)\s*TB", RegexOptions.IgnoreCase);
                if (mt.Success) s = (int.Parse(mt.Groups[1].Value) * 1000).ToString();
            }
            return $"{r}/{s}";
        }

        private string AutoRamStorage(string ram, string storage) => AutoFormatRamStorage(ram, storage);

        // ── QR Code Logic ──────────────────────────────────────────────────────

        private void TriggerQrRefresh()
        {
            qrTimer.Stop();
            qrTimer.Start();
        }

        private string BuildQrPayloadString()
        {
            string San(string v) => (v ?? "").Replace(",", " ").Trim();
            string asset = San(txtAssetNo?.Text);
            string sn    = San(txtSerial?.Text);
            string combo = string.IsNullOrEmpty(asset) && string.IsNullOrEmpty(sn) ? "UNKNOWN"
                         : string.IsNullOrEmpty(asset) ? sn
                         : string.IsNullOrEmpty(sn)    ? asset
                         : $"{asset}/{sn}";

            return string.Join(",",
                combo,
                San(txtModel?.Text),
                San(txtCpu?.Text),
                San(txtRamStorage?.Text),
                CleanBatteryHealth(txtBatteryHealth?.Text),
                San(cmbStatus?.Text),
                San(cmbWipIssue?.Text),
                San(cmbGrade?.Text),
                San(txtRemarks?.Text),
                San(txtShelf?.Text));
        }

        private async Task RefreshQrAsync()
        {
            try
            {
                string data = BuildQrPayloadString();
                var bmp = await Task.Run(() =>
                {
                    using var g   = new QRCodeGenerator();
                    using var qrd = g.CreateQrCode(data, QRCodeGenerator.ECCLevel.M);
                    using var qr  = new QRCode(qrd);
                    return qr.GetGraphic(6, Color.FromArgb(15, 23, 42), Color.White, true);
                });

                if (!this.IsDisposed && picQrCode != null)
                {
                    var old = picQrCode.Image;
                    picQrCode.Image = bmp;
                    old?.Dispose();
                }
            }
            catch { }
        }

        // ── PDF Generation ─────────────────────────────────────────────────────

        private string GeneratePdfReportManual()
        {
            try
            {
                var reportData = new PdfReportGenerator.AssetReportData
                {
                    AssetTag      = txtAssetNo?.Text?.Trim() ?? "",
                    SerialNumber  = txtSerial?.Text?.Trim() ?? "",
                    Model         = txtModel?.Text?.Trim() ?? "",
                    Processor     = txtCpu?.Text?.Trim() ?? "",
                    RamStorage    = txtRamStorage?.Text?.Trim() ?? "",
                    BatteryHealth = CleanBatteryHealth(txtBatteryHealth?.Text),
                    Status        = cmbStatus?.Text?.Trim() ?? "RTS",
                    WipIssue      = cmbWipIssue?.Text?.Trim() ?? "All Okay",
                    PhysicalGrade = cmbGrade?.Text?.Trim() ?? "A+",
                    ShelfLocation = txtShelf?.Text?.Trim() ?? "",
                    Remarks       = txtRemarks?.Text?.Trim() ?? "",
                    Technician    = "QA Inspector",
                    QrBitmap      = picQrCode?.Image != null ? new Bitmap(picQrCode.Image) : null
                };

                string pdfPath = PdfReportGenerator.GeneratePdfReport(reportData);
                SetStatus($"PDF generated: {Path.GetFileName(pdfPath)}", ClrGreenPass);

                var res = MessageBox.Show(
                    $"PDF Certificate generated successfully!\n\nFile: {Path.GetFileName(pdfPath)}\nLocation: {pdfPath}\n\nWould you like to open it now?",
                    "PDF Generated",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);

                if (res == DialogResult.Yes)
                {
                    try { Process.Start(new ProcessStartInfo(pdfPath) { UseShellExecute = true }); } catch { }
                }

                return pdfPath;
            }
            catch (Exception ex)
            {
                SetStatus($"PDF Generation Error: {ex.Message}", ClrRedFail);
                DarkMessageBox.Show(ex.Message, "PDF Error");
                return null;
            }
        }

        private void CopyTextSummary()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== SUPERAUTOMATER QC DIAGNOSTIC SUMMARY ===");
                sb.AppendLine($"Asset Tag      : {txtAssetNo?.Text?.Trim()}");
                sb.AppendLine($"Serial Number  : {txtSerial?.Text?.Trim()}");
                sb.AppendLine($"Model Name     : {txtModel?.Text?.Trim()}");
                sb.AppendLine($"Processor      : {txtCpu?.Text?.Trim()}");
                sb.AppendLine($"RAM / Storage  : {txtRamStorage?.Text?.Trim()}");
                sb.AppendLine($"Battery Health : {CleanBatteryHealth(txtBatteryHealth?.Text)}%");
                sb.AppendLine($"Status         : {cmbStatus?.Text?.Trim()}");
                sb.AppendLine($"WIP Issue      : {cmbWipIssue?.Text?.Trim()}");
                sb.AppendLine($"Physical Grade : {cmbGrade?.Text?.Trim()}");
                sb.AppendLine($"Shelf Location : {txtShelf?.Text?.Trim()}");
                sb.AppendLine($"Remarks        : {txtRemarks?.Text?.Trim()}");
                sb.AppendLine($"Timestamp      : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

                string summary = sb.ToString();
                Clipboard.SetText(summary);
                SetStatus("Text summary copied to clipboard!", ClrGreenPass);
                DarkMessageBox.Show("QC Summary copied to Windows Clipboard.", "Summary Copied");
            }
            catch (Exception ex) { DarkMessageBox.Show(ex.Message, "Copy Error"); }
        }

        // ── Google Sheets Upload ───────────────────────────────────────────────

        private async Task UploadToSheetsAsync()
        {
            if (_isUploading) return; // Concurrency lock to prevent double entries

            string url = txtSheetsUrl?.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(url) || !url.StartsWith("https://script.google.com/macros/s/"))
            {
                SetStatus("Paste your Google Apps Script Web App URL first (click 'VIEW & COPY SCRIPT')", ClrAmberWarn);
                ShowScriptSetupDialog();
                return;
            }

            _isUploading = true;
            if (btnUpload != null)
            {
                btnUpload.Enabled = false;
                btnUpload.Text = "UPLOADING...";
            }
            SetStatus("Uploading asset to Google Sheets...", ClrCyanAccent);

            try
            {
                string assetTag      = txtAssetNo?.Text?.Trim() ?? "";
                string serialNo      = txtSerial?.Text?.Trim() ?? "";
                string model         = txtModel?.Text?.Trim() ?? "";
                string processor     = txtCpu?.Text?.Trim() ?? "";
                string memory        = txtRamStorage?.Text?.Trim() ?? "";
                string batteryHealth = CleanBatteryHealth(txtBatteryHealth?.Text);
                string status        = cmbStatus?.Text?.Trim() ?? "RTS";
                string wipIssue      = cmbWipIssue?.Text?.Trim() ?? "All Okay";
                string physicalGrade = cmbGrade?.Text?.Trim() ?? "A+";
                string remarks       = txtRemarks?.Text?.Trim() ?? "";
                string shelf         = txtShelf?.Text?.Trim() ?? "";
                string timestamp     = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                // Checkbox: auto-generate PDF locally if user requested
                string savedPdfMsg = "";
                if (chkAutoSavePdf.Checked)
                {
                    try
                    {
                        var reportData = new PdfReportGenerator.AssetReportData
                        {
                            AssetTag      = assetTag,
                            SerialNumber  = serialNo,
                            Model         = model,
                            Processor     = processor,
                            RamStorage    = memory,
                            BatteryHealth = batteryHealth,
                            Status        = status,
                            WipIssue      = wipIssue,
                            PhysicalGrade = physicalGrade,
                            ShelfLocation = shelf,
                            Remarks       = remarks,
                            Technician    = "QA Inspector",
                            QrBitmap      = picQrCode?.Image != null ? new Bitmap(picQrCode.Image) : null
                        };
                        string pdfPath = PdfReportGenerator.GeneratePdfReport(reportData);
                        savedPdfMsg = $"\nPDF Certificate: {Path.GetFileName(pdfPath)}";
                    }
                    catch { }
                }

                // Payload matching user requested column order
                string json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    Asset_Tag      = assetTag,
                    Serial_Number  = serialNo,
                    Model          = model,
                    Processor      = processor,
                    Memory         = memory,
                    Battery_Health = int.Parse(batteryHealth),
                    Status         = status,
                    Wip_Issue      = wipIssue,
                    Physical_Grade = physicalGrade,
                    Remarks        = remarks,
                    Shelf_Location = shelf,
                    Timestamp      = timestamp
                });

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _http.PostAsync(url, content);
                string body = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode && (body.Contains("\"status\":\"OK\"") || body.Contains("\"status\": \"OK\"") || body.Contains("OK")))
                {
                    string actionMsg = body.Contains("UPDATED") ? "Updated existing record"
                                     : body.Contains("DEDUPLICATED") ? "Updated (Duplicate prevented)"
                                     : "New record added";

                    SetStatus($"SUCCESS: {actionMsg} in Google Sheets!{savedPdfMsg}", ClrGreenPass);
                    DarkMessageBox.Show($"Successfully synced to Google Sheets!\n\nAsset: {assetTag} ({serialNo})\nResult: {actionMsg}{savedPdfMsg}", "Google Sheets Sync Success");

                    // Flush any previously buffered offline records in the background
                    if (OfflineSyncQueue.Instance.PendingCount > 0)
                    {
                        _ = Task.Run(async () =>
                        {
                            await OfflineSyncQueue.Instance.FlushQueueAsync(url);
                            UpdateQueueButton();
                        });
                    }
                }
                else
                {
                    string snippet = body.Length > 120 ? body.Substring(0, 117) + "..." : body;
                    SetStatus($"Upload notice (HTTP {(int)response.StatusCode}): {snippet}", ClrAmberWarn);
                }
            }
            catch (Exception ex)
            {
                // Network error, offline, or timeout: Save to offline queue safely
                var qRec = BuildCurrentQueueRecord();
                OfflineSyncQueue.Instance.Enqueue(qRec);
                int pending = OfflineSyncQueue.Instance.PendingCount;
                UpdateQueueButton();

                string reason = ex is TaskCanceledException ? "Connection timed out" : ex.Message;
                SetStatus($"OFFLINE: Saved to local queue ({pending} pending). Will auto-sync when online.", ClrAmberWarn);
                DarkMessageBox.Show($"Network unavailable ({reason}).\n\nAsset record for '{qRec.Asset_Tag}' has been safely saved to the Offline Sync Queue ({pending} pending sync).\nIt will automatically upload to Google Sheets once Wi-Fi connects.", "Saved to Offline Queue");
            }
            finally
            {
                _isUploading = false;
                if (btnUpload != null)
                {
                    btnUpload.Enabled = true;
                    btnUpload.Text = "UPLOAD TO GOOGLE SHEETS";
                }
            }
        }

        private AssetQueueRecord BuildCurrentQueueRecord()
        {
            return new AssetQueueRecord
            {
                Asset_Tag      = txtAssetNo?.Text?.Trim() ?? "",
                Serial_Number  = txtSerial?.Text?.Trim() ?? "",
                Model          = txtModel?.Text?.Trim() ?? "",
                Processor      = txtCpu?.Text?.Trim() ?? "",
                Memory         = txtRamStorage?.Text?.Trim() ?? "",
                Battery_Health = int.TryParse(CleanBatteryHealth(txtBatteryHealth?.Text), out int b) ? b : 100,
                Status         = cmbStatus?.Text?.Trim() ?? "RTS",
                Wip_Issue      = cmbWipIssue?.Text?.Trim() ?? "All Okay",
                Physical_Grade = cmbGrade?.Text?.Trim() ?? "A+",
                Remarks        = txtRemarks?.Text?.Trim() ?? "",
                Shelf_Location = txtShelf?.Text?.Trim() ?? "",
                Timestamp      = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
        }

        private void PrintChassisLabelManual()
        {
            var record = BuildCurrentQueueRecord();
            ThermalLabelPrinter.PrintLabel(record, this);
        }

        private async Task FlushOfflineQueueManualAsync()
        {
            string url = txtSheetsUrl?.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(url) || !url.StartsWith("https://script.google.com/macros/s/"))
                url = DefaultEmbeddedSheetsUrl;

            SetStatus("Flushing offline sync queue...", ClrCyanAccent);
            int synced = await OfflineSyncQueue.Instance.FlushQueueAsync(url);
            int remaining = OfflineSyncQueue.Instance.PendingCount;
            if (synced > 0)
            {
                SetStatus($"SUCCESS: Synced {synced} queued record(s) to Google Sheets! ({remaining} remaining)", ClrGreenPass);
                DarkMessageBox.Show($"Successfully flushed offline queue!\n\nSynced Records: {synced}\nRemaining in Queue: {remaining}", "Queue Synced");
            }
            else
            {
                SetStatus($"Queue flush completed. ({remaining} pending sync)", ClrAmberWarn);
            }
            UpdateQueueButton();
        }

        private void OnQueueChanged(int count)
        {
            UpdateQueueButton();
        }

        private void UpdateQueueButton()
        {
            if (this.IsDisposed || !this.IsHandleCreated || btnSyncQueue == null) return;
            this.BeginInvoke(new Action(() =>
            {
                int count = OfflineSyncQueue.Instance.PendingCount;
                btnSyncQueue.Text = $"☁ SYNC QUEUE ({count})";
                btnSyncQueue.Enabled = count > 0;
            }));
        }

        public static async Task<bool> UploadDirectOrQueueAsync(AssetQueueRecord record, string webhookUrl = null)
        {
            if (record == null) return false;
            string url = webhookUrl;
            if (string.IsNullOrWhiteSpace(url)) url = DefaultEmbeddedSheetsUrl;

            try
            {
                string json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    Asset_Tag      = record.Asset_Tag,
                    Serial_Number  = record.Serial_Number,
                    Model          = record.Model,
                    Processor      = record.Processor,
                    Memory         = record.Memory,
                    Battery_Health = record.Battery_Health,
                    Status         = record.Status,
                    Wip_Issue      = record.Wip_Issue,
                    Physical_Grade = record.Physical_Grade,
                    Remarks        = record.Remarks,
                    Shelf_Location = record.Shelf_Location,
                    Timestamp      = record.Timestamp
                });

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _http.PostAsync(url, content);
                string body = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode && (body.Contains("OK") || body.Contains("status\":\"OK\"")))
                {
                    return true;
                }
            }
            catch { }

            // Save to offline queue on any network error or timeout
            OfflineSyncQueue.Instance.Enqueue(record);
            return false;
        }

        private void SetStatus(string msg, Color color)
        {
            if (lblUploadStatus == null || lblUploadStatus.IsDisposed) return;
            if (lblUploadStatus.InvokeRequired)
                lblUploadStatus.BeginInvoke(new Action(() => { lblUploadStatus.Text = msg; lblUploadStatus.ForeColor = color; }));
            else
            {
                lblUploadStatus.Text = msg;
                lblUploadStatus.ForeColor = color;
            }
        }

        private void ShowScriptSetupDialog()
        {
            using (var dlg = new GoogleSheetsSetupDialog())
            {
                dlg.ShowDialog(this);
            }
        }

        public const string DefaultEmbeddedSheetsUrl =
            "https://script.google.com/macros/s/AKfycbyx4LIL1xbzTypuYKTUK2XuMVnLq8TRbdVsupEQlSjI0CxGQ3mG92yR7rY3bjq1EH4t/exec";

        private string LoadSheetsUrl()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    string saved = File.ReadAllText(SettingsFile).Trim();
                    if (!string.IsNullOrWhiteSpace(saved)) return saved;
                }
            }
            catch { }
            return DefaultEmbeddedSheetsUrl;
        }

        private void SaveSheetsUrl(string url)
        {
            try { File.WriteAllText(SettingsFile, url); } catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { OfflineSyncQueue.Instance.QueueChanged -= OnQueueChanged; } catch { }
                qrTimer?.Stop();
                qrTimer?.Dispose();
                picQrCode?.Image?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // ── IN-APP SCRIPT SETUP VIEWER DIALOG ──────────────────────────────────────

    public class GoogleSheetsSetupDialog : Form
    {
        public GoogleSheetsSetupDialog()
        {
            this.Text = "Google Sheets Integration — Apps Script (code.gs)";
            this.Size = new Size(840, 640);
            this.MinimumSize = new Size(760, 540);
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = Color.FromArgb(12, 16, 22);
            this.ForeColor = Color.FromArgb(241, 245, 249);
            this.FormBorderStyle = FormBorderStyle.Sizable;

            BuildUI();
        }

        private void BuildUI()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(12)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F)); // Header banner
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74F)); // Steps instructions
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // Monospace code editor
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F)); // Action buttons

            // 1. Header Banner
            var pnlHeader = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6) };
            var lblTitle = new Label
            {
                Text = "GOOGLE APPS SCRIPT (code.gs) INTEGRATION",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                Dock = DockStyle.Top,
                Height = 24
            };
            var lblSub = new Label
            {
                Text = "Deploy this script to your Google Sheet as a Web App to enable 1-click cloud sync.",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(148, 163, 184),
                Dock = DockStyle.Bottom,
                Height = 20
            };
            pnlHeader.Controls.Add(lblSub);
            pnlHeader.Controls.Add(lblTitle);
            root.Controls.Add(pnlHeader, 0, 0);

            // 2. Steps Guide
            var pnlSteps = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(18, 26, 36),
                Padding = new Padding(8),
                Margin = new Padding(0, 0, 0, 8)
            };
            var lblSteps = new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(226, 232, 240),
                Text = "1. Open your Google Sheet  →  Click Extensions  →  Apps Script.\n" +
                       "2. Delete all existing code, then click 'COPY CODE TO CLIPBOARD' below and paste it.\n" +
                       "3. Click Deploy  →  New deployment  →  Select type: Web app  →  Who has access: Anyone  →  Deploy.\n" +
                       "4. Copy the Web App URL into SuperAutoMater's 'Apps Script Web App URL' box. Done!"
            };
            pnlSteps.Controls.Add(lblSteps);
            root.Controls.Add(pnlSteps, 0, 1);

            // 3. Monospace Code Viewer
            var txtCode = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                BackColor = Color.FromArgb(8, 12, 16),
                ForeColor = Color.FromArgb(74, 222, 128),
                Font = new Font("Consolas", 9f, FontStyle.Regular),
                BorderStyle = BorderStyle.FixedSingle,
                Text = StartupServices.CodeGsContent,
                Margin = new Padding(0, 0, 0, 8)
            };
            root.Controls.Add(txtCode, 0, 2);

            // 4. Action Buttons
            var pnlButtons = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Margin = new Padding(0)
            };
            pnlButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));
            pnlButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
            pnlButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24F));
            pnlButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14F));

            var btnCopyCode = new ModernButton
            {
                Text = "COPY CODE TO CLIPBOARD",
                Dock = DockStyle.Fill,
                AccentColor = Color.FromArgb(74, 222, 128),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Margin = new Padding(0, 0, 4, 0)
            };
            btnCopyCode.Click += (_, __) =>
            {
                Clipboard.SetText(StartupServices.CodeGsContent);
                btnCopyCode.Text = "COPIED TO CLIPBOARD!";
                btnCopyCode.AccentColor = Color.FromArgb(56, 189, 248);
            };

            var btnOpenSheets = new ModernButton
            {
                Text = "OPEN GOOGLE SHEETS",
                Dock = DockStyle.Fill,
                AccentColor = Color.FromArgb(56, 189, 248),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Margin = new Padding(4, 0, 4, 0)
            };
            btnOpenSheets.Click += (_, __) => StartupServices.OpenGoogleSheetsInBrowser();

            var btnOpenFolder = new ModernButton
            {
                Text = "OPEN SETUP FOLDER",
                Dock = DockStyle.Fill,
                AccentColor = Color.FromArgb(148, 163, 184),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Margin = new Padding(4, 0, 4, 0)
            };
            btnOpenFolder.Click += (_, __) => StartupServices.OpenSheetsSetupFolder();

            var btnClose = new ModernButton
            {
                Text = "CLOSE",
                Dock = DockStyle.Fill,
                AccentColor = Color.FromArgb(148, 163, 184),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Margin = new Padding(4, 0, 0, 0)
            };
            btnClose.Click += (_, __) => this.Close();

            pnlButtons.Controls.Add(btnCopyCode, 0, 0);
            pnlButtons.Controls.Add(btnOpenSheets, 1, 0);
            pnlButtons.Controls.Add(btnOpenFolder, 2, 0);
            pnlButtons.Controls.Add(btnClose, 3, 0);

            root.Controls.Add(pnlButtons, 0, 3);
            this.Controls.Add(root);
        }
    }

    // ── MODERN CLEAN CARD PANEL (NO OVERLAPPING HEADERS) ──────────────────────

    public class ModernCardPanel : Panel
    {
        public string Title { get; set; } = "";
        public string BadgeText { get; set; } = "";
        public Color BadgeColor { get; set; } = Color.FromArgb(56, 189, 248);

        public ModernCardPanel()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.BackColor = Color.FromArgb(16, 22, 28);
            // Generous top padding (34px) guarantees child controls NEVER collide with the 28px header bar:
            this.Padding = new Padding(8, 34, 8, 8);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var bounds = new Rectangle(0, 0, this.Width - 1, this.Height - 1);

            // Card background
            using (var bgBrush = new SolidBrush(this.BackColor))
                g.FillRectangle(bgBrush, bounds);

            // Header bar (28px)
            var headerRect = new Rectangle(0, 0, this.Width, 28);
            using (var headerBrush = new SolidBrush(Color.FromArgb(22, 30, 38)))
                g.FillRectangle(headerBrush, headerRect);

            // Header accent line
            using (var borderPen = new Pen(Color.FromArgb(36, 48, 60), 1f))
            {
                g.DrawLine(borderPen, 0, 28, this.Width, 28);
                g.DrawRectangle(borderPen, bounds);
            }

            // Title accent notch
            using (var accentBrush = new SolidBrush(Color.FromArgb(56, 189, 248)))
                g.FillRectangle(accentBrush, 6, 6, 3, 16);

            // Title text
            using (var titleFont = new Font("Segoe UI", 9f, FontStyle.Bold))
            using (var titleBrush = new SolidBrush(Color.FromArgb(241, 245, 249)))
            {
                g.DrawString(Title, titleFont, titleBrush, 14, 6);
            }

            // Subtitle Badge
            if (!string.IsNullOrEmpty(BadgeText))
            {
                using (var badgeFont = new Font("Segoe UI", 8.5f, FontStyle.Bold))
                using (var badgeBrush = new SolidBrush(BadgeColor))
                {
                    var sz = g.MeasureString(BadgeText, badgeFont);
                    float badgeX = this.Width - sz.Width - 10;
                    if (badgeX > 200) // only draw if enough room to avoid collision
                    {
                        g.DrawString(BadgeText, badgeFont, badgeBrush, badgeX, 6);
                    }
                }
            }

            base.OnPaint(e);
        }
    }

    // ── MODERN CLEAN BUTTON (NO BROKEN EMOJI GLYPHS, NO UNWANTED BRACKETS) ────

    public class ModernButton : Button
    {
        public Color AccentColor { get; set; } = Color.FromArgb(56, 189, 248);

        private bool isHovered = false;
        private bool isPressed = false;

        public ModernButton()
        {
            this.FlatStyle = FlatStyle.Flat;
            this.FlatAppearance.BorderSize = 0;
            this.DoubleBuffered = true;
            this.Cursor = Cursors.Hand;
            this.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            this.BackColor = Color.FromArgb(20, 28, 36);
            this.ForeColor = Color.FromArgb(241, 245, 249);
            this.Margin = new Padding(2);
        }

        protected override void OnMouseEnter(EventArgs e) { isHovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { isHovered = false; isPressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs me) { isPressed = true; Invalidate(); base.OnMouseDown(me); }
        protected override void OnMouseUp(MouseEventArgs me) { isPressed = false; Invalidate(); base.OnMouseUp(me); }

        protected override void OnPaint(PaintEventArgs pe)
        {
            Graphics g = pe.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color bg;
            Color border;
            Color text;

            if (!this.Enabled)
            {
                bg = Color.FromArgb(14, 18, 22);
                border = Color.FromArgb(28, 36, 44);
                text = Color.FromArgb(80, 95, 110);
            }
            else if (isPressed)
            {
                bg = Color.FromArgb(12, 18, 24);
                border = AccentColor;
                text = AccentColor;
            }
            else if (isHovered)
            {
                bg = Color.FromArgb(28, 38, 48);
                border = AccentColor;
                text = Color.White;
            }
            else
            {
                bg = Color.FromArgb(20, 28, 36);
                border = Color.FromArgb(38, 50, 62);
                text = AccentColor;
            }

            var r = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            using (var b = new SolidBrush(bg))
                g.FillRectangle(b, r);

            using (var p = new Pen(border, 1.2f))
                g.DrawRectangle(p, r);

            using (var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter
            })
            using (var tb = new SolidBrush(text))
            {
                g.DrawString(this.Text, this.Font, tb, this.ClientRectangle, sf);
            }
        }
    }
}
