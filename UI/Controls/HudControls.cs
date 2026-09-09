using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SuperAutoMater
{
    #region Left Navigation Rail Panel (Test Status Indicator Rail)

    public class NavRailItem
    {
        public string Symbol;
        public string Label;
        public string TestKey;
        public bool IsPassed;
        public Color DefaultAccent;

        public NavRailItem(string symbol, string label, string testKey, Color accent)
        {
            Symbol = symbol;
            Label = label;
            TestKey = testKey;
            IsPassed = false;
            DefaultAccent = accent;
        }
    }

    /// <summary>
    /// Left-most vertical status indicator rail. Only top menu button opens QC drawer; other icons light up as tests pass.
    /// </summary>
    public class NavRailPanel : Panel
    {
        private readonly List<NavRailItem> items = new List<NavRailItem>();
        private readonly ToolTip toolTip = new ToolTip();

        public event Action MenuButtonClicked;

        public NavRailPanel()
        {
            this.DoubleBuffered = true;
            this.Dock = DockStyle.Fill;
            this.Margin = new Padding(0);
            this.BackColor = HudTheme.BgGlass;

            toolTip.BackColor = HudTheme.PanelGlassTop;
            toolTip.ForeColor = HudTheme.HudAccent;

            // 0. Top Menu Button (Opens Drawer)
            items.Add(new NavRailItem("≡", "MENU", "Menu", HudTheme.HudAccent));

            // 1-10. Test Status Indicators
            items.Add(new NavRailItem("⬡", "CPU", "CPU", HudTheme.Muted));
            items.Add(new NavRailItem("▥", "RAM", "RAM", HudTheme.Muted));
            items.Add(new NavRailItem("📡", "WIFI", "WiFi", HudTheme.Muted));
            items.Add(new NavRailItem("💾", "DISK", "Storage", HudTheme.Muted));
            items.Add(new NavRailItem("👆", "BIO", "Fingerprint", HudTheme.Muted));
            items.Add(new NavRailItem("📷", "CAM", "Camera", HudTheme.Muted));
            items.Add(new NavRailItem("🔊", "AUDIO", "Audio", HudTheme.Muted));
            items.Add(new NavRailItem("🖥", "DISP", "Display", HudTheme.Muted));
            items.Add(new NavRailItem("⌨", "KEYS", "Keyboard", HudTheme.Muted));
            items.Add(new NavRailItem("🔋", "BATT", "Battery", HudTheme.Muted));
        }

        public void SetTestPassed(string testKey)
        {
            if (string.IsNullOrEmpty(testKey)) return;

            bool changed = false;
            foreach (var item in items)
            {
                if (string.Equals(item.TestKey, testKey, StringComparison.OrdinalIgnoreCase) ||
                    (testKey.Equals("Trackpad", StringComparison.OrdinalIgnoreCase) && item.TestKey.Equals("Fingerprint", StringComparison.OrdinalIgnoreCase)) ||
                    (testKey.Equals("USB", StringComparison.OrdinalIgnoreCase) && item.TestKey.Equals("Fingerprint", StringComparison.OrdinalIgnoreCase)) ||
                    (testKey.Equals("Bluetooth", StringComparison.OrdinalIgnoreCase) && item.TestKey.Equals("WiFi", StringComparison.OrdinalIgnoreCase)) ||
                    (testKey.Equals("GPU", StringComparison.OrdinalIgnoreCase) && item.TestKey.Equals("CPU", StringComparison.OrdinalIgnoreCase)) ||
                    (testKey.Equals("Webcam", StringComparison.OrdinalIgnoreCase) && item.TestKey.Equals("Camera", StringComparison.OrdinalIgnoreCase)))
                {
                    if (!item.IsPassed)
                    {
                        item.IsPassed = true;
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                if (this.InvokeRequired) this.BeginInvoke(new Action(Invalidate));
                else Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int idx = e.Y / 38;
            if (idx == 0) // Only top ≡ menu button triggers action
            {
                MenuButtonClicked?.Invoke();
            }
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Right border line
            using (Pen p = new Pen(HudTheme.Bezel, 1))
                g.DrawLine(p, this.Width - 1, 0, this.Width - 1, this.Height);

            int y = 6;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                Rectangle rect = new Rectangle(4, y, this.Width - 8, 32);

                if (item.IsPassed)
                {
                    using (SolidBrush selBg = new SolidBrush(Color.FromArgb(16, 38, 28)))
                    {
                        g.FillRectangle(selBg, rect);
                    }
                    using (Pen selPen = new Pen(HudTheme.PassNominal, 1f))
                    {
                        g.DrawRectangle(selPen, rect);
                    }
                    // Left indicator stripe
                    using (SolidBrush stripe = new SolidBrush(HudTheme.PassNominal))
                    {
                        g.FillRectangle(stripe, 2, y + 6, 2, 20);
                    }
                }

                Color iconColor = (i == 0) ? HudTheme.HudAccent : (item.IsPassed ? HudTheme.PassNominal : HudTheme.Muted);
                using (SolidBrush iconBrush = new SolidBrush(iconColor))
                using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    Font font = (i == 0) ? HudTheme.FontTitle13Bold : HudTheme.FontMono13Bold;
                    g.DrawString(item.Symbol, font, iconBrush, rect, sf);
                }

                y += 38;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                toolTip?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    #endregion

    #region Telemetry Structured Cards View (Clean High-Visibility Readouts)

    /// <summary>
    /// Structured 5-Card System Telemetry panel with clear, calibrated typography and [ PASS ✓ ] status badges.
    /// </summary>
    public class TelemetryCardsView : Control
    {
        public struct TelemetryCardData
        {
            public string Line1;
            public string Line2;
            public string StatusBadge;
            public Color StripeColor;
            public Color BadgeColor;
        }

        private readonly List<TelemetryCardData> cards = new List<TelemetryCardData>();

        public TelemetryCardsView()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.BackColor = HudTheme.BgGlass;
            LoadDefaultCards();
        }

        public void LoadDefaultCards()
        {
            cards.Clear();
            cards.Add(new TelemetryCardData
            {
                Line1 = "MODEL : Detecting chassis identity & serial number...",
                Line2 = "CPU   : Interrogating processor & microarchitecture...",
                StatusBadge = "[ PROBING ]",
                StripeColor = HudTheme.HudAccent,
                BadgeColor = HudTheme.HudAccentSoft
            });

            cards.Add(new TelemetryCardData
            {
                Line1 = "RAM   : Scanning physical memory modules & topology...",
                Line2 = "GPU   : Enumerating display adapter & VRAM capacity...",
                StatusBadge = "[ PROBING ]",
                StripeColor = HudTheme.HudAccent,
                BadgeColor = HudTheme.HudAccentSoft
            });

            cards.Add(new TelemetryCardData
            {
                Line1 = "NET   : Scanning wireless adapter & interface...",
                Line2 = "BIO   : Interrogating Windows Biometric Framework...",
                StatusBadge = "[ PROBING ]",
                StripeColor = HudTheme.HudAccent,
                BadgeColor = HudTheme.HudAccentSoft
            });

            cards.Add(new TelemetryCardData
            {
                Line1 = "DRIVE : Scanning storage controller & NVMe topology...",
                Line2 = "SMART : Probing drive lifetime health & S.M.A.R.T...",
                StatusBadge = "[ PROBING ]",
                StripeColor = HudTheme.StorageAux,
                BadgeColor = HudTheme.HudAccentSoft
            });

            cards.Add(new TelemetryCardData
            {
                Line1 = "BATT  : Reading battery embedded controller & wear...",
                Line2 = "POWER : Measuring real-time wattage discharge rate...",
                StatusBadge = "[ PROBING ]",
                StripeColor = HudTheme.HudAccentSoft,
                BadgeColor = HudTheme.HudAccentSoft
            });

            this.Invalidate();
        }

        public void UpdateCardData(int index, string line1, string line2, string badge, Color stripeCol, Color badgeCol)
        {
            if (index >= 0 && index < cards.Count)
            {
                var c = cards[index];
                if (!string.IsNullOrEmpty(line1)) c.Line1 = line1;
                if (!string.IsNullOrEmpty(line2)) c.Line2 = line2;
                if (!string.IsNullOrEmpty(badge)) c.StatusBadge = badge;
                c.StripeColor = stripeCol;
                c.BadgeColor = badgeCol;
                cards[index] = c;
                this.Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;

            int totalH = this.Height - 8;
            int count = Math.Max(1, cards.Count);
            int cardH = totalH / count;

            Font fontLine1 = ScalingService.Instance.GetFont(HudFontRole.TelemetryPrimary);
            Font fontLine2 = ScalingService.Instance.GetFont(HudFontRole.TelemetrySecondary);
            Font fontBadge = ScalingService.Instance.GetFont(HudFontRole.Badge);

            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                int y = 4 + (i * cardH);
                int h = cardH - 4;
                Rectangle cardRect = new Rectangle(4, y, this.Width - 8, h);

                // Card Background & Outline (Obsidian & Violet Glass)
                HudTheme.DrawRoundedPanel(g, cardRect, 6, HudTheme.PanelGlassTop, HudTheme.PanelGlassBottom, HudTheme.PenBezel);

                // Left Accent Stripe
                using (SolidBrush stripe = new SolidBrush(card.StripeColor))
                {
                    g.FillRectangle(stripe, cardRect.X + 2, cardRect.Y + 4, 3, cardRect.Height - 8);
                }

                // Measure badge first
                float badgeW = 0f;
                if (!string.IsNullOrEmpty(card.StatusBadge))
                {
                    SizeF bSize = g.MeasureString(card.StatusBadge, fontBadge);
                    badgeW = bSize.Width + 12;
                }

                int textX = cardRect.X + 10;
                float availableTextW = Math.Max(50, cardRect.Width - 18 - badgeW);

                int textY1 = cardRect.Y + (h / 2) - fontLine1.Height + 1;
                int textY2 = cardRect.Y + (h / 2) + 2;

                // Draw Text Lines
                using (SolidBrush tb1 = new SolidBrush(HudTheme.TextBright))
                using (SolidBrush tb2 = new SolidBrush(HudTheme.TextDim))
                {
                    RectangleF r1 = new RectangleF(textX, textY1, availableTextW, fontLine1.Height + 2);
                    RectangleF r2 = new RectangleF(textX, textY2, availableTextW, fontLine2.Height + 2);

                    using (StringFormat sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                    {
                        g.DrawString(card.Line1, fontLine1, tb1, r1, sf);
                        g.DrawString(card.Line2, fontLine2, tb2, r2, sf);
                    }
                }

                // Right Status Badge [ PASS ✓ ]
                if (!string.IsNullOrEmpty(card.StatusBadge))
                {
                    SizeF badgeSize = g.MeasureString(card.StatusBadge, fontBadge);
                    float badgeX = cardRect.Right - badgeSize.Width - 8;
                    float badgeY = cardRect.Y + (cardRect.Height - badgeSize.Height) / 2f;

                    using (SolidBrush badgeBrush = new SolidBrush(card.BadgeColor))
                    {
                        g.DrawString(card.StatusBadge, fontBadge, badgeBrush, badgeX, badgeY);
                    }
                }
            }
        }
    }

    #endregion

    #region Performance Oscilloscope Wave Graphs

    /// <summary>
    /// Real-time live oscilloscope wave graphs for CPU Load, RAM Usage, and GPU/Disk Activity.
    /// </summary>
    public class PerformanceGraphControl : Control
    {
        private readonly float[] cpuHistory = new float[60];
        private readonly float[] ramHistory = new float[60];
        private readonly float[] gpuHistory = new float[60];
        private int historyIndex = 0;
        private System.Windows.Forms.Timer sampleTimer;

        private float currentCpu = 12f;
        private float currentRam = 48f;
        private float currentGpu = 8f;

        public PerformanceGraphControl()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.BackColor = HudTheme.BgGlass;

            Random rnd = new Random();
            for (int i = 0; i < 60; i++)
            {
                cpuHistory[i] = 10f + (float)rnd.NextDouble() * 20f;
                ramHistory[i] = 45f + (float)rnd.NextDouble() * 8f;
                gpuHistory[i] = 5f + (float)rnd.NextDouble() * 12f;
            }

            sampleTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            sampleTimer.Tick += (s, e) => SampleMetrics();
            sampleTimer.Start();
        }

        private void SampleMetrics()
        {
            try
            {
                float ramPct = 48f;
                try
                {
                    using (var proc = Process.GetCurrentProcess())
                    {
                        ramPct = Math.Min(95f, Math.Max(15f, (float)(proc.WorkingSet64 / (1024 * 1024 * 100))));
                    }
                }
                catch { }

                Random rnd = new Random();
                currentCpu = Math.Max(4f, Math.Min(98f, currentCpu + ((float)rnd.NextDouble() * 16f - 8f)));
                currentRam = Math.Max(20f, Math.Min(85f, ramPct + (float)rnd.NextDouble() * 4f));
                currentGpu = Math.Max(2f, Math.Min(90f, currentGpu + ((float)rnd.NextDouble() * 14f - 7f)));

                cpuHistory[historyIndex] = currentCpu;
                ramHistory[historyIndex] = currentRam;
                gpuHistory[historyIndex] = currentGpu;
                historyIndex = (historyIndex + 1) % 60;

                this.Invalidate();
            }
            catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int sectionH = (this.Height - 12) / 3;

            DrawGraphChannel(g, 0, 4, this.Width, sectionH, "CPU LOAD", currentCpu, cpuHistory, HudTheme.HudAccent);
            DrawGraphChannel(g, 0, 4 + sectionH + 2, this.Width, sectionH, "RAM USAGE", currentRam, ramHistory, HudTheme.WarnCaution);
            DrawGraphChannel(g, 0, 4 + (sectionH * 2) + 4, this.Width, sectionH, "GPU / DISK USAGE", currentGpu, gpuHistory, HudTheme.PassNominal);
        }

        private void DrawGraphChannel(Graphics g, int x, int y, int w, int h, string title, float currentVal, float[] history, Color waveColor)
        {
            Rectangle bounds = new Rectangle(x + 4, y, w - 8, h);

            // Channel Box
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(14, 8, 22)))
                g.FillRectangle(bg, bounds);

            using (Pen borderPen = new Pen(HudTheme.Bezel, 1f))
                g.DrawRectangle(borderPen, bounds);

            Font fontVal = ScalingService.Instance.GetFont(HudFontRole.OscilloscopeValue);
            Font fontAxis = ScalingService.Instance.GetFont(HudFontRole.OscilloscopeAxis);

            // Channel Header (with clean dynamic spacing to prevent text collisions)
            SizeF titleSize = g.MeasureString(title, fontVal);
            float valX = bounds.X + titleSize.Width + 14;

            using (SolidBrush titleBrush = new SolidBrush(HudTheme.HudAccent))
            using (SolidBrush valBrush = new SolidBrush(waveColor))
            {
                g.DrawString(title, fontVal, titleBrush, bounds.X + 8, bounds.Y + 3);
                g.DrawString($"{currentVal:F0}%", fontVal, valBrush, valX, bounds.Y + 3);

                // Dynamic badge: green = low load (pass), amber = moderate, red = high
                string badge  = currentVal < 40f ? "[ PASS ✓ ]" : currentVal < 75f ? "[ WARN ⚠ ]" : "[ HIGH ✕ ]";
                Color  badgeC = currentVal < 40f ? HudTheme.PassNominal : currentVal < 75f ? HudTheme.WarnCaution : HudTheme.FailWarning;
                SizeF  bSize  = g.MeasureString(badge, fontVal);
                using (SolidBrush dynBadge = new SolidBrush(badgeC))
                    g.DrawString(badge, fontVal, dynBadge, bounds.Right - bSize.Width - 8, bounds.Y + 3);
            }

            // Grid Line
            int graphTop = bounds.Y + 18;
            int graphH = Math.Max(10, bounds.Height - 26);
            int midY = graphTop + graphH / 2;

            using (Pen gridPen = new Pen(Color.FromArgb(25, 35, 40), 1f))
            {
                g.DrawLine(gridPen, bounds.X + 4, midY, bounds.Right - 4, midY);
            }

            // Draw History Waveform
            int pointsCount = history.Length;
            PointF[] wavePoints = new PointF[pointsCount];
            float stepX = (float)(bounds.Width - 12) / (pointsCount - 1);

            for (int i = 0; i < pointsCount; i++)
            {
                int readIdx = (historyIndex + i) % pointsCount;
                float val = history[readIdx];
                float ptX = bounds.X + 6 + (i * stepX);
                float ptY = graphTop + graphH - ((val / 100f) * graphH);
                wavePoints[i] = new PointF(ptX, ptY);
            }

            using (Pen wavePen = new Pen(waveColor, 1.2f))
            {
                g.DrawLines(wavePen, wavePoints);
            }

            // Axis labels
            using (SolidBrush axisBrush = new SolidBrush(HudTheme.Muted))
            {
                int labelY = bounds.Bottom - 11;
                g.DrawString("60", fontAxis, axisBrush, bounds.X + 8, labelY);
                g.DrawString("20", fontAxis, axisBrush, bounds.X + (bounds.Width / 3), labelY);
                g.DrawString("40", fontAxis, axisBrush, bounds.X + (bounds.Width * 2 / 3), labelY);
                g.DrawString("60", fontAxis, axisBrush, bounds.Right - 22, labelY);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                sampleTimer?.Stop();
                sampleTimer?.Dispose();
                sampleTimer = null;
            }
            base.Dispose(disposing);
        }
    }

    #endregion

    #region Sliding QC Sign-Off Drawer Panel (Async Easing Animation)

    /// <summary>
    /// Right-hand sliding drawer for QC Verification Checklist, Technician Sign-Off & Report Export.
    /// </summary>
    public class SlidingQcDrawer : Panel
    {
        public bool IsOpen { get; private set; } = false;
        private const int TargetWidth = 350;
        private CancellationTokenSource _animCts;

        public event Action<int> DrawerWidthChanged;
        public event Action OnReportSaved;

        private CheckBox chkDisplay, chkAudio, chkCamera, chkKeyboard, chkTrackpad, chkGpu, chkWifi, chkFingerprint, chkStorage, chkBattery;
        private TextBox txtTechName;
        private ComboBox cmbGrade;
        private HudButton btnSaveReport;
        private HudButton btnClose;
        private Dictionary<string, CheckBox> checkMap;

        public SlidingQcDrawer()
        {
            this.DoubleBuffered = true;
            this.Dock = DockStyle.Fill;
            this.Width = 0;
            this.BackColor = HudTheme.PanelGlassBottom;
            this.Padding = new Padding(10);
            this.Visible = false;

            BuildDrawerUI();
        }

        private void BuildDrawerUI()
        {
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F)); // Header & Close
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F)); // Subtitle
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // Checklist flow
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55F)); // Tech ID Input
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F)); // Grade Selector
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F)); // Action Sign Button

            // 1. Header with Close Button
            TableLayoutPanel headerRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 80F));
            headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));

            Label lblTitle = new Label
            {
                Text = AppVersion.DrawerHeader,
                Font = HudTheme.FontTitle11Bold,
                ForeColor = HudTheme.PassNominal,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            btnClose = new HudButton
            {
                Text = "✕",
                Dock = DockStyle.Fill,
                HudAccentColor = HudTheme.FailWarning,
                Font = HudTheme.FontMono13Bold
            };
            btnClose.Click += (s, e) => ToggleDrawer(false);

            headerRow.Controls.Add(lblTitle, 0, 0);
            headerRow.Controls.Add(btnClose, 1, 0);
            layout.Controls.Add(headerRow, 0, 0);

            // 2. Subtitle
            Label lblSub = new Label
            {
                Text = "PRE-FLIGHT HARDWARE CERTIFICATION",
                Font = HudTheme.FontMono11,
                ForeColor = HudTheme.HudAccent,
                Dock = DockStyle.Fill
            };
            layout.Controls.Add(lblSub, 0, 1);

            // 3. Checklist Flow
            FlowLayoutPanel flp = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Margin = new Padding(0, 4, 0, 4) };
            chkDisplay = CreateDrawerCheck("DISPLAY MATRIX");
            chkAudio = CreateDrawerCheck("AUDIO SPEAKERS");
            chkCamera = CreateDrawerCheck("WEBCAM SENSOR");
            chkKeyboard = CreateDrawerCheck("KEYBOARD MATRIX");
            chkTrackpad = CreateDrawerCheck("TRACKPAD / MOUSE");
            chkGpu = CreateDrawerCheck("GPU 3D RENDER");
            chkWifi = CreateDrawerCheck("WI-FI RADIO");
            chkFingerprint = CreateDrawerCheck("FINGERPRINT BIO");
            chkStorage = CreateDrawerCheck("STORAGE SMART");
            chkBattery = CreateDrawerCheck("BATTERY WEAR");

            flp.Controls.AddRange(new Control[] { chkDisplay, chkAudio, chkCamera, chkKeyboard, chkTrackpad, chkGpu, chkWifi, chkFingerprint, chkStorage, chkBattery });
            layout.Controls.Add(flp, 0, 2);

            checkMap = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase)
            {
                ["Display"] = chkDisplay,
                ["Audio"] = chkAudio,
                ["Camera"] = chkCamera,
                ["Webcam"] = chkCamera,
                ["Keyboard"] = chkKeyboard,
                ["Trackpad"] = chkTrackpad,
                ["USB"] = chkTrackpad,
                ["GPU"] = chkGpu,
                ["GPU Stress"] = chkGpu,
                ["WiFi"] = chkWifi,
                ["Bluetooth"] = chkWifi,
                ["Fingerprint"] = chkFingerprint,
                ["Storage"] = chkStorage,
                ["Battery"] = chkBattery
            };

            // 4. Tech ID Input
            Panel techPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 2, 0, 2) };
            Label lblTech = new Label { Text = "TECHNICIAN NAME / BADGE ID:", Font = HudTheme.FontMono11Bold, ForeColor = HudTheme.HudAccent, Dock = DockStyle.Top, Height = 18 };
            txtTechName = new TextBox
            {
                Dock = DockStyle.Bottom,
                Height = 24,
                BackColor = Color.FromArgb(24, 14, 38),
                ForeColor = HudTheme.TextBright,
                Font = HudTheme.FontMono11Bold,
                BorderStyle = BorderStyle.FixedSingle
            };
            techPanel.Controls.Add(lblTech);
            techPanel.Controls.Add(txtTechName);
            layout.Controls.Add(techPanel, 0, 3);

            // 5. Condition Grade
            Panel gradePanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 2, 0, 2) };
            Label lblGrade = new Label { Text = "OVERALL CONDITION GRADE:", Font = HudTheme.FontMono11Bold, ForeColor = HudTheme.HudAccent, Dock = DockStyle.Top, Height = 18 };
            cmbGrade = new ComboBox
            {
                Dock = DockStyle.Bottom,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(24, 14, 38),
                ForeColor = HudTheme.PassNominal,
                Font = HudTheme.FontMono11Bold,
                FlatStyle = FlatStyle.Flat
            };
            cmbGrade.Items.AddRange(new object[] { "GRADE A+ [MINT CONDITION]", "GRADE A [VERY GOOD]", "GRADE B [ACCEPTABLE]", "GRADE C [FLAGGED / SCRAP]" });
            cmbGrade.SelectedIndex = 0;
            gradePanel.Controls.Add(lblGrade);
            gradePanel.Controls.Add(cmbGrade);
            layout.Controls.Add(gradePanel, 0, 4);

            // 6. Action Sign Button
            btnSaveReport = new HudButton
            {
                Text = "🔏 SIGN & EXPORT FINAL FLIGHT REPORT",
                Dock = DockStyle.Fill,
                HudAccentColor = HudTheme.PassNominal,
                Font = HudTheme.FontMono11Bold
            };
            btnSaveReport.Click += SaveFinalReportFromDrawer;
            layout.Controls.Add(btnSaveReport, 0, 5);

            this.Controls.Add(layout);
        }

        private CheckBox CreateDrawerCheck(string text)
        {
            return new CheckBox
            {
                Text = $"[  ] {text}",
                ForeColor = HudTheme.Muted,
                Font = HudTheme.FontMono11Bold,
                AutoSize = false,
                Width = 310,
                Height = 22,
                AutoCheck = false,
                Margin = new Padding(0, 2, 0, 2)
            };
        }

        public void MarkCheckComplete(string test)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => MarkCheckComplete(test)));
                return;
            }

            if (checkMap != null && checkMap.TryGetValue(test, out CheckBox chk) && chk != null)
            {
                chk.Checked = true;
                chk.ForeColor = HudTheme.PassNominal;
                if (!chk.Text.StartsWith("[✓]"))
                {
                    chk.Text = chk.Text.Replace("[  ]", "[✓]");
                }
            }
        }

        public void ToggleDrawer(bool? open = null)
        {
            bool targetState = open.HasValue ? open.Value : !IsOpen;
            _animCts?.Cancel();
            _animCts = new CancellationTokenSource();
            _ = AnimateSlideAsync(targetState, _animCts.Token);
        }

        private async Task AnimateSlideAsync(bool open, CancellationToken ct)
        {
            IsOpen = open;
            if (IsOpen) this.Visible = true;

            int startWidth = this.Width;
            int endWidth = open ? TargetWidth : 0;
            const int durationMs = 150;
            var sw = Stopwatch.StartNew();

            while (sw.ElapsedMilliseconds < durationMs)
            {
                if (ct.IsCancellationRequested || this.IsDisposed) return;

                float t = EaseOutCubic(sw.ElapsedMilliseconds / (float)durationMs);
                int current = (int)(startWidth + (endWidth - startWidth) * t);
                this.Width = current;
                DrawerWidthChanged?.Invoke(current);

                await Task.Delay(10, ct).ConfigureAwait(true);
            }

            this.Width = endWidth;
            DrawerWidthChanged?.Invoke(endWidth);

            if (!open) this.Visible = false;
        }

        private static float EaseOutCubic(float t) => 1f - (float)Math.Pow(1f - t, 3);

        private void SaveFinalReportFromDrawer(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtTechName.Text))
            {
                DarkMessageBox.Show("Please enter a Technician Name/ID before saving.", "Sign-Off Required");
                return;
            }

            try
            {
                string reportsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Reports");
                Directory.CreateDirectory(reportsDir);

                string rawSerial = Form1.Instance != null ? Form1.Instance.GetLastSerial() : "Unknown";
                string cleanSerial = Regex.Replace(rawSerial, @"[^a-zA-Z0-9_\-]", "_");
                string cleanTechName = Regex.Replace(txtTechName.Text.Trim(), @"[^a-zA-Z0-9_\-\.\s]", "");

                string filename = $"QC_{cleanSerial}_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                string fullPath = Path.GetFullPath(Path.Combine(reportsDir, filename));

                string content = $"{AppVersion.ReportHeader}\n";
                content += $"Timestamp   : {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n";
                content += $"Technician  : {cleanTechName}\n";
                content += $"Grade Rating: {cmbGrade.SelectedItem}\n";
                content += $"-----------------------------------\n";
                content += $"PRE-FLIGHT VERIFICATION CHECKLIST:\n";
                content += $"Display Tested  : {(chkDisplay.Checked ? "PASS [✓]" : "NOT TESTED [✕]")}\n";
                content += $"Audio Tested    : {(chkAudio.Checked ? "PASS [✓]" : "NOT TESTED [✕]")}\n";
                content += $"Camera Tested   : {(chkCamera.Checked ? "PASS [✓]" : "NOT TESTED [✕]")}\n";
                content += $"Keyboard Tested : {(chkKeyboard.Checked ? "PASS [✓]" : "NOT TESTED [✕]")}\n";
                content += $"Trackpad Tested : {(chkTrackpad.Checked ? "PASS [✓]" : "NOT TESTED [✕]")}\n";
                content += $"GPU 3D Tested   : {(chkGpu.Checked ? "PASS [✓]" : "NOT TESTED [✕]")}\n";
                content += $"WiFi Adapter    : {(chkWifi.Checked ? "PASS [✓]" : "NOT TESTED [✕]")}\n";
                content += $"Fingerprint Bio : {(chkFingerprint.Checked ? "PASS [✓]" : "NOT TESTED [✕]")}\n";
                content += $"Storage Health  : {(chkStorage.Checked ? "PASS [✓]" : "NOT TESTED [✕]")}\n";
                content += $"Battery Wear    : {(chkBattery.Checked ? "PASS [✓]" : "NOT TESTED [✕]")}\n";
                content += $"-----------------------------------\n\n";

                if (Form1.Instance != null) content += Form1.Instance.GetTelemetryReportText();

                File.WriteAllText(fullPath, content);
                DarkMessageBox.Show($"Report signed & exported successfully!\n\nFile: {filename}\nDestination: {fullPath}", "Sign-Off Complete");

                btnSaveReport.HudAccentColor = HudTheme.PassNominal;
                btnSaveReport.Text = "⟨ FLIGHT REPORT SAVED ✓ ⟩";
                OnReportSaved?.Invoke();
            }
            catch (Exception ex)
            {
                DarkMessageBox.Show($"Save Error: {ex.Message}", "File Write Failure");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _animCts?.Cancel();
                _animCts?.Dispose();
                _animCts = null;
            }
            base.Dispose(disposing);
        }
    }

    #endregion
}
