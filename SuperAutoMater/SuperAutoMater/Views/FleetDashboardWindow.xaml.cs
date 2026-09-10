using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SuperAutoMater.Wpf.Services;
using SuperAutoMater.Wpf.ViewModels;

namespace SuperAutoMater.Wpf.Views
{
    public partial class FleetDashboardWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private DispatcherTimer _refreshTimer;

        public FleetDashboardWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;

            Loaded += FleetDashboardWindow_Loaded;
            Closed += FleetDashboardWindow_Closed;

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _refreshTimer.Tick += (s, e) => RefreshDashboard();
        }

        private void FleetDashboardWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var fleet = WarehouseFleetService.Instance;
            fleet.StartService(_viewModel);

            ImgQrCode.Source = fleet.QrCodeBitmap;
            TxtDashboardUrl.Text = fleet.LocalDashboardUrl;
            ItemsPeerBenches.ItemsSource = fleet.OnlineBenches;

            fleet.FleetUpdated += OnFleetUpdated;

            RefreshDashboard();
            _refreshTimer.Start();
        }

        private void FleetDashboardWindow_Closed(object sender, EventArgs e)
        {
            _refreshTimer?.Stop();
            WarehouseFleetService.Instance.FleetUpdated -= OnFleetUpdated;
        }

        private void OnFleetUpdated()
        {
            Dispatcher.InvokeAsync(() =>
            {
                int peerCount = WarehouseFleetService.Instance.OnlineBenches.Count;
                int totalBenches = peerCount + 1;
                TxtBenchesOnlineCount.Text = totalBenches == 1
                    ? "1 BENCH ONLINE (THIS NODE)"
                    : $"{totalBenches} BENCHES ONLINE ON SUBNET";
            });
        }

        private void RefreshDashboard()
        {
            var hw = HardwareDiagnosticsService.Instance;

            TxtLocalHostName.Text = Environment.MachineName;
            TxtLocalModel.Text = !string.IsNullOrEmpty(hw.SystemIdentity.Model) && hw.SystemIdentity.Model != "Detecting Chassis..."
                ? hw.SystemIdentity.Model
                : "Standard Laptop Chassis";
            TxtLocalSerial.Text = $"S/N: {hw.SystemIdentity.Serial}";
            TxtLocalIp.Text = $"URL: {WarehouseFleetService.Instance.LocalDashboardUrl}";

            if (_viewModel != null)
            {
                int passed = _viewModel.TestPipeline?.Count(t => t.IsPassed) ?? 0;
                int total = _viewModel.TestPipeline?.Count ?? 10;
                TxtLocalPassCount.Text = $"{passed}/{total} PASSED";
                TxtLocalGrade.Text = _viewModel.Grade ?? "GRADE A+";
            }

            int peerCount = WarehouseFleetService.Instance.OnlineBenches.Count;
            int totalOnline = peerCount + 1;
            TxtBenchesOnlineCount.Text = totalOnline == 1
                ? "1 BENCH ONLINE (THIS NODE)"
                : $"{totalOnline} BENCHES ONLINE ON SUBNET";
        }

        private void BtnCopyUrl_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(WarehouseFleetService.Instance.LocalDashboardUrl);
                MessageBox.Show($"Copied LAN Dashboard URL to clipboard:\n{WarehouseFleetService.Instance.LocalDashboardUrl}", "URL Copied", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
        }

        private void BtnOpenBrowser_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string url = WarehouseFleetService.Instance.LocalDashboardUrl;
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch { }
        }

        private void BtnOpenPeer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement elem && elem.Tag is string url && !string.IsNullOrEmpty(url))
                {
                    Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                }
            }
            catch { }
        }

        private void BtnForceBeacon_Click(object sender, RoutedEventArgs e)
        {
            RefreshDashboard();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
            else if (e.Key == Key.F5)
            {
                RefreshDashboard();
            }
            else if (e.Key == Key.O)
            {
                BtnOpenBrowser_Click(this, new RoutedEventArgs());
            }
        }
    }
}
