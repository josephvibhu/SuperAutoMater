using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SuperAutoMater
{
    /// <summary>
    /// Circular vector arc meter for Battery Health, QC Test Progress, or SMART wear.
    /// Features rounded end caps, smooth anti-aliasing, and center telemetry readout.
    /// </summary>
    public class RadialMeter : Control
    {
        private float value = 0f;
        private float minimum = 0f;
        private float maximum = 100f;
        private float startAngle = 135f;
        private float sweepAngle = 270f;
        private float trackWidth = 8f;
        private Color meterColor = HudTheme.PassNominal;
        private Color trackColor = Color.FromArgb(30, 16, 48);
        private string titleText = "HEALTH";
        private string unit = "%";

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public float Value
        {
            get => value;
            set
            {
                this.value = Math.Clamp(value, minimum, maximum);
                Invalidate();
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public float Minimum
        {
            get => minimum;
            set { minimum = value; Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public float Maximum
        {
            get => maximum;
            set { maximum = Math.Max(minimum + 1f, value); Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public float TrackWidth
        {
            get => trackWidth;
            set { trackWidth = Math.Max(2f, value); Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color MeterColor
        {
            get => meterColor;
            set { meterColor = value; Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color TrackColor
        {
            get => trackColor;
            set { trackColor = value; Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string TitleText
        {
            get => titleText;
            set { titleText = value; Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Unit
        {
            get => unit;
            set { unit = value; Invalidate(); }
        }

        public RadialMeter()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.Size = new Size(110, 110);
            this.BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int pad = (int)Math.Ceiling(trackWidth / 2f) + 4;
            int size = Math.Min(this.Width - (pad * 2), this.Height - (pad * 2));
            if (size <= 10) return;

            int x = (this.Width - size) / 2;
            int y = (this.Height - size) / 2;
            Rectangle arcBounds = new Rectangle(x, y, size, size);

            // 1. Draw Background Track Arc
            using (Pen trackPen = new Pen(trackColor, trackWidth))
            {
                trackPen.StartCap = LineCap.Round;
                trackPen.EndCap = LineCap.Round;
                g.DrawArc(trackPen, arcBounds, startAngle, sweepAngle);
            }

            // 2. Draw Active Arc
            float range = Math.Max(0.001f, maximum - minimum);
            float progress = Math.Clamp((value - minimum) / range, 0f, 1f);
            float activeSweep = progress * sweepAngle;

            if (activeSweep > 1f)
            {
                using (Pen meterPen = new Pen(meterColor, trackWidth))
                {
                    meterPen.StartCap = LineCap.Round;
                    meterPen.EndCap = LineCap.Round;
                    g.DrawArc(meterPen, arcBounds, startAngle, activeSweep);
                }
            }

            // 3. Center Numeric Readout
            Font valFont = ScalingService.Instance.GetFont(HudFontRole.Title);
            Font subFont = ScalingService.Instance.GetFont(HudFontRole.Badge);

            string readout = $"{value:0}{unit}";
            using (SolidBrush textBrush = new SolidBrush(HudTheme.TextBright))
            using (SolidBrush subBrush = new SolidBrush(HudTheme.TextDim))
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                Rectangle valRect = new Rectangle(0, (this.Height / 2) - 16, this.Width, 20);
                Rectangle subRect = new Rectangle(0, (this.Height / 2) + 4, this.Width, 14);

                g.DrawString(readout, valFont, textBrush, valRect, sf);
                if (!string.IsNullOrEmpty(titleText))
                {
                    g.DrawString(titleText.ToUpperInvariant(), subFont, subBrush, subRect, sf);
                }
            }
        }
    }
}
