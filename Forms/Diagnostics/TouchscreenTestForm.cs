using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SuperAutoMater
{
    public class TouchscreenTestForm : Form
    {
        private const int COLS = 14;
        private const int ROWS = 9;
        private readonly bool[,] touchedGrid = new bool[COLS, ROWS];
        private int touchedCount = 0;
        private readonly int totalBlocks = COLS * ROWS;

        private readonly List<Point> trailPoints = new List<Point>();
        private bool isTouching = false;

        private bool hasTouchHardware = false;
        private int maxTouches = 0;

        private readonly Font hudFontBold = new Font("Consolas", 10.5F, FontStyle.Bold);
        private readonly Font hudFontRegular = new Font("Consolas", 9F, FontStyle.Regular);
        private readonly Font blockFont = new Font("Consolas", 8F, FontStyle.Bold);

        public TouchscreenTestForm()
        {
            this.WindowState = FormWindowState.Maximized;
            this.FormBorderStyle = FormBorderStyle.None;
            this.TopMost = true;
            this.DoubleBuffered = true;
            this.Cursor = Cursors.Cross;
            this.BackColor = Form1.BtopBg;

            DetectTouchHardware();

            this.MouseDown += (s, e) =>
            {
                isTouching = true;
                trailPoints.Clear();
                ProcessTouchPoint(e.Location);
            };

            this.MouseMove += (s, e) =>
            {
                if (isTouching || e.Button != MouseButtons.None)
                {
                    ProcessTouchPoint(e.Location);
                }
            };

            this.MouseUp += (s, e) =>
            {
                isTouching = false;
                this.Invalidate();
            };

            this.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    this.DialogResult = DialogResult.Cancel;
                    this.Close();
                }
                else if (e.KeyCode == Keys.C || e.KeyCode == Keys.R)
                {
                    ResetGrid();
                }
                else if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.F || e.KeyCode == Keys.P)
                {
                    Form1.Instance?.MarkTestComplete("Display");
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
            };
        }

        private void DetectTouchHardware()
        {
            try
            {
                int digitizer = NativeMethods.GetSystemMetrics(NativeMethods.SM_DIGITIZER);
                maxTouches = NativeMethods.GetSystemMetrics(NativeMethods.SM_MAXIMUMTOUCHES);

                // Check NID_INTEGRATED_TOUCH (0x01) or NID_EXTERNAL_TOUCH (0x02) or NID_MULTI_INPUT (0x40)
                hasTouchHardware = (digitizer & 0x01) != 0 || (digitizer & 0x02) != 0 || maxTouches > 0;
            }
            catch
            {
                hasTouchHardware = false;
                maxTouches = 0;
            }
        }

        private void ProcessTouchPoint(Point pt)
        {
            int topOffset = 65; // Below top HUD banner
            int usableH = this.Height - topOffset;
            if (usableH <= 0) return;

            int colW = this.Width / COLS;
            int rowH = usableH / ROWS;

            if (pt.Y >= topOffset && colW > 0 && rowH > 0)
            {
                int col = Math.Max(0, Math.Min(COLS - 1, pt.X / colW));
                int row = Math.Max(0, Math.Min(ROWS - 1, (pt.Y - topOffset) / rowH));

                if (!touchedGrid[col, row])
                {
                    touchedGrid[col, row] = true;
                    touchedCount++;

                    // Auto-pass if 95% coverage reached
                    if (touchedCount >= (int)(totalBlocks * 0.95))
                    {
                        Form1.Instance?.MarkTestComplete("Display");
                    }
                }
            }

            trailPoints.Add(pt);
            if (trailPoints.Count > 35) trailPoints.RemoveAt(0);

            this.Invalidate();
        }

        private void ResetGrid()
        {
            for (int x = 0; x < COLS; x++)
                for (int y = 0; y < ROWS; y++)
                    touchedGrid[x, y] = false;

            touchedCount = 0;
            trailPoints.Clear();
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = this.Width;
            int h = this.Height;
            int topOffset = 65;
            int usableH = h - topOffset;

            int colW = w / COLS;
            int rowH = usableH / ROWS;

            // 1. Draw Grid Blocks
            for (int c = 0; c < COLS; c++)
            {
                for (int r = 0; r < ROWS; r++)
                {
                    int bx = c * colW;
                    int by = topOffset + r * rowH;
                    int bw = (c == COLS - 1) ? (w - bx) : colW;
                    int bh = (r == ROWS - 1) ? (h - by) : rowH;

                    Rectangle blockRect = new Rectangle(bx, by, bw, bh);
                    bool touched = touchedGrid[c, r];

                    Color bg = touched ? Color.FromArgb(20, 60, 45) : Form1.BtopBtnBg;
                    Color border = touched ? Color.FromArgb(80, 250, 123) : Form1.BtopBorder;

                    using (SolidBrush sb = new SolidBrush(bg))
                        g.FillRectangle(sb, blockRect);

                    using (Pen bp = new Pen(border, 1))
                        g.DrawRectangle(bp, blockRect);

                    if (touched)
                    {
                        using (SolidBrush checkBrush = new SolidBrush(Color.FromArgb(80, 250, 123)))
                        using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        {
                            g.DrawString("✓", blockFont, checkBrush, blockRect, sf);
                        }
                    }
                }
            }

            // 2. Draw Touch Drag Trail
            if (trailPoints.Count > 1)
            {
                using (Pen trailPen = new Pen(Color.FromArgb(180, 139, 233, 253), 4f))
                {
                    trailPen.StartCap = LineCap.Round;
                    trailPen.EndCap = LineCap.Round;
                    g.DrawLines(trailPen, trailPoints.ToArray());
                }

                Point lastPt = trailPoints[trailPoints.Count - 1];
                using (SolidBrush dotBrush = new SolidBrush(Color.FromArgb(220, 80, 250, 123)))
                using (Pen dotRing = new Pen(Color.White, 2f))
                {
                    g.FillEllipse(dotBrush, lastPt.X - 12, lastPt.Y - 12, 24, 24);
                    g.DrawEllipse(dotRing, lastPt.X - 12, lastPt.Y - 12, 24, 24);
                }
            }

            // 3. Draw Top Control & Telemetry Banner
            DrawTopBanner(g, w, topOffset);
        }

        private void DrawTopBanner(Graphics g, int w, int h)
        {
            Rectangle bannerRect = new Rectangle(0, 0, w, h);
            using (SolidBrush bannerBg = new SolidBrush(Color.FromArgb(240, 12, 16, 22)))
            using (Pen borderPen = new Pen(Form1.BtopCyan, 1.5f))
            {
                g.FillRectangle(bannerBg, bannerRect);
                g.DrawLine(borderPen, 0, h - 1, w, h - 1);
            }

            double pct = (touchedCount / (double)totalBlocks) * 100.0;
            string hardwareLabel = hasTouchHardware ? $"Touchscreen Online ({maxTouches} Touch Points)" : "No Touch Digitizer Flagged (Mouse/Stylus Emulation)";
            string statusStr = $"👆 TOUCHSCREEN & DIGITIZER VERIFICATION | {hardwareLabel}";
            string coverageStr = $"Coverage: {Math.Round(pct)}% ({touchedCount}/{totalBlocks} Zones) | [R] Reset | [Enter/F] Mark Pass | [ESC] Exit";

            Color titleColor = (pct >= 95) ? Color.FromArgb(80, 250, 123) : Form1.BtopCyan;
            using (SolidBrush titleBrush = new SolidBrush(titleColor))
            using (SolidBrush subBrush = new SolidBrush(Form1.BtopWhite))
            {
                g.DrawString(statusStr, hudFontBold, titleBrush, 20, 12);
                g.DrawString(coverageStr, hudFontRegular, subBrush, 20, 36);
            }

            // Draw Right-aligned Progress Bar
            int barW = 200;
            int barH = 18;
            int barX = w - barW - 30;
            int barY = 24;

            Rectangle barOutline = new Rectangle(barX, barY, barW, barH);
            int fillW = (int)Math.Round((pct / 100.0) * barW);

            using (SolidBrush emptyBrush = new SolidBrush(Color.FromArgb(30, 35, 45)))
            using (SolidBrush fillBrush = new SolidBrush((pct >= 95) ? Color.FromArgb(80, 250, 123) : Form1.BtopCyan))
            using (Pen barPen = new Pen(Form1.BtopBorder, 1))
            {
                g.FillRectangle(emptyBrush, barOutline);
                if (fillW > 0) g.FillRectangle(fillBrush, barX, barY, fillW, barH);
                g.DrawRectangle(barPen, barOutline);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { hudFontBold?.Dispose(); } catch { }
                try { hudFontRegular?.Dispose(); } catch { }
                try { blockFont?.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }
    }
}
