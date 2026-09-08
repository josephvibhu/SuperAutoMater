using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace ITAS_QC_Tool
{
    #region App Version (Single Source of Truth)

    public static class AppVersion
    {
        private static readonly Version _ver = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(6, 4, 0, 0);
        public static string VersionString => $"{_ver.Major}.{_ver.Minor}";
        public static string Display => $"AutoMater v{VersionString}";
        public static string WindowTitle => $"AutoMater v{VersionString}";
        public static string StationTag => "● OPERATIONAL";
        public static string DrawerHeader => $"⌁ QC SIGN-OFF (v{VersionString})";
        public static string ReportHeader => $"AUTOMATER QC TELEMETRY DASHBOARD - FINAL FLIGHT REPORT (v{VersionString})";
    }

    #endregion

    #region Font Roles & Scaling Service

    public enum HudFontRole
    {
        TelemetryPrimary,
        TelemetrySecondary,
        Button,
        PanelHeader,
        OscilloscopeValue,
        OscilloscopeAxis,
        Badge,
        Title
    }

    public interface IScalingService
    {
        float Scale { get; }
        Font GetFont(HudFontRole role);
        void Recalculate(int panelHeight, int deviceDpi);
    }

    public sealed class ScalingService : IScalingService
    {
        public static readonly ScalingService Instance = new ScalingService();

        private readonly Dictionary<HudFontRole, Font> _cache = new Dictionary<HudFontRole, Font>();
        private readonly object _lock = new object();
        public float Scale { get; private set; } = 1.0f;

        public ScalingService()
        {
            Recalculate(400, 96);
        }

        public void Recalculate(int panelHeight, int deviceDpi)
        {
            lock (_lock)
            {
                var dpiFactor = Math.Max(1.0f, deviceDpi / 96f);
                Scale = Math.Clamp(panelHeight / 420.0f, 0.85f, 1.15f) * dpiFactor;

                foreach (var font in _cache.Values)
                {
                    try { font.Dispose(); } catch { }
                }
                _cache.Clear();

                string monoFamily = HudTheme.MonoFamily;
                string titleFamily = HudTheme.TitleFamily;

                _cache[HudFontRole.TelemetryPrimary]   = new Font(monoFamily, 9.5f * Scale, FontStyle.Bold);
                _cache[HudFontRole.TelemetrySecondary] = new Font(monoFamily, 8.5f * Scale, FontStyle.Regular);
                _cache[HudFontRole.Button]             = new Font(monoFamily, 8.5f * Scale, FontStyle.Bold);
                _cache[HudFontRole.PanelHeader]        = new Font(titleFamily, 9.0f * Scale, FontStyle.Bold);
                _cache[HudFontRole.OscilloscopeValue]  = new Font(monoFamily, 8.5f * Scale, FontStyle.Bold);
                _cache[HudFontRole.OscilloscopeAxis]   = new Font(monoFamily, 7.5f * Scale, FontStyle.Regular);
                _cache[HudFontRole.Badge]              = new Font(monoFamily, 8.5f * Scale, FontStyle.Bold);
                _cache[HudFontRole.Title]              = new Font(titleFamily, 11.0f * Scale, FontStyle.Bold);
            }
        }

        public Font GetFont(HudFontRole role)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(role, out var font)) return font;
                return HudTheme.FontMono11Bold;
            }
        }
    }

    #endregion

    /// <summary>
    /// Centralized Flight Deck / Avionics HUD Design System Token & Rendering Engine.
    /// </summary>
    public static class HudTheme
    {
        #region Color Tokens

        public static readonly Color BgGlass          = Color.FromArgb(10, 13, 14);    // #0A0D0E (App background)
        public static readonly Color PanelGlassTop    = Color.FromArgb(16, 22, 26);    // #10161A (Panel glass top)
        public static readonly Color PanelGlassBottom = Color.FromArgb(13, 18, 20);    // #0D1214 (Panel glass bottom)
        public static readonly Color Bezel            = Color.FromArgb(34, 49, 51);    // #223133 (Hairline frame)
        public static readonly Color BezelActive      = Color.FromArgb(50, 75, 80);    // #324B50 (Active bezel)
        public static readonly Color HudAccent        = Color.FromArgb(139, 233, 253); // #8BE9FD (HUD Cyan chrome & ticks)
        public static readonly Color PassNominal      = Color.FromArgb(80, 250, 123);  // #50FA7B (Nominal / PASS)
        public static readonly Color WarnCaution      = Color.FromArgb(255, 184, 108); // #FFB86C (Caution / WARN)
        public static readonly Color FailWarning      = Color.FromArgb(255, 85, 85);   // #FF5555 (Master-warning / FAIL)
        public static readonly Color StorageAux       = Color.FromArgb(189, 147, 249); // #BD93F9 (Aux / Storage telemetry)
        public static readonly Color Muted            = Color.FromArgb(58, 74, 77);    // #3A4A4D (Inactive / off)
        public static readonly Color TextBright       = Color.FromArgb(248, 248, 242); // #F8F8F2 (Primary readout)
        public static readonly Color TextDim          = Color.FromArgb(98, 114, 164);  // #6272A4 (Secondary readout)

        #endregion

        #region Pre-cached GDI+ Brushes & Pens (Zero Allocation on OnPaint)

        public static readonly SolidBrush BrushBgGlass     = new SolidBrush(BgGlass);
        public static readonly SolidBrush BrushPanelGlass  = new SolidBrush(PanelGlassTop);
        public static readonly SolidBrush BrushHudAccent   = new SolidBrush(HudAccent);
        public static readonly SolidBrush BrushPassNominal = new SolidBrush(PassNominal);
        public static readonly SolidBrush BrushWarnCaution = new SolidBrush(WarnCaution);
        public static readonly SolidBrush BrushFailWarning = new SolidBrush(FailWarning);
        public static readonly SolidBrush BrushStorageAux  = new SolidBrush(StorageAux);
        public static readonly SolidBrush BrushMuted       = new SolidBrush(Muted);
        public static readonly SolidBrush BrushTextBright  = new SolidBrush(TextBright);
        public static readonly SolidBrush BrushTextDim     = new SolidBrush(TextDim);

        public static readonly Pen PenBezel          = new Pen(Bezel, 1f);
        public static readonly Pen PenBezelActive    = new Pen(BezelActive, 1f);
        public static readonly Pen PenHudAccent      = new Pen(HudAccent, 1f);
        public static readonly Pen PenHudAccentThick = new Pen(HudAccent, 1.5f);
        public static readonly Pen PenPassNominal    = new Pen(PassNominal, 1f);
        public static readonly Pen PenWarnCaution    = new Pen(WarnCaution, 1f);
        public static readonly Pen PenFailWarning    = new Pen(FailWarning, 1f);
        public static readonly Pen PenStorageAux     = new Pen(StorageAux, 1f);
        public static readonly Pen PenMuted          = new Pen(Muted, 1f);

        #endregion

        #region Typography Scale

        private static readonly PrivateFontCollection FontCollection = new PrivateFontCollection();
        public static readonly string MonoFamily;
        public static readonly string TitleFamily;

        public static readonly Font FontMono11;
        public static readonly Font FontMono11Bold;
        public static readonly Font FontMono13;
        public static readonly Font FontMono13Bold;
        public static readonly Font FontMono16;
        public static readonly Font FontMono16Bold;
        public static readonly Font FontMono20Bold;
        public static readonly Font FontMono28Bold;

        public static readonly Font FontTitle11Bold;
        public static readonly Font TitleFont11Bold;
        public static readonly Font FontTitle13Bold;
        public static readonly Font FontTitle16Bold;
        public static readonly Font FontTitle20Bold;
        public static readonly Font FontTitle28Bold;

        static HudTheme()
        {
            string fontsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib", "fonts");
            if (Directory.Exists(fontsDir))
            {
                foreach (var fontFile in Directory.GetFiles(fontsDir, "*.ttf"))
                {
                    try { FontCollection.AddFontFile(fontFile); } catch { }
                }
                foreach (var fontFile in Directory.GetFiles(fontsDir, "*.otf"))
                {
                    try { FontCollection.AddFontFile(fontFile); } catch { }
                }
            }

            string foundMono = "Consolas";
            string foundTitle = "Segoe UI";

            foreach (var family in FontFamily.Families)
            {
                if (family.Name.Equals("JetBrains Mono", StringComparison.OrdinalIgnoreCase)) foundMono = family.Name;
                if (family.Name.Equals("Orbitron", StringComparison.OrdinalIgnoreCase)) foundTitle = family.Name;
                else if (family.Name.Equals("Michroma", StringComparison.OrdinalIgnoreCase) && foundTitle == "Segoe UI") foundTitle = family.Name;
            }

            foreach (var family in FontCollection.Families)
            {
                if (family.Name.IndexOf("Mono", StringComparison.OrdinalIgnoreCase) >= 0) foundMono = family.Name;
                if (family.Name.IndexOf("Orbitron", StringComparison.OrdinalIgnoreCase) >= 0 || family.Name.IndexOf("Michroma", StringComparison.OrdinalIgnoreCase) >= 0) foundTitle = family.Name;
            }

            MonoFamily = foundMono;
            TitleFamily = foundTitle;

            FontMono11     = new Font(MonoFamily, 8.5F, FontStyle.Regular);
            FontMono11Bold = new Font(MonoFamily, 8.5F, FontStyle.Bold);
            FontMono13     = new Font(MonoFamily, 9.5F, FontStyle.Regular);
            FontMono13Bold = new Font(MonoFamily, 9.5F, FontStyle.Bold);
            FontMono16     = new Font(MonoFamily, 11F, FontStyle.Regular);
            FontMono16Bold = new Font(MonoFamily, 11F, FontStyle.Bold);
            FontMono20Bold = new Font(MonoFamily, 14F, FontStyle.Bold);
            FontMono28Bold = new Font(MonoFamily, 18F, FontStyle.Bold);

            FontTitle11Bold = new Font(TitleFamily, 8.5F, FontStyle.Bold);
            TitleFont11Bold = FontTitle11Bold;
            FontTitle13Bold = new Font(TitleFamily, 9.5F, FontStyle.Bold);
            FontTitle16Bold = new Font(TitleFamily, 11F, FontStyle.Bold);
            FontTitle20Bold = new Font(TitleFamily, 14F, FontStyle.Bold);
            FontTitle28Bold = new Font(TitleFamily, 18F, FontStyle.Bold);
        }

        #endregion

        #region Avionics GDI+ Vector Drawing Primitives

        /// <summary>
        /// Draws L-shaped corner brackets (⌐ ¬, ⌞ ⌟) for avionics HUD panels with 0px radius.
        /// </summary>
        public static void DrawCornerBrackets(Graphics g, Rectangle bounds, Pen bracketPen, int armLength = 8)
        {
            int l = bounds.Left;
            int r = bounds.Right - 1;
            int t = bounds.Top;
            int b = bounds.Bottom - 1;
            int arm = Math.Min(armLength, Math.Min(bounds.Width, bounds.Height) / 4);

            // Top-Left (⌐)
            g.DrawLine(bracketPen, l, t, l + arm, t);
            g.DrawLine(bracketPen, l, t, l, t + arm);

            // Top-Right (¬)
            g.DrawLine(bracketPen, r - arm, t, r, t);
            g.DrawLine(bracketPen, r, t, r, t + arm);

            // Bottom-Left (⌞)
            g.DrawLine(bracketPen, l, b, l + arm, b);
            g.DrawLine(bracketPen, l, b, l, b - arm);

            // Bottom-Right (⌟)
            g.DrawLine(bracketPen, r - arm, b, r, b);
            g.DrawLine(bracketPen, r, b, r, b - arm);
        }

        #endregion
    }

    /// <summary>
    /// Avionics Glass Panel with corner brackets (⌐ ¬), gradient fill, labeled bezel plate.
    /// </summary>
    public class HudBracketPanel : Panel
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string BezelTitle { get; set; } = "";

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string SubtitleBadge { get; set; } = "";

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color AccentColor { get; set; } = HudTheme.HudAccent;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool ShowCornerBrackets { get; set; } = true;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int BracketArmLength { get; set; } = 8;

        public HudBracketPanel()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.BackColor = HudTheme.PanelGlassTop;
            this.Padding = new Padding(6, 24, 6, 6);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;

            // 1. Panel Glass Background Gradient
            using (LinearGradientBrush bgBrush = new LinearGradientBrush(this.ClientRectangle, HudTheme.PanelGlassTop, HudTheme.PanelGlassBottom, 90f))
            {
                g.FillRectangle(bgBrush, this.ClientRectangle);
            }

            // 2. Hairline Outer Bezel
            g.DrawRectangle(HudTheme.PenBezel, 0, 0, this.Width - 1, this.Height - 1);

            // 3. Corner Brackets
            if (ShowCornerBrackets)
            {
                using (Pen p = new Pen(AccentColor, 1.2f))
                {
                    HudTheme.DrawCornerBrackets(g, new Rectangle(1, 1, this.Width - 2, this.Height - 2), p, BracketArmLength);
                }
            }

            // 4. Labeled Bezel Header Plate
            if (!string.IsNullOrEmpty(BezelTitle))
            {
                int headerHeight = 20;
                using (SolidBrush plateBrush = new SolidBrush(Color.FromArgb(20, 28, 32)))
                {
                    g.FillRectangle(plateBrush, 1, 1, this.Width - 2, headerHeight);
                }
                g.DrawLine(HudTheme.PenBezel, 0, headerHeight, this.Width, headerHeight);

                // Accent Tag
                using (SolidBrush accentBrush = new SolidBrush(AccentColor))
                {
                    g.FillRectangle(accentBrush, 4, 4, 3, 12);
                }

                // Title Text
                Font headerFont = ScalingService.Instance.GetFont(HudFontRole.PanelHeader);
                using (SolidBrush titleBrush = new SolidBrush(HudTheme.TextBright))
                {
                    g.DrawString(BezelTitle.ToUpperInvariant(), headerFont, titleBrush, 12, 3);
                }

                // Subtitle Badge
                if (!string.IsNullOrEmpty(SubtitleBadge))
                {
                    Font badgeFont = ScalingService.Instance.GetFont(HudFontRole.Badge);
                    SizeF subSize = g.MeasureString(SubtitleBadge, badgeFont);
                    float subX = this.Width - subSize.Width - 8;
                    using (SolidBrush badgeBrush = new SolidBrush(AccentColor))
                    {
                        g.DrawString(SubtitleBadge, badgeFont, badgeBrush, subX, 3);
                    }
                }
            }

            base.OnPaint(e);
        }
    }

    /// <summary>
    /// Bracket-wrapped Avionics Button ⟨ INITIATE ⟩ with subtle, mild click highlight.
    /// </summary>
    public class HudButton : Button
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string RawActionText { get; set; } = "";

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color HudAccentColor { get; set; } = HudTheme.HudAccent;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsArmed { get; set; } = false;

        private bool isHovered = false;
        private bool isPressed = false;

        public HudButton()
        {
            this.FlatStyle = FlatStyle.Flat;
            this.FlatAppearance.BorderSize = 0;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick, true);
            this.Cursor = Cursors.Hand;
            this.Font = HudTheme.FontMono11Bold;
            this.BackColor = Color.FromArgb(14, 20, 24);
            this.ForeColor = HudTheme.HudAccent;
            this.Margin = new Padding(2);
        }

        protected override void OnMouseEnter(EventArgs e) { isHovered = true; base.OnMouseEnter(e); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { isHovered = false; isPressed = false; base.OnMouseLeave(e); Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs mevent) { isPressed = true; base.OnMouseDown(mevent); Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs mevent) { isPressed = false; base.OnMouseUp(mevent); Invalidate(); }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.None;

            Color bg;
            Color borderCol;
            Color textCol;

            if (isPressed || IsArmed)
            {
                bg = Color.FromArgb(28, 48, 56);
                borderCol = HudAccentColor;
                textCol = HudTheme.TextBright;
            }
            else if (isHovered)
            {
                bg = Color.FromArgb(20, 30, 36);
                borderCol = HudAccentColor;
                textCol = HudAccentColor;
            }
            else
            {
                bg = Color.FromArgb(14, 20, 24);
                borderCol = this.Enabled ? HudTheme.Bezel : Color.FromArgb(20, 28, 30);
                textCol = this.Enabled ? HudAccentColor : HudTheme.Muted;
            }

            // Background
            using (SolidBrush b = new SolidBrush(bg))
            {
                g.FillRectangle(b, this.ClientRectangle);
            }

            // Outline
            using (Pen p = new Pen(borderCol, 1f))
            {
                g.DrawRectangle(p, 0, 0, this.Width - 1, this.Height - 1);
            }

            // Bracket styling ⟨ TEXT ⟩
            string displayText = this.Text;
            if (!displayText.StartsWith("⟨") && !displayText.StartsWith("[") && !displayText.StartsWith("⚡") && !displayText.StartsWith("◄") && !displayText.StartsWith("⇄") && !displayText.StartsWith("🔏"))
            {
                displayText = $"⟨ {displayText.Trim()} ⟩";
            }

            Font btnFont = ScalingService.Instance.GetFont(HudFontRole.Button);
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
            using (SolidBrush tb = new SolidBrush(textCol))
            {
                g.DrawString(displayText, btnFont, tb, this.ClientRectangle, sf);
            }
        }
    }

    /// <summary>
    /// Segmented Avionics VU Peak Level Meter (Green -> Amber -> Red).
    /// </summary>
    public class HudVuMeter : Control
    {
        private float level = 0f;
        private float peakHold = 0f;
        private int peakHoldTicks = 0;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public float Level
        {
            get => level;
            set
            {
                level = Math.Max(0f, Math.Min(1f, value));
                if (level >= peakHold)
                {
                    peakHold = level;
                    peakHoldTicks = 12;
                }
                else if (peakHoldTicks > 0)
                {
                    peakHoldTicks--;
                }
                else
                {
                    peakHold = Math.Max(0f, peakHold - 0.05f);
                }
                this.Invalidate();
            }
        }

        public HudVuMeter()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            this.Height = 16;
            this.BackColor = Color.FromArgb(12, 16, 18);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;

            // Bezel frame
            g.DrawRectangle(HudTheme.PenBezel, 0, 0, this.Width - 1, this.Height - 1);

            int totalSegments = Math.Max(12, this.Width / 6);
            int activeSegments = (int)(level * totalSegments);
            int peakSegment = (int)(peakHold * totalSegments);
            int segW = Math.Max(2, (this.Width - 4) / totalSegments);

            for (int i = 0; i < totalSegments; i++)
            {
                int x = 2 + (i * segW);
                int w = Math.Max(1, segW - 1);
                int y = 2;
                int h = this.Height - 4;

                bool isActive = i < activeSegments;
                bool isPeak = (i == peakSegment && peakHold > 0.05f);
                float segPct = (float)i / totalSegments;

                Color col;
                if (segPct >= 0.85f) col = (isActive || isPeak) ? HudTheme.FailWarning : Color.FromArgb(40, 20, 20);
                else if (segPct >= 0.60f) col = (isActive || isPeak) ? HudTheme.WarnCaution : Color.FromArgb(40, 30, 15);
                else col = (isActive || isPeak) ? HudTheme.PassNominal : Color.FromArgb(15, 35, 25);

                using (SolidBrush b = new SolidBrush(col))
                {
                    g.FillRectangle(b, x, y, w, h);
                }
            }
        }
    }
}
