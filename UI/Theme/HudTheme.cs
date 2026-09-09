using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace SuperAutoMater
{
    #region App Version (Single Source of Truth)

    public static class AppVersion
    {
        private static readonly Version _ver = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 1, 0, 0);
        public static string VersionString => $"{_ver.Major}.{_ver.Minor}";
        public static string Display => $"SuperAutoMater v{VersionString}";
        public static string WindowTitle => $"SuperAutoMater v{VersionString}";
        public static string StationTag => "● OPERATIONAL";
        public static string DrawerHeader => $"⌁ QC SIGN-OFF (v{VersionString})";
        public static string ReportHeader => $"SUPERAUTOMATER QC TELEMETRY DASHBOARD - FINAL FLIGHT REPORT (v{VersionString})";
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

        public static readonly Color BgGlass          = Color.FromArgb(6, 4, 10);      // #06040A (Deep Obsidian Black)
        public static readonly Color PanelGlassTop    = Color.FromArgb(18, 10, 30);    // #120A1E (Amethyst Glass Top)
        public static readonly Color PanelGlassBottom = Color.FromArgb(11, 6, 20);     // #0B0614 (Obsidian Glass Bottom)
        public static readonly Color Bezel            = Color.FromArgb(46, 25, 73);    // #2E1949 (Hairline Violet)
        public static readonly Color BezelActive      = Color.FromArgb(88, 43, 140);   // #582B8C (Active Glowing Violet)
        public static readonly Color HudAccent        = Color.FromArgb(168, 85, 247);  // #A855F7 (Electric Violet)
        public static readonly Color HudAccentSoft    = Color.FromArgb(192, 132, 252); // #C084FC (Radiant Lavender)
        public static readonly Color PassNominal      = Color.FromArgb(16, 185, 129);  // #10B981 (Nominal / PASS)
        public static readonly Color WarnCaution      = Color.FromArgb(245, 158, 11);  // #F59E0B (Caution / WARN)
        public static readonly Color FailWarning      = Color.FromArgb(239, 68, 68);   // #EF4444 (Master-warning / FAIL)
        public static readonly Color StorageAux       = Color.FromArgb(217, 70, 239);  // #D946EF (Neon Fuchsia / Aux)
        public static readonly Color Muted            = Color.FromArgb(70, 48, 96);    // #463060 (Inactive / Dim Violet)
        public static readonly Color TextBright       = Color.FromArgb(243, 232, 255); // #F3E8FF (Primary readout - Lilac)
        public static readonly Color TextDim          = Color.FromArgb(139, 127, 163); // #8B7FA3 (Secondary readout - Lavender)

        #endregion

        #region Pre-cached GDI+ Brushes & Pens (Zero Allocation on OnPaint)

        public static readonly SolidBrush BrushBgGlass      = new SolidBrush(BgGlass);
        public static readonly SolidBrush BrushPanelGlass   = new SolidBrush(PanelGlassTop);
        public static readonly SolidBrush BrushHudAccent    = new SolidBrush(HudAccent);
        public static readonly SolidBrush BrushHudAccentSoft= new SolidBrush(HudAccentSoft);
        public static readonly SolidBrush BrushPassNominal  = new SolidBrush(PassNominal);
        public static readonly SolidBrush BrushWarnCaution  = new SolidBrush(WarnCaution);
        public static readonly SolidBrush BrushFailWarning  = new SolidBrush(FailWarning);
        public static readonly SolidBrush BrushStorageAux   = new SolidBrush(StorageAux);
        public static readonly SolidBrush BrushMuted        = new SolidBrush(Muted);
        public static readonly SolidBrush BrushTextBright   = new SolidBrush(TextBright);
        public static readonly SolidBrush BrushTextDim      = new SolidBrush(TextDim);

        public static readonly Pen PenBezel          = new Pen(Bezel, 1f);
        public static readonly Pen PenBezelActive    = new Pen(BezelActive, 1f);
        public static readonly Pen PenHudAccent      = new Pen(HudAccent, 1f);
        public static readonly Pen PenHudAccentThick = new Pen(HudAccent, 1.5f);
        public static readonly Pen PenHudAccentSoft  = new Pen(HudAccentSoft, 1f);
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

        private static bool IsFontAvailable(string familyName)
        {
            try
            {
                using (var test = new FontFamily(familyName))
                {
                    return string.Equals(test.Name, familyName, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }

        static HudTheme()
        {
            string foundMono = "Consolas";
            string foundTitle = "Segoe UI";

            // Fast direct font probing (instant 0.2ms - avoids scanning hundreds of system fonts)
            if (IsFontAvailable("JetBrains Mono")) foundMono = "JetBrains Mono";
            if (IsFontAvailable("Orbitron")) foundTitle = "Orbitron";
            else if (IsFontAvailable("Michroma")) foundTitle = "Michroma";

            // Optional bundled fonts in lib/fonts (safe check)
            try
            {
                string fontsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib", "fonts");
                if (Directory.Exists(fontsDir))
                {
                    foreach (var fontFile in Directory.GetFiles(fontsDir, "*.*"))
                    {
                        if (fontFile.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
                            fontFile.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
                        {
                            try { FontCollection.AddFontFile(fontFile); } catch { }
                        }
                    }

                    foreach (var family in FontCollection.Families)
                    {
                        if (family.Name.IndexOf("Mono", StringComparison.OrdinalIgnoreCase) >= 0) foundMono = family.Name;
                        if (family.Name.IndexOf("Orbitron", StringComparison.OrdinalIgnoreCase) >= 0 || family.Name.IndexOf("Michroma", StringComparison.OrdinalIgnoreCase) >= 0) foundTitle = family.Name;
                    }
                }
            }
            catch { }

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

        /// <summary>
        /// Generates a GraphicsPath representing a rounded rectangle.
        /// </summary>
        public static GraphicsPath GetRoundedPath(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (radius <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }

            int diameter = radius * 2;
            Size size = new Size(diameter, diameter);
            Rectangle arc = new Rectangle(bounds.Location, size);

            // Top-left
            path.AddArc(arc, 180, 90);

            // Top-right
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);

            // Bottom-right
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);

            // Bottom-left
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);

            path.CloseFigure();
            return path;
        }

        /// <summary>
        /// Draws a modern double-buffered rounded bento panel with gradient fill and hairline border.
        /// </summary>
        public static void DrawRoundedPanel(Graphics g, Rectangle bounds, int radius, Color topColor, Color bottomColor, Pen borderPen)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (GraphicsPath path = GetRoundedPath(bounds, radius))
            {
                using (LinearGradientBrush fill = new LinearGradientBrush(bounds, topColor, bottomColor, 90f))
                {
                    g.FillPath(fill, path);
                }

                if (borderPen != null)
                {
                    g.DrawPath(borderPen, path);
                }
            }
        }

        /// <summary>
        /// Draws a pill-shaped status or telemetry badge.
        /// </summary>
        public static void DrawPillBadge(Graphics g, Rectangle bounds, string text, Font font, Color bgColor, Color textColor, Color borderColor)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int radius = bounds.Height / 2;
            using (GraphicsPath path = GetRoundedPath(bounds, radius))
            {
                using (SolidBrush bg = new SolidBrush(bgColor))
                {
                    g.FillPath(bg, path);
                }

                if (borderColor != Color.Transparent)
                {
                    using (Pen borderPen = new Pen(borderColor, 1f))
                    {
                        g.DrawPath(borderPen, path);
                    }
                }

                using (SolidBrush textBrush = new SolidBrush(textColor))
                using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    g.DrawString(text, font, textBrush, bounds, sf);
                }
            }
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
                using (SolidBrush plateBrush = new SolidBrush(Color.FromArgb(24, 14, 38)))
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
            this.BackColor = Color.FromArgb(18, 10, 30);
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
                bg = Color.FromArgb(64, 28, 108);
                borderCol = HudTheme.HudAccentSoft;
                textCol = HudTheme.TextBright;
            }
            else if (isHovered)
            {
                bg = Color.FromArgb(42, 20, 72);
                borderCol = HudAccentColor;
                textCol = HudTheme.HudAccentSoft;
            }
            else
            {
                bg = Color.FromArgb(18, 10, 30);
                borderCol = this.Enabled ? HudTheme.Bezel : Color.FromArgb(28, 16, 44);
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
