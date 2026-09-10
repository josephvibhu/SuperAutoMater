using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SuperAutoMater.Wpf.Services;

namespace SuperAutoMater.Wpf.Views
{
    public partial class TouchscreenTestWindow : Window
    {
        private const int COLS = 14;
        private const int ROWS = 9;
        private const int TOTAL_BLOCKS = COLS * ROWS;

        private readonly Border[,] _blockBorders = new Border[COLS, ROWS];
        private readonly TextBlock[,] _blockLabels = new TextBlock[COLS, ROWS];
        private readonly bool[,] _touched = new bool[COLS, ROWS];
        private int _touchedCount = 0;
        private bool _isMouseDown = false;

        public bool TestPassed { get; private set; } = false;

        public TouchscreenTestWindow()
        {
            InitializeComponent();
            InitGrid();
            Loaded += TouchscreenTestWindow_Loaded;
        }

        private void TouchscreenTestWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var hw = HardwareDiagnosticsService.Instance;
            if (hw.HasTouchscreen)
            {
                TxtHwStatus.Text = "TOUCH DIGITIZER ACTIVE";
                TxtMaxTouches.Text = $"{Math.Max(10, hw.MaxTouchContacts)} TOUCH CONTACTS";
            }
            else
            {
                TxtHwStatus.Text = "STANDARD PANEL (MOUSE/PEN INPUT)";
                TxtHwStatus.Foreground = (Brush)FindResource("BrushTextVioletLight");
                BorderHwStatus.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#21262D"));
                BorderHwStatus.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#30363D"));
                TxtMaxTouches.Text = "DIGITIZER TEST MODE";
            }
        }

        private void InitGrid()
        {
            BlocksContainer.Children.Clear();

            for (int r = 0; r < ROWS; r++)
            {
                for (int c = 0; c < COLS; c++)
                {
                    var tb = new TextBlock
                    {
                        Text = "",
                        FontFamily = new FontFamily("Consolas"),
                        FontWeight = FontWeights.Bold,
                        FontSize = 13,
                        Foreground = (Brush)FindResource("BrushTextEmerald"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    var border = new Border
                    {
                        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F141C")),
                        BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1F2633")),
                        BorderThickness = new Thickness(1),
                        Margin = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Child = tb
                    };

                    _blockBorders[c, r] = border;
                    _blockLabels[c, r] = tb;
                    _touched[c, r] = false;
                    BlocksContainer.Children.Add(border);
                }
            }

            UpdateCoverageText();
        }

        private void ProcessPoint(Point pt)
        {
            double w = DigitizerGrid.ActualWidth;
            double h = DigitizerGrid.ActualHeight;
            if (w <= 0 || h <= 0) return;

            int col = (int)(pt.X / (w / COLS));
            int row = (int)(pt.Y / (h / ROWS));

            if (col >= 0 && col < COLS && row >= 0 && row < ROWS)
            {
                if (!_touched[col, row])
                {
                    _touched[col, row] = true;
                    _touchedCount++;

                    _blockBorders[col, row].Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#143526"));
                    _blockBorders[col, row].BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399"));
                    _blockLabels[col, row].Text = "✓";

                    UpdateCoverageText();

                    // Auto-qualify test if >= 90% covered
                    if (_touchedCount >= (int)(TOTAL_BLOCKS * 0.90))
                    {
                        TestPassed = true;
                    }
                }
            }

            AddTrailPoint(pt);
        }

        private void AddTrailPoint(Point pt)
        {
            var dot = new Ellipse
            {
                Width = 14,
                Height = 14,
                Fill = new SolidColorBrush(Color.FromArgb(180, 0x3F, 0xB9, 0x50)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(dot, pt.X - 7);
            Canvas.SetTop(dot, pt.Y - 7);
            TrailCanvas.Children.Add(dot);

            if (TrailCanvas.Children.Count > 40)
            {
                TrailCanvas.Children.RemoveAt(0);
            }
        }

        private void UpdateCoverageText()
        {
            int pct = (int)Math.Round((_touchedCount / (double)TOTAL_BLOCKS) * 100.0);
            TxtCoverage.Text = $"{_touchedCount}/{TOTAL_BLOCKS} BLOCKS ({pct}%)";
        }

        private void ResetGrid()
        {
            for (int r = 0; r < ROWS; r++)
            {
                for (int c = 0; c < COLS; c++)
                {
                    _touched[c, r] = false;
                    _blockBorders[c, r].Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F141C"));
                    _blockBorders[c, r].BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1F2633"));
                    _blockLabels[c, r].Text = "";
                }
            }
            _touchedCount = 0;
            TrailCanvas.Children.Clear();
            UpdateCoverageText();
        }

        #region Mouse and Touch Events

        private void Grid_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _isMouseDown = true;
                ProcessPoint(e.GetPosition(DigitizerGrid));
            }
        }

        private void Grid_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isMouseDown || e.LeftButton == MouseButtonState.Pressed)
            {
                ProcessPoint(e.GetPosition(DigitizerGrid));
            }
        }

        private void Grid_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _isMouseDown = false;
        }

        private void Grid_TouchDown(object sender, TouchEventArgs e)
        {
            ProcessPoint(e.GetTouchPoint(DigitizerGrid).Position);
        }

        private void Grid_TouchMove(object sender, TouchEventArgs e)
        {
            ProcessPoint(e.GetTouchPoint(DigitizerGrid).Position);
        }

        private void Grid_TouchUp(object sender, TouchEventArgs e)
        {
        }

        #endregion

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
            else if (e.Key == Key.Enter || e.Key == Key.Space || e.Key == Key.P)
            {
                TestPassed = true;
                DialogResult = true;
                Close();
            }
            else if (e.Key == Key.C || e.Key == Key.R)
            {
                ResetGrid();
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
