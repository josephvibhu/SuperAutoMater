using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SuperAutoMater.Wpf.Controls
{
    public partial class TrackpadTestControl : UserControl
    {
        private bool _isLeftTested = false;
        private bool _isMiddleTested = false;
        private bool _isRightTested = false;
        private bool _isMotionTested = false;
        private bool _isScrollTested = false;

        private double _accumulatedDistance = 0.0;
        private Point? _lastPos = null;
        private int _totalScrollTicks = 0;

        private readonly SolidColorBrush _brushTestedBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1B4D3E"));
        private readonly SolidColorBrush _brushTestedBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2EA043"));
        private readonly SolidColorBrush _brushTestedText = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3FB950"));

        private readonly SolidColorBrush _brushActiveBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1C2C42"));
        private readonly SolidColorBrush _brushActiveBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#388BFD"));
        private readonly SolidColorBrush _brushActiveText = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#58A6FF"));

        private readonly SolidColorBrush _brushReadyBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#161B22"));
        private readonly SolidColorBrush _brushReadyBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#30363D"));
        private readonly SolidColorBrush _brushReadyText = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B949E"));

        public event Action<bool> CertificationChanged;

        public bool IsCertified => _isLeftTested && _isRightTested && _isMotionTested;

        public TrackpadTestControl()
        {
            InitializeComponent();
        }

        #region Trackpad Canvas & Surface Handlers

        private void Trackpad_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            Point p = e.GetPosition(TrackpadCanvas);

            // Update crosshair puck
            TrackingPuck.Visibility = Visibility.Visible;
            StackGuideHint.Visibility = Visibility.Collapsed;
            Canvas.SetLeft(TrackingPuck, p.X - (TrackingPuck.ActualWidth > 0 ? TrackingPuck.ActualWidth / 2 : 18));
            Canvas.SetTop(TrackingPuck, p.Y - (TrackingPuck.ActualHeight > 0 ? TrackingPuck.ActualHeight / 2 : 18));

            TxtCoordReadout.Text = $"X: {(int)p.X:000} · Y: {(int)p.Y:000}";

            // Track movement distance
            if (_lastPos.HasValue)
            {
                double dx = p.X - _lastPos.Value.X;
                double dy = p.Y - _lastPos.Value.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist > 0.5 && dist < 300)
                {
                    _accumulatedDistance += dist;
                    TxtMotionBadge.Text = $"MOTION [{(int)_accumulatedDistance}px]";

                    if (_accumulatedDistance >= 60 && !_isMotionTested)
                    {
                        _isMotionTested = true;
                        BadgeMotion.Background = _brushTestedBg;
                        BadgeMotion.BorderBrush = _brushTestedBorder;
                        TxtMotionBadge.Foreground = _brushTestedText;
                        TxtMotionBadge.Text = "MOTION [✓]";
                        CheckCertification();
                    }
                }
            }
            _lastPos = p;

            if (e.LeftButton == MouseButtonState.Pressed || e.RightButton == MouseButtonState.Pressed)
            {
                TxtGestureReadout.Text = "DRAGGING / SWIPE ACTIVE";
                TxtGestureReadout.Foreground = _brushActiveText;
            }
            else
            {
                TxtGestureReadout.Text = "POINTER TRACKING ACTIVE";
                TxtGestureReadout.Foreground = _brushTestedText;
            }
        }

        private void Trackpad_MouseLeave(object sender, MouseEventArgs e)
        {
            TrackingPuck.Visibility = Visibility.Collapsed;
            _lastPos = null;
            TxtGestureReadout.Text = "POINTER IDLE";
            TxtGestureReadout.Foreground = _brushReadyText;
        }

        private void Trackpad_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                ActuateLeft(true);
            }
            else if (e.ChangedButton == MouseButton.Right)
            {
                ActuateRight(true);
            }
            else if (e.ChangedButton == MouseButton.Middle)
            {
                ActuateMiddle(true);
            }
            e.Handled = true;
        }

        private void Trackpad_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                ReleaseLeft();
            }
            else if (e.ChangedButton == MouseButton.Right)
            {
                ReleaseRight();
            }
            else if (e.ChangedButton == MouseButton.Middle)
            {
                ReleaseMiddle();
            }
            e.Handled = true;
        }

        private void Trackpad_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            _totalScrollTicks += Math.Abs(e.Delta);
            TxtScrollBadge.Text = $"SCROLL [{_totalScrollTicks}]";
            TxtGestureReadout.Text = $"SCROLL DETECTED (Δ {e.Delta})";
            TxtGestureReadout.Foreground = _brushActiveText;

            if (_totalScrollTicks >= 120 && !_isScrollTested)
            {
                _isScrollTested = true;
                BadgeScroll.Background = _brushTestedBg;
                BadgeScroll.BorderBrush = _brushTestedBorder;
                TxtScrollBadge.Foreground = _brushTestedText;
                TxtScrollBadge.Text = "SCROLL [✓]";
                CheckCertification();
            }
            e.Handled = true;
        }

        #endregion

        #region Dedicated Physical Button Cards Handlers

        private void BtnLeft_MouseDown(object sender, MouseButtonEventArgs e)
        {
            ActuateLeft(true);
            e.Handled = true;
        }

        private void BtnLeft_MouseUp(object sender, MouseButtonEventArgs e)
        {
            ReleaseLeft();
            e.Handled = true;
        }

        private void BtnMiddle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            ActuateMiddle(true);
            e.Handled = true;
        }

        private void BtnMiddle_MouseUp(object sender, MouseButtonEventArgs e)
        {
            ReleaseMiddle();
            e.Handled = true;
        }

        private void BtnRight_MouseDown(object sender, MouseButtonEventArgs e)
        {
            ActuateRight(true);
            e.Handled = true;
        }

        private void BtnRight_MouseUp(object sender, MouseButtonEventArgs e)
        {
            ReleaseRight();
            e.Handled = true;
        }

        private void ActuateLeft(bool pressed)
        {
            _isLeftTested = true;
            CardLeftBtn.Background = _brushActiveBg;
            CardLeftBtn.BorderBrush = _brushActiveBorder;
            BadgeLeftState.Background = _brushTestedBg;
            TxtLeftState.Foreground = _brushTestedText;
            TxtLeftState.Text = pressed ? "PRESSED" : "PASS [✓]";

            BadgeLeft.Background = _brushTestedBg;
            BadgeLeft.BorderBrush = _brushTestedBorder;
            TxtLeftBadge.Foreground = _brushTestedText;
            TxtLeftBadge.Text = "LMB [✓]";

            CheckCertification();
        }

        private void ReleaseLeft()
        {
            if (_isLeftTested)
            {
                CardLeftBtn.Background = _brushTestedBg;
                CardLeftBtn.BorderBrush = _brushTestedBorder;
                TxtLeftState.Text = "PASS [✓]";
            }
        }

        private void ActuateMiddle(bool pressed)
        {
            _isMiddleTested = true;
            CardMiddleBtn.Background = _brushActiveBg;
            CardMiddleBtn.BorderBrush = _brushActiveBorder;
            BadgeMiddleState.Background = _brushTestedBg;
            TxtMiddleState.Foreground = _brushTestedText;
            TxtMiddleState.Text = pressed ? "PRESSED" : "PASS [✓]";

            BadgeMiddle.Background = _brushTestedBg;
            BadgeMiddle.BorderBrush = _brushTestedBorder;
            TxtMiddleBadge.Foreground = _brushTestedText;
            TxtMiddleBadge.Text = "MMB [✓]";

            CheckCertification();
        }

        private void ReleaseMiddle()
        {
            if (_isMiddleTested)
            {
                CardMiddleBtn.Background = _brushTestedBg;
                CardMiddleBtn.BorderBrush = _brushTestedBorder;
                TxtMiddleState.Text = "PASS [✓]";
            }
        }

        private void ActuateRight(bool pressed)
        {
            _isRightTested = true;
            CardRightBtn.Background = _brushActiveBg;
            CardRightBtn.BorderBrush = _brushActiveBorder;
            BadgeRightState.Background = _brushTestedBg;
            TxtRightState.Foreground = _brushTestedText;
            TxtRightState.Text = pressed ? "PRESSED" : "PASS [✓]";

            BadgeRight.Background = _brushTestedBg;
            BadgeRight.BorderBrush = _brushTestedBorder;
            TxtRightBadge.Foreground = _brushTestedText;
            TxtRightBadge.Text = "RMB [✓]";

            CheckCertification();
        }

        private void ReleaseRight()
        {
            if (_isRightTested)
            {
                CardRightBtn.Background = _brushTestedBg;
                CardRightBtn.BorderBrush = _brushTestedBorder;
                TxtRightState.Text = "PASS [✓]";
            }
        }

        #endregion

        #region State Management & Reset

        private void CheckCertification()
        {
            if (IsCertified)
            {
                DotTpStatus.Fill = _brushTestedText;
                BorderOverallStatus.Background = _brushTestedBg;
                BorderOverallStatus.BorderBrush = _brushTestedBorder;
                TxtOverallStatus.Text = "VERIFIED NOMINAL ✓";
                TxtOverallStatus.Foreground = _brushTestedText;
                CertificationChanged?.Invoke(true);
            }
            else
            {
                int count = (_isLeftTested ? 1 : 0) + (_isRightTested ? 1 : 0) + (_isMotionTested ? 1 : 0);
                TxtOverallStatus.Text = $"{count}/3 TESTED";
            }
        }

        public void Reset()
        {
            _isLeftTested = false;
            _isMiddleTested = false;
            _isRightTested = false;
            _isMotionTested = false;
            _isScrollTested = false;
            _accumulatedDistance = 0.0;
            _totalScrollTicks = 0;
            _lastPos = null;

            DotTpStatus.Fill = _brushActiveText;
            BorderOverallStatus.Background = _brushReadyBg;
            BorderOverallStatus.BorderBrush = _brushReadyBorder;
            TxtOverallStatus.Text = "AWAITING INPUT";
            TxtOverallStatus.Foreground = _brushReadyText;

            BadgeMotion.Background = _brushReadyBg;
            BadgeMotion.BorderBrush = _brushReadyBorder;
            TxtMotionBadge.Foreground = _brushReadyText;
            TxtMotionBadge.Text = "MOTION [0px]";

            BadgeScroll.Background = _brushReadyBg;
            BadgeScroll.BorderBrush = _brushReadyBorder;
            TxtScrollBadge.Foreground = _brushReadyText;
            TxtScrollBadge.Text = "SCROLL [0]";

            BadgeLeft.Background = _brushReadyBg;
            BadgeLeft.BorderBrush = _brushReadyBorder;
            TxtLeftBadge.Foreground = _brushReadyText;
            TxtLeftBadge.Text = "LMB";

            BadgeMiddle.Background = _brushReadyBg;
            BadgeMiddle.BorderBrush = _brushReadyBorder;
            TxtMiddleBadge.Foreground = _brushReadyText;
            TxtMiddleBadge.Text = "MMB";

            BadgeRight.Background = _brushReadyBg;
            BadgeRight.BorderBrush = _brushReadyBorder;
            TxtRightBadge.Foreground = _brushReadyText;
            TxtRightBadge.Text = "RMB";

            CardLeftBtn.Background = _brushReadyBg;
            CardLeftBtn.BorderBrush = _brushReadyBorder;
            BadgeLeftState.Background = _brushReadyBg;
            TxtLeftState.Foreground = _brushReadyText;
            TxtLeftState.Text = "READY";

            CardMiddleBtn.Background = _brushReadyBg;
            CardMiddleBtn.BorderBrush = _brushReadyBorder;
            BadgeMiddleState.Background = _brushReadyBg;
            TxtMiddleState.Foreground = _brushReadyText;
            TxtMiddleState.Text = "READY";

            CardRightBtn.Background = _brushReadyBg;
            CardRightBtn.BorderBrush = _brushReadyBorder;
            BadgeRightState.Background = _brushReadyBg;
            TxtRightState.Foreground = _brushReadyText;
            TxtRightState.Text = "READY";

            StackGuideHint.Visibility = Visibility.Visible;
            TrackingPuck.Visibility = Visibility.Collapsed;
            TxtCoordReadout.Text = "X: 000 · Y: 000";
            TxtGestureReadout.Text = "SWIPE / POINTER READY";
            TxtGestureReadout.Foreground = _brushReadyText;

            CertificationChanged?.Invoke(false);
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            Reset();
        }

        #endregion
    }
}
