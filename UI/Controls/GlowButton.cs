using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SuperAutoMater
{
    /// <summary>
    /// Cyber-Industrial button with neon violet illumination, rounded corners,
    /// and built-in hotkey badge indicator.
    /// </summary>
    public class GlowButton : Button
    {
        private bool isPrimary = false;
        private string hotkeyText = "";
        private int cornerRadius = 10;
        private Color accentColor = HudTheme.HudAccent;

        private bool isHovered = false;
        private bool isPressed = false;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsPrimary
        {
            get => isPrimary;
            set { isPrimary = value; Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string HotkeyText
        {
            get => hotkeyText;
            set { hotkeyText = value; Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int CornerRadius
        {
            get => cornerRadius;
            set { cornerRadius = Math.Max(0, value); Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color AccentColor
        {
            get => accentColor;
            set { accentColor = value; Invalidate(); }
        }

        public GlowButton()
        {
            this.FlatStyle = FlatStyle.Flat;
            this.FlatAppearance.BorderSize = 0;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick | ControlStyles.OptimizedDoubleBuffer, true);
            this.Cursor = Cursors.Hand;
            this.Font = HudTheme.FontMono11Bold;
            this.BackColor = Color.FromArgb(18, 10, 30);
            this.ForeColor = HudTheme.TextBright;
        }

        protected override void OnMouseEnter(EventArgs e) { isHovered = true; base.OnMouseEnter(e); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { isHovered = false; isPressed = false; base.OnMouseLeave(e); Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs mevent) { isPressed = true; base.OnMouseDown(mevent); Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs mevent) { isPressed = false; base.OnMouseUp(mevent); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Rectangle rect = new Rectangle(1, 1, this.Width - 3, this.Height - 3);
            if (rect.Width <= 0 || rect.Height <= 0) return;

            Color fillTop;
            Color fillBottom;
            Color borderCol;
            Color textCol;

            if (isPrimary)
            {
                // Primary Vibrant Neon Violet / Purple Button
                if (isPressed)
                {
                    fillTop = Color.FromArgb(100, 45, 160);
                    fillBottom = Color.FromArgb(70, 30, 120);
                    borderCol = Color.FromArgb(220, 170, 255);
                    textCol = Color.White;
                }
                else if (isHovered)
                {
                    fillTop = Color.FromArgb(180, 95, 255);
                    fillBottom = Color.FromArgb(120, 60, 200);
                    borderCol = Color.FromArgb(235, 195, 255);
                    textCol = Color.White;
                }
                else
                {
                    fillTop = Color.FromArgb(150, 75, 235);
                    fillBottom = Color.FromArgb(95, 45, 165);
                    borderCol = HudTheme.HudAccentSoft;
                    textCol = Color.White;
                }

                // Outer ambient glow on hover
                if (isHovered)
                {
                    using (Pen glow = new Pen(Color.FromArgb(90, HudTheme.HudAccent), 3f))
                    using (GraphicsPath glowPath = HudTheme.GetRoundedPath(new Rectangle(0, 0, this.Width - 1, this.Height - 1), cornerRadius + 1))
                    {
                        g.DrawPath(glow, glowPath);
                    }
                }
            }
            else
            {
                // Secondary Obsidian Button
                if (isPressed)
                {
                    fillTop = Color.FromArgb(48, 24, 78);
                    fillBottom = Color.FromArgb(32, 16, 54);
                    borderCol = accentColor;
                    textCol = HudTheme.TextBright;
                }
                else if (isHovered)
                {
                    fillTop = Color.FromArgb(36, 18, 58);
                    fillBottom = Color.FromArgb(24, 12, 40);
                    borderCol = accentColor;
                    textCol = HudTheme.HudAccentSoft;
                }
                else
                {
                    fillTop = Color.FromArgb(20, 11, 33);
                    fillBottom = Color.FromArgb(14, 8, 24);
                    borderCol = this.Enabled ? HudTheme.Bezel : Color.FromArgb(28, 16, 44);
                    textCol = this.Enabled ? HudTheme.TextBright : HudTheme.Muted;
                }
            }

            // Draw Rounded Background & Border
            using (Pen bPen = new Pen(borderCol, 1f))
            {
                HudTheme.DrawRoundedPanel(g, rect, cornerRadius, fillTop, fillBottom, bPen);
            }

            // Calculate hotkey width
            float hotkeyWidth = 0f;
            if (!string.IsNullOrEmpty(hotkeyText))
            {
                Font badgeFont = ScalingService.Instance.GetFont(HudFontRole.Badge);
                string badgeStr = $"[{hotkeyText}]";
                SizeF bSize = g.MeasureString(badgeStr, badgeFont);
                hotkeyWidth = bSize.Width + 12;

                Rectangle badgeRect = new Rectangle(rect.Right - (int)bSize.Width - 14, (this.Height - (int)bSize.Height) / 2, (int)bSize.Width + 8, (int)bSize.Height);
                Color badgeBg = isPrimary ? Color.FromArgb(60, 0, 0, 0) : Color.FromArgb(35, 18, 55);
                Color badgeText = isPrimary ? Color.FromArgb(235, 215, 255) : HudTheme.HudAccentSoft;
                Color badgeBorder = isPrimary ? Color.FromArgb(80, 255, 255, 255) : HudTheme.Bezel;

                HudTheme.DrawPillBadge(g, badgeRect, badgeStr, badgeFont, badgeBg, badgeText, badgeBorder);
            }

            // Draw Button Text
            Font btnFont = ScalingService.Instance.GetFont(HudFontRole.Button);
            Rectangle textRect = new Rectangle(rect.Left + 10, rect.Top, rect.Width - 20 - (int)hotkeyWidth, rect.Height);

            using (SolidBrush tb = new SolidBrush(textCol))
            using (StringFormat sf = new StringFormat
            {
                Alignment = string.IsNullOrEmpty(hotkeyText) ? StringAlignment.Center : StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter
            })
            {
                g.DrawString(this.Text, btnFont, tb, textRect, sf);
            }
        }
    }
}
