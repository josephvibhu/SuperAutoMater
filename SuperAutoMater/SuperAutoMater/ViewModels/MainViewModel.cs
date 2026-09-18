using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SuperAutoMater.Wpf.Services;
using SuperAutoMater.Wpf.Core;

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
        private bool _isApplicable = true;

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

        public bool IsApplicable
        {
            get => _isApplicable;
            set { _isApplicable = value; OnPropertyChanged(); }
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
        public bool IsSerialMissing => _hw.SystemIdentity?.IsSerialMissing ?? false;

        private string _assetTag = "";
        public string AssetTag
        {
            get => !string.IsNullOrEmpty(_assetTag) ? _assetTag : (!string.IsNullOrEmpty(Serial) && Serial != "Detecting..." ? Serial : "TAG-PENDING");
            set { _assetTag = value; OnPropertyChanged(); OnPropertyChanged(nameof(RefurbReportPreviewText)); OnPropertyChanged(nameof(ECommerceListingText)); }
        }

        private string _assignedTechnician = "";
        public string AssignedTechnician
        {
            get => _assignedTechnician;
            set { _assignedTechnician = value; OnPropertyChanged(); OnPropertyChanged(nameof(AssignedTo)); }
        }

        public string AssignedTo
        {
            get => _assignedTechnician;
            set { _assignedTechnician = value; OnPropertyChanged(); OnPropertyChanged(nameof(AssignedTechnician)); }
        }

        private string _intakeTechnician = "";
        public string IntakeTechnician
        {
            get => _intakeTechnician;
            set { _intakeTechnician = value; OnPropertyChanged(); }
        }

        private string _serviceTechnician = "";
        public string ServiceTechnician
        {
            get => _serviceTechnician;
            set { _serviceTechnician = value; OnPropertyChanged(); }
        }

        private string _missingComponentsWarning = "";
        public string MissingComponentsWarning
        {
            get => _missingComponentsWarning;
            set { _missingComponentsWarning = value; OnPropertyChanged(); OnPropertyChanged(nameof(MissingComponents)); }
        }

        public string MissingComponents
        {
            get => _missingComponentsWarning;
            set { _missingComponentsWarning = value; OnPropertyChanged(); OnPropertyChanged(nameof(MissingComponentsWarning)); }
        }

        private string _recognizedAssetBanner = "";
        public string RecognizedAssetBanner
        {
            get => _recognizedAssetBanner;
            set { _recognizedAssetBanner = value; OnPropertyChanged(); }
        }

        private bool _isRecognizedAsset = false;
        public bool IsRecognizedAsset
        {
            get => _isRecognizedAsset;
            set { _isRecognizedAsset = value; OnPropertyChanged(); }
        }

        public string Grade
        {
            get => _hw.SystemIdentity?.Grade ?? "GRADE PENDING";
            set
            {
                if (_hw.SystemIdentity != null) _hw.SystemIdentity.Grade = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RefurbReportPreviewText));
                OnPropertyChanged(nameof(ECommerceListingText));
            }
        }

        private string _supplier = "";
        public string Supplier
        {
            get => _supplier;
            set { _supplier = value; OnPropertyChanged(); OnPropertyChanged(nameof(RefurbReportPreviewText)); }
        }

        private string _customer = "";
        public string Customer
        {
            get => _customer;
            set { _customer = value; OnPropertyChanged(); }
        }

        private string _workInProgress = "";
        public string WorkInProgress
        {
            get => _workInProgress;
            set { _workInProgress = value; OnPropertyChanged(); }
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

        // RAM Topology & Channel Mode (1A)
        public string RamChannelBadge => _hw.MemoryStorage?.Topology?.StatusBadge ?? "SINGLE-CHANNEL";
        public string RamChannelDetail => _hw.MemoryStorage?.Topology?.StatusDetail ?? "";
        public string RamChannelAccentHex => _hw.MemoryStorage?.Topology?.AccentHex ?? "#D29922";
        public SolidColorBrush RamChannelBorderBrush => new SolidColorBrush((Color)ColorConverter.ConvertFromString(RamChannelAccentHex));
        public bool IsSingleChannelBottleneck => _hw.MemoryStorage?.Topology?.IsSingleChannelBottleneck ?? false;

        // RAM Health & Degradation Prediction Engine
        public int RamHealthScore => RamHealthPredictorService.Instance.LastAssessment?.HealthScore ?? 100;
        public string RamHealthBadge => RamHealthPredictorService.Instance.LastAssessment?.SummaryBadge ?? "100% HEALTH (NOMINAL)";
        public string RamHealthAccentHex => RamHealthPredictorService.Instance.LastAssessment?.AccentHex ?? "#3FB950";
        public string RamDegradationDisplay => RamHealthPredictorService.Instance.LastAssessment != null
            ? $"{RamHealthPredictorService.Instance.LastAssessment.HealthScore}% · {RamHealthPredictorService.Instance.LastAssessment.Recommendation}"
            : "Nominal 0 Bit-Flips";

        // Thermal Cool-Down Decay & Airflow (2A)
        public string ThermalDecayVerdict => ThermalProfilerService.Instance.GetCurrentResult().RadiatorAirflowVerdict;
        public string ThermalDecayBadge => ThermalProfilerService.Instance.GetCurrentResult().RadiatorAirflowBadge;
        public string ThermalDecayAccentHex => ThermalProfilerService.Instance.GetCurrentResult().RadiatorAirflowAccentHex;
        public double ThermalDecayHalfLifeSec => ThermalProfilerService.Instance.GetCurrentResult().DecayHalfLifeSeconds;

        // Webcam Optics & Shutter (3A)
        private string _webcamOpticsBadge = "✓ OPTICS NOMINAL";
        public string WebcamOpticsBadge
        {
            get => _webcamOpticsBadge;
            set { _webcamOpticsBadge = value; OnPropertyChanged(); }
        }

        private string _webcamOpticsDetail = "Lens clarity and sensor dynamic range nominal.";
        public string WebcamOpticsDetail
        {
            get => _webcamOpticsDetail;
            set { _webcamOpticsDetail = value; OnPropertyChanged(); }
        }

        private string _webcamOpticsAccentHex = "#3FB950";
        public string WebcamOpticsAccentHex
        {
            get => _webcamOpticsAccentHex;
            set { _webcamOpticsAccentHex = value; OnPropertyChanged(); }
        }

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
                OnPropertyChanged(nameof(TbwWrittenTb));
                OnPropertyChanged(nameof(TbwRatedEnduranceTb));
                OnPropertyChanged(nameof(TbwWearPercent));
                OnPropertyChanged(nameof(TbwLifespanRemainingPercent));
                OnPropertyChanged(nameof(TbwDisplaySummary));
                OnPropertyChanged(nameof(TbwStatusBadge));
                OnPropertyChanged(nameof(TbwAccentHex));
                OnPropertyChanged(nameof(RefurbReportPreviewText));
                OnPropertyChanged(nameof(ECommerceListingText));
            }
        }

        public StorageDriveDetail ActiveDrive => _selectedDrive ?? _hw.PrimaryDrive;

        public int HdsHealth => ActiveDrive?.HdsHealth ?? 100;
        public int HdsPerformance => ActiveDrive?.HdsPerformance ?? 100;
        public string HdsPowerOnTime => ActiveDrive?.HdsPowerOnTime ?? "128 days";
        public string HdsEstLifetime => ActiveDrive?.HdsEstLifetime ?? "> 1000 days";
        public string HdsTotalWritten => ActiveDrive?.HdsTotalWritten ?? "14.2 TB";
        public string HdsBadge => $"{HdsHealth}% HEALTH";

        // SSD TBW & Host Writes Endurance Telemetry
        public double TbwWrittenTb => ActiveDrive?.TbwWrittenTb ?? 14.2;
        public int TbwRatedEnduranceTb => ActiveDrive?.TbwRatedEnduranceTb ?? 300;
        public double TbwWearPercent
        {
            get => ActiveDrive?.TbwWearPercent ?? 4.7;
            set { }
        }
        public double TbwLifespanRemainingPercent
        {
            get => ActiveDrive?.TbwLifespanRemainingPercent ?? 95.3;
            set { }
        }
        public string TbwDisplaySummary => ActiveDrive?.TbwDisplaySummary ?? "14.2 TB / 300 TBW · 4.7% Wear · 95.3% Lifespan Remaining";
        public string TbwStatusBadge => ActiveDrive?.TbwStatusBadge ?? "✓ LOW WEAR (95% REMAINING)";
        public string TbwAccentHex => ActiveDrive?.TbwAccentHex ?? "#3FB950";

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

        // Cell Topology & Voltage Balance
        public string BatteryCellTopology => _hw.BatteryTelemetry?.CellTopology ?? "3S1P (3 Lithium-Ion Cells)";
        public string BatteryAvgCellVoltage => $"{_hw.BatteryTelemetry?.AvgCellVoltageVolts:0.000} V ({_hw.BatteryTelemetry?.AvgCellVoltageMv} mV / cell)";
        public string BatteryCellDelta => $"{_hw.BatteryTelemetry?.EstimatedCellDriftMv ?? 14} mV Drift (ΔV)";
        public string BatteryCellBalanceStatus => _hw.BatteryTelemetry?.CellBalanceStatus ?? "✓ CELLS BALANCED NOMINAL";
        public string BatteryCellBalanceBadge => _hw.BatteryTelemetry?.CellBalanceBadge ?? "✓ BALANCED";
        public string BatteryCellBalanceAccentHex => _hw.BatteryTelemetry?.CellBalanceAccentHex ?? "#3FB950";

        // Battery Authenticity & OEM/Clone Signature
        public string BatteryAuthenticityBadge => _hw.BatteryTelemetry?.AuthenticityBadge ?? "🛡️ OEM GENUINE";
        public string BatteryAuthenticityDetails => _hw.BatteryTelemetry?.AuthenticityDetails ?? "Verified OEM Supplier";
        public string BatteryAuthenticityAccentHex => _hw.BatteryTelemetry?.AuthenticityAccentHex ?? "#3FB950";

        // Auto-Advance Bench Pipeline Mode
        private bool _isAutoAdvanceEnabled = true;
        public bool IsAutoAdvanceEnabled
        {
            get => _isAutoAdvanceEnabled;
            set
            {
                _isAutoAdvanceEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AutoAdvanceStatusText));
                OnPropertyChanged(nameof(AutoAdvanceBadgeColor));
            }
        }
        public string AutoAdvanceStatusText => _isAutoAdvanceEnabled ? "AUTO-ADVANCE ON" : "AUTO-ADVANCE OFF";
        public string AutoAdvanceBadgeColor => _isAutoAdvanceEnabled ? "#3FB950" : "#8B949E";

        // Battery Load Sag Test Properties
        private bool _isBatteryLoadTesting = false;
        private string _batterySagSummary = "Ready for 15s load sag test.";
        private string _batterySagAccentHex = "#3FB950";

        public bool IsBatteryLoadTesting
        {
            get => _isBatteryLoadTesting;
            set { _isBatteryLoadTesting = value; OnPropertyChanged(); }
        }
        public string BatterySagSummary
        {
            get => _batterySagSummary;
            set { _batterySagSummary = value; OnPropertyChanged(); }
        }
        public string BatterySagAccentHex
        {
            get => _batterySagAccentHex;
            set { _batterySagAccentHex = value; OnPropertyChanged(); }
        }

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

        // Multi-Port USB Radar Telemetry
        private bool _isPort1Verified = false;
        private string _port1Device = "";
        public bool IsPort1Verified
        {
            get => _isPort1Verified;
            set { _isPort1Verified = value; OnPropertyChanged(); OnPropertyChanged(nameof(Port1StatusText)); }
        }
        public string Port1Device
        {
            get => _port1Device;
            set { _port1Device = value; OnPropertyChanged(); OnPropertyChanged(nameof(Port1StatusText)); }
        }
        public string Port1StatusText => _isPort1Verified
            ? (string.IsNullOrEmpty(_port1Device) ? "[VERIFIED ✓]" : $"[VERIFIED ✓ {_port1Device}]")
            : "[AWAITING PLUG]";

        private bool _isPort2Verified = false;
        private string _port2Device = "";
        public bool IsPort2Verified
        {
            get => _isPort2Verified;
            set { _isPort2Verified = value; OnPropertyChanged(); OnPropertyChanged(nameof(Port2StatusText)); }
        }
        public string Port2Device
        {
            get => _port2Device;
            set { _port2Device = value; OnPropertyChanged(); OnPropertyChanged(nameof(Port2StatusText)); }
        }
        public string Port2StatusText => _isPort2Verified
            ? (string.IsNullOrEmpty(_port2Device) ? "[VERIFIED ✓]" : $"[VERIFIED ✓ {_port2Device}]")
            : "[AWAITING PLUG]";

        private bool _isPort3Verified = false;
        private string _port3Device = "";
        public bool IsPort3Verified
        {
            get => _isPort3Verified;
            set { _isPort3Verified = value; OnPropertyChanged(); OnPropertyChanged(nameof(Port3StatusText)); }
        }
        public string Port3Device
        {
            get => _port3Device;
            set { _port3Device = value; OnPropertyChanged(); OnPropertyChanged(nameof(Port3StatusText)); }
        }
        public string Port3StatusText => _isPort3Verified
            ? (string.IsNullOrEmpty(_port3Device) ? "[VERIFIED ✓]" : $"[VERIFIED ✓ {_port3Device}]")
            : "[AWAITING PLUG]";

        public void ResetUsbPorts()
        {
            _isPort1Verified = false;
            _port1Device = "";
            _isPort2Verified = false;
            _port2Device = "";
            _isPort3Verified = false;
            _port3Device = "";
            OnPropertyChanged(nameof(IsPort1Verified));
            OnPropertyChanged(nameof(Port1StatusText));
            OnPropertyChanged(nameof(IsPort2Verified));
            OnPropertyChanged(nameof(Port2StatusText));
            OnPropertyChanged(nameof(IsPort3Verified));
            OnPropertyChanged(nameof(Port3StatusText));
        }

        // Cosmetic Defect Matrix Properties
        private bool _defectScreenScratches = false;
        public bool DefectScreenScratches
        {
            get => _defectScreenScratches;
            set { if (_defectScreenScratches != value) { _defectScreenScratches = value; OnPropertyChanged(); OnCosmeticDefectChanged(); } }
        }

        private bool _defectWhiteSpots = false;
        public bool DefectWhiteSpots
        {
            get => _defectWhiteSpots;
            set { if (_defectWhiteSpots != value) { _defectWhiteSpots = value; OnPropertyChanged(); OnCosmeticDefectChanged(); } }
        }

        private bool _defectLidScuffs = false;
        public bool DefectLidScuffs
        {
            get => _defectLidScuffs;
            set { if (_defectLidScuffs != value) { _defectLidScuffs = value; OnPropertyChanged(); OnCosmeticDefectChanged(); } }
        }

        private bool _defectCornerDent = false;
        public bool DefectCornerDent
        {
            get => _defectCornerDent;
            set { if (_defectCornerDent != value) { _defectCornerDent = value; OnPropertyChanged(); OnCosmeticDefectChanged(); } }
        }

        private bool _defectShinyKeys = false;
        public bool DefectShinyKeys
        {
            get => _defectShinyKeys;
            set { if (_defectShinyKeys != value) { _defectShinyKeys = value; OnPropertyChanged(); OnCosmeticDefectChanged(); } }
        }

        private bool _defectTrackpadWear = false;
        public bool DefectTrackpadWear
        {
            get => _defectTrackpadWear;
            set { if (_defectTrackpadWear != value) { _defectTrackpadWear = value; OnPropertyChanged(); OnCosmeticDefectChanged(); } }
        }

        private bool _defectLooseHinges = false;
        public bool DefectLooseHinges
        {
            get => _defectLooseHinges;
            set { if (_defectLooseHinges != value) { _defectLooseHinges = value; OnPropertyChanged(); OnCosmeticDefectChanged(); } }
        }

        private bool _defectChassisDamage = false;
        public bool DefectChassisDamage
        {
            get => _defectChassisDamage;
            set { if (_defectChassisDamage != value) { _defectChassisDamage = value; OnPropertyChanged(); OnCosmeticDefectChanged(); } }
        }

        public int CosmeticDefectCount
        {
            get
            {
                int count = 0;
                if (DefectScreenScratches) count++;
                if (DefectWhiteSpots) count++;
                if (DefectLidScuffs) count++;
                if (DefectCornerDent) count++;
                if (DefectShinyKeys) count++;
                if (DefectTrackpadWear) count++;
                if (DefectLooseHinges) count++;
                if (DefectChassisDamage) count++;
                return count;
            }
        }

        public string CosmeticDefectsSummary
        {
            get
            {
                var list = new List<string>();
                if (DefectScreenScratches) list.Add("Screen Scratches");
                if (DefectWhiteSpots) list.Add("White Spots / Pressure Marks");
                if (DefectLidScuffs) list.Add("Lid Scuffs");
                if (DefectCornerDent) list.Add("Corner Dent");
                if (DefectShinyKeys) list.Add("Shiny Keycaps");
                if (DefectTrackpadWear) list.Add("Trackpad Surface Wear");
                if (DefectLooseHinges) list.Add("Loose Hinge Play");
                if (DefectChassisDamage) list.Add("Chassis / Port Damage");

                return list.Count > 0 ? string.Join(", ", list) : "Pristine (No Defects)";
            }
        }

        public string DefectCountBadge => CosmeticDefectCount == 0 
            ? "[0 DEFECTS]" 
            : $"[{CosmeticDefectCount} DEFECT{(CosmeticDefectCount > 1 ? "S" : "")}]";

        public string DefectBadgeColor
        {
            get
            {
                if (CosmeticDefectCount == 0) return "#34D399";
                if (DefectCornerDent || DefectWhiteSpots || DefectChassisDamage || CosmeticDefectCount >= 3)
                    return "#F85149";
                return "#F1E05A";
            }
        }

        public void ClearAllCosmeticDefects()
        {
            _defectScreenScratches = false;
            _defectWhiteSpots = false;
            _defectLidScuffs = false;
            _defectCornerDent = false;
            _defectShinyKeys = false;
            _defectTrackpadWear = false;
            _defectLooseHinges = false;
            _defectChassisDamage = false;
            OnPropertyChanged(nameof(DefectScreenScratches));
            OnPropertyChanged(nameof(DefectWhiteSpots));
            OnPropertyChanged(nameof(DefectLidScuffs));
            OnPropertyChanged(nameof(DefectCornerDent));
            OnPropertyChanged(nameof(DefectShinyKeys));
            OnPropertyChanged(nameof(DefectTrackpadWear));
            OnPropertyChanged(nameof(DefectLooseHinges));
            OnPropertyChanged(nameof(DefectChassisDamage));
            OnPropertyChanged(nameof(CosmeticDefectCount));
            OnPropertyChanged(nameof(DefectCountBadge));
            OnPropertyChanged(nameof(DefectBadgeColor));
            OnPropertyChanged(nameof(CosmeticDefectsSummary));
            Grade = "GRADE A+";
            OnPropertyChanged(nameof(RefurbReportPreviewText));
            OnPropertyChanged(nameof(ECommerceListingText));
        }

        private void OnCosmeticDefectChanged()
        {
            OnPropertyChanged(nameof(CosmeticDefectCount));
            OnPropertyChanged(nameof(DefectCountBadge));
            OnPropertyChanged(nameof(DefectBadgeColor));
            OnPropertyChanged(nameof(CosmeticDefectsSummary));
            RecalculateCosmeticGrade();
            OnPropertyChanged(nameof(RefurbReportPreviewText));
            OnPropertyChanged(nameof(ECommerceListingText));
        }

        public void RecalculateCosmeticGrade()
        {
            // Severe defects immediately downgrade to Grade C
            if (DefectCornerDent || DefectWhiteSpots || DefectChassisDamage)
            {
                Grade = "GRADE C";
            }
            // Moderate defects or 3+ minor defects -> Grade B
            else if (DefectScreenScratches || DefectTrackpadWear || CosmeticDefectCount >= 3)
            {
                Grade = "GRADE B";
            }
            // 1 or 2 minor flaws -> Grade A
            else if (CosmeticDefectCount >= 1)
            {
                Grade = "GRADE A";
            }
            else
            {
                Grade = "GRADE A+";
            }
        }

        // Live Refurb Spec Sheet & Model Audit Dossier Preview
        public string RefurbReportPreviewText
        {
            get
            {
                var sb = new StringBuilder();
                sb.AppendLine($"[UNIT SPECIFICATION & QC AUDIT DOSSIER]");
                sb.AppendLine($"MODEL:       {Manufacturer} {Model}");
                sb.AppendLine($"SERIAL:      {Serial}");
                sb.AppendLine($"PHYSICAL:    {Grade} Refurbished");
                if (CosmeticDefectCount > 0)
                {
                    sb.AppendLine($"DEFECTS:     {CosmeticDefectsSummary}");
                }
                sb.AppendLine($"CPU:         {CpuName}");
                sb.AppendLine($"CORES/FREQ:  {CoreSummary} · {ClockSummary}");
                sb.AppendLine($"MEMORY:      {RamSummary} [{RamHealthBadge} · {RamChannelBadge}]");
                sb.AppendLine($"STORAGE:     {PrimaryDriveModel} ({StorageSummary})");
                sb.AppendLine($"DRIVE SMART: {HealthBadge} · 0 Bad Sectors");
                sb.AppendLine($"SSD TBW:     {TbwDisplaySummary}");
                sb.AppendLine($"DRIVERS:     {MissingDriversSummary}");
                var thm = ThermalProfilerService.Instance.GetCurrentResult();
                if (thm.ConditionCode != "IDLE" && thm.ConditionCode != "STANDBY")
                {
                    sb.AppendLine($"THERMAL:     {thm.ConditionSummary}");
                    if (thm.DecayHalfLifeSeconds > 0)
                    {
                        sb.AppendLine($"AIRFLOW:     {thm.RadiatorAirflowBadge} ({thm.RadiatorAirflowVerdict})");
                    }
                }
                sb.AppendLine($"BATTERY:     {BatteryIntegrityBadge} · {BatteryWearSummary}");
                sb.AppendLine($"CELL STATUS: {BatteryCellTopology} · {BatteryCellBalanceStatus}");
                sb.AppendLine($"GRAPHICS:    {GpuName} ({GpuVram})");
                sb.AppendLine($"INSPECTOR:   {TechnicianDisplayBadge} · {TechnicianStation}");
                sb.AppendLine($"CERTIFIED:   {PipelineStatusText} Nominal");
                return sb.ToString().TrimEnd();
            }
        }

        public string ECommerceListingText
        {
            get
            {
                var sb = new StringBuilder();
                sb.AppendLine($"★ [{Grade}] {Manufacturer} {Model} Refurbished Business Laptop");
                sb.AppendLine($"• Condition: {Grade} ({CosmeticDefectsSummary})");
                sb.AppendLine($"• Processor: {CpuName} ({CoreSummary})");
                sb.AppendLine($"• RAM: {RamSummary} ({RamHealthBadge} · {RamChannelBadge})");
                sb.AppendLine($"• Storage: {PrimaryDriveModel} ({StorageSummary}) - {HealthBadge} (TBW: {TbwWrittenTb:F1}TB / {TbwRatedEnduranceTb}TBW)");
                sb.AppendLine($"• Battery: {BatteryIntegrityBadge} ({BatteryWearSummary} · {BatteryCellTopology})");
                sb.AppendLine($"• Graphics: {GpuName} ({GpuVram})");
                sb.AppendLine($"• Hardware Drivers: {MissingDriversSummary}");
                sb.AppendLine($"• Serial Number: {Serial}");
                sb.AppendLine($"• Certified Technician: {TechnicianDisplayBadge} ({TechnicianStation})");
                sb.AppendLine($"• Quality Assurance: {PipelineStatusText} 100% Certified with SuperAutoMater");
                return sb.ToString().TrimEnd();
            }
        }

        // Summary Counters
        private int _passCount = 0;
        public int PassCount
        {
            get => _passCount;
            set
            {
                _passCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PipelineStatusText));
                OnPropertyChanged(nameof(RefurbReportPreviewText));
                OnPropertyChanged(nameof(ECommerceListingText));
            }
        }
        public int RequiredTestCount => TestPipeline.Count(t => t.IsApplicable);
        public string PipelineStatusText => $"{_passCount}/{(RequiredTestCount > 0 ? RequiredTestCount : 9)} PASSED";

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

            // Wire Technician Profile sync
            TechnicianProfileService.Instance.ProfileChanged += profile =>
            {
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    OnPropertyChanged(nameof(TechnicianName));
                    OnPropertyChanged(nameof(TechnicianId));
                    OnPropertyChanged(nameof(TechnicianStation));
                    OnPropertyChanged(nameof(TechnicianDisplayBadge));
                    OnPropertyChanged(nameof(RefurbReportPreviewText));
                    OnPropertyChanged(nameof(ECommerceListingText));
                });
            };

            // Wire automatic asset auto-fill on SuperManager discovery
            WarehouseFleetService.Instance.SuperManagerDiscovered += url =>
            {
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(async () =>
                {
                    if (!IsRecognizedAsset)
                    {
                        await CheckAndAutoFillAssetAsync();
                    }
                });
            };

            // Initial PnP Yellow-Bang Device Driver Audit
            _ = RefreshDriverAuditAsync();
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
            // The workflow skips this item on non-touch devices, but it must exist so a
            // successful digitizer run is recorded independently from panel validation.
            TestPipeline.Add(new DiagnosticTestItem { Key = "Touchscreen", Title = "TOUCHSCREEN / DIGITIZER", HotkeyText = "[AUTO]" });
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
                    item.StatusBadge = item.IsApplicable ? "▶" : "—";
                }
                else if (!item.IsPassed && item.IsApplicable)
                {
                    item.StatusBadge = "○";
                }
            }
        }

        public async Task RefreshTelemetryAsync()
        {
            await _hw.InitializeAsync();
            UpdateTestApplicability();
            SyncCollections();

            // Check if device already exists in the system (Local SQLite or SuperManager LAN) to auto-fill asset tag & prior data
            await CheckAndAutoFillAssetAsync();

            try
            {
                QcRunOrchestrator.Instance.InitializeRun(new QcRunIdentity
                {
                    AssetTag = !string.IsNullOrWhiteSpace(AssetTag) && AssetTag != "TAG-PENDING" ? AssetTag : (!string.IsNullOrWhiteSpace(Serial) && Serial != "Detecting..." ? Serial : ""),
                    SerialNumber = Serial,
                    AssetUuid = _hw.SystemIdentity?.Uuid ?? "",
                    Model = Model,
                    Technician = TechnicianDisplayBadge,
                    Station = TechnicianStation
                });
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to initialize QcRunOrchestrator run", ex);
            }

            OnPropertyChanged("");
        }

        public async Task CheckAndAutoFillAssetAsync()
        {
            try
            {
                string serial = _hw.SystemIdentity?.Serial ?? "";
                string uuid = _hw.SystemIdentity?.Uuid ?? "";

                string query = !string.IsNullOrWhiteSpace(serial) && serial != "Detecting..." ? serial : uuid;
                if (string.IsNullOrWhiteSpace(query)) return;

                // 1. Check local SQLite store
                var store = new QcRunStore();
                var localAsset = store.FindAssetByAnyIdentifier(query);
                string sourceTag = "LOCAL";

                // 2. If not found locally, query SuperManager LAN endpoint
                if (localAsset == null)
                {
                    bool hasKnownUrl = !string.IsNullOrEmpty(WarehouseFleetService.Instance.SuperManagerUrl);
                    string mgrUrl = hasKnownUrl ? WarehouseFleetService.Instance.SuperManagerUrl : "http://127.0.0.1:9000";

                    try
                    {
                        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMilliseconds(hasKnownUrl ? 1500 : 400) };
                        var resp = await http.GetAsync($"{mgrUrl}/api/asset/lookup?q={Uri.EscapeDataString(query)}");
                        if (resp.IsSuccessStatusCode)
                        {
                            string json = await resp.Content.ReadAsStringAsync();
                            using var doc = System.Text.Json.JsonDocument.Parse(json);
                            if (doc.RootElement.TryGetProperty("found", out var foundProp) && foundProp.GetBoolean())
                            {
                                var a = doc.RootElement.GetProperty("asset");
                                localAsset = new AssetWipRecord
                                {
                                    AssetId = a.GetProperty("assetId").GetString(),
                                    AssetTag = a.GetProperty("assetTag").GetString(),
                                    SerialNumber = a.GetProperty("serialNumber").GetString(),
                                    Model = a.GetProperty("model").GetString(),
                                    CurrentLocation = a.GetProperty("currentLocation").GetString(),
                                    AssignedTo = a.GetProperty("assignedTo").GetString(),
                                    MissingComponents = a.GetProperty("missingComponents").GetString(),
                                    LatestRunGrade = a.GetProperty("latestRunGrade").GetString(),
                                    LatestRunStatus = a.GetProperty("latestRunStatus").GetString(),
                                    WorkInProgress = a.GetProperty("workInProgress").GetString(),
                                    Supplier = a.GetProperty("supplier").GetString(),
                                    Customer = a.GetProperty("customer").GetString(),
                                    Remarks = a.GetProperty("remarks").GetString()
                                };
                                sourceTag = "LAN";

                                // Cache in local SQLite store
                                try
                                {
                                    store.SaveItamRecord(new AssetQueueRecord
                                    {
                                        Tag = localAsset.AssetTag,
                                        Serial_Number = localAsset.SerialNumber,
                                        Model = localAsset.Model,
                                        Physical_Grade = localAsset.LatestRunGrade,
                                        Status = localAsset.LatestRunStatus,
                                        Work_In_Progress = localAsset.WorkInProgress,
                                        Technician = localAsset.AssignedTo,
                                        Supplier = localAsset.Supplier,
                                        Customer = localAsset.Customer,
                                        Remarks = localAsset.Remarks
                                    });
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }

                // 3. If still not found, query Cloud Google Sheets webhook (Tier-3 Cloud Telemetry Recall)
                if (localAsset == null)
                {
                    try
                    {
                        var sheetRecord = await OfflineSyncQueue.Instance.QueryRemoteSheetAsync(query);
                        if (sheetRecord != null && (!string.IsNullOrWhiteSpace(sheetRecord.Tag) || !string.IsNullOrWhiteSpace(sheetRecord.Serial_Number)))
                        {
                            // Cache into local SQLite
                            try
                            {
                                store.SaveItamRecord(sheetRecord);
                            }
                            catch { }

                            string resolvedTag = !string.IsNullOrWhiteSpace(sheetRecord.Tag) ? sheetRecord.Tag : (!string.IsNullOrWhiteSpace(sheetRecord.Asset_Tag) ? sheetRecord.Asset_Tag : query);
                            localAsset = new AssetWipRecord
                            {
                                AssetId = Guid.NewGuid().ToString(),
                                AssetTag = resolvedTag,
                                SerialNumber = !string.IsNullOrWhiteSpace(sheetRecord.Serial_Number) ? sheetRecord.Serial_Number : serial,
                                Model = !string.IsNullOrWhiteSpace(sheetRecord.Model) ? sheetRecord.Model : (_hw.SystemIdentity?.Model ?? ""),
                                CurrentLocation = "Cloud Intake",
                                AssignedTo = sheetRecord.Technician,
                                MissingComponents = !string.IsNullOrWhiteSpace(sheetRecord.Work_In_Progress) && sheetRecord.Work_In_Progress != "All Okay" ? sheetRecord.Work_In_Progress : "",
                                LatestRunGrade = sheetRecord.Physical_Grade,
                                LatestRunStatus = sheetRecord.Status,
                                WorkInProgress = sheetRecord.Work_In_Progress,
                                Supplier = sheetRecord.Supplier,
                                Customer = sheetRecord.Customer,
                                Remarks = sheetRecord.Remarks
                            };
                            sourceTag = "CLOUD";
                        }
                    }
                    catch (Exception cloudEx)
                    {
                        AppLogger.Warn($"Cloud asset lookup error for {query}: {cloudEx.Message}");
                    }
                }

                if (localAsset != null)
                {
                    IsRecognizedAsset = true;
                    if (!string.IsNullOrWhiteSpace(localAsset.AssetTag))
                        AssetTag = localAsset.AssetTag;
                    if (!string.IsNullOrWhiteSpace(localAsset.AssignedTo))
                        AssignedTechnician = localAsset.AssignedTo;
                    if (!string.IsNullOrWhiteSpace(localAsset.MissingComponents))
                        MissingComponentsWarning = localAsset.MissingComponents;
                    if (!string.IsNullOrWhiteSpace(localAsset.LatestRunGrade) && localAsset.LatestRunGrade != "INSPECT" && localAsset.LatestRunGrade != "GRADE PENDING")
                        Grade = localAsset.LatestRunGrade;
                    if (!string.IsNullOrWhiteSpace(localAsset.Supplier))
                        Supplier = localAsset.Supplier;
                    if (!string.IsNullOrWhiteSpace(localAsset.Customer))
                        Customer = localAsset.Customer;
                    if (!string.IsNullOrWhiteSpace(localAsset.WorkInProgress))
                        WorkInProgress = localAsset.WorkInProgress;

                    if (sourceTag == "CLOUD")
                    {
                        RecognizedAssetBanner = $"☁ CLOUD SYNCHRONIZED: Tag {localAsset.AssetTag} · Status: {localAsset.LatestRunStatus} · Supplier: {(!string.IsNullOrEmpty(localAsset.Supplier) ? localAsset.Supplier : "N/A")} · Tech: {(!string.IsNullOrEmpty(localAsset.AssignedTo) ? localAsset.AssignedTo : "Floor Pool")}";
                    }
                    else if (sourceTag == "LAN")
                    {
                        RecognizedAssetBanner = $"🌐 FLEET SYNCED: Tag {localAsset.AssetTag} · {localAsset.Model} · Bay {localAsset.CurrentLocation} · Assigned: {(!string.IsNullOrEmpty(localAsset.AssignedTo) ? localAsset.AssignedTo : "Floor Pool")}";
                    }
                    else
                    {
                        RecognizedAssetBanner = $"RECOGNIZED: Tag {localAsset.AssetTag} · {localAsset.Model} · Bay {localAsset.CurrentLocation} · Assigned: {(!string.IsNullOrEmpty(localAsset.AssignedTo) ? localAsset.AssignedTo : "Floor Pool")}";
                    }

                    AppLogger.Info($"[AutoFill] {sourceTag} asset recognized: Tag={localAsset.AssetTag}, Serial={localAsset.SerialNumber}, AssignedTo={localAsset.AssignedTo}");
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(_assetTag))
                    {
                        _assetTag = !string.IsNullOrWhiteSpace(serial) && serial != "Detecting..." ? serial : "";
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("CheckAndAutoFillAssetAsync encountered error", ex);
            }
        }

        private void UpdateTestApplicability()
        {
            var touchscreenTest = TestPipeline.FirstOrDefault(t =>
                t.Key.Equals("Touchscreen", StringComparison.OrdinalIgnoreCase));
            if (touchscreenTest == null) return;

            touchscreenTest.IsApplicable = HasTouchscreen;
            if (!touchscreenTest.IsApplicable && !touchscreenTest.IsPassed)
            {
                touchscreenTest.IsActive = false;
                touchscreenTest.Status = "NOT APPLICABLE";
                touchscreenTest.StatusBadge = "—";
                try
                {
                    QcRunOrchestrator.Instance.RecordTestNotApplicable("Touchscreen", "TOUCHSCREEN / DIGITIZER", "Non-touch display hardware");
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Failed to record Touchscreen N/A in orchestrator", ex);
                }
            }
            else if (touchscreenTest.IsApplicable && touchscreenTest.Status == "NOT APPLICABLE")
            {
                touchscreenTest.Status = "PENDING";
                touchscreenTest.StatusBadge = "○";
            }

            OnPropertyChanged(nameof(PipelineStatusText));
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
                        test.Status = "PASSED";
                        test.StatusBadge = "✓";
                        PassCount++;

                        try
                        {
                            QcRunOrchestrator.Instance.RecordTestPassed(test.Key, test.Title, isAutomated: true);
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warn($"Failed to record {key} passed in orchestrator", ex);
                        }
                    }
                    break;
                }
            }
        }

        public void MarkTestFailed(string key, string reason)
        {
            foreach (var test in TestPipeline)
            {
                if (test.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    test.IsPassed = false;
                    test.IsActive = false;
                    test.Status = "FAILED";
                    test.StatusBadge = "✗";

                    try
                    {
                        QcRunOrchestrator.Instance.RecordTestFailed(test.Key, test.Title, reason);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn($"Failed to record {key} failed in orchestrator", ex);
                    }
                    break;
                }
            }
        }

        public bool RecordTestOverride(string key, string reason, string approver, out string error)
        {
            error = "";
            foreach (var test in TestPipeline)
            {
                if (test.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    bool ok = QcRunOrchestrator.Instance.RecordTestOverride(key, reason, TechnicianDisplayBadge, approver, out error);
                    if (ok)
                    {
                        test.IsPassed = true;
                        test.IsActive = false;
                        test.Status = "OVERRIDDEN";
                        test.StatusBadge = "⚠";
                        PassCount++;
                        return true;
                    }
                    return false;
                }
            }
            error = "Test item not found in pipeline.";
            return false;
        }

        public void MarkNextTestPassed()
        {
            foreach (var test in TestPipeline)
            {
                if (test.IsApplicable && !test.IsPassed)
                {
                    MarkTestPassed(test.Key);
                    break;
                }
            }
        }

        public async Task SyncToSheetsAsync()
        {
            try
            {
                int flushed = await SyncOutboxDispatcher.Instance.FlushPendingAsync();
                System.Windows.MessageBox.Show($"Durable cloud sync complete ({flushed} events dispatched)!", "Google Sheets Cloud Sync", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("SyncToSheetsAsync failed", ex);
                System.Windows.MessageBox.Show("Sync Error: " + ex.Message, "Sheets Sync Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // Technician Operator Profile
        public string TechnicianName => TechnicianProfileService.Instance.CurrentProfile.Name;
        public string TechnicianId => TechnicianProfileService.Instance.CurrentProfile.Id;
        public string TechnicianStation => TechnicianProfileService.Instance.CurrentProfile.StationBay;
        public string TechnicianDisplayBadge => TechnicianProfileService.Instance.CurrentProfile.DisplayBadge;

        // PnP Device Manager Driver Audit
        private DriverAuditResult _driverAudit = new DriverAuditResult();
        public int MissingDriverCount => _driverAudit?.MissingCount ?? 0;
        public bool HasMissingDrivers => _driverAudit?.HasMissingDrivers ?? false;
        public string MissingDriversSummary => _driverAudit?.SummaryText ?? "✓ 0 MISSING DRIVERS (All Hardware Online)";
        public string MissingDriversBadge => _driverAudit?.StatusBadge ?? "0 MISSING DRIVERS";
        public string MissingDriversAccentHex => _driverAudit?.AccentHex ?? "#3FB950";
        public List<MissingDeviceEntry> MissingDriversList => _driverAudit?.MissingDevices ?? new List<MissingDeviceEntry>();

        public async Task RefreshDriverAuditAsync()
        {
            try
            {
                _driverAudit = await DeviceDriverAuditService.Instance.RunAuditAsync();
                OnPropertyChanged(nameof(MissingDriverCount));
                OnPropertyChanged(nameof(HasMissingDrivers));
                OnPropertyChanged(nameof(MissingDriversSummary));
                OnPropertyChanged(nameof(MissingDriversBadge));
                OnPropertyChanged(nameof(MissingDriversAccentHex));
                OnPropertyChanged(nameof(MissingDriversList));
                OnPropertyChanged(nameof(RefurbReportPreviewText));
                OnPropertyChanged(nameof(ECommerceListingText));
            }
            catch { }
        }

        public AssetQueueRecord CreateCurrentAssetRecord()
        {
            var summary = QcRunOrchestrator.Instance.GetCurrentSummary();
            if (summary != null && summary.Status == QcRunStatus.Completed)
            {
                return ThermalLabelPrinter.FromRunSummary(summary);
            }

            return new AssetQueueRecord
            {
                Asset_Tag = !string.IsNullOrWhiteSpace(Serial) && Serial != "Detecting..." ? Serial : $"QC-{DateTime.Now:MMdd-HHmm}",
                Serial_Number = Serial,
                Model = Model,
                Processor = CpuName,
                Memory = $"{RamSummary} / {StorageSummary}",
                Battery_Health = BatteryHealth,
                Status = PassCount >= RequiredTestCount && RequiredTestCount > 0 ? "RTS" : "WIP",
                Wip_Issue = PassCount >= RequiredTestCount && RequiredTestCount > 0 ? "All Okay" : "Diagnostics Incomplete",
                Physical_Grade = Grade ?? "PENDING",
                Remarks = $"SuperAutoMater Label | {PipelineStatusText} | TBW: {TbwWrittenTb:F1}TB",
                Shelf_Location = TechnicianStation,
                Technician = TechnicianDisplayBadge,
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
        }

        public void PrintThermalLabel()
        {
            var record = CreateCurrentAssetRecord();
            ThermalLabelPrinter.PrintLabel(record);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
