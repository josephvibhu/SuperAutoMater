using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SuperAutoMater
{
    /// <summary>
    /// Cyber-Industrial Bento Grid container card.
    /// Double-buffered, rounded corners with hairline violet border and optional neon glow.
    /// </summary>
    public class BentoCard : Panel
    {
        private string headerTitle = "";
        private string headerSubtitle = "";
        private Color headerSubtitleColor = HudTheme.HudAccentSoft;
        private Color tagAccentColor = HudTheme.HudAccent;
        private int cornerRadius = 12;
        private bool showHeader = true;
        private int headerHeight = 30;
        private bool activeGlow = false;
        private Color activeGlowColor = HudTheme.HudAccent;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string HeaderTitle
        {
            get => headerTitle;
            set { headerTitle = value; Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string HeaderSubtitle
        {
            get => headerSubtitle;
            set { headerSubtitle = value; Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color HeaderSubtitleColor
        {
            get => headerSubtitleColor;
            set { headerSubtitleColor = value; Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color TagAccentColor
        {
            get => tagAccentColor;
            set { tagAccentColor = value; Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int CornerRadius
        {
            get => cornerRadius;
            set { cornerRadius = Math.Max(0, value); Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool ShowHeader
        {
            get => showHeader;
            set
            {
                showHeader = value;
                UpdatePadding();
                Invalidate();
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int HeaderHeight
        {
            get => headerHeight;
            set
            {
                headerHeight = Math.Max(16, value);
                UpdatePadding();
                Invalidate();
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool ActiveGlow
        {
            get => activeGlow;
            set { activeGlow = value; Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color ActiveGlowColor
        {
            get => activeGlowColor;
            set { activeGlowColor = value; Invalidate(); }
        }

        public BentoCard()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.BackColor = HudTheme.BgBase;
            UpdatePadding();
        }

        private void UpdatePadding()
        {
            int topPad = showHeader ? (headerHeight + 6) : 8;
            this.Padding = new Padding(10, topPad, 10, 10);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (SolidBrush bgBrush = new SolidBrush(this.BackColor))
            {
                g.FillRectangle(bgBrush, this.ClientRectangle);
            }

            Rectangle rect = new Rectangle(1, 1, this.Width - 3, this.Height - 3);
            if (rect.Width <= 0 || rect.Height <= 0) return;

            // 1. Draw outer glow if active
            if (activeGlow)
            {
                using (Pen glowPen = new Pen(Color.FromArgb(90, activeGlowColor), 3f))
                using (GraphicsPath glowPath = HudTheme.GetRoundedPath(new Rectangle(0, 0, this.Width - 1, this.Height - 1), cornerRadius + 1))
                {
                    g.DrawPath(glowPen, glowPath);
                }
            }

            // 2. Draw Card Body (Gradient Fill + Hairline Border)
            Color topColor = HudTheme.PanelGlassTop;
            Color bottomColor = HudTheme.PanelGlassBottom;
            Pen borderPen = activeGlow ? new Pen(activeGlowColor, 1.2f) : HudTheme.PenBezel;

            HudTheme.DrawRoundedPanel(g, rect, cornerRadius, topColor, bottomColor, borderPen);

            if (activeGlow) borderPen.Dispose();

            // 3. Header Bar (if enabled)
            if (showHeader && headerHeight > 0)
            {
                int hY = rect.Y + headerHeight;
                using (Pen divPen = new Pen(Color.FromArgb(40, HudTheme.Bezel.R, HudTheme.Bezel.G, HudTheme.Bezel.B), 1f))
                {
                    g.DrawLine(divPen, rect.Left + 8, hY, rect.Right - 8, hY);
                }

                // Tag indicator dot or bar
                using (SolidBrush tagBrush = new SolidBrush(tagAccentColor))
                {
                    g.FillEllipse(tagBrush, rect.Left + 10, rect.Y + (headerHeight / 2) - 4, 8, 8);
                }

                // Header Title
                Font titleFont = ScalingService.Instance.GetFont(HudFontRole.PanelHeader);
                using (SolidBrush textBrush = new SolidBrush(HudTheme.TextBright))
                {
                    g.DrawString(headerTitle.ToUpperInvariant(), titleFont, textBrush, rect.Left + 24, rect.Y + (headerHeight / 2) - 7);
                }

                // Header Subtitle Badge
                if (!string.IsNullOrEmpty(headerSubtitle))
                {
                    Font subFont = ScalingService.Instance.GetFont(HudFontRole.Badge);
                    SizeF subSize = g.MeasureString(headerSubtitle, subFont);
                    float subX = rect.Right - subSize.Width - 12;
                    float subY = rect.Y + (headerHeight / 2) - (subSize.Height / 2);

                    using (SolidBrush subBrush = new SolidBrush(headerSubtitleColor))
                    {
                        g.DrawString(headerSubtitle, subFont, subBrush, subX, subY);
                    }
                }
            }

            base.OnPaint(e);
        }
    }
}
