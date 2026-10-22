using System;
using System.Drawing;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AForge.Video;
using AForge.Video.DirectShow;
using SuperAutoMater.Wpf.Services;

namespace SuperAutoMater.Wpf.Views
{
    public partial class WebcamTestWindow : Window
    {
        private FilterInfoCollection _videoDevices;
        private VideoCaptureDevice _videoSource;
        private int _framesReceived = 0;
        private CancellationTokenSource _watchdogCts;
        public bool TestPassed { get; private set; } = false;

        public WebcamTestWindow()
        {
            InitializeComponent();
            Loaded += WebcamTestWindow_Loaded;
            Closed += WebcamTestWindow_Closed;
            KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape)
                {
                    BtnCancel_Click(this, new RoutedEventArgs());
                }
                else if (e.Key == System.Windows.Input.Key.Enter)
                {
                    BtnPass_Click(this, new RoutedEventArgs());
                }
            };
        }

        private void WebcamTestWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshCameraDevices();
        }

        private void RefreshCameraDevices()
        {
            try
            {
                _videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                CmbCameras.Items.Clear();

                bool isPrivacyBlocked = CameraDeviceService.IsWindowsCameraPrivacyRestricted(out string privacyMsg);
                BtnDlgUnblockPrivacy.Visibility = isPrivacyBlocked ? Visibility.Visible : Visibility.Collapsed;

                if (_videoDevices.Count == 0)
                {
                    StackNoCamera.Visibility = Visibility.Visible;
                    TxtNoCameraMsg.Text = CameraDeviceService.GetDiagnosticsSummary();
                    TxtStatus.Text = "○ NO CAMERA DETECTED";
                    TxtStatus.Foreground = (System.Windows.Media.Brush)FindResource("BrushTextAmber");
                    return;
                }

                foreach (FilterInfo device in _videoDevices)
                {
                    CmbCameras.Items.Add(CameraDeviceService.FormatCameraDisplayName(device.Name));
                }

                int bestIdx = CameraDeviceService.SelectBestCameraIndex(_videoDevices);
                CmbCameras.SelectedIndex = bestIdx >= 0 ? bestIdx : 0;
            }
            catch (Exception ex)
            {
                StackNoCamera.Visibility = Visibility.Visible;
                TxtNoCameraMsg.Text = "Camera Probe Error: " + ex.Message + "\n" + CameraDeviceService.GetDiagnosticsSummary();
            }
        }

        private void CmbCameras_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbCameras.SelectedIndex >= 0 && _videoDevices != null && CmbCameras.SelectedIndex < _videoDevices.Count)
            {
                StartCamera(_videoDevices[CmbCameras.SelectedIndex].MonikerString);
            }
        }

        private void StartCamera(string monikerString)
        {
            StopCamera();
            _framesReceived = 0;
            _watchdogCts?.Cancel();
            _watchdogCts = new CancellationTokenSource();
            var token = _watchdogCts.Token;

            try
            {
                _videoSource = new VideoCaptureDevice(monikerString);
                var optimalCap = CameraDeviceService.ConfigureOptimalResolution(_videoSource);

                _videoSource.NewFrame += VideoSource_NewFrame;
                _videoSource.VideoSourceError += VideoSource_VideoSourceError;
                _videoSource.Start();

                StackNoCamera.Visibility = Visibility.Collapsed;
                TxtStatus.Text = "● SENSOR STREAM ACTIVE";
                TxtStatus.Foreground = (System.Windows.Media.Brush)FindResource("BrushTextEmerald");

                if (optimalCap != null)
                {
                    TxtResolution.Text = $"{optimalCap.FrameSize.Width}x{optimalCap.FrameSize.Height} @ {optimalCap.AverageFrameRate} FPS";
                }
                else if (_videoSource.VideoCapabilities != null && _videoSource.VideoCapabilities.Length > 0)
                {
                    var cap = _videoSource.VideoCapabilities[0];
                    TxtResolution.Text = $"{cap.FrameSize.Width}x{cap.FrameSize.Height} @ {cap.AverageFrameRate} FPS";
                }
                else
                {
                    TxtResolution.Text = "Hardware Native / Driver Default";
                }

                // Watchdog: If no frames arrive in 2.5s, retry with driver default resolution (fixes Dell Precision 5540/5550/5560 pin failures)
                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(2500, token);
                        if (!token.IsCancellationRequested && _framesReceived == 0)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                if (_videoSource != null && _videoSource.VideoResolution != null)
                                {
                                    // Fallback to driver default format
                                    _videoSource.SignalToStop();
                                    _videoSource.VideoResolution = null;
                                    _videoSource.Start();
                                    TxtResolution.Text = "Fallback: Driver Default";
                                }
                                else if (_framesReceived == 0)
                                {
                                    StackNoCamera.Visibility = Visibility.Visible;
                                    bool isBlocked = CameraDeviceService.IsWindowsCameraPrivacyRestricted(out string msg);
                                    BtnDlgUnblockPrivacy.Visibility = isBlocked ? Visibility.Visible : Visibility.Collapsed;
                                    TxtNoCameraMsg.Text = isBlocked ? $"⚠ {msg}" : "No frames received from camera. Check physical privacy switch or try another camera input.";
                                }
                            });
                        }
                    }
                    catch { }
                }, token);
            }
            catch (Exception ex)
            {
                StackNoCamera.Visibility = Visibility.Visible;
                TxtNoCameraMsg.Text = "Failed to start camera: " + ex.Message;
                bool isBlocked = CameraDeviceService.IsWindowsCameraPrivacyRestricted(out string msg);
                BtnDlgUnblockPrivacy.Visibility = isBlocked ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void VideoSource_VideoSourceError(object sender, VideoSourceErrorEventArgs eventArgs)
        {
            Dispatcher.InvokeAsync(() =>
            {
                // If custom resolution failed, automatically retry with driver default
                if (_videoSource != null && _videoSource.VideoResolution != null)
                {
                    try
                    {
                        _videoSource.SignalToStop();
                        _videoSource.VideoResolution = null;
                        _videoSource.Start();
                        TxtResolution.Text = "Fallback: Driver Default";
                        return;
                    }
                    catch { }
                }

                StackNoCamera.Visibility = Visibility.Visible;
                TxtNoCameraMsg.Text = "Driver Error: " + eventArgs.Description;
                bool isBlocked = CameraDeviceService.IsWindowsCameraPrivacyRestricted(out string msg);
                BtnDlgUnblockPrivacy.Visibility = isBlocked ? Visibility.Visible : Visibility.Collapsed;
            });
        }

        private void VideoSource_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            try
            {
                Interlocked.Increment(ref _framesReceived);
                var old = PicCameraFeed.Image;
                PicCameraFeed.Image = (Bitmap)eventArgs.Frame.Clone();
                old?.Dispose();

                if (StackNoCamera.Visibility == Visibility.Visible)
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        StackNoCamera.Visibility = Visibility.Collapsed;
                    });
                }
            }
            catch { }
        }

        private void StopCamera()
        {
            _watchdogCts?.Cancel();
            if (_videoSource != null)
            {
                try
                {
                    _videoSource.SignalToStop();
                    _videoSource.NewFrame -= VideoSource_NewFrame;
                    _videoSource.VideoSourceError -= VideoSource_VideoSourceError;
                    _videoSource = null;
                }
                catch { }
            }
        }

        private void BtnDlgUnblockPrivacy_Click(object sender, RoutedEventArgs e)
        {
            if (CameraDeviceService.TryUnblockWindowsCameraPrivacy(out string msg))
            {
                MessageBox.Show(msg, "Windows Camera Privacy", MessageBoxButton.OK, MessageBoxImage.Information);
                RefreshCameraDevices();
            }
            else
            {
                MessageBox.Show(msg, "Windows Camera Privacy", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnDlgRescan_Click(object sender, RoutedEventArgs e)
        {
            RefreshCameraDevices();
        }

        private void BtnDlgWindowsApp_Click(object sender, RoutedEventArgs e)
        {
            CameraDeviceService.LaunchWindowsCameraApp();
        }

        private void WebcamTestWindow_Closed(object sender, EventArgs e)
        {
            StopCamera();
            PicCameraFeed.Image?.Dispose();
        }

        private void BtnPass_Click(object sender, RoutedEventArgs e)
        {
            TestPassed = true;
            try { SystemSounds.Asterisk.Play(); } catch { }
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
