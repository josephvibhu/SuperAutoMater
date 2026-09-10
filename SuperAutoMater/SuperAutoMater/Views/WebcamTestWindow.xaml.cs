using System;
using System.Drawing;
using System.Media;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AForge.Video;
using AForge.Video.DirectShow;

namespace SuperAutoMater.Wpf.Views
{
    public partial class WebcamTestWindow : Window
    {
        private FilterInfoCollection _videoDevices;
        private VideoCaptureDevice _videoSource;
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
                    TxtStatus.Foreground = (System.Windows.Media.Brush)FindResource("BrushTextAmber");
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
                TxtStatus.Foreground = (System.Windows.Media.Brush)FindResource("BrushTextEmerald");

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
                var old = PicCameraFeed.Image;
                PicCameraFeed.Image = (Bitmap)eventArgs.Frame.Clone();
                old?.Dispose();
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
