using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace SuperAutoMater
{
    public class AdvancedDisplayTestForm : Form
    {
        private enum PatternStage
        {
            SolidWhite = 0,
            SolidBlack = 1,
            SolidRed = 2,
            SolidGreen = 3,
            SolidBlue = 4,
            SolidMagenta = 5,
            SolidCyan = 6,
            SolidYellow = 7,
            BacklightBleed = 8,
            GrayscaleGamma = 9,
            ColorRamps = 10,
            GeometryGrid = 11,
            TypographySharpness = 12,
            MotionGhosting = 13
        }

        private PatternStage currentStage = PatternStage.SolidWhite;
        private readonly int totalStages = 14;

        private bool showHud = true;
        private System.Windows.Forms.Timer hudTimer;
        private System.Windows.Forms.Timer motionTimer;
        private float motionBarX = 0f;
        private int frameCount = 0;
        private int currentFps = 60;
        private DateTime lastFpsTime = DateTime.Now;

        private readonly Font hudFontBold = new Font("Consolas", 10.5F, FontStyle.Bold);
        private readonly Font hudFontRegular = new Font("Consolas", 9F, FontStyle.Regular);

        public AdvancedDisplayTestForm()
        {
            this.WindowState = FormWindowState.Maximized;
            this.FormBorderStyle = FormBorderStyle.None;
            this.TopMost = true;
            this.DoubleBuffered = true;
            this.Cursor = Cursors.Hand;
            this.BackColor = Color.White;

            hudTimer = new System.Windows.Forms.Timer { Interval = 3500 };
            hudTimer.Tick += (s, e) =>
            {
                showHud = false;
                hudTimer.Stop();
                this.Invalidate();
            };
            hudTimer.Start();

            motionTimer = new System.Windows.Forms.Timer { Interval = 16 }; // ~60fps+
            motionTimer.Tick += (s, e) =>
            {
                if (currentStage == PatternStage.MotionGhosting)
                {
                    motionBarX += 14f;
                    if (motionBarX > this.Width + 100) motionBarX = -100;

                    frameCount++;
                    if ((DateTime.Now - lastFpsTime).TotalMilliseconds >= 1000)
                    {
                        currentFps = frameCount;
                        frameCount = 0;
                        lastFpsTime = DateTime.Now;
                    }
                    this.Invalidate();
                }
            };
            motionTimer.Start();

            this.Click += (s, e) => NextPattern();
            this.KeyDown += OnKeyDown;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            showHud = true;
            hudTimer.Stop();
            hudTimer.Start();

            if (e.KeyCode == Keys.Escape)
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            }
            else if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Right || e.KeyCode == Keys.PageDown)
            {
                NextPattern();
            }
            else if (e.KeyCode == Keys.Left || e.KeyCode == Keys.PageUp)
            {
                PrevPattern();
            }
            else if (e.KeyCode == Keys.H)
            {
                showHud = !showHud;
                this.Invalidate();
            }
            else if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.F || e.KeyCode == Keys.P)
            {
                Form1.Instance?.MarkTestComplete("Display");
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            else if (e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D7)
            {
                int num = e.KeyCode - Keys.D1;
                switch (num)
                {
                    case 0: currentStage = PatternStage.SolidWhite; break;
                    case 1: currentStage = PatternStage.BacklightBleed; break;
                    case 2: currentStage = PatternStage.GrayscaleGamma; break;
                    case 3: currentStage = PatternStage.ColorRamps; break;
                    case 4: currentStage = PatternStage.GeometryGrid; break;
                    case 5: currentStage = PatternStage.TypographySharpness; break;
                    case 6: currentStage = PatternStage.MotionGhosting; break;
                }
                this.Invalidate();
            }
        }

        private void NextPattern()
        {
            showHud = true;
            hudTimer.Stop();
            hudTimer.Start();

            int next = (int)currentStage + 1;
            if (next >= totalStages)
            {
                Form1.Instance?.MarkTestComplete("Display");
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            else
            {
                currentStage = (PatternStage)next;
                this.Invalidate();
            }
        }

        private void PrevPattern()
        {
            showHud = true;
            hudTimer.Stop();
            hudTimer.Start();

            int prev = (int)currentStage - 1;
            if (prev < 0) prev = totalStages - 1;
            currentStage = (PatternStage)prev;
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int w = this.Width;
            int h = this.Height;

            switch (currentStage)
            {
                case PatternStage.SolidWhite: g.Clear(Color.White); break;
                case PatternStage.SolidBlack: g.Clear(Color.Black); break;
                case PatternStage.SolidRed: g.Clear(Color.FromArgb(255, 0, 0)); break;
                case PatternStage.SolidGreen: g.Clear(Color.FromArgb(0, 255, 0)); break;
                case PatternStage.SolidBlue: g.Clear(Color.FromArgb(0, 0, 255)); break;
                case PatternStage.SolidMagenta: g.Clear(Color.FromArgb(255, 0, 255)); break;
                case PatternStage.SolidCyan: g.Clear(Color.FromArgb(0, 255, 255)); break;
                case PatternStage.SolidYellow: g.Clear(Color.FromArgb(255, 255, 0)); break;

                case PatternStage.BacklightBleed:
                    DrawBacklightBleedPattern(g, w, h);
                    break;

                case PatternStage.GrayscaleGamma:
                    DrawGrayscaleGammaPattern(g, w, h);
                    break;

                case PatternStage.ColorRamps:
                    DrawColorRampsPattern(g, w, h);
                    break;

                case PatternStage.GeometryGrid:
                    DrawGeometryGridPattern(g, w, h);
                    break;

                case PatternStage.TypographySharpness:
                    DrawTypographyPattern(g, w, h);
                    break;

                case PatternStage.MotionGhosting:
                    DrawMotionGhostingPattern(g, w, h);
                    break;
            }

            if (showHud)
            {
                DrawHud(g, w, h);
            }
        }

        private void DrawBacklightBleedPattern(Graphics g, int w, int h)
        {
            g.Clear(Color.Black);

            // Subtle 1px reference marks in corners & center crosshair to inspect bezel frame bleed
            using (Pen faintPen = new Pen(Color.FromArgb(30, 255, 255, 255), 1))
            {
                g.DrawRectangle(faintPen, 20, 20, w - 40, h - 40);
                g.DrawLine(faintPen, w / 2 - 20, h / 2, w / 2 + 20, h / 2);
                g.DrawLine(faintPen, w / 2, h / 2 - 20, w / 2, h / 2 + 20);
            }
        }

        private void DrawGrayscaleGammaPattern(Graphics g, int w, int h)
        {
            g.Clear(Color.FromArgb(20, 20, 20));

            int pad = 40;
            int sectionH = (h - pad * 3) / 2;

            // 1. Continuous 0-255 Smooth Black-to-White Ramp
            Rectangle gradRect = new Rectangle(pad, pad, w - pad * 2, sectionH);
            using (LinearGradientBrush lgb = new LinearGradientBrush(gradRect, Color.Black, Color.White, LinearGradientMode.Horizontal))
            {
                g.FillRectangle(lgb, gradRect);
            }
            using (Pen p = new Pen(Color.Gray, 1)) g.DrawRectangle(p, gradRect);

            // 2. Discrete 16-Step Grayscale Step Blocks (0%, 6.25%, 12.5% ... 100%)
            int steps = 16;
            int stepY = pad * 2 + sectionH;
            int blockW = (w - pad * 2) / steps;

            for (int i = 0; i < steps; i++)
            {
                int val = (int)Math.Round((i / (double)(steps - 1)) * 255.0);
                Color stepColor = Color.FromArgb(val, val, val);
                Rectangle blockRect = new Rectangle(pad + i * blockW, stepY, blockW, sectionH);

                using (SolidBrush sb = new SolidBrush(stepColor))
                    g.FillRectangle(sb, blockRect);

                using (Pen bp = new Pen(Color.FromArgb(50, 50, 50), 1))
                    g.DrawRectangle(bp, blockRect);

                // Label stepping percentage
                string pctStr = $"{Math.Round((i / (double)(steps - 1)) * 100)}%";
                Color textColor = val > 128 ? Color.Black : Color.White;
                using (SolidBrush textBrush = new SolidBrush(textColor))
                using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    g.DrawString(pctStr, hudFontRegular, textBrush, blockRect, sf);
                }
            }
        }

        private void DrawColorRampsPattern(Graphics g, int w, int h)
        {
            g.Clear(Color.FromArgb(10, 12, 16));

            int pad = 40;
            int barCount = 4;
            int barH = (h - pad * (barCount + 1)) / barCount;

            // Red Ramp
            DrawColorBar(g, pad, pad + 0 * (barH + pad), w - pad * 2, barH, Color.Black, Color.Red, "RED CHANNEL DYNAMIC RANGE & QUANTIZATION (0-255)");

            // Green Ramp
            DrawColorBar(g, pad, pad + 1 * (barH + pad), w - pad * 2, barH, Color.Black, Color.Lime, "GREEN CHANNEL DYNAMIC RANGE & QUANTIZATION (0-255)");

            // Blue Ramp
            DrawColorBar(g, pad, pad + 2 * (barH + pad), w - pad * 2, barH, Color.Black, Color.Blue, "BLUE CHANNEL DYNAMIC RANGE & QUANTIZATION (0-255)");

            // Spectrum Rainbow Ramp
            Rectangle specRect = new Rectangle(pad, pad + 3 * (barH + pad), w - pad * 2, barH);
            using (LinearGradientBrush lgb = new LinearGradientBrush(specRect, Color.Red, Color.Blue, LinearGradientMode.Horizontal))
            {
                ColorBlend cb = new ColorBlend(7);
                cb.Colors = new Color[] { Color.Red, Color.Orange, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue, Color.Magenta };
                cb.Positions = new float[] { 0.0f, 0.17f, 0.33f, 0.5f, 0.67f, 0.83f, 1.0f };
                lgb.InterpolationColors = cb;
                g.FillRectangle(lgb, specRect);
            }
            using (Pen p = new Pen(Color.Gray, 1)) g.DrawRectangle(p, specRect);
            using (SolidBrush tb = new SolidBrush(Color.White))
                g.DrawString("FULL SPECTRUM COLOR DITHERING & BANDING (360° HUE)", hudFontBold, tb, pad + 8, pad + 3 * (barH + pad) + 8);
        }

        private void DrawColorBar(Graphics g, int x, int y, int w, int h, Color from, Color to, string label)
        {
            Rectangle rect = new Rectangle(x, y, w, h);
            using (LinearGradientBrush lgb = new LinearGradientBrush(rect, from, to, LinearGradientMode.Horizontal))
            {
                g.FillRectangle(lgb, rect);
            }
            using (Pen p = new Pen(Color.Gray, 1)) g.DrawRectangle(p, rect);
            using (SolidBrush tb = new SolidBrush(Color.White))
                g.DrawString(label, hudFontBold, tb, x + 8, y + 8);
        }

        private void DrawGeometryGridPattern(Graphics g, int w, int h)
        {
            g.Clear(Color.Black);

            int gridSize = 50;

            // 1. Grid Lines
            using (Pen gridPen = new Pen(Color.FromArgb(80, 80, 80), 1))
            {
                for (int x = 0; x < w; x += gridSize)
                    g.DrawLine(gridPen, x, 0, x, h);
                for (int y = 0; y < h; y += gridSize)
                    g.DrawLine(gridPen, 0, y, w, y);
            }

            // 2. Outer 1px Perimeter Boundary Box (Overscan check)
            using (Pen outerPen = new Pen(Color.FromArgb(255, 80, 80), 2))
            {
                g.DrawRectangle(outerPen, 1, 1, w - 2, h - 2);
            }

            // 3. Center Concentric Circles (Aspect Ratio / Stretching check)
            int cx = w / 2;
            int cy = h / 2;
            int[] circleRadii = { 100, 200, 300, 400 };

            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen circlePen = new Pen(Color.Cyan, 2))
            {
                foreach (int r in circleRadii)
                {
                    if (r * 2 < Math.Min(w, h))
                        g.DrawEllipse(circlePen, cx - r, cy - r, r * 2, r * 2);
                }
            }

            // 4. Center Crosshairs
            using (Pen crossPen = new Pen(Color.Yellow, 2))
            {
                g.DrawLine(crossPen, cx - 40, cy, cx + 40, cy);
                g.DrawLine(crossPen, cx, cy - 40, cx, cy + 40);
            }
        }

        private void DrawTypographyPattern(Graphics g, int w, int h)
        {
            g.Clear(Color.FromArgb(15, 18, 24));
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            int midX = w / 2;

            // Left Side: Dark on Light
            Rectangle leftRect = new Rectangle(20, 20, midX - 30, h - 40);
            using (SolidBrush whiteBrush = new SolidBrush(Color.White))
                g.FillRectangle(whiteBrush, leftRect);

            // Right Side: Light on Dark
            Rectangle rightRect = new Rectangle(midX + 10, 20, midX - 30, h - 40);
            using (SolidBrush darkBrush = new SolidBrush(Color.FromArgb(12, 14, 18)))
                g.FillRectangle(darkBrush, rightRect);

            float[] fontSizes = { 6.5f, 8f, 9.5f, 11f, 13f, 16f, 20f, 26f };

            // Render on Left
            int leftY = 40;
            using (SolidBrush b = new SolidBrush(Color.Black))
            {
                g.DrawString("CLEAR-TYPE SUBPIXEL TEXT TEST (STANDARD CONTRAST)", hudFontBold, b, 40, leftY);
                leftY += 35;
                foreach (float sz in fontSizes)
                {
                    using (Font f = new Font("Segoe UI", sz, FontStyle.Regular))
                    {
                        g.DrawString($"[{sz}pt] The quick brown fox jumps over the lazy dog 1234567890", f, b, 40, leftY);
                        leftY += (int)(sz * 2.2f) + 6;
                    }
                }
            }

            // Render on Right
            int rightY = 40;
            using (SolidBrush wb = new SolidBrush(Color.White))
            {
                g.DrawString("CLEAR-TYPE SUBPIXEL TEXT TEST (INVERTED CONTRAST)", hudFontBold, wb, midX + 30, rightY);
                rightY += 35;
                foreach (float sz in fontSizes)
                {
                    using (Font f = new Font("Segoe UI", sz, FontStyle.Regular))
                    {
                        g.DrawString($"[{sz}pt] The quick brown fox jumps over the lazy dog 1234567890", f, wb, midX + 30, rightY);
                        rightY += (int)(sz * 2.2f) + 6;
                    }
                }
            }
        }

        private void DrawMotionGhostingPattern(Graphics g, int w, int h)
        {
            g.Clear(Color.FromArgb(8, 10, 14));

            int barWidth = 60;
            int barHeight = 220;
            int y = h / 2 - barHeight / 2;

            // Multiple motion pursuit tracks
            Rectangle block1 = new Rectangle((int)motionBarX, y - 140, barWidth, 100);
            Rectangle block2 = new Rectangle((int)motionBarX, y, barWidth, 100);
            Rectangle block3 = new Rectangle((int)motionBarX, y + 140, barWidth, 100);

            using (SolidBrush b1 = new SolidBrush(Color.White))
            using (SolidBrush b2 = new SolidBrush(Color.Cyan))
            using (SolidBrush b3 = new SolidBrush(Color.FromArgb(255, 85, 85)))
            {
                g.FillRectangle(b1, block1);
                g.FillRectangle(b2, block2);
                g.FillRectangle(b3, block3);
            }

            // Draw stationary vertical sync markers
            using (Pen p = new Pen(Color.FromArgb(60, 60, 60), 2))
            {
                for (int x = 100; x < w; x += 150)
                {
                    g.DrawLine(p, x, 0, x, h);
                }
            }
        }

        private void DrawHud(Graphics g, int w, int h)
        {
            int hudW = 740;
            int hudH = 75;
            int hudX = 24;
            int hudY = h - hudH - 24;

            Rectangle hudRect = new Rectangle(hudX, hudY, hudW, hudH);

            using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(220, 10, 14, 20)))
            using (Pen borderPen = new Pen(Form1.BtopCyan, 1.5f))
            {
                g.FillRectangle(bgBrush, hudRect);
                g.DrawRectangle(borderPen, hudRect);
            }

            string stageName = GetStageTitle(currentStage);
            string stageDesc = GetStageDescription(currentStage);
            string techInfo = $"Res: {w}x{h} | FPS: {currentFps} | Pattern [{(int)currentStage + 1}/{totalStages}]";

            using (SolidBrush titleBrush = new SolidBrush(Form1.BtopCyan))
            using (SolidBrush descBrush = new SolidBrush(Form1.BtopWhite))
            using (SolidBrush keyBrush = new SolidBrush(Form1.BtopAmber))
            {
                g.DrawString($"TEST: {stageName}  ({techInfo})", hudFontBold, titleBrush, hudX + 12, hudY + 8);
                g.DrawString($"FOCUS: {stageDesc}", hudFontRegular, descBrush, hudX + 12, hudY + 28);
                g.DrawString($"[Space/Click] Next | [Left/Right] Nav | [1-7] Jump | [H] HUD | [Enter/F] Pass | [ESC] Exit", hudFontRegular, keyBrush, hudX + 12, hudY + 48);
            }
        }

        private string GetStageTitle(PatternStage s)
        {
            switch (s)
            {
                case PatternStage.SolidWhite: return "Dead Pixel Isolation (Pure White)";
                case PatternStage.SolidBlack: return "Stuck Pixel Isolation (Pure Black)";
                case PatternStage.SolidRed: return "Red Subpixel Inspection";
                case PatternStage.SolidGreen: return "Green Subpixel Inspection";
                case PatternStage.SolidBlue: return "Blue Subpixel Inspection";
                case PatternStage.SolidMagenta: return "Magenta Subpixel Alignment";
                case PatternStage.SolidCyan: return "Cyan Subpixel Alignment";
                case PatternStage.SolidYellow: return "Yellow Subpixel Alignment";
                case PatternStage.BacklightBleed: return "Backlight Bleed & IPS Glow Uniformity";
                case PatternStage.GrayscaleGamma: return "256-Level Grayscale & Gamma Ramp";
                case PatternStage.ColorRamps: return "RGB Color Banding & Dynamic Range Ramps";
                case PatternStage.GeometryGrid: return "Convergence, 1:1 Pixel Grid & Aspect Ratio";
                case PatternStage.TypographySharpness: return "Typography & Subpixel ClearType Sharpness";
                case PatternStage.MotionGhosting: return "Motion Ghosting & Refresh Rate Frame Pacing";
                default: return "Display Inspection";
            }
        }

        private string GetStageDescription(PatternStage s)
        {
            switch (s)
            {
                case PatternStage.SolidWhite: return "Inspect for dark dead pixels, dust under glass, and panel pressure spots.";
                case PatternStage.SolidBlack: return "Inspect for stuck (always lit) colored subpixels and OLED panel defects.";
                case PatternStage.SolidRed:
                case PatternStage.SolidGreen:
                case PatternStage.SolidBlue: return "Isolates individual primary color subpixels to detect weak or defective color emitters.";
                case PatternStage.BacklightBleed: return "Look for bright light leaks around edges/corners from frame pinching or uneven LEDs.";
                case PatternStage.GrayscaleGamma: return "Verify all 16 discrete blocks are distinctly visible (no crushed blacks or clipped whites).";
                case PatternStage.ColorRamps: return "Check for smooth gradients without harsh banding lines (tests 6-bit vs 8-bit/10-bit dithering).";
                case PatternStage.GeometryGrid: return "Circles must be perfectly round (no aspect stretching) and outer red border fully visible.";
                case PatternStage.TypographySharpness: return "Inspect text edges for colored halos (ClearType subpixel fringing) and scaling blur.";
                case PatternStage.MotionGhosting: return "Inspect the moving bar for trailing ghost lines, tearing, and response time lag.";
                default: return "";
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { hudTimer?.Stop(); hudTimer?.Dispose(); } catch { }
                try { motionTimer?.Stop(); motionTimer?.Dispose(); } catch { }
                try { hudFontBold?.Dispose(); } catch { }
                try { hudFontRegular?.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }
    }
}
