using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Velox.Shared.Dtos;
using Velox.Shared.Models;
using VeloxBench.Core;
using VeloxBench.Services;
using VeloxBench.Controls;

namespace VeloxBench.ViewModels;

public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}

public class MainViewModel : INotifyPropertyChanged
{
    private static MainViewModel? _instance;
    public static MainViewModel Instance => _instance ??= new MainViewModel();

    private readonly HardwareDiagnosticsService _hw = HardwareDiagnosticsService.Instance;
    private readonly CloudSyncService _sync = CloudSyncService.Instance;

    public ObservableCollection<ExpressSubsystemResult> Subsystems { get; } = new();

    // ── System Identity ───────────────────────────────────────────────────────
    public string Manufacturer => _hw.SystemIdentity?.Manufacturer ?? "Generic";
    public string Model => _hw.SystemIdentity?.Model ?? "Detecting Chassis...";
    public string Serial => _hw.SystemIdentity?.Serial ?? "Detecting...";
    public string BiosVersion => _hw.SystemIdentity?.BiosVersion ?? "1.0.0";
    public bool IsSerialMissing => _hw.SystemIdentity?.IsSerialMissing ?? false;

    private string _assetTag = "";
    public string AssetTag
    {
        get => !string.IsNullOrEmpty(_assetTag) ? _assetTag : (!string.IsNullOrEmpty(Serial) && Serial != "Detecting..." ? Serial : "TAG-PENDING");
        set
        {
            if (_assetTag != value)
            {
                _assetTag = value;
                OnPropertyChanged();
                _ = CheckAndAutoFillAssetAsync(value);
            }
        }
    }

    private string _assignedTechnician = "Unassigned";
    public string AssignedTechnician
    {
        get => _assignedTechnician;
        set { _assignedTechnician = value; OnPropertyChanged(); _ = PushLiveUpdateAsync(); }
    }

    private string _supplier = "";
    public string Supplier
    {
        get => _supplier;
        set { _supplier = value; OnPropertyChanged(); _ = PushLiveUpdateAsync(); }
    }

    private string _customer = "";
    public string Customer
    {
        get => _customer;
        set { _customer = value; OnPropertyChanged(); _ = PushLiveUpdateAsync(); }
    }

    private string _grade = "GRADE PENDING";
    public string Grade
    {
        get => _grade;
        set { _grade = value; OnPropertyChanged(); _ = PushLiveUpdateAsync(); }
    }

    private string _missingComponents = "";
    public string MissingComponents
    {
        get => _missingComponents;
        set { _missingComponents = value; OnPropertyChanged(); _ = PushLiveUpdateAsync(); }
    }

    private string _notes = "";
    public string Notes
    {
        get => _notes;
        set { _notes = value; OnPropertyChanged(); _ = PushLiveUpdateAsync(); }
    }

    // ── Hardware Telemetry ───────────────────────────────────────────────────
    public string CpuName => _hw.CpuTelemetry?.CpuName ?? "Detecting CPU...";
    public string CpuClockSummary => $"{_hw.CpuTelemetry?.CurrentClockGhz:0.0} GHz ({_hw.CpuTelemetry?.CoreSummary})";
    public int CpuUsagePercent => _hw.CpuTelemetry?.UsagePercent ?? 0;
    public int CpuTempC => _hw.CpuTelemetry?.TemperatureC ?? 0;

    public string RamSummary => _hw.MemoryStorage != null ? $"{_hw.MemoryStorage.RamSummary} ({_hw.MemoryStorage.Topology?.StatusBadge})" : "Detecting RAM...";
    public string RamHealthBadge => RamHealthPredictorService.Instance.LastAssessment?.SummaryBadge ?? "100% HEALTH (Nominal)";

    public string StorageSummary => _hw.PrimaryDrive?.DisplayPill ?? "Storage Scanning...";
    public int StorageHealthPercent => _hw.MemoryStorage?.HealthPercent ?? 100;

    public string BatterySummary => _hw.BatteryTelemetry != null ? $"{_hw.BatteryTelemetry.ChargePercent}% ({_hw.BatteryTelemetry.AcStatusText})" : "AC Powered";
    public int BatteryHealthPercent => _hw.BatteryTelemetry?.HealthPercent ?? 100;

