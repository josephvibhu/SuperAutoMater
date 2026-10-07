using System;
using System.Drawing;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AForge.Video;
using AForge.Video.DirectShow;

namespace VeloxBench.Views
{
    public partial class WebcamTestWindow : Window
    {
        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);

        private FilterInfoCollection? _videoDevices;
        private VideoCaptureDevice? _videoSource;
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
            try
            {
                _videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                CmbCameras.Items.Clear();

                if (_videoDevices.Count == 0)
                {
                    StackNoCamera.Visibility = Visibility.Visible;
                    TxtNoCameraMsg.Text = "No webcam or video capture sensors detected.";
                    TxtStatus.Text = "○ NO CAMERA DETECTED";
                    TxtStatus.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#D29922"));
                    return;
                }

                foreach (FilterInfo device in _videoDevices)
                {
                    CmbCameras.Items.Add(device.Name);
                }

                CmbCameras.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                StackNoCamera.Visibility = Visibility.Visible;
                TxtNoCameraMsg.Text = "Camera Probe Error: " + ex.Message;
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

            try
            {
                _videoSource = new VideoCaptureDevice(monikerString);
                _videoSource.NewFrame += VideoSource_NewFrame;
                _videoSource.Start();

                StackNoCamera.Visibility = Visibility.Collapsed;
                TxtStatus.Text = "● SENSOR STREAM ACTIVE";
                TxtStatus.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3FB950"));

                if (_videoSource.VideoCapabilities != null && _videoSource.VideoCapabilities.Length > 0)
                {
                    var cap = _videoSource.VideoCapabilities[0];
                    TxtResolution.Text = $"{cap.FrameSize.Width}x{cap.FrameSize.Height} @ {cap.AverageFrameRate} FPS";
                }
            }
            catch (Exception ex)
            {
                StackNoCamera.Visibility = Visibility.Visible;
                TxtNoCameraMsg.Text = "Failed to start camera: " + ex.Message;
            }
        }

        private void VideoSource_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            try
            {
                using var bitmap = (Bitmap)eventArgs.Frame.Clone();
                IntPtr hBitmap = bitmap.GetHbitmap();
                try
                {
                    var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    bitmapSource.Freeze();

                    Dispatcher.InvokeAsync(() =>
                    {
                        ImgCameraFeed.Source = bitmapSource;
                    }, DispatcherPriority.Render);
                }
                finally
                {
                    DeleteObject(hBitmap);
                }
            }
            catch { }
        }

        private void StopCamera()
        {
            if (_videoSource != null)
            {
                try
                {
                    _videoSource.SignalToStop();
                    _videoSource.NewFrame -= VideoSource_NewFrame;
                    _videoSource = null;
                }
                catch { }
            }
            Dispatcher.InvokeAsync(() =>
            {
                ImgCameraFeed.Source = null;
            });
        }

        private void WebcamTestWindow_Closed(object? sender, EventArgs e)
        {
            StopCamera();
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
