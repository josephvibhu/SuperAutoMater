using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
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
        }

        private void BtnCopyWebUrl_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(ViewModel.ManagerWebUrl);
                ViewModel.StatusMessage = $"✓ Copied {ViewModel.ManagerWebUrl} to clipboard!";
                Process.Start(new ProcessStartInfo(ViewModel.ManagerWebUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ViewModel.StatusMessage = $"Web HUD: {ViewModel.ManagerWebUrl} ({ex.Message})";
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
    }
}
