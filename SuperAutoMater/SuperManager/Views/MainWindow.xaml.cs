using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SuperAutoMater.Wpf.Core;
using SuperManager.Models;
using SuperManager.Services;
using SuperManager.ViewModels;

namespace SuperManager.Views
{
    public partial class MainWindow : Window
    {
        public ManagerMainViewModel ViewModel { get; }

        public MainWindow()
        {
            ViewModel = new ManagerMainViewModel();
            DataContext = ViewModel;

            InitializeComponent();

            Loaded += (s, e) =>
            {
                WindowState = WindowState.Maximized;
            };
        }

        private void BtnCopyWebUrl_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(ViewModel.ManagerWebUrl);
                ViewModel.StatusMessage = $"✓ Opened Central Web HUD ({ViewModel.ManagerWebUrl}) and copied URL to clipboard!";
                Process.Start(new ProcessStartInfo(ViewModel.ManagerWebUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ViewModel.StatusMessage = $"Web HUD: {ViewModel.ManagerWebUrl} ({ex.Message})";
            }
        }

        private void BtnCopyOnlyUrl_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(ViewModel.ManagerWebUrl);
                ViewModel.StatusMessage = $"✓ Copied Central Web HUD URL to clipboard: {ViewModel.ManagerWebUrl}";
            }
            catch (Exception ex)
            {
                ViewModel.StatusMessage = $"❌ Copy error: {ex.Message}";
            }
        }

        private void BtnFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                ViewModel.StatusFilter = tag;
            }
        }

        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            string path = ViewModel.ExportInventoryCsv();
            if (!string.IsNullOrEmpty(path))
            {
                MessageBox.Show($"✓ Complete Warehouse Fleet Inventory exported to:\n{path}", "Fleet CSV Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnRefreshWip_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.WipBoard.RefreshBoard();
        }

        private void BtnConfirmIntake_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.WipBoard.ExecuteIntake();
        }

        private void BtnShowQr_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var qrWindow = new Window
                {
                    Title = "Connect Mobile / Tablet Dashboard",
                    Width = 400,
                    Height = 490,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0B0C10")),
                    ResizeMode = ResizeMode.NoResize
                };

                var stack = new StackPanel { Margin = new Thickness(24, 20, 24, 20), HorizontalAlignment = HorizontalAlignment.Center };

                var titleTxt = new TextBlock
                {
                    Text = "📱 MOBILE WAREHOUSE HUD",
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI, Arial"),
                    FontWeight = FontWeights.Bold,
                    FontSize = 13,
                    Foreground = System.Windows.Media.Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 12)
                };

                var imgBorder = new Border
                {
                    Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#161B22")),
                    BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#30363D")),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(12),
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                var qrBmp = ManagerWebServer.Instance.QrCodeBitmap;
                var img = new Image { Source = qrBmp, Width = 220, Height = 220 };
                imgBorder.Child = img;

                var txtUrl = new TextBlock
                {
                    Text = ViewModel.ManagerWebUrl,
                    FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas"),
                    Foreground = (TryFindResource("BrushAccentCyan") as System.Windows.Media.Brush) ??
                                 (TryFindResource("BrushAccentBlue") as System.Windows.Media.Brush) ??
                                 new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x58, 0xA6, 0xFF)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 12, 0, 4),
                    FontSize = 14,
                    FontWeight = FontWeights.Bold
                };

                var hintTxt = new TextBlock
                {
                    Text = "1. Connect phone/tablet to the same Wi-Fi network.\n2. Scan code above or enter URL in browser.\n3. If blocked, ensure Windows Firewall allows Port 9000.",
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI, Arial"),
                    FontSize = 11,
                    Foreground = (TryFindResource("BrushTextSubdued") as System.Windows.Media.Brush) ?? System.Windows.Media.Brushes.Gray,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 8, 0, 12),
                    LineHeight = 16
                };

                var btnCopy = new Button
                {
                    Content = "📋 COPY URL TO CLIPBOARD",
                    Style = (TryFindResource("StyleDarkActionBtn") as Style),
                    Padding = new Thickness(16, 6, 16, 6),
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                btnCopy.Click += (s, ev) =>
                {
                    try
                    {
                        Clipboard.SetText(ViewModel.ManagerWebUrl);
                        btnCopy.Content = "✓ COPIED!";
                    }
                    catch { }
                };

                stack.Children.Add(titleTxt);
                stack.Children.Add(imgBorder);
                stack.Children.Add(txtUrl);
                stack.Children.Add(hintTxt);
                stack.Children.Add(btnCopy);
                qrWindow.Content = stack;
                qrWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Mobile HUD URL: {ViewModel.ManagerWebUrl}\n\nNote: {ex.Message}", "Mobile Dashboard", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.UpdateKpis();
            ViewModel.StatusMessage = "Subnet re-scan refreshed.";
        }

        private async void BtnBenchPing_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is BenchDevice bench)
            {
                await ViewModel.PingBenchAsync(bench);
            }
        }

        private void BtnBenchRemote_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is BenchDevice bench)
            {
                var win = new RemoteDesktopWindow(bench) { Owner = this };
                win.Show();
            }
        }

        private void BtnBenchHud_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is BenchDevice bench)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(bench.Url) { UseShellExecute = true });
                    ViewModel.StatusMessage = $"✓ Opened Web HUD for {bench.MachineName} ({bench.Url})";
                }
                catch (Exception ex)
                {
                    ViewModel.StatusMessage = $"❌ Failed to open Web HUD: {ex.Message}";
                }
            }
        }

        private void BtnBenchInspect_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is BenchDevice bench)
            {
                var dlg = new BenchDetailDialog(bench) { Owner = this };
                dlg.ShowDialog();
            }
        }

        private async void BtnBenchCert_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is BenchDevice bench)
            {
                string savedPath = await ViewModel.DownloadCertificateAsync(bench);
                if (!string.IsNullOrEmpty(savedPath))
                {
                    MessageBox.Show($"✓ Certificate archived to:\n{savedPath}", "Certificate Downloaded", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        #region Warehouse WIP Drag-and-Drop & Context Menu

        private Point _dragStartPoint;
        private AssetWipRecord _draggedAsset;

        private void Card_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            if (sender is FrameworkElement element && element.DataContext is AssetWipRecord asset)
            {
                _draggedAsset = asset;
            }
            else
            {
                _draggedAsset = null;
            }
        }

        private void Card_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _draggedAsset == null)
                return;

            Point currentPoint = e.GetPosition(null);
            Vector diff = _dragStartPoint - currentPoint;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                if (sender is FrameworkElement element)
                {
                    var data = new DataObject();
                    data.SetData(typeof(AssetWipRecord), _draggedAsset);
                    data.SetData(typeof(AssetWipRecord).FullName, _draggedAsset);

                    try
                    {
                        DragDrop.DoDragDrop(element, data, DragDropEffects.Move);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[DragDrop] Error during DoDragDrop: {ex.Message}");
                    }
                    finally
                    {
                        _draggedAsset = null;
                    }
                }
            }
        }

        private void Lane_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(AssetWipRecord)) ||
                e.Data.GetDataPresent(typeof(AssetWipRecord).FullName))
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void Lane_DragEnter(object sender, DragEventArgs e)
        {
            if (sender is Border border &&
                (e.Data.GetDataPresent(typeof(AssetWipRecord)) ||
                 e.Data.GetDataPresent(typeof(AssetWipRecord).FullName)))
            {
                border.BorderThickness = new Thickness(2.5);
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            }
        }

        private void Lane_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is Border border)
            {
                border.BorderThickness = new Thickness(1.5);
            }
        }

        private void Lane_Drop(object sender, DragEventArgs e)
        {
            if (sender is Border border)
            {
                border.BorderThickness = new Thickness(1.5);

                AssetWipRecord asset = null;
                if (e.Data.GetDataPresent(typeof(AssetWipRecord)))
                {
                    asset = e.Data.GetData(typeof(AssetWipRecord)) as AssetWipRecord;
                }
                else if (e.Data.GetDataPresent(typeof(AssetWipRecord).FullName))
                {
                    asset = e.Data.GetData(typeof(AssetWipRecord).FullName) as AssetWipRecord;
                }

                if (asset != null && border.Tag is string targetQueueStr)
                {
                    if (Enum.TryParse<AssetQueueStatus>(targetQueueStr, out var targetQueue))
                    {
                        ViewModel.WipBoard.TransitionAsset(asset, targetQueue);
                        e.Handled = true;
                    }
                }
            }
        }

        private void MenuItemMoveQueue_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.Tag is string targetQueueStr)
            {
                AssetWipRecord asset = null;
                if (item.DataContext is AssetWipRecord directAsset)
                {
                    asset = directAsset;
                }
                else if (item.Parent is ContextMenu cm && cm.PlacementTarget is FrameworkElement fe && fe.DataContext is AssetWipRecord contextAsset)
                {
                    asset = contextAsset;
                }

                if (asset != null && Enum.TryParse<AssetQueueStatus>(targetQueueStr, out var targetQueue))
                {
                    ViewModel.WipBoard.TransitionAsset(asset, targetQueue);
                }
            }
        }

        #endregion
    }
}
