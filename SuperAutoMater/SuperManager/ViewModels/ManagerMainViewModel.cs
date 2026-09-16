using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Data;
using SuperManager.Models;
using SuperManager.Services;

namespace SuperManager.ViewModels
{
    public class ManagerMainViewModel : INotifyPropertyChanged
    {
        private string _searchText = "";
        private string _statusFilter = "ALL";
        private BenchDevice _selectedBench;
        private FleetKpiSummary _kpi = new FleetKpiSummary();
        private string _statusMessage = "Ready · Listening on UDP 9876 & Port 9000";

        public ObservableCollection<BenchDevice> AllDevices => FleetDiscoveryService.Instance.Devices;
        public ICollectionView FilteredDevices { get; }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetField(ref _searchText, value))
                {
                    FilteredDevices.Refresh();
                }
            }
        }

        public string StatusFilter
        {
            get => _statusFilter;
            set
            {
                if (SetField(ref _statusFilter, value))
                {
                    FilteredDevices.Refresh();
                }
            }
        }

        public BenchDevice SelectedBench
        {
            get => _selectedBench;
            set => SetField(ref _selectedBench, value);
        }

        public FleetKpiSummary Kpi
        {
            get => _kpi;
            private set => SetField(ref _kpi, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetField(ref _statusMessage, value);
        }

        public string ManagerWebUrl => ManagerWebServer.Instance.DashboardUrl;
        public WipBoardViewModel WipBoard { get; } = new WipBoardViewModel();

        public ManagerMainViewModel()
        {
            FilteredDevices = CollectionViewSource.GetDefaultView(AllDevices);
            FilteredDevices.Filter = FilterDevicePredicate;

            FleetDiscoveryService.Instance.FleetUpdated += OnFleetUpdated;
            UpdateKpis();
        }

        private bool FilterDevicePredicate(object obj)
        {
            if (obj is not BenchDevice dev) return false;

            // 1. Text filter
            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                string q = _searchText.Trim().ToLowerInvariant();
                bool match = (dev.MachineName?.ToLowerInvariant().Contains(q) ?? false) ||
                             (dev.Serial?.ToLowerInvariant().Contains(q) ?? false) ||
                             (dev.Model?.ToLowerInvariant().Contains(q) ?? false) ||
                             (dev.IpAddress?.ToLowerInvariant().Contains(q) ?? false);
                if (!match) return false;
            }

            // 2. Status filter
            return _statusFilter switch
            {
                "TESTING" => dev.PassedCount > 0 && dev.PassedCount < dev.TotalCount,
                "PASSED" => dev.PassedCount >= dev.TotalCount && dev.TotalCount > 0,
                "ALERTS" => dev.Alert || dev.CpuTemp >= 90,
                _ => true
            };
        }

        private void OnFleetUpdated()
        {
            UpdateKpis();
            WipBoard.RefreshBoard();
            OnPropertyChanged(nameof(ManagerWebUrl));
        }

        public void UpdateKpis()
        {
            Kpi = InventoryStorageService.Instance.ComputeKpiSummary(AllDevices);
        }

        public async Task<bool> PingBenchAsync(BenchDevice bench)
        {
            if (bench == null) return false;
            StatusMessage = $"Pinging {bench.MachineName} ({bench.IpAddress})...";
            bool ok = await FleetCommandService.Instance.SendPingAsync(bench);
            StatusMessage = ok ? $"✓ Ping sent to {bench.MachineName}!" : $"❌ Failed to reach {bench.MachineName}";
            return ok;
        }

        public async Task<string> DownloadCertificateAsync(BenchDevice bench)
        {
            if (bench == null) return null;
            StatusMessage = $"Fetching PDF Certificate from {bench.MachineName}...";
            string archiveDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "SuperManager_Certificates");
            string savedPath = await FleetCommandService.Instance.FetchCertificateAsync(bench, archiveDir);

            if (!string.IsNullOrEmpty(savedPath))
            {
                StatusMessage = $"✓ Archived certificate to Desktop/SuperManager_Certificates!";
                // Persist into inventory record
                InventoryStorageService.Instance.SaveRecord(new InventoryRecord
                {
                    SerialNumber = bench.Serial,
                    MachineName = bench.MachineName,
                    Model = bench.Model,
                    Grade = bench.Grade,
                    PassedCount = bench.PassedCount,
                    TotalCount = bench.TotalCount,
                    BatteryHealth = $"{bench.BatteryHealth}%",
                    Storage = bench.Storage,
                    Cpu = bench.Cpu,
                    Ram = bench.Ram,
                    CertificatePath = savedPath
                });
                UpdateKpis();
            }
            else
            {
                StatusMessage = $"❌ Failed to download certificate from {bench.MachineName}";
            }

            return savedPath;
        }

        public string ExportInventoryCsv()
        {
            try
            {
                string exportDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "SuperManager_Reports");
                Directory.CreateDirectory(exportDir);
                string exportPath = Path.Combine(exportDir, $"Fleet_Intake_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                InventoryStorageService.Instance.ExportToCsv(exportPath);
                StatusMessage = $"✓ Exported fleet inventory CSV to Desktop!";
                return exportPath;
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ CSV Export error: {ex.Message}";
                return null;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }
    }
}