    public string GpuSummary => _hw.GpuTelemetry?.GpuName ?? "Detecting GPU...";

    public StorageDriveDetail? ActiveDrive => _hw.PrimaryDrive;
    public string MissingComponentsWarning => MissingComponents;
    public int? BatteryCharge => _hw.BatteryTelemetry?.ChargePercent ?? 100;
    public string WifiSsid => _hw.NetworkTelemetry?.Ssid ?? "Connected";
    public string WifiSignal => $"{_hw.NetworkTelemetry?.SignalPercent ?? 100}%";
    public string WifiRadioType => _hw.NetworkTelemetry?.RadioType ?? "Wi-Fi 6";

    // ── Real-Time Cloud Connectivity ──────────────────────────────────────────
    private string _syncBanner = "CHECKING CONNECTIVITY...";
    public string SyncBanner
    {
        get => _syncBanner;
        set { _syncBanner = value; OnPropertyChanged(); }
    }

    private string _syncBannerColor = "#8B949E";
    public string SyncBannerColor
    {
        get => _syncBannerColor;
        set { _syncBannerColor = value; OnPropertyChanged(); }
    }

    public bool IsCloudLive => _sync.IsOnline;
    public bool IsLanConnected => _sync.IsLanConnected;

    // ── Express QC Status ────────────────────────────────────────────────────
    private bool _isQcRunning;
    public bool IsQcRunning
    {
        get => _isQcRunning;
        set { _isQcRunning = value; OnPropertyChanged(); }
    }

    private string _qcOverallStatus = "READY FOR TESTING";
    public string QcOverallStatus
    {
        get => _qcOverallStatus;
        set { _qcOverallStatus = value; OnPropertyChanged(); }
    }

    // ── Recognized Asset & Pipeline Selection ───────────────────────────────
    public bool IsRecognizedAsset => !string.IsNullOrEmpty(Serial) && Serial != "Detecting...";
    public string RecognizedAssetBanner => $"CHASSIS DETECTED: {Manufacturer} {Model} (S/N: {Serial})";

