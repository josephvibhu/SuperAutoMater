using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace SuperAutoMater.Wpf.Views
{
    public partial class ThermalLabelPreviewWindow : Window
    {
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        private readonly AssetQueueRecord _record;
        private Bitmap _currentBitmap;

        public ThermalLabelPreviewWindow(AssetQueueRecord record)
        {
            InitializeComponent();
            _record = record ?? new AssetQueueRecord();

            Loaded += (s, e) => UpdatePreview();
            PreviewKeyDown += ThermalLabelPreviewWindow_PreviewKeyDown;
            Closed += (s, e) => _currentBitmap?.Dispose();
        }

        private void ThermalLabelPreviewWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.P && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                BtnPrint_Click(this, new RoutedEventArgs());
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        }

        private LabelSizePreset GetSelectedPreset()
        {
            if (RadioSize2x1?.IsChecked == true) return LabelSizePreset.Compact2x1;
            if (RadioSize4x2?.IsChecked == true) return LabelSizePreset.Pallet4x2;
            return LabelSizePreset.Chassis3x2;
        }

        private BarcodeMode GetSelectedBarcodeMode()
        {
            if (RadioCode128?.IsChecked == true) return BarcodeMode.Code128;
            return BarcodeMode.QrCode;
        }

        private void Option_Changed(object sender, RoutedEventArgs e)
        {
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (_record == null || ImgLabelPreview == null) return;

            try
            {
                var preset = GetSelectedPreset();
                var barcodeMode = GetSelectedBarcodeMode();

                _currentBitmap?.Dispose();
                _currentBitmap = ThermalLabelPrinter.RenderLabelBitmap(_record, preset, barcodeMode);

                IntPtr hBitmap = _currentBitmap.GetHbitmap();
                try
                {
                    var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    bitmapSource.Freeze();

                    ImgLabelPreview.Source = bitmapSource;
                }
                finally
                {
                    DeleteObject(hBitmap);
                }

                string sizeStr = preset switch
                {
                    LabelSizePreset.Compact2x1 => "2\" × 1\" (50 × 25 mm)",
                    LabelSizePreset.Pallet4x2 => "4\" × 2\" (100 × 50 mm)",
                    _ => "3\" × 2\" (75 × 50 mm)"
                };

                TxtLabelDpiInfo.Text = $"{sizeStr} · {(barcodeMode == BarcodeMode.Code128 ? "CODE-128" : "QR CODE")}";
                TxtStatusMessage.Text = $"Rendered {sizeStr} ({_currentBitmap.Width}×{_currentBitmap.Height}px) ✓";
            }
            catch (Exception ex)
            {
                TxtStatusMessage.Text = "Preview Error: " + ex.Message;
            }
        }

        private void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var preset = GetSelectedPreset();
                var barcodeMode = GetSelectedBarcodeMode();
                ThermalLabelPrinter.PrintLabel(_record, preset, barcodeMode);
                TxtStatusMessage.Text = "Print command dispatched to spooler ✓";
            }
            catch (Exception ex)
            {
                TxtStatusMessage.Text = "Print failed: " + ex.Message;
            }
        }

        private void BtnCopyImage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ImgLabelPreview.Source is BitmapSource bs)
                {
                    Clipboard.SetImage(bs);
                    TxtStatusMessage.Text = "Label image copied to clipboard ✓";
                }
            }
            catch (Exception ex)
            {
                TxtStatusMessage.Text = "Copy failed: " + ex.Message;
            }
        }

        private void BtnSavePng_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentBitmap == null) return;

                var dlg = new SaveFileDialog
                {
                    FileName = $"ThermalLabel_{_record.Serial_Number}_{DateTime.Now:yyyyMMdd}.png",
                    DefaultExt = ".png",
                    Filter = "PNG Image (.png)|*.png"
                };

                if (dlg.ShowDialog(this) == true)
                {
                    _currentBitmap.Save(dlg.FileName, ImageFormat.Png);
                    TxtStatusMessage.Text = $"Saved to {Path.GetFileName(dlg.FileName)} ✓";
                }
            }
            catch (Exception ex)
            {
                TxtStatusMessage.Text = "Save failed: " + ex.Message;
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
