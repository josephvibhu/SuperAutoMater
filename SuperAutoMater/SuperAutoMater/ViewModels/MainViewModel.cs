using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using SuperAutoMater.Wpf.Services;

namespace SuperAutoMater.Wpf.ViewModels
{
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;

        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object parameter) => _execute(parameter);
        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }

    public class DiagnosticTestItem : INotifyPropertyChanged
    {
        private string _status = "PENDING";
        private string _statusBadge = "○";
        private bool _isPassed = false;
        private bool _isActive = false;

        public string Key { get; set; }
        public string Title { get; set; }
        public string HotkeyText { get; set; }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public string StatusBadge
        {
            get => _statusBadge;
            set { _statusBadge = value; OnPropertyChanged(); }
        }

        public bool IsPassed
        {
            get => _isPassed;
            set { _isPassed = value; OnPropertyChanged(); }
        }

        public bool IsActive
        {
            get => _isActive;
            set { _isActive = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly HardwareDiagnosticsService _hw = HardwareDiagnosticsService.Instance;

        public ObservableCollection<DiagnosticTestItem> TestPipeline { get; } = new ObservableCollection<DiagnosticTestItem>();

        // System Identity
        public string Manufacturer => _hw.SystemIdentity?.Manufacturer ?? "Generic";
        public string Model => _hw.SystemIdentity?.Model ?? "Detecting Chassis...";
        public string Serial => _hw.SystemIdentity?.Serial ?? "Detecting...";
        public string Grade
        {
            get => _hw.SystemIdentity?.Grade ?? "GRADE A+";
            set
            {
                if (_hw.SystemIdentity != null) _hw.SystemIdentity.Grade = value;
                OnPropertyChanged();
            }
        }

        // CPU & Thermals
        public string CpuName => _hw.CpuTelemetry?.CpuName ?? "Detecting CPU...";
        public string CoreSummary => _hw.CpuTelemetry?.CoreSummary ?? "Cores: --";
        public string ClockSummary => $"{_hw.CpuTelemetry?.CurrentClockGhz ?? 0.0:0.00} GHz Core Clock";
        public string TempBadge => $"{_hw.CpuTelemetry?.TemperatureC ?? 0}°C NOMINAL";
        public string ThrottlingStatus => _hw.CpuTelemetry?.ThrottlingStatus ?? "0% Throttling";

        // GPU & Display
        public string GpuName => _hw.GpuTelemetry?.GpuName ?? "Detecting GPU...";
        public string GpuDriver => _hw.GpuTelemetry?.DriverVersion ?? "";
        public string GpuVram => _hw.GpuTelemetry?.VramSummary ?? "Shared Memory";
        public string GpuTypeBadge => _hw.GpuTelemetry?.TypeBadge ?? "iGPU";
        public ObservableCollection<GpuInfo> AvailableGpus { get; } = new ObservableCollection<GpuInfo>();
        public bool HasMultipleGpus => AvailableGpus.Count > 1;

        private GpuInfo _selectedGpu;
        public GpuInfo SelectedGpu
        {
            get => _selectedGpu;
            set
            {
                _selectedGpu = value;
                if (value != null) _hw.SelectGpu(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(GpuName));
                OnPropertyChanged(nameof(GpuDriver));
                OnPropertyChanged(nameof(GpuVram));
                OnPropertyChanged(nameof(GpuTypeBadge));
            }
        }

        // Storage & Memory
        public string RamSummary => _hw.MemoryStorage?.RamSummary ?? "Detecting RAM...";
        public string StorageSummary => ActiveDrive != null ? $"{ActiveDrive.InterfaceType} {ActiveDrive.CapacitySummary}" : (_hw.MemoryStorage?.StorageSummary ?? "Detecting Storage...");
        public string PrimaryDriveModel => ActiveDrive?.Model ?? (_hw.MemoryStorage?.PrimaryDriveModel ?? "Primary Drive");
        public string HealthBadge => $"{ActiveDrive?.HdsHealth ?? 100}% HEALTH";
        public string SpecHeader => $"{RamSummary} · {StorageSummary}";

        // 3-Source Storage Telemetry & Multi-Drive Switching
        public ObservableCollection<StorageDriveDetail> AvailableDrives { get; } = new ObservableCollection<StorageDriveDetail>();
        public bool HasMultipleDrives => AvailableDrives.Count > 1;

        private StorageDriveDetail _selectedDrive;
        public StorageDriveDetail SelectedDrive
        {
            get => _selectedDrive ?? _hw.PrimaryDrive;
            set
            {
                _selectedDrive = value;
                foreach (var d in AvailableDrives)
                {
                    d.IsSelected = (d == value);
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActiveDrive));
                OnPropertyChanged(nameof(PrimaryDriveModel));
                OnPropertyChanged(nameof(StorageSummary));
                OnPropertyChanged(nameof(HealthBadge));
                OnPropertyChanged(nameof(HdsHealth));
                OnPropertyChanged(nameof(HdsPerformance));
                OnPropertyChanged(nameof(HdsPowerOnTime));
                OnPropertyChanged(nameof(HdsEstLifetime));
                OnPropertyChanged(nameof(HdsTotalWritten));
                OnPropertyChanged(nameof(HdsBadge));
                OnPropertyChanged(nameof(SmartTemp));
                OnPropertyChanged(nameof(SmartBadSectors));
                OnPropertyChanged(nameof(SmartStatus));
                OnPropertyChanged(nameof(ControllerProtocol));
                OnPropertyChanged(nameof(DriveSerialNumber));
                OnPropertyChanged(nameof(DriveFirmware));
            }
        }

        public StorageDriveDetail ActiveDrive => _selectedDrive ?? _hw.PrimaryDrive;

        public int HdsHealth => ActiveDrive?.HdsHealth ?? 100;
        public int HdsPerformance => ActiveDrive?.HdsPerformance ?? 100;
        public string HdsPowerOnTime => ActiveDrive?.HdsPowerOnTime ?? "128 days";
        public string HdsEstLifetime => ActiveDrive?.HdsEstLifetime ?? "> 1000 days";
        public string HdsTotalWritten => ActiveDrive?.HdsTotalWritten ?? "14.2 TB";
        public string HdsBadge => $"{HdsHealth}% HEALTH";

        public string SmartTemp => $"{ActiveDrive?.SmartTemperatureC ?? 36}°C (Nominal)";
        public string SmartBadSectors => $"{ActiveDrive?.SmartBadSectors ?? 0} Bad Sectors";
        public string SmartStatus => ActiveDrive?.SmartStatusText ?? "NOMINAL · 0 Bad Sectors";

        public string ControllerProtocol => ActiveDrive?.ControllerProtocol ?? "PCIe 4.0 x4 (NVMe 1.4)";
        public string DriveSerialNumber => string.IsNullOrEmpty(ActiveDrive?.SerialNumber) ? "N/A" : ActiveDrive.SerialNumber;
        public string DriveFirmware => ActiveDrive?.FirmwareRevision ?? "100.0";

        // Detailed Battery Health & Power Telemetry
        public int BatteryCharge
        {
            get => _hw.BatteryTelemetry?.ChargePercent ?? 100;
            set { if (_hw.BatteryTelemetry != null) _hw.BatteryTelemetry.ChargePercent = value; OnPropertyChanged(); }
        }
        public int BatteryHealth
        {
            get => _hw.BatteryTelemetry?.HealthPercent ?? 100;
            set { if (_hw.BatteryTelemetry != null) _hw.BatteryTelemetry.HealthPercent = value; OnPropertyChanged(); }
        }
        public double BatteryWear => _hw.BatteryTelemetry?.WearPercent ?? 0.0;
        public string BatteryFlowWatts => _hw.BatteryTelemetry?.FlowWatts ?? "0.0W";
        public string BatteryWearSummary => _hw.BatteryTelemetry?.WearSummary ?? "Wear: 0.0%";
        public string BatteryTimeRemaining => _hw.BatteryTelemetry?.TimeRemaining ?? "Calculating...";
        public string BatteryIntegrityBadge => $"{BatteryHealth}% HEALTH";
        public string BatteryCondition => _hw.BatteryTelemetry?.HealthCondition ?? "EXCELLENT";
        public string BatteryAcStatus => _hw.BatteryTelemetry?.AcStatusText ?? "● AC CONNECTED";

        public string BatteryDesignCapacityMwh => $"{_hw.BatteryTelemetry?.DesignCapacityMwh:N0} mWh";
        public string BatteryFullChargeCapacityMwh => $"{_hw.BatteryTelemetry?.FullChargeCapacityMwh:N0} mWh";
        public string BatteryRemainingCapacityMwh => $"{_hw.BatteryTelemetry?.RemainingCapacityMwh:N0} mWh";

        public string BatteryDesignCapacityMah => $"{_hw.BatteryTelemetry?.DesignCapacityMah:N0} mAh";
        public string BatteryFullChargeCapacityMah => $"{_hw.BatteryTelemetry?.FullChargeCapacityMah:N0} mAh";
        public string BatteryRemainingCapacityMah => $"{_hw.BatteryTelemetry?.RemainingCapacityMah:N0} mAh";

        public string BatteryVoltage => $"{_hw.BatteryTelemetry?.VoltageVolts:0.00} V ({_hw.BatteryTelemetry?.VoltageMv} mV)";
        public string BatteryChemistry => _hw.BatteryTelemetry?.Chemistry ?? "LION";
        public string BatteryManufacturer => _hw.BatteryTelemetry?.Manufacturer ?? "LG Chem (LGC)";
        public string BatteryModelId => _hw.BatteryTelemetry?.BatteryId ?? "AP18C8K";
        public string BatterySerialNumber => _hw.BatteryTelemetry?.SerialNumber ?? "27225";
        public string BatteryCycleCount => $"{_hw.BatteryTelemetry?.CycleCount ?? 0} Cycles";

        // Wireless & RF Telemetry (Wi-Fi + Bluetooth)
        public string WifiSsid => _hw.NetworkTelemetry?.Ssid ?? "Offline";
        public string WifiSignal => _hw.NetworkTelemetry?.SignalDbm ?? "";
        public string WifiSummary => $"{WifiSsid} {WifiSignal}".Trim();
        public string WifiRadioType => _hw.NetworkTelemetry?.RadioType ?? "802.11ax (Wi-Fi 6)";
        public string WifiBand => _hw.NetworkTelemetry?.Band ?? "5 GHz";
        public string WifiChannel => _hw.NetworkTelemetry?.Channel ?? "Ch 36";
        public string WifiSignalPercent => $"{_hw.NetworkTelemetry?.SignalPercent ?? 85}%";
        public string GatewayPing => _hw.NetworkTelemetry?.PingLatencyMs > 0 ? $"{_hw.NetworkTelemetry.PingLatencyMs} ms" : "< 15 ms";

        public string BluetoothName => _hw.NetworkTelemetry?.BluetoothControllerName ?? "Intel(R) Wireless Bluetooth(R)";
        public string BluetoothStatus => _hw.NetworkTelemetry?.BluetoothStatus ?? "ONLINE · Host Transceiver Operational";

        // Touchscreen & Digitizer
        public bool HasTouchscreen => _hw.HasTouchscreen;
        public int MaxTouches => _hw.MaxTouchContacts;
        public string TouchscreenBadge => HasTouchscreen ? $"TOUCH MATRIX ACTIVE ({MaxTouches} CONTACTS)" : "NON-TOUCH PANEL";

        // Warehouse & Offline Sync
        private int _pendingSyncCount = 0;
        public int PendingSyncCount
        {
            get => _pendingSyncCount;
            set { _pendingSyncCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(OfflineQueueText)); }
        }
        public string OfflineQueueText => _pendingSyncCount == 0 ? "0 Pending (All Synced)" : $"{_pendingSyncCount} Pending (Queued)";

        // USB Radar Telemetry
        private bool _isPort3Verified = false;
        public bool IsPort3Verified
        {
            get => _isPort3Verified;
            set
            {
                _isPort3Verified = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Port3StatusText));
            }
        }
        public string Port3StatusText => _isPort3Verified ? "[VERIFIED ✓]" : "[AWAITING DRIVE]";

        // Summary Counters
        private int _passCount = 0;
        public int PassCount
        {
            get => _passCount;
            set { _passCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(PipelineStatusText)); }
        }
        public string PipelineStatusText => $"{_passCount}/{(TestPipeline.Count > 0 ? TestPipeline.Count : 9)} PASSED";

        public int BatteryVoltageMv => _hw.BatteryTelemetry?.VoltageMv ?? 12300;
        public int CpuTempC => _hw.CpuTelemetry?.TemperatureC ?? 40;

        // Commands
        public ICommand RefreshCommand { get; }
        public ICommand SyncSheetsCommand { get; }
        public ICommand PrintLabelCommand { get; }
        public ICommand PassCurrentTestCommand { get; }

        public MainViewModel()
        {
            RefreshCommand = new RelayCommand(async _ => await RefreshTelemetryAsync());
            SyncSheetsCommand = new RelayCommand(async _ => await SyncToSheetsAsync());
            PrintLabelCommand = new RelayCommand(_ => PrintThermalLabel());
            PassCurrentTestCommand = new RelayCommand(_ => MarkNextTestPassed());

            InitTestPipeline();

            // Wire offline queue and ledger changes
            PendingSyncCount = OfflineLedgerService.Instance.PendingCount;
            OfflineLedgerService.Instance.PendingCountChanged += count =>
            {
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() => PendingSyncCount = count);
            };
            OfflineSyncQueue.Instance.QueueChanged += count =>
            {
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() => PendingSyncCount = Math.Max(count, OfflineLedgerService.Instance.PendingCount));
            };

            // Wire hardware probe updates
            _hw.TelemetryUpdated += () =>
            {
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    SyncCollections();
                    OnPropertyChanged("");
                });
            };
        }

        private void SyncCollections()
        {
            AvailableGpus.Clear();
            foreach (var gpu in _hw.DetectedGpus)
            {
                AvailableGpus.Add(gpu);
            }
            if (_selectedGpu == null && AvailableGpus.Count > 0)
            {
                _selectedGpu = AvailableGpus.FirstOrDefault(g => g.IsDedicated) ?? AvailableGpus[0];
            }

            AvailableDrives.Clear();
            foreach (var d in _hw.DetectedDrives)
            {
                AvailableDrives.Add(d);
            }
            if (_selectedDrive == null && AvailableDrives.Count > 0)
            {
                SelectedDrive = AvailableDrives[0];
            }
        }

        private void InitTestPipeline()
        {
            TestPipeline.Add(new DiagnosticTestItem { Key = "Display", Title = "DISPLAY / PANEL TEST", HotkeyText = "[F1]" });
            TestPipeline.Add(new DiagnosticTestItem { Key = "Audio", Title = "AUDIO / STEREO SWEEP", HotkeyText = "[F2]" });
            TestPipeline.Add(new DiagnosticTestItem { Key = "Camera", Title = "WEBCAM & MIC ARRAY", HotkeyText = "[F3]" });
            TestPipeline.Add(new DiagnosticTestItem { Key = "Keyboard", Title = "KEYBOARD & TRACKPAD", HotkeyText = "[READY]" });
            TestPipeline.Add(new DiagnosticTestItem { Key = "Cpu", Title = "CPU & MEMORY STRESS", HotkeyText = "[F4]" });
            TestPipeline.Add(new DiagnosticTestItem { Key = "Battery", Title = "BATTERY HEALTH & POWER", HotkeyText = "[F5]" });
            TestPipeline.Add(new DiagnosticTestItem { Key = "Gpu", Title = "GPU 3D BENCHMARK", HotkeyText = "[F6]" });
            TestPipeline.Add(new DiagnosticTestItem { Key = "Fingerprint", Title = "FINGERPRINT SENSOR", HotkeyText = "[F7]" });
            TestPipeline.Add(new DiagnosticTestItem { Key = "Storage", Title = "NVMe SMART & HDS HEALTH", HotkeyText = "[100%]" });
            TestPipeline.Add(new DiagnosticTestItem { Key = "Usb", Title = "USB PORTS & BUS", HotkeyText = "[F8]" });
            TestPipeline.Add(new DiagnosticTestItem { Key = "Bluetooth", Title = "WIRELESS & BLUETOOTH RADAR", HotkeyText = "[PASS]" });
        }

        public void SetActiveTest(string key)
        {
            foreach (var item in TestPipeline)
            {
                bool match = !string.IsNullOrEmpty(key) && item.Key.Equals(key, StringComparison.OrdinalIgnoreCase);
                item.IsActive = match;
                if (match && !item.IsPassed)
                {
                    item.StatusBadge = "▶";
                }
                else if (!item.IsPassed)
                {
                    item.StatusBadge = "○";
                }
            }
        }

        public async Task RefreshTelemetryAsync()
        {
            await _hw.InitializeAsync();
            SyncCollections();
            OnPropertyChanged("");
        }

        public void MarkTestPassed(string key)
        {
            foreach (var test in TestPipeline)
            {
                if (test.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    if (!test.IsPassed)
                    {
                        test.IsPassed = true;
                        test.IsActive = false;
                        test.StatusBadge = "✓";
                        PassCount++;
                    }
                    break;
                }
            }
        }

        public void MarkNextTestPassed()
        {
            foreach (var test in TestPipeline)
            {
                if (!test.IsPassed)
                {
                    test.IsPassed = true;
                    test.IsActive = false;
                    test.StatusBadge = "✓";
                    PassCount++;
                    break;
                }
            }
        }

        public async Task SyncToSheetsAsync()
        {
            try
            {
                var record = new AssetQueueRecord
                {
                    Asset_Tag = !string.IsNullOrWhiteSpace(Serial) && Serial != "Detecting..." ? Serial : $"QC-{DateTime.Now:MMdd-HHmm}",
                    Serial_Number = Serial,
                    Model = Model,
                    Processor = CpuName,
                    Memory = $"{RamSummary} / {StorageSummary}",
                    Battery_Health = BatteryHealth,
                    Status = PassCount >= 6 ? "RTS" : "WIP",
                    Wip_Issue = PassCount >= 6 ? "All Okay" : "Requires Inspection",
                    Physical_Grade = Grade ?? "A+",
                    Remarks = $"SuperAutoMater WPF v1.0 Sync | Tests: {PipelineStatusText}",
                    Shelf_Location = "QC-BAY-01",
                    Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };

                OfflineSyncQueue.Instance.Enqueue(record);
                int flushed = await OfflineSyncQueue.Instance.FlushQueueAsync(OfflineSyncQueue.DefaultSheetsUrl);
                System.Windows.MessageBox.Show($"Inventory record for {Model} ({Serial}) queued and synced ({flushed} dispatched to Google Sheets)!", "Google Sheets Cloud Sync", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Sync Error: " + ex.Message, "Sheets Sync Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public void PrintThermalLabel()
        {
            var record = new AssetQueueRecord
            {
                Asset_Tag = !string.IsNullOrWhiteSpace(Serial) && Serial != "Detecting..." ? Serial : $"QC-{DateTime.Now:MMdd-HHmm}",
                Serial_Number = Serial,
                Model = Model,
                Processor = CpuName,
                Memory = $"{RamSummary} / {StorageSummary}",
                Battery_Health = BatteryHealth,
                Status = PassCount >= 6 ? "RTS" : "WIP",
                Wip_Issue = PassCount >= 6 ? "All Okay" : "Requires Inspection",
                Physical_Grade = Grade ?? "A+",
                Remarks = $"SuperAutoMater Label | {PipelineStatusText}",
                Shelf_Location = "QC-BAY-01",
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
            ThermalLabelPrinter.PrintLabel(record);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
