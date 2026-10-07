using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SuperAutoMater.Wpf.Controls
{
    public partial class RadialMeter : UserControl
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register("Value", typeof(double), typeof(RadialMeter), new PropertyMetadata(96.0, OnValueChanged));

        public static readonly DependencyProperty SubtitleProperty =
            DependencyProperty.Register("Subtitle", typeof(string), typeof(RadialMeter), new PropertyMetadata("NOMINAL", OnSubtitleChanged));

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public string Subtitle
        {
            get => (string)GetValue(SubtitleProperty);
            set => SetValue(SubtitleProperty, value);
        }

        public RadialMeter()
        {
            InitializeComponent();
            Loaded += (s, e) => RedrawArcs();
            SizeChanged += (s, e) => RedrawArcs();
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is RadialMeter meter)
            {
                meter.TxtPercent.Text = $"{(int)meter.Value}%";
                meter.RedrawArcs();
            }
        }

        private static void OnSubtitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is RadialMeter meter)
            {
                meter.TxtSubtitle.Text = meter.Subtitle;
            }
        }

        public void RedrawArcs()
        {
            double cx = 60, cy = 48, r = 40;
            double startAngle = 145.0;
            double sweepRange = 250.0;

            // 1. Draw track
            PathTrack.Data = CreateArcGeometry(cx, cy, r, startAngle, startAngle + sweepRange);

            // 2. Draw progress
            double val = Math.Clamp(Value, 0.0, 100.0);
            double progressSweep = (val / 100.0) * sweepRange;
            if (progressSweep < 1.0) progressSweep = 1.0;

            PathProgress.Data = CreateArcGeometry(cx, cy, r, startAngle, startAngle + progressSweep);

            // Color status
            if (val >= 80)
            {
                PathProgress.Stroke = (Brush)FindResource("BrushAccentEmeraldLight");
                TxtPercent.Foreground = (Brush)FindResource("BrushTextEmerald");
            }
            else if (val >= 50)
            {
                PathProgress.Stroke = (Brush)FindResource("BrushAccentAmber");
                TxtPercent.Foreground = (Brush)FindResource("BrushTextAmber");
            }
            else
            {
                PathProgress.Stroke = (Brush)FindResource("BrushAccentRed");
                TxtPercent.Foreground = (Brush)FindResource("BrushTextRed");
            }
        }

        private static Geometry CreateArcGeometry(double cx, double cy, double r, double startDeg, double endDeg)
        {
            double startRad = (Math.PI / 180) * startDeg;
            double endRad = (Math.PI / 180) * endDeg;

            Point pStart = new Point(cx + r * Math.Cos(startRad), cy + r * Math.Sin(startRad));
            Point pEnd = new Point(cx + r * Math.Cos(endRad), cy + r * Math.Sin(endRad));

            bool isLargeArc = Math.Abs(endDeg - startDeg) > 180.0;

            PathFigure figure = new PathFigure
            {
                StartPoint = pStart,
                IsClosed = false
            };
            figure.Segments.Add(new ArcSegment(pEnd, new Size(r, r), 0, isLargeArc, SweepDirection.Clockwise, true));

            PathGeometry geom = new PathGeometry();
            geom.Figures.Add(figure);
            return geom;
        }
    }
}
