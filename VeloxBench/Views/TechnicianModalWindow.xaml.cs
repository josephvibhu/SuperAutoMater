using System;
using System.Collections.Generic;
using System.Drawing;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AForge.Video;
using AForge.Video.DirectShow;
using SuperAutoMater.Wpf.Services;
using ZXing;
using ZXing.Common;
using ZXing.Windows.Compatibility;

namespace SuperAutoMater.Wpf.Views
{
    public partial class TechnicianModalWindow : Window
    {
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        private VideoCaptureDevice _videoSource;
        private readonly object _frameLock = new object();
        private Bitmap _latestFrame;
        private DateTime _lastScanTime = DateTime.MinValue;
        private bool _isScanning = true;
        private readonly BarcodeReader _barcodeReader;

        public TechnicianModalWindow()
        {
            InitializeComponent();

            _barcodeReader = new BarcodeReader
            {
                AutoRotate = true,
                Options = new DecodingOptions
                {
                    TryHarder = true,
                    PossibleFormats = new List<BarcodeFormat>
                    {
                        BarcodeFormat.QR_CODE,
                        BarcodeFormat.DATA_MATRIX,
                        BarcodeFormat.CODE_128,
                        BarcodeFormat.CODE_39,
                        BarcodeFormat.EAN_13,
                        BarcodeFormat.UPC_A
                    }
                }
            };

            Loaded += TechnicianModalWindow_Loaded;
            Closing += TechnicianModalWindow_Closing;
            PreviewKeyDown += TechnicianModalWindow_PreviewKeyDown;
        }

        private void TechnicianModalWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var profile = TechnicianProfileService.Instance.CurrentProfile;
            TxtTechName.Text = profile.Name;
            TxtTechId.Text = profile.Id;
            TxtTechStation.Text = profile.StationBay;
            TxtCurrentBadgePill.Text = profile.DisplayBadge;

            StartCamera();
        }

        private void TechnicianModalWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                BtnSave_Click(this, new RoutedEventArgs());
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                BtnCancel_Click(this, new RoutedEventArgs());
            }
        }

        private void StartCamera()
        {
            try
            {
                var videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                if (videoDevices.Count == 0)
                {
                    PanelCamPlaceholder.Visibility = Visibility.Visible;
                    TxtScanStatus.Text = "ℹ No webcam detected. Manual entry available.";
                    return;
                }

                // Pick first video capture device
                _videoSource = new VideoCaptureDevice(videoDevices[0].MonikerString);
                _videoSource.NewFrame += VideoSource_NewFrame;
                _videoSource.Start();
                PanelCamPlaceholder.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                PanelCamPlaceholder.Visibility = Visibility.Visible;
                TxtScanStatus.Text = "Camera unavailable: " + ex.Message;
            }
        }

        private void VideoSource_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            Bitmap frameClone = null;
            try
            {
                lock (_frameLock)
                {
                    _latestFrame?.Dispose();
                    _latestFrame = (Bitmap)eventArgs.Frame.Clone();
                    frameClone = (Bitmap)_latestFrame.Clone();
                }

                // 1. Render frame to WPF Image
                IntPtr hBitmap = frameClone.GetHbitmap();
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
                        ImgCamPreview.Source = bitmapSource;
                    }, DispatcherPriority.Render);
                }
                finally
                {
                    DeleteObject(hBitmap);
                }

                // 2. Sample frame for Barcode / QR Code every 200ms
                if (_isScanning && (DateTime.Now - _lastScanTime).TotalMilliseconds >= 200)
                {
                    _lastScanTime = DateTime.Now;

                    Result result = null;
                    try
                    {
                        result = _barcodeReader.Decode(frameClone);
                    }
                    catch { }

                    if (result != null && !string.IsNullOrWhiteSpace(result.Text))
                    {
                        string scannedText = result.Text;
                        Dispatcher.InvokeAsync(() =>
                        {
                            OnBadgeDecoded(scannedText);
                        });
                    }
                }
            }
            catch { }
            finally
            {
                frameClone?.Dispose();
            }
        }

        private void OnBadgeDecoded(string rawBadgeText)
        {
            try
            {
                SystemSounds.Asterisk.Play();
            }
            catch { }

            var (name, id, station) = TechnicianProfileService.Instance.ParseBadgeText(rawBadgeText);

            TxtTechName.Text = name;
            TxtTechId.Text = id;
            TxtTechStation.Text = station;
            TxtCurrentBadgePill.Text = $"{id} ({name})";

            TxtScanStatus.Text = $"✓ DETECTED: {id} · {name}";
            TxtScanStatus.Foreground = (System.Windows.Media.Brush)FindResource("BrushAccentEmerald");

            TxtFooterFeedback.Text = $"Scanned: '{rawBadgeText.Trim()}'";
            TxtFooterFeedback.Foreground = (System.Windows.Media.Brush)FindResource("BrushAccentEmerald");
        }

        private void StopCamera()
        {
            _isScanning = false;
            try
            {
                if (_videoSource != null)
                {
                    _videoSource.SignalToStop();
                    _videoSource.NewFrame -= VideoSource_NewFrame;
                    _videoSource = null;
                }
            }
            catch { }

            lock (_frameLock)
            {
                _latestFrame?.Dispose();
                _latestFrame = null;
            }
        }

        private void TechnicianModalWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            StopCamera();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            string name = TxtTechName.Text;
            string id = TxtTechId.Text;
            string station = TxtTechStation.Text;

            TechnicianProfileService.Instance.SaveProfile(name, id, station);
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
