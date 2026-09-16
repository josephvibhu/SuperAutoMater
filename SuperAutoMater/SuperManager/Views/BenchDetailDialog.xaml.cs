using System;
using System.Diagnostics;
using System.Windows;
using SuperManager.Models;
using SuperManager.Services;

namespace SuperManager.Views
{
    public partial class BenchDetailDialog : Window
    {
        private readonly BenchDevice _bench;
        private bool _isInitialized = false;

        public BenchDetailDialog() : this(new BenchDevice()) { }

        public BenchDetailDialog(BenchDevice bench)
        {
            _bench = bench ?? new BenchDevice();
            InitializeComponent();
            _isInitialized = true;

            PopulateData();

            KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape)
                {
                    Close();
                }
            };
        }

        private async void PopulateData()
        {
            if (!_isInitialized || _bench == null) return;

            TxtMachineName.Text = _bench.MachineName;
            TxtIpBadge.Text = $"{_bench.IpAddress}:{_bench.Port}";
            TxtModelSerial.Text = $"{_bench.Model} · S/N: {_bench.Serial}";
            TxtGradeBadge.Text = _bench.Grade;

            TxtProgressText.Text = _bench.ProgressText;
            ProgBarPipeline.Maximum = _bench.TotalCount > 0 ? _bench.TotalCount : 11;
            ProgBarPipeline.Value = _bench.PassedCount;

            TxtActiveTest.Text = _bench.Status;
            TxtHeartbeat.Text = $"Last seen: {_bench.LastSeenSummary}";

            // Trigger deep telemetry fetch
            await FleetCommandService.Instance.FetchDeepStatusAsync(_bench);

            TxtCpuSpec.Text = string.IsNullOrEmpty(_bench.Cpu) ? "CPU Telemetry Standby" : _bench.Cpu;
            TxtCpuThermals.Text = $"{_bench.CpuTemp}°C · Stress Load: {_bench.CpuUsage}%";
            TxtRamSpec.Text = string.IsNullOrEmpty(_bench.Ram) ? "RAM Telemetry Standby" : _bench.Ram;
            TxtBatteryCharge.Text = $"{_bench.Battery}";
            TxtBatteryHealth.Text = $"{_bench.BatteryHealth}% Health";
            TxtStorageSpec.Text = string.IsNullOrEmpty(_bench.Storage) ? "Storage Standby" : _bench.Storage;
            TxtStorageHealth.Text = $"{_bench.StorageHealth} Health";
        }

        private async void BtnPing_Click(object sender, RoutedEventArgs e)
        {
            bool ok = await FleetCommandService.Instance.SendPingAsync(_bench);
            MessageBox.Show(ok ? $"✓ Ping sent to {_bench.MachineName}! Laptop is chiming." : "❌ Ping failed to connect.", "Bench Ping", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnDownloadCert_Click(object sender, RoutedEventArgs e)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string savedPath = await FleetCommandService.Instance.FetchCertificateAsync(_bench, System.IO.Path.Combine(desktop, "SuperManager_Certificates"));
            if (!string.IsNullOrEmpty(savedPath))
            {
                MessageBox.Show($"✓ Certificate successfully archived to:\n{savedPath}", "QC Certificate Download", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("❌ Failed to pull certificate. Ensure testing is complete on bench.", "QC Certificate Download", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnOpenWeb_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = _bench.Url, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open browser: {ex.Message}");
            }
        }

        private async void BtnRemotePass_Click(object sender, RoutedEventArgs e)
        {
            bool ok = await FleetCommandService.Instance.SendPassActionAsync(_bench);
            if (ok)
            {
                _bench.PassedCount = Math.Min(_bench.TotalCount, _bench.PassedCount + 1);
                PopulateData();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
