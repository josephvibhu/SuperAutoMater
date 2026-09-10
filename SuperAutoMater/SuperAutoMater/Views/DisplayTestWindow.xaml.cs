using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using SuperAutoMater.Wpf.Services;

namespace SuperAutoMater.Wpf.Views
{
    public partial class DisplayTestWindow : Window
    {
        private enum DisplayStage
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

        private DisplayStage _currentStage = DisplayStage.SolidWhite;
        private const int TOTAL_STAGES = 14;

        private DispatcherTimer _hudTimer;
        private DispatcherTimer _motionTimer;
        private double _motionBarX = 0;
        private Rectangle _motionRect;
        public bool TestPassed { get; private set; } = false;

        public DisplayTestWindow()
        {
            InitializeComponent();

            Loaded += DisplayTestWindow_Loaded;
            Closed += DisplayTestWindow_Closed;

            _hudTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
            _hudTimer.Tick += (s, e) =>
            {
                HudPanel.Visibility = Visibility.Collapsed;
                _hudTimer.Stop();
            };

            _motionTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _motionTimer.Tick += MotionTimer_Tick;
        }

        private void DisplayTestWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (HardwareDiagnosticsService.Instance.HasTouchscreen)
            {
                BtnLaunchTouch.Visibility = Visibility.Visible;
            }
            else
            {
                BtnLaunchTouch.Visibility = Visibility.Visible; // Technician can still launch touch test
            }

            DisplayTimingService.Instance.StartMeasurement();
            DisplayTimingService.Instance.TimingUpdated += OnDisplayTimingUpdated;

            RenderStage(_currentStage);
            ShowHud();
        }

        private void DisplayTestWindow_Closed(object sender, EventArgs e)
        {
            DisplayTimingService.Instance.TimingUpdated -= OnDisplayTimingUpdated;
            DisplayTimingService.Instance.StopMeasurement();
            _hudTimer?.Stop();
            _motionTimer?.Stop();
        }

