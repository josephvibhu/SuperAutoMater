using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SuperAutoMater
{
    /// <summary>
    /// Cyber-Industrial rolling telemetry sparkline graph with anti-aliasing,
    /// translucent gradient area fill, and live readout badge.
    /// </summary>
    public class TelemetrySparkline : Control
    {
        private readonly List<float> points = new List<float>();
        private int capacity = 60;
        private float minValue = 0f;
        private float maxValue = 100f;
        private Color lineColor = HudTheme.HudAccent;
        private Color areaTopColor = Color.FromArgb(80, 168, 85, 247);
        private Color areaBottomColor = Color.FromArgb(10, 168, 85, 247);
        private string unit = "%";
        private bool showCurrentBadge = true;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Capacity
        {
            get => capacity;
            set
            {
                capacity = Math.Max(10, value);
                while (points.Count > capacity) points.RemoveAt(0);
                Invalidate();
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public float MinValue
        {
            get => minValue;
            set { minValue = value; Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public float MaxValue
        {
            get => maxValue;
            set { maxValue = Math.Max(minValue + 0.1f, value); Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color LineColor
        {
            get => lineColor;
            set
            {
                lineColor = value;
                areaTopColor = Color.FromArgb(80, value.R, value.G, value.B);
                areaBottomColor = Color.FromArgb(10, value.R, value.G, value.B);
                Invalidate();
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Unit
        {
            get => unit;
            set { unit = value; Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool ShowCurrentBadge
        {
            get => showCurrentBadge;
            set { showCurrentBadge = value; Invalidate(); }
        }

        public float LatestValue => points.Count > 0 ? points[points.Count - 1] : 0f;

        public TelemetrySparkline()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.BackColor = Color.FromArgb(12, 7, 20);

            // Pre-seed with nominal zeroes
            for (int i = 0; i < 30; i++) points.Add(0f);
        }

        public void AddValue(float val)
        {
            points.Add(val);
            while (points.Count > capacity)
            {
                points.RemoveAt(0);
            }

            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(this.Invalidate));
            }
            else
            {
                this.Invalidate();
            }
        }

        public void Clear()
        {
            points.Clear();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = this.Width;
            int h = this.Height;
            if (w <= 4 || h <= 4) return;

            // Background fill
            using (SolidBrush bg = new SolidBrush(this.BackColor))
            {
                g.FillRectangle(bg, 0, 0, w, h);
            }

            // Hairline frame
            g.DrawRectangle(HudTheme.PenBezel, 0, 0, w - 1, h - 1);

            // Faint horizontal grid lines (25%, 50%, 75%)
            using (Pen gridPen = new Pen(Color.FromArgb(25, HudTheme.Bezel.R, HudTheme.Bezel.G, HudTheme.Bezel.B), 1f))
            {
                g.DrawLine(gridPen, 2, h / 4, w - 3, h / 4);
                g.DrawLine(gridPen, 2, h / 2, w - 3, h / 2);
                g.DrawLine(gridPen, 2, (h * 3) / 4, w - 3, (h * 3) / 4);
            }

            if (points.Count < 2) return;

            // Map points to canvas coordinates
            PointF[] screenPoints = new PointF[points.Count];
            float xStep = (float)(w - 4) / Math.Max(1, capacity - 1);
            float range = Math.Max(0.001f, maxValue - minValue);

            int startIndex = capacity - points.Count;
            for (int i = 0; i < points.Count; i++)
            {
                float x = 2 + (startIndex + i) * xStep;
                float normalized = Math.Clamp((points[i] - minValue) / range, 0f, 1f);
                float y = (h - 4) - (normalized * (h - 8));
                screenPoints[i] = new PointF(x, y);
            }

            // Fill gradient area under the curve
            using (GraphicsPath areaPath = new GraphicsPath())
            {
                areaPath.AddLine(screenPoints[0].X, h - 2, screenPoints[0].X, screenPoints[0].Y);
                for (int i = 1; i < screenPoints.Length; i++)
                {
                    areaPath.AddLine(screenPoints[i - 1], screenPoints[i]);
                }
                areaPath.AddLine(screenPoints[screenPoints.Length - 1].X, screenPoints[screenPoints.Length - 1].Y, screenPoints[screenPoints.Length - 1].X, h - 2);
                areaPath.CloseFigure();

                using (LinearGradientBrush fill = new LinearGradientBrush(new Rectangle(0, 0, w, h), areaTopColor, areaBottomColor, 90f))
                {
                    g.FillPath(fill, areaPath);
                }
            }

            // Draw line on top
            using (Pen sparkPen = new Pen(lineColor, 1.8f))
            {
                sparkPen.LineJoin = LineJoin.Round;
                for (int i = 1; i < screenPoints.Length; i++)
                {
                    g.DrawLine(sparkPen, screenPoints[i - 1], screenPoints[i]);
                }
            }

            // Draw current value badge in top right
            if (showCurrentBadge && points.Count > 0)
            {
                float cur = LatestValue;
                string badgeText = $"{cur:0.0}{unit}";
                Font badgeFont = ScalingService.Instance.GetFont(HudFontRole.Badge);
                SizeF bSize = g.MeasureString(badgeText, badgeFont);

                Rectangle badgeRect = new Rectangle(w - (int)bSize.Width - 10, 4, (int)bSize.Width + 6, (int)bSize.Height + 2);
                HudTheme.DrawPillBadge(g, badgeRect, badgeText, badgeFont, Color.FromArgb(200, 20, 10, 36), lineColor, lineColor);
            }
        }
    }
}