    private string _fittedComponentsBanner = "";
    public string FittedComponentsBanner
    {
        get => _fittedComponentsBanner;
        set { _fittedComponentsBanner = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasFittedBanner)); }
    }
    public bool HasFittedBanner => !string.IsNullOrWhiteSpace(FittedComponentsBanner);

    private ExpressSubsystemResult? _selectedSubsystem;
    public ExpressSubsystemResult? SelectedSubsystem
    {
        get => _selectedSubsystem;
        set
        {
            _selectedSubsystem = value;
            foreach (var s in Subsystems)
            {
                s.IsSelected = (s == value);
            }
            OnPropertyChanged();
        }
    }

    public ICommand RunQcCommand { get; }
    public ICommand RunSingleSubsystemCommand { get; }
    public ICommand GeneratePdfCommand { get; }
    public ICommand RefreshHardwareCommand { get; }
    public ICommand PassQcAndPushCloudCommand { get; }

    public MainViewModel()
    {
        _instance = this;

        RunQcCommand = new RelayCommand(async _ => await RunExpressQcAsync());
        RunSingleSubsystemCommand = new RelayCommand(async key => await RunSingleSubsystemAsync(key as string));
        GeneratePdfCommand = new RelayCommand(_ => GeneratePdfCertificate());
        RefreshHardwareCommand = new RelayCommand(async _ => await InitializeHardwareAsync());
        PassQcAndPushCloudCommand = new RelayCommand(async _ => await PassQcAndPushCloudAsync());

        InitializeDefaultSubsystems();

        _sync.ConnectivityChanged += UpdateConnectivityBadges;

        _ = InitializeHardwareAsync();
    }

    public void InitializeDefaultSubsystems()
    {
        Subsystems.Clear();
        string[] keys = { "Camera", "Mic", "Speaker", "Storage", "CpuRam", "Battery", "Display", "Network", "Keyboard", "Fingerprint" };
        string[] names = { "Webcam Optics", "Mic Array", "Stereo Speakers", "Storage NVMe", "CPU & RAM", "Battery Power", "Display VSync", "Wi-Fi & Network", "Keyboard & Trackpad", "Fingerprint Sensor" };
        string[] icons = { "📷", "🎤", "🔊", "💾", "⚡", "🔋", "🖥", "📶", "⌨", "👆" };
        string[] hotkeys = { "[F1]", "[F2]", "[F3]", "[F4]", "[F6]", "[F7]", "[F8]", "[F9]", "[F10]", "[F11]" };

        for (int i = 0; i < keys.Length; i++)
        {
            var item = new ExpressSubsystemResult
            {
                Key = keys[i],
                Name = names[i],
                Icon = icons[i],
                HotkeyText = hotkeys[i],
                Status = ExpressTestStatus.Pending,
                Headline = "Ready for autonomous audit..."
            };
            Subsystems.Add(item);
        }
        SelectedSubsystem = Subsystems.FirstOrDefault();
    }

    public void MarkSubsystemPassed(string key, string headline, string detail = "", string metric = "PASS")
    {
        var target = Subsystems.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));
        if (target != null)
        {
            target.Status = ExpressTestStatus.Passed;
            target.Headline = headline;
            if (!string.IsNullOrEmpty(detail)) target.Detail = detail;
            target.MetricValue = metric;
            OnPropertyChanged(nameof(Subsystems));
        }
    }

    private async Task InitializeHardwareAsync()
    {
        await _hw.InitializeAsync();
        RefreshAllProperties();
        UpdateConnectivityBadges();

        // Check if current serial is already known in cloud
        if (!string.IsNullOrEmpty(Serial) && Serial != "Detecting...")
        {
            await CheckAndAutoFillAssetAsync(Serial);
        }
    }

    public void RefreshAllProperties()
    {
        OnPropertyChanged(nameof(Manufacturer));
        OnPropertyChanged(nameof(Model));
        OnPropertyChanged(nameof(Serial));
        OnPropertyChanged(nameof(BiosVersion));
        OnPropertyChanged(nameof(AssetTag));
        OnPropertyChanged(nameof(CpuName));
        OnPropertyChanged(nameof(CpuClockSummary));
        OnPropertyChanged(nameof(CpuUsagePercent));
        OnPropertyChanged(nameof(CpuTempC));
        OnPropertyChanged(nameof(RamSummary));
        OnPropertyChanged(nameof(RamHealthBadge));
        OnPropertyChanged(nameof(StorageSummary));
        OnPropertyChanged(nameof(StorageHealthPercent));
        OnPropertyChanged(nameof(BatterySummary));
        OnPropertyChanged(nameof(BatteryHealthPercent));
        OnPropertyChanged(nameof(GpuSummary));
    }

    private void UpdateConnectivityBadges()
    {
        if (_sync.IsOnline)
        {
            SyncBanner = "☁ CLOUD SYNCHRONIZED";
            SyncBannerColor = "#3FB950"; // Green
        }
        else if (_sync.IsLanConnected)
        {
            SyncBanner = "🌐 LAN MIRROR CONNECTED";
            SyncBannerColor = "#58A6FF"; // Blue
        }
        else
        {
            SyncBanner = "⚠ OFFLINE — LOCAL RESILIENCE CACHE";
            SyncBannerColor = "#D29922"; // Yellow
        }
        OnPropertyChanged(nameof(IsCloudLive));
        OnPropertyChanged(nameof(IsLanConnected));
    }

    // ── Auto-Fill from Cloud ─────────────────────────────────────────────────
    public async Task CheckAndAutoFillAssetAsync(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier) || identifier == "Detecting...") return;

        var result = await _sync.LookupAssetAsync(identifier);
        if (result.Found && result.Asset != null)
        {
            var a = result.Asset;
            if (!string.IsNullOrWhiteSpace(a.AssetTag)) _assetTag = a.AssetTag;
            if (!string.IsNullOrWhiteSpace(a.AssignedEmployeeName)) _assignedTechnician = a.AssignedEmployeeName;
            if (!string.IsNullOrWhiteSpace(a.Supplier)) _supplier = a.Supplier;
            if (!string.IsNullOrWhiteSpace(a.Customer)) _customer = a.Customer;
            if (a.Grade != AssetGrade.Unknown) _grade = $"GRADE {a.Grade}";
            if (!string.IsNullOrWhiteSpace(a.MissingComponents)) _missingComponents = a.MissingComponents;
            if (!string.IsNullOrWhiteSpace(a.Notes)) _notes = a.Notes;

            OnPropertyChanged(nameof(AssetTag));
            OnPropertyChanged(nameof(AssignedTechnician));
            OnPropertyChanged(nameof(Supplier));
            OnPropertyChanged(nameof(Customer));
            OnPropertyChanged(nameof(Grade));
            OnPropertyChanged(nameof(MissingComponents));
            OnPropertyChanged(nameof(Notes));

            try
            {
                var components = await _sync.GetFittedComponentsAsync(a.Id);
                if (components.Count > 0)
                {
                    FittedComponentsBanner = "MODS: " + string.Join(" • ", components.Select(c => $"{c.Description}"));
                }
                else
                {
                    FittedComponentsBanner = "";
                }
            }
            catch
            {
                FittedComponentsBanner = "";
            }
            OnPropertyChanged(nameof(FittedComponentsBanner));
            OnPropertyChanged(nameof(HasFittedBanner));

            SyncBanner = result.Source switch
            {
                SyncSource.CloudAuthoritative => "☁ CLOUD RECOGNIZED & AUTO-FILLED",
                SyncSource.LanManager => "🌐 LAN SYNCED & AUTO-FILLED",
                _ => "✓ LOCAL CACHE RECOGNIZED"
            };
            SyncBannerColor = "#3FB950";
        }
    }

    // ── Push Live Updates ─────────────────────────────────────────────────────
    private async Task PushLiveUpdateAsync()
    {
        if (string.IsNullOrWhiteSpace(Serial) || Serial == "Detecting...") return;

        var intake = new IntakeRequest
        {
            SerialNumber = Serial,
            AssetTag = AssetTag,
            Model = Model,
            Location = BenchConfigService.Current.BenchName,
            Supplier = Supplier,
            Actor = AssignedTechnician,
            Notes = Notes
        };

        await _sync.PushIntakeOrUpdateAsync(intake);
    }

    // ── Express QC Execution ─────────────────────────────────────────────────
    public async Task RunExpressQcAsync()
    {
        if (IsQcRunning) return;
        IsQcRunning = true;
        QcOverallStatus = "TESTING IN PROGRESS...";

        try
        {
            var engine = ExpressQcEngineService.Instance;

            Action<ExpressSubsystemResult> onUpdate = updated =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    var existing = Subsystems.FirstOrDefault(s => s.Key == updated.Key);
                    if (existing != null)
                    {
                        existing.Status = updated.Status;
                        existing.Headline = updated.Headline;
                        existing.Detail = updated.Detail;
                        existing.MetricValue = updated.MetricValue;
                        existing.SnapshotImage = updated.SnapshotImage;
                        existing.SignalLevel = updated.SignalLevel;
                    }
                    if (SelectedSubsystem == null || SelectedSubsystem.Key == updated.Key)
                    {
                        SelectedSubsystem = existing;
                    }
                });
            };

            engine.SubsystemUpdated += onUpdate;
            try
            {
                // Run automated 8-stage QC
                var report = await engine.RunFullExpressQcAsync(this);
                if (report != null)
                {
                    QcOverallStatus = report.AllPassed ? "✓ ALL SUBSYSTEMS PASSED" : "⚠ SUBSYSTEM WARNINGS DETECTED";

                    // Push completion event to cloud
                    await _sync.PushIntakeOrUpdateAsync(new IntakeRequest
                    {
                        SerialNumber = Serial,
                        AssetTag = AssetTag,
                        Model = Model,
                        Location = BenchConfigService.Current.BenchName,
                        Actor = AssignedTechnician,
                        Notes = $"QC Completed: {QcOverallStatus} | Grade: {Grade}"
                    });
                }
            }
            finally
            {
                engine.SubsystemUpdated -= onUpdate;
            }
        }
        catch (Exception ex)
        {
            QcOverallStatus = $"TEST ERROR: {ex.Message}";
            AppLogger.Error("Express QC failed", ex);
        }
        finally
        {
            IsQcRunning = false;
        }
    }

    public async Task RunSingleSubsystemAsync(string? key)
    {
        if (IsQcRunning) return;
        if (string.IsNullOrWhiteSpace(key))
        {
            key = SelectedSubsystem?.Key;
        }
        if (string.IsNullOrWhiteSpace(key)) return;

        var target = Subsystems.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));
        if (target == null) return;

        SelectedSubsystem = target;
        IsQcRunning = true;
        QcOverallStatus = $"TESTING [{target.Name.ToUpperInvariant()}]...";

        try
        {
            var engine = ExpressQcEngineService.Instance;
            Action<ExpressSubsystemResult> onUpdate = updated =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    var existing = Subsystems.FirstOrDefault(s => s.Key == updated.Key);
                    if (existing != null)
                    {
                        existing.Status = updated.Status;
                        existing.Headline = updated.Headline;
                        existing.Detail = updated.Detail;
                        existing.MetricValue = updated.MetricValue;
                        existing.SnapshotImage = updated.SnapshotImage;
                        existing.SignalLevel = updated.SignalLevel;
                    }
                    if (SelectedSubsystem == null || SelectedSubsystem.Key == updated.Key)
                    {
                        SelectedSubsystem = existing;
                    }
                });
            };

            engine.SubsystemUpdated += onUpdate;
            try
            {
                await engine.RunSingleSubsystemAsync(key, this);
                QcOverallStatus = target.Status == ExpressTestStatus.Passed
                    ? $"✓ {target.Name.ToUpperInvariant()} PASSED"
                    : $"⚠ {target.Name.ToUpperInvariant()} {target.StatusBadge}";
            }
            finally
            {
                engine.SubsystemUpdated -= onUpdate;
            }
        }
        catch (Exception ex)
        {
            QcOverallStatus = $"TEST ERROR: {ex.Message}";
            AppLogger.Error($"Subsystem {key} test failed", ex);
        }
        finally
        {
            IsQcRunning = false;
        }
    }

    public async Task PassQcAndPushCloudAsync()
    {
        try
        {
            QcOverallStatus = "✓ QC CERTIFIED & PUSHED TO CLOUD";
            await _sync.PushIntakeOrUpdateAsync(new IntakeRequest
            {
                SerialNumber = Serial,
                AssetTag = AssetTag,
                Model = Model,
                Location = BenchConfigService.Current.BenchName,
                Actor = AssignedTechnician,
                Notes = $"QC Certified [{QcOverallStatus}] | Grade: {Grade} | Subsystems: {Subsystems.Count(s => s.IsPassed)}/{Subsystems.Count} Passed"
            });
            VeloxMessageBox.Show($"Chassis {Serial} certified and synchronized to Fleet Cloud DB!", "QC Pushed to Cloud", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            VeloxMessageBox.Show($"Failed pushing QC to cloud: {ex.Message}", "Sync Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── PDF Certificate Generation ───────────────────────────────────────────
    public void GeneratePdfCertificate()
    {
        try
        {
            var certData = new CertificateData
            {
                RunId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
                SerialNumber = Serial,
                Manufacturer = Manufacturer,
                Model = Model,
                BiosVersion = BiosVersion,
                CpuModel = CpuName,
                RamDetails = RamSummary,
                StorageModel = StorageSummary,
                StorageHealthPercent = StorageHealthPercent,
                BatteryHealthSummary = BatterySummary,
                GpuModel = GpuSummary,
                PhysicalGrade = Grade,
                RamHealthScoreSummary = RamHealthBadge
            };

            var outDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Velox_Certificates");
            Directory.CreateDirectory(outDir);
            var outFile = Path.Combine(outDir, $"Certificate_{Serial}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.pdf");

            PdfCertificateService.Instance.GenerateCertificate(certData, outFile);

            VeloxMessageBox.Show($"Certificate successfully generated:\n{outFile}", "Velox QC Certificate", MessageBoxButton.OK, MessageBoxImage.Information);
            Process.Start(new ProcessStartInfo(outFile) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed generating PDF certificate", ex);
            VeloxMessageBox.Show($"Certificate generation failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