        private void OnDisplayTimingUpdated(DisplayTimingStats stats)
        {
            Dispatcher.InvokeAsync(() =>
            {
                TxtRefreshRate.Text = $"{stats.MeasuredHz:F1} Hz PANEL";
                TxtRefreshRate.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(stats.AccentHex));
                TxtTimingDetails.Text = $"{stats.ResolutionWidth}x{stats.ResolutionHeight} · {stats.JitterMs:F2}ms Jitter · {stats.DroppedFrames} Drops";
            }, DispatcherPriority.Render);
        }

        private void ShowHud()
        {
            HudPanel.Visibility = Visibility.Visible;
            _hudTimer?.Stop();
            _hudTimer?.Start();
        }

        private void RenderStage(DisplayStage stage)
        {
            _currentStage = stage;
            int idx = (int)stage + 1;
            TxtStageIndex.Text = $"STAGE {idx}/{TOTAL_STAGES}";

            DisplayCanvas.Children.Clear();
            _motionTimer.Stop();

            double w = ActualWidth > 100 ? ActualWidth : 1920;
            double h = ActualHeight > 100 ? ActualHeight : 1080;

            switch (stage)
            {
                case DisplayStage.SolidWhite:
                    Background = Brushes.White;
                    TxtStageTitle.Text = "SOLID WHITE (UNIFORMITY & DEAD PIXELS)";
                    break;

                case DisplayStage.SolidBlack:
                    Background = Brushes.Black;
                    TxtStageTitle.Text = "SOLID BLACK (BRIGHT STUCK PIXELS)";
                    break;

                case DisplayStage.SolidRed:
                    Background = Brushes.Red;
                    TxtStageTitle.Text = "PRIMARY RED SUBPIXEL CERTIFICATION";
                    break;

                case DisplayStage.SolidGreen:
                    Background = Brushes.Lime;
                    TxtStageTitle.Text = "PRIMARY GREEN SUBPIXEL CERTIFICATION";
                    break;

                case DisplayStage.SolidBlue:
                    Background = Brushes.Blue;
                    TxtStageTitle.Text = "PRIMARY BLUE SUBPIXEL CERTIFICATION";
                    break;

                case DisplayStage.SolidMagenta:
                    Background = Brushes.Magenta;
                    TxtStageTitle.Text = "SECONDARY MAGENTA UNIFORMITY";
                    break;

                case DisplayStage.SolidCyan:
                    Background = Brushes.Cyan;
                    TxtStageTitle.Text = "SECONDARY CYAN UNIFORMITY";
                    break;

                case DisplayStage.SolidYellow:
                    Background = Brushes.Yellow;
                    TxtStageTitle.Text = "SECONDARY YELLOW UNIFORMITY";
                    break;

                case DisplayStage.BacklightBleed:
                    Background = Brushes.Black;
                    TxtStageTitle.Text = "BACKLIGHT BLEED & BEZEL PINCH (DARKROOM TEST)";
                    DrawBacklightBleed(w, h);
                    break;

                case DisplayStage.GrayscaleGamma:
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#141414"));
                    TxtStageTitle.Text = "GRAYSCALE GAMMA RAMP & STEPPING QUANTIZATION";
                    DrawGrayscaleGamma(w, h);
                    break;

                case DisplayStage.ColorRamps:
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0A0C10"));
                    TxtStageTitle.Text = "RGB & 360° SPECTRUM COLOR RAMPS & DITHERING";
                    DrawColorRamps(w, h);
                    break;

                case DisplayStage.GeometryGrid:
                    Background = Brushes.Black;
                    TxtStageTitle.Text = "GEOMETRY GRID, OVERSCAN & ASPECT RATIO CIRCLES";
                    DrawGeometryGrid(w, h);
                    break;

                case DisplayStage.TypographySharpness:
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0E1117"));
                    TxtStageTitle.Text = "TYPOGRAPHY SHARPNESS & CLEARTYPE SUBPIXEL TEST";
                    DrawTypographySharpness(w, h);
                    break;

                case DisplayStage.MotionGhosting:
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#161B22"));
                    TxtStageTitle.Text = "MOTION GHOSTING & PANEL REFRESH RESPONSE TIME";
                    StartMotionGhosting(w, h);
                    break;
            }
        }

        private void DrawBacklightBleed(double w, double h)
        {
            var faintBorder = new Rectangle
            {
                Width = w - 40,
                Height = h - 40,
                Stroke = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                StrokeThickness = 1
            };
            Canvas.SetLeft(faintBorder, 20);
            Canvas.SetTop(faintBorder, 20);
            DisplayCanvas.Children.Add(faintBorder);

            var crossH = new Line
            {
                X1 = w / 2 - 30, Y1 = h / 2,
                X2 = w / 2 + 30, Y2 = h / 2,
                Stroke = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                StrokeThickness = 1
            };
            var crossV = new Line
            {
                X1 = w / 2, Y1 = h / 2 - 30,
                X2 = w / 2, Y2 = h / 2 + 30,
                Stroke = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                StrokeThickness = 1
            };
            DisplayCanvas.Children.Add(crossH);
            DisplayCanvas.Children.Add(crossV);
        }

        private void DrawGrayscaleGamma(double w, double h)
        {
            double pad = 50;
            double sectionH = (h - pad * 3.5) / 2.0;
            double sectionW = w - pad * 2;

            // 1. Continuous Smooth Ramp
            var gradBrush = new LinearGradientBrush(Colors.Black, Colors.White, 0);
            var smoothRect = new Rectangle
            {
                Width = sectionW,
                Height = sectionH,
                Fill = gradBrush,
                Stroke = Brushes.Gray,
                StrokeThickness = 1
            };
            Canvas.SetLeft(smoothRect, pad);
            Canvas.SetTop(smoothRect, pad);
            DisplayCanvas.Children.Add(smoothRect);

            // 2. Discrete 16-Step Blocks
            int steps = 16;
            double blockW = sectionW / steps;
            double stepY = pad * 2 + sectionH;

            for (int i = 0; i < steps; i++)
            {
                byte val = (byte)Math.Round((i / (double)(steps - 1)) * 255.0);
                var block = new Rectangle
                {
                    Width = blockW,
                    Height = sectionH,
                    Fill = new SolidColorBrush(Color.FromRgb(val, val, val)),
                    Stroke = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                    StrokeThickness = 1
                };
                Canvas.SetLeft(block, pad + i * blockW);
                Canvas.SetTop(block, stepY);
                DisplayCanvas.Children.Add(block);

                int pct = (int)Math.Round((i / (double)(steps - 1)) * 100.0);
                var lbl = new TextBlock
                {
                    Text = $"{pct}%",
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = val > 128 ? Brushes.Black : Brushes.White
                };
                Canvas.SetLeft(lbl, pad + i * blockW + (blockW / 2) - 12);
                Canvas.SetTop(lbl, stepY + (sectionH / 2) - 8);
                DisplayCanvas.Children.Add(lbl);
            }
        }

        private void DrawColorRamps(double w, double h)
        {
            double pad = 40;
            int barCount = 4;
            double barH = (h - pad * (barCount + 1.5)) / barCount;
            double barW = w - pad * 2;

            // Red
            AddColorBar(pad, pad + 0 * (barH + pad), barW, barH, Colors.Black, Colors.Red, "RED DYNAMIC RANGE (0-255)");
            // Green
            AddColorBar(pad, pad + 1 * (barH + pad), barW, barH, Colors.Black, Colors.Lime, "GREEN DYNAMIC RANGE (0-255)");
            // Blue
            AddColorBar(pad, pad + 2 * (barH + pad), barW, barH, Colors.Black, Colors.Blue, "BLUE DYNAMIC RANGE (0-255)");

            // Rainbow Spectrum
            var rainbowBrush = new LinearGradientBrush();
            rainbowBrush.StartPoint = new Point(0, 0);
            rainbowBrush.EndPoint = new Point(1, 0);
            rainbowBrush.GradientStops.Add(new GradientStop(Colors.Red, 0.0));
            rainbowBrush.GradientStops.Add(new GradientStop(Colors.Orange, 0.17));
            rainbowBrush.GradientStops.Add(new GradientStop(Colors.Yellow, 0.33));
            rainbowBrush.GradientStops.Add(new GradientStop(Colors.Lime, 0.5));
            rainbowBrush.GradientStops.Add(new GradientStop(Colors.Cyan, 0.67));
            rainbowBrush.GradientStops.Add(new GradientStop(Colors.Blue, 0.83));
            rainbowBrush.GradientStops.Add(new GradientStop(Colors.Magenta, 1.0));

            var specRect = new Rectangle
            {
                Width = barW,
                Height = barH,
                Fill = rainbowBrush,
                Stroke = Brushes.Gray,
                StrokeThickness = 1
            };
            Canvas.SetLeft(specRect, pad);
            Canvas.SetTop(specRect, pad + 3 * (barH + pad));
            DisplayCanvas.Children.Add(specRect);

            var specLbl = new TextBlock
            {
                Text = "FULL SPECTRUM 360° HUE BANDING & DITHERING",
                FontFamily = new FontFamily("Consolas"),
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Foreground = Brushes.White
            };
            Canvas.SetLeft(specLbl, pad + 10);
            Canvas.SetTop(specLbl, pad + 3 * (barH + pad) + 8);
            DisplayCanvas.Children.Add(specLbl);
        }

        private void AddColorBar(double x, double y, double w, double h, Color c1, Color c2, string label)
        {
            var brush = new LinearGradientBrush(c1, c2, 0);
            var rect = new Rectangle { Width = w, Height = h, Fill = brush, Stroke = Brushes.Gray, StrokeThickness = 1 };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            DisplayCanvas.Children.Add(rect);

            var lbl = new TextBlock
            {
                Text = label,
                FontFamily = new FontFamily("Consolas"),
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Foreground = Brushes.White
            };
            Canvas.SetLeft(lbl, x + 10);
            Canvas.SetTop(lbl, y + 8);
            DisplayCanvas.Children.Add(lbl);
        }

        private void DrawGeometryGrid(double w, double h)
        {
            double gridSize = 60;
            var gridBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));

            for (double x = 0; x < w; x += gridSize)
            {
                var l = new Line { X1 = x, Y1 = 0, X2 = x, Y2 = h, Stroke = gridBrush, StrokeThickness = 1 };
                DisplayCanvas.Children.Add(l);
            }
            for (double y = 0; y < h; y += gridSize)
            {
                var l = new Line { X1 = 0, Y1 = y, X2 = w, Y2 = y, Stroke = gridBrush, StrokeThickness = 1 };
                DisplayCanvas.Children.Add(l);
            }

            // Outer Perimeter Overscan Box (Red)
            var outer = new Rectangle
            {
                Width = w - 4,
                Height = h - 4,
                Stroke = Brushes.Red,
                StrokeThickness = 2
            };
            Canvas.SetLeft(outer, 2);
            Canvas.SetTop(outer, 2);
            DisplayCanvas.Children.Add(outer);

            // Concentric Aspect Ratio Circles
            double cx = w / 2;
            double cy = h / 2;
            double[] radii = { 120, 240, 360, 480 };
            foreach (double r in radii)
            {
                if (r * 2 < Math.Min(w, h))
                {
                    var c = new Ellipse
                    {
                        Width = r * 2,
                        Height = r * 2,
                        Stroke = Brushes.Cyan,
                        StrokeThickness = 1.5
                    };
                    Canvas.SetLeft(c, cx - r);
                    Canvas.SetTop(c, cy - r);
                    DisplayCanvas.Children.Add(c);
                }
            }

            // Center Crosshair
            var ch1 = new Line { X1 = cx - 40, Y1 = cy, X2 = cx + 40, Y2 = cy, Stroke = Brushes.Yellow, StrokeThickness = 2 };
            var ch2 = new Line { X1 = cx, Y1 = cy - 40, X2 = cx, Y2 = cy + 40, Stroke = Brushes.Yellow, StrokeThickness = 2 };
            DisplayCanvas.Children.Add(ch1);
            DisplayCanvas.Children.Add(ch2);
        }

        private void DrawTypographySharpness(double w, double h)
        {
            var panel = new StackPanel { Margin = new Thickness(60, 40, 60, 40) };

            string[] testLines = {
                "6pt  :: The quick brown fox jumps over the lazy dog. 0123456789 ABCDEFGHIJKLMNOPQRSTUVWXYZ",
                "8pt  :: The quick brown fox jumps over the lazy dog. 0123456789 ABCDEFGHIJKLMNOPQRSTUVWXYZ",
                "10pt :: The quick brown fox jumps over the lazy dog. 0123456789 ABCDEFGHIJKLMNOPQRSTUVWXYZ",
                "12pt :: The quick brown fox jumps over the lazy dog. 0123456789 ABCDEFGHIJKLMNOPQRSTUVWXYZ",
                "14pt :: The quick brown fox jumps over the lazy dog. 0123456789 ABCDEFGHIJKLMNOPQRSTUVWXYZ",
                "18pt :: Subpixel ClearType Optical Crispness & Aliasing Verification (RGB Pattern)",
                "24pt :: SUPERAUTOMATER QC DISPLAY LABORATORY"
            };
            double[] sizes = { 8, 11, 13, 16, 18, 24, 32 };

            for (int i = 0; i < testLines.Length; i++)
            {
                var tb = new TextBlock
                {
                    Text = testLines[i],
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = sizes[i],
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 0, 0, 14)
                };
                panel.Children.Add(tb);
            }

            Canvas.SetLeft(panel, 0);
            Canvas.SetTop(panel, 0);
            DisplayCanvas.Children.Add(panel);
        }

        private void StartMotionGhosting(double w, double h)
        {
            _motionBarX = 0;

            _motionRect = new Rectangle
            {
                Width = 140,
                Height = h * 0.6,
                Fill = Brushes.White,
                Stroke = Brushes.Cyan,
                StrokeThickness = 2
            };
            Canvas.SetLeft(_motionRect, 0);
            Canvas.SetTop(_motionRect, h * 0.2);
            DisplayCanvas.Children.Add(_motionRect);

            _motionTimer.Start();
        }

        private void MotionTimer_Tick(object sender, EventArgs e)
        {
            if (_currentStage != DisplayStage.MotionGhosting || _motionRect == null) return;

            double w = ActualWidth > 100 ? ActualWidth : 1920;
            _motionBarX += 16.0;
            if (_motionBarX > w + 150) _motionBarX = -150;

            Canvas.SetLeft(_motionRect, _motionBarX);

            // Measured frame rate is maintained with microsecond precision via DisplayTimingService
        }

        private void NextStage()
        {
            int next = (int)_currentStage + 1;
            if (next >= TOTAL_STAGES)
            {
                TestPassed = true;
                DialogResult = true;
                Close();
            }
            else
            {
                RenderStage((DisplayStage)next);
                ShowHud();
            }
        }

        private void PrevStage()
        {
            int prev = (int)_currentStage - 1;
            if (prev < 0) prev = TOTAL_STAGES - 1;
            RenderStage((DisplayStage)prev);
            ShowHud();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            ShowHud();

            if (e.Key == Key.Escape)
            {
                TestPassed = true;
                DialogResult = true;
                Close();
            }
            else if (e.Key == Key.Space || e.Key == Key.Right || e.Key == Key.PageDown)
            {
                NextStage();
            }
            else if (e.Key == Key.Left || e.Key == Key.PageUp)
            {
                PrevStage();
            }
            else if (e.Key == Key.H)
            {
                HudPanel.Visibility = (HudPanel.Visibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible;
            }
            else if (e.Key == Key.Enter || e.Key == Key.P)
            {
                TestPassed = true;
                DialogResult = true;
                Close();
            }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                NextStage();
            }
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            ShowHud();
        }

        private void BtnLaunchTouch_Click(object sender, RoutedEventArgs e)
        {
            var touchWin = new TouchscreenTestWindow { Owner = this };
            touchWin.ShowDialog();
            if (touchWin.TestPassed)
            {
                TestPassed = true;
                DialogResult = true;
                Close();
            }
        }

        private void BtnPass_Click(object sender, RoutedEventArgs e)
        {
            TestPassed = true;
            DialogResult = true;
            Close();
        }
    }
}
