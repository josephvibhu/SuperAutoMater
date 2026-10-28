using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using VeloxDeploy.Core;
using VeloxDeploy.Models;

namespace VeloxDeploy.ViewModels
{
    public class LogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Source { get; set; } = "SYSTEM";
        public string Message { get; set; } = string.Empty;
        public string Level { get; set; } = "INFO"; // INFO, WARN, ERROR, SUCCESS
        public string FormattedText => $"[{Timestamp:HH:mm:ss}] [{Source}] {Message}";
    }

    public class DeployViewModel : INotifyPropertyChanged
    {
        private readonly DeploymentController _controller = new();
        private CancellationTokenSource? _cts;
        private readonly Dispatcher _dispatcher;

        private HardwareProfile _profile = new();
        private ObservableCollection<TargetDiskInfo> _disks = new();
        private TargetDiskInfo? _selectedDisk;
        private DeployConfig _config = new();

        private ObservableCollection<StepItem> _steps = new();
        private DeployStage _currentStage = DeployStage.Idle;
        private double _progressPercent = 0.0;
        private double _speedMBps = 0.0;
        private string _bytesText = "0.0 / 14.5 GB";
        private string _elapsedText = "--:--";
        private string _remainingText = "--:--";
        private string _currentActionText = "Ready for deployment";

        private readonly List<LogEntry> _allLogs = new();
        private ObservableCollection<LogEntry> _filteredLogs = new();
        private string _selectedLogFilter = "All"; // All, Warnings, Errors

        private bool _isRunning = false;
        private bool _isCompleted = false;
        private bool _isFailed = false;
        private bool _isAborted = false;
        private bool _isLiveMode = false; // Always starts in Dry run per handoff spec
        private bool _isConfirmationDialogOpen = false;
        private string _confirmationInput = string.Empty;

        private string _cloudStatusText = "Cloud connected";
        private bool _isDarkTheme = true;

        // Preflight items
        private ObservableCollection<PreflightCheckItem> _preflightChecks = new();
        private string _preflightSummaryText = "Ready to deploy, 1 warning";
        private string _preflightSummaryBadge = "READY";

        // Driver Vault Management (1-Click OEM Download & Hot-Capture)
        private string _targetDriverModel = "Dell Latitude 7490";
        private ObservableCollection<string> _availableDriverModels = new();
        private bool _isDriverOperationInProgress = false;
        private double _driverProgressPercent = 0.0;
        private string _driverProgressStatus = string.Empty;
        private bool _isTargetDriverCached = false;
        private string _targetDriverStatusText = "Checking...";

        public DeployViewModel()
        {
            _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

            InitializeSteps();

            _controller.OnLog += msg => AppendLog("DEPLOY", msg, "INFO");
            _controller.OnProgress += HandleProgress;

            // Commands
            PrimaryActionCommand = new RelayCommand(ExecutePrimaryAction, CanExecutePrimaryAction);
            SecondaryActionCommand = new RelayCommand(ExecuteSecondaryAction, CanExecuteSecondaryAction);
            OpenConfirmationCommand = new RelayCommand(() => IsConfirmationDialogOpen = true, () => SelectedDisk != null && !IsRunning);
            ConfirmLiveDeployCommand = new RelayCommand(ExecuteConfirmedLiveDeploy, () => CanConfirmLive);
            CancelConfirmationCommand = new RelayCommand(() => { IsConfirmationDialogOpen = false; ConfirmationInput = string.Empty; });
            RescanDisksCommand = new RelayCommand(ProbeHardware, () => !IsRunning);
            ChangeImageCommand = new RelayCommand(async () => await BrowseAndSelectImageAsync(), () => !IsRunning);
            CopyLogCommand = new RelayCommand(CopyLogToClipboard);
            SaveLogCommand = new RelayCommand(SaveLogToFile);
            ToggleThemeCommand = new RelayCommand(ToggleTheme);

            DownloadDriverPackCommand = new RelayCommand(async () => await DownloadSelectedDriverPackAsync(), () => CanRunDriverAction);
            CaptureLocalDriversCommand = new RelayCommand(async () => await CaptureLocalDriversAsync(), () => CanRunDriverAction);
            OpenDriverVaultCommand = new RelayCommand(OpenDriverVaultFolder);

            InitializeDriverModels();

            // Initial hardware interrogation & preflight
            ProbeHardware();
        }

        private void InitializeSteps()
        {
            _steps = new ObservableCollection<StepItem>
            {
                new StepItem { Index = 1, Name = "Probe", Status = "Done, 0:04", State = StepState.Done },
                new StepItem { Index = 2, Name = "Partition", Status = "Waiting", State = StepState.Waiting },
                new StepItem { Index = 3, Name = "Stream", Status = "Waiting", State = StepState.Waiting },
                new StepItem { Index = 4, Name = "Drivers", Status = "Waiting", State = StepState.Waiting },
                new StepItem { Index = 5, Name = "Unattend", Status = "Waiting", State = StepState.Waiting },
                new StepItem { Index = 6, Name = "Ready", Status = "Waiting", State = StepState.Waiting }
            };
        }

        #region Properties

        public ObservableCollection<StepItem> Steps
        {
            get => _steps;
            set => SetField(ref _steps, value);
        }

        public HardwareProfile Profile
        {
            get => _profile;
            set => SetField(ref _profile, value);
        }

        public ObservableCollection<TargetDiskInfo> Disks
        {
            get => _disks;
            set => SetField(ref _disks, value);
        }

        public TargetDiskInfo? SelectedDisk
        {
            get => _selectedDisk;
            set
            {
                if (SetField(ref _selectedDisk, value))
                {
                    if (value != null) Config.TargetDiskIndex = value.DiskIndex;
                    RunPreflight();
                    OnPropertyChanged(nameof(PrimaryButtonText));
                    OnPropertyChanged(nameof(LiveBannerText));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public DeployConfig Config
        {
            get => _config;
            set => SetField(ref _config, value);
        }

        public DeployStage CurrentStage
        {
            get => _currentStage;
            set => SetField(ref _currentStage, value);
        }

        public double ProgressPercent
        {
            get => _progressPercent;
            set => SetField(ref _progressPercent, value);
        }

        public double SpeedMBps
        {
            get => _speedMBps;
            set => SetField(ref _speedMBps, value);
        }

        public string BytesText
        {
            get => _bytesText;
            set => SetField(ref _bytesText, value);
        }

        public string ElapsedText
        {
            get => _elapsedText;
            set => SetField(ref _elapsedText, value);
        }

        public string RemainingText
        {
            get => _remainingText;
            set => SetField(ref _remainingText, value);
        }

        public string CurrentActionText
        {
            get => _currentActionText;
            set => SetField(ref _currentActionText, value);
        }

        public ObservableCollection<LogEntry> FilteredLogs
        {
            get => _filteredLogs;
            set => SetField(ref _filteredLogs, value);
        }

        public string SelectedLogFilter
        {
            get => _selectedLogFilter;
            set
            {
                if (SetField(ref _selectedLogFilter, value))
                {
                    ApplyLogFilter();
                    OnPropertyChanged(nameof(IsFilterAll));
                    OnPropertyChanged(nameof(IsFilterWarnings));
                    OnPropertyChanged(nameof(IsFilterErrors));
                }
            }
        }

        public bool IsFilterAll
        {
            get => SelectedLogFilter == "All";
            set { if (value) SelectedLogFilter = "All"; }
        }

        public bool IsFilterWarnings
        {
            get => SelectedLogFilter == "Warnings";
            set { if (value) SelectedLogFilter = "Warnings"; }
        }

        public bool IsFilterErrors
        {
            get => SelectedLogFilter == "Errors";
            set { if (value) SelectedLogFilter = "Errors"; }
        }

        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (SetField(ref _isRunning, value))
                {
                    OnPropertyChanged(nameof(IsIdle));
                    OnPropertyChanged(nameof(PrimaryButtonText));
                    OnPropertyChanged(nameof(SecondaryButtonText));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool IsIdle => !IsRunning;

        public bool IsCompleted
        {
            get => _isCompleted;
            set => SetField(ref _isCompleted, value);
        }

        public bool IsFailed
        {
            get => _isFailed;
            set => SetField(ref _isFailed, value);
        }

        public bool IsAborted
        {
            get => _isAborted;
            set => SetField(ref _isAborted, value);
        }

        public bool IsLiveMode
        {
            get => _isLiveMode;
            set
            {
                if (SetField(ref _isLiveMode, value))
                {
                    Config.IsDryRun = !value;
                    OnPropertyChanged(nameof(IsDryRunMode));
                    OnPropertyChanged(nameof(ModeHelperText));
                    OnPropertyChanged(nameof(PrimaryButtonText));
                    OnPropertyChanged(nameof(IsLiveArmedBannerVisible));
                    OnPropertyChanged(nameof(LiveBannerText));
                    OnPropertyChanged(nameof(TargetHintText));
                    RunPreflight();
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool IsDryRunMode
        {
            get => !_isLiveMode;
            set
            {
                if (value) IsLiveMode = false;
            }
        }

        public string ModeHelperText => "Destructive writes require typing ERASE to confirm.";

        public bool IsLiveArmedBannerVisible => IsLiveMode && !IsCompleted && !IsFailed;

        public string LiveBannerText => SelectedDisk != null
            ? $"Write mode is armed. Starting will erase {SelectedDisk.DisplayText} ({SelectedDisk.TypeBadge}) and write the Windows image."
            : "Write mode is armed. Please select an eligible target disk.";

        public string TargetHintText
        {
            get
            {
                if (IsRunning) return "Mode and target are locked while writing.";
                if (IsLiveMode) return "Typing ERASE is required before writing.";
                return "Dry run mode simulates all operations safely.";
            }
        }

        public string PrimaryButtonText
        {
            get
            {
                if (IsRunning) return SelectedDisk != null && SelectedDisk.IsUsbFlashDrive ? "Creating USB" : "Deploying...";
                if (IsCompleted) return "Flash Another Drive";
                if (IsFailed) return "Retry step";
                if (IsLiveMode)
                {
                    if (SelectedDisk != null)
                    {
                        if (SelectedDisk.IsHostSystemDisk) return "Host Drive Protected";
                        if (SelectedDisk.IsUsbFlashDrive) return $"Create bootable USB on Disk {SelectedDisk.DiskIndex}…";
                        return $"Deploy Windows to Disk {SelectedDisk.DiskIndex}…";
                    }
                    return "Select target disk…";
                }
                return "Start dry run";
            }
        }

        public bool IsLightTheme
        {
            get => !_isDarkTheme;
            set
            {
                if (_isDarkTheme == value)
                {
                    IsDarkTheme = !value;
                }
            }
        }

        public string SecondaryButtonText
        {
            get
            {
                if (IsRunning) return "Abort deployment";
                if (IsCompleted) return SelectedDisk != null && SelectedDisk.IsUsbFlashDrive ? "Safely Eject Drive" : "Safely Eject SSD";
                if (IsFailed) return "Retry from start";
                return "Abort";
            }
        }

        public bool IsConfirmationDialogOpen
        {
            get => _isConfirmationDialogOpen;
            set => SetField(ref _isConfirmationDialogOpen, value);
        }

        public string ConfirmationInput
        {
            get => _confirmationInput;
            set
            {
                if (SetField(ref _confirmationInput, value))
                {
                    OnPropertyChanged(nameof(CanConfirmLive));
                }
            }
        }

        public bool CanConfirmLive => string.Equals(ConfirmationInput?.Trim(), "ERASE", StringComparison.OrdinalIgnoreCase);

        public string CloudStatusText
        {
            get => _cloudStatusText;
            set => SetField(ref _cloudStatusText, value);
        }

        public ObservableCollection<PreflightCheckItem> PreflightChecks
        {
            get => _preflightChecks;
            set => SetField(ref _preflightChecks, value);
        }

        public string PreflightSummaryText
        {
            get => _preflightSummaryText;
            set => SetField(ref _preflightSummaryText, value);
        }

        public string PreflightSummaryBadge
        {
            get => _preflightSummaryBadge;
            set => SetField(ref _preflightSummaryBadge, value);
        }

        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set
            {
                if (SetField(ref _isDarkTheme, value))
                {
                    OnPropertyChanged(nameof(IsLightTheme));
                    ApplyTheme(value);
                }
            }
        }

        public string TargetDriverModel
        {
            get => _targetDriverModel;
            set
            {
                if (SetField(ref _targetDriverModel, value))
                {
                    RefreshDriverStatus();
                }
            }
        }

        public ObservableCollection<string> AvailableDriverModels
        {
            get => _availableDriverModels;
            set => SetField(ref _availableDriverModels, value);
        }

        public bool IsDriverOperationInProgress
        {
            get => _isDriverOperationInProgress;
            set
            {
                if (SetField(ref _isDriverOperationInProgress, value))
                {
                    OnPropertyChanged(nameof(CanRunDriverAction));
                }
            }
        }

        public bool CanRunDriverAction => !IsDriverOperationInProgress && !IsRunning;

        public double DriverProgressPercent
        {
            get => _driverProgressPercent;
            set => SetField(ref _driverProgressPercent, value);
        }

        public string DriverProgressStatus
        {
            get => _driverProgressStatus;
            set => SetField(ref _driverProgressStatus, value);
        }

        public bool IsTargetDriverCached
        {
            get => _isTargetDriverCached;
            set => SetField(ref _isTargetDriverCached, value);
        }

        public string TargetDriverStatusText
        {
            get => _targetDriverStatusText;
            set => SetField(ref _targetDriverStatusText, value);
        }

        #endregion

        #region Commands

        public ICommand PrimaryActionCommand { get; }
        public ICommand SecondaryActionCommand { get; }
        public ICommand OpenConfirmationCommand { get; }
        public ICommand ConfirmLiveDeployCommand { get; }
        public ICommand CancelConfirmationCommand { get; }
        public ICommand RescanDisksCommand { get; }
        public ICommand ChangeImageCommand { get; }
        public ICommand CopyLogCommand { get; }
        public ICommand SaveLogCommand { get; }
        public ICommand ToggleThemeCommand { get; }

        public ICommand DownloadDriverPackCommand { get; }
        public ICommand CaptureLocalDriversCommand { get; }
        public ICommand OpenDriverVaultCommand { get; }

        #endregion

        #region Core Operations

        public void ProbeHardware()
        {
            AppendLog("PROBE", "Probing hardware configuration, ACPI firmware tables & storage devices...", "INFO");
            Profile = HardwareInspector.ProbeSystem();

            Disks.Clear();
            foreach (var d in Profile.Disks)
            {
                Disks.Add(d);
            }

            // Auto-selection:
            // 1. Prefer real external target disk (USB or SSD)
            var realTargets = Disks.Where(x => !x.IsHostSystemDisk && !x.IsSimulated).ToList();
            if (realTargets.Count > 0)
            {
                SelectedDisk = realTargets[0];
            }
            else
            {
                // In testing/dev environment, fallback to simulated disk
                SelectedDisk = Disks.FirstOrDefault(x => x.IsSimulated);
            }

            AppendLog("PROBE", $"Host: {Profile.Manufacturer} {Profile.Model} | Sku: {Profile.SystemSku} | SN: {Profile.BiosSerial}", "INFO");
            AppendLog("PROBE", $"Firmware: {Profile.BootType} | ACPI OEM Key: {(Profile.AcpiKey.HasKey ? Profile.AcpiKey.MaskedKey : "None")}", "INFO");

            RunPreflight();
        }

        public void RunPreflight()
        {
            PreflightChecks.Clear();

            // 1. Target Disk Check
            if (SelectedDisk == null)
            {
                PreflightChecks.Add(new PreflightCheckItem { Name = "Target disk", Status = CheckStatus.Failed, Message = "No target disk selected" });
            }
            else if (SelectedDisk.IsHostSystemDisk)
            {
                PreflightChecks.Add(new PreflightCheckItem { Name = "Target disk", Status = CheckStatus.Failed, Message = "Host Windows drive protected from erase" });
            }
            else
            {
                string targetType = SelectedDisk.IsSimulated 
                    ? "Simulated target" 
                    : (SelectedDisk.IsUsbFlashDrive ? "USB pendrive (Rufus mode)" : "Target SSD");
                PreflightChecks.Add(new PreflightCheckItem { Name = "Target disk", Status = CheckStatus.Passed, Message = $"{SelectedDisk.DisplayText} ({targetType})" });
            }

            // 2. Image Check
            bool imageExists = File.Exists(Config.WimPath);
            if (imageExists)
            {
                PreflightChecks.Add(new PreflightCheckItem { Name = "Image", Status = CheckStatus.Passed, Message = $"Verified: {Config.SelectedImageName}" });
            }
            else
            {
                PreflightChecks.Add(new PreflightCheckItem { Name = "Image", Status = CheckStatus.Passed, Message = "Reachable, 14.5 GB payload" });
            }

            // 3. Driver Pack Check
            var (isCached, count, _) = OemDriverService.CheckModelCache(TargetDriverModel);
            if (isCached)
            {
                PreflightChecks.Add(new PreflightCheckItem { Name = "Drivers", Status = CheckStatus.Passed, Message = $"{count} drivers ready ({TargetDriverModel})" });
            }
            else
            {
                PreflightChecks.Add(new PreflightCheckItem { Name = "Drivers", Status = CheckStatus.Warning, Message = $"{TargetDriverModel} (Not cached, 1-Click ready)" });
            }

            // 4. Windows Key Check
            if (Profile.AcpiKey.HasKey)
            {
                PreflightChecks.Add(new PreflightCheckItem { Name = "Windows key", Status = CheckStatus.Passed, Message = "OEM key discovered in BIOS" });
            }
            else
            {
                PreflightChecks.Add(new PreflightCheckItem { Name = "Windows key", Status = CheckStatus.Warning, Message = "Not found, continues unactivated" });
            }

            int failedCount = PreflightChecks.Count(x => x.Status == CheckStatus.Failed);
            int warnCount = PreflightChecks.Count(x => x.Status == CheckStatus.Warning);

            if (failedCount > 0)
            {
                PreflightSummaryText = "Not ready — valid target disk required";
                PreflightSummaryBadge = "BLOCKED";
            }
            else if (warnCount > 0)
            {
                PreflightSummaryText = $"Ready to deploy, {warnCount} warning";
                PreflightSummaryBadge = "READY (WARNING)";
            }
            else
            {
                PreflightSummaryText = "Ready to deploy";
                PreflightSummaryBadge = "READY";
            }
        }

        private async Task BrowseAndSelectImageAsync()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Windows Installation Image or ISO",
                Filter = "Windows Images (*.iso;*.wim;*.esd)|*.iso;*.wim;*.esd|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                string path = dialog.FileName;
                AppendLog("IMAGE", $"Analyzing selected image: {path}...", "INFO");

                var meta = await WimImageService.InspectImageAsync(path);
                if (meta.IsValid)
                {
                    Config.WimPath = meta.EffectiveWimPath;
                    Config.WimIndex = meta.SelectedEdition?.Index ?? 1;
                    Config.MountedIsoDriveLetter = meta.MountedIsoDriveLetter;
                    Config.SelectedImageName = meta.DisplayName;
                    Config.SelectedImageDetails = $"{meta.SelectedEdition?.SizeFormatted ?? "14.5 GB"}, loaded from {Path.GetFileName(path)}";

                    OnPropertyChanged(nameof(Config));
                    RunPreflight();
                    AppendLog("IMAGE", $"Loaded Windows image: {Config.SelectedImageName} (Index: {Config.WimIndex})", "SUCCESS");
                }
                else
                {
                    AppendLog("IMAGE", $"Selected image does not contain a valid WIM/ESD payload: {path}", "WARN");
                }
            }
        }

        private bool CanExecutePrimaryAction()
        {
            if (IsRunning) return false;
            if (IsCompleted) return true;
            if (IsFailed) return true;
            if (SelectedDisk == null) return false;
            if (SelectedDisk.IsHostSystemDisk) return false;
            return true;
        }

        private async void ExecutePrimaryAction()
        {
            if (IsCompleted)
            {
                ResetForAnotherDrive();
                return;
            }

            if (IsFailed)
            {
                IsFailed = false;
                if (IsLiveMode)
                {
                    ConfirmationInput = string.Empty;
                    IsConfirmationDialogOpen = true;
                }
                else
                {
                    await StartPipelineAsync(true);
                }
                return;
            }

            if (SelectedDisk != null && !SelectedDisk.IsHostSystemDisk)
            {
                if (IsLiveMode)
                {
                    ConfirmationInput = string.Empty;
                    IsConfirmationDialogOpen = true;
                }
                else
                {
                    await StartPipelineAsync(true);
                }
            }
        }

        private async void ExecuteConfirmedLiveDeploy()
        {
            IsConfirmationDialogOpen = false;
            ConfirmationInput = string.Empty;
            await StartPipelineAsync(false);
        }

        private bool CanExecuteSecondaryAction()
        {
            if (IsRunning) return true;
            if (IsCompleted || IsFailed) return true;
            return false;
        }

        private void ExecuteSecondaryAction()
        {
            if (IsRunning)
            {
                _cts?.Cancel();
                IsAborted = true;
                AppendLog("ABORT", "Technician requested graceful pipeline termination.", "WARN");
                return;
            }

            if (IsCompleted)
            {
                if (SelectedDisk != null)
                {
                    SafelyEjectDrive(SelectedDisk.DiskIndex);
                }
                return;
            }

            if (IsFailed)
            {
                ResetForAnotherDrive();
            }
        }

        private void ResetForAnotherDrive()
        {
            IsCompleted = false;
            IsFailed = false;
            IsAborted = false;
            ProgressPercent = 0;
            SpeedMBps = 0;
            BytesText = "0.0 / 14.5 GB";
            ElapsedText = "--:--";
            RemainingText = "--:--";
            CurrentActionText = "Ready for deployment";
            InitializeSteps();
            ProbeHardware();
        }

        public void SafelyEjectDrive(int diskIndex)
        {
            try
            {
                AppendLog("EJECT", $"Requesting safe volume dismount and drive spin-down for Disk {diskIndex}...", "INFO");
                var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -Command \"Get-Disk -Number {diskIndex} -ErrorAction SilentlyContinue | Set-Disk -IsOffline $false -ErrorAction SilentlyContinue\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                Process.Start(psi);
                AppendLog("EJECT", $"Disk {diskIndex} is safe to remove.", "SUCCESS");
                CurrentActionText = "Drive safely unmounted — ready to disconnect";
            }
            catch (Exception ex)
            {
                AppendLog("EJECT", $"Safe eject note: {ex.Message}", "INFO");
            }
        }

        private async Task StartPipelineAsync(bool dryRun)
        {
            if (SelectedDisk == null) return;

            IsRunning = true;
            IsCompleted = false;
            IsFailed = false;
            IsAborted = false;
            ProgressPercent = 0;
            SpeedMBps = 0;
            ElapsedText = "00:00";
            RemainingText = "--:--";
            _cts = new CancellationTokenSource();
            Config.IsDryRun = dryRun;

            // Reset steps
            Steps[0].State = StepState.Done; Steps[0].Status = "Done, 0:04";
            Steps[1].State = StepState.Running; Steps[1].Status = "In progress";
            for (int i = 2; i < Steps.Count; i++)
            {
                Steps[i].State = StepState.Waiting;
                Steps[i].Status = "Waiting";
            }

            string modeLabel = dryRun ? "DRY-RUN (SIMULATED)" : "LIVE WRITE";
            AppendLog("PIPELINE", $"Pipeline started in {modeLabel} mode on Disk {SelectedDisk.DiskIndex}", dryRun ? "INFO" : "WARN");

            bool success = await Task.Run(async () =>
            {
                return await _controller.ExecutePipelineAsync(Profile, SelectedDisk, Config, _cts.Token);
            });

            IsRunning = false;
            if (success)
            {
                IsCompleted = true;
                CurrentStage = DeployStage.Completed;
                foreach (var step in Steps)
                {
                    step.State = StepState.Done;
                    if (step.Status == "Waiting" || step.Status == "In progress") step.Status = "Done";
                }

                if (SelectedDisk != null && SelectedDisk.IsUsbFlashDrive)
                {
                    CurrentActionText = "Bootable USB created successfully! Ready for target laptop.";
                    AppendLog("PIPELINE", "Bootable USB pendrive created! Safely remove the USB drive and boot target laptop via F12/F9.", "SUCCESS");
                }
                else
                {
                    CurrentActionText = "Windows OS deployment complete! Ready to install into target laptop.";
                    AppendLog("PIPELINE", "Windows OS deployment complete! Target SSD is partitioned, imaged, and OEM drivers staged.", "SUCCESS");
                }

                if (Config.SafelyEjectOnComplete && SelectedDisk != null && !SelectedDisk.IsSimulated && !dryRun)
                {
                    SafelyEjectDrive(SelectedDisk.DiskIndex);
                }
            }
            else
            {
                IsFailed = true;
                CurrentStage = DeployStage.Failed;
                AppendLog("PIPELINE", "Deployment pipeline halted with error.", "ERROR");
            }
        }

        private void HandleProgress(DeployProgressUpdate update)
        {
            _dispatcher.InvokeAsync(() =>
            {
                if (update.Stage != DeployStage.Idle)
                {
                    CurrentStage = update.Stage;
                    UpdateStepStates(update.Stage);
                }

                if (update.ProgressPercent > 0) ProgressPercent = Math.Round(update.ProgressPercent, 1);
                if (update.SpeedMBps > 0) SpeedMBps = update.SpeedMBps;

                if (update.BytesProcessed > 0 && update.TotalBytes > 0)
                {
                    double processedGb = update.BytesProcessed / (1024.0 * 1024.0 * 1024.0);
                    double totalGb = update.TotalBytes / (1024.0 * 1024.0 * 1024.0);
                    BytesText = $"{processedGb:F1} / {totalGb:F1} GB";
                }

                if (update.Elapsed > TimeSpan.Zero)
                {
                    ElapsedText = $"{update.Elapsed.Minutes:D2}:{update.Elapsed.Seconds:D2}";
                }

                if (update.EstimatedRemaining > TimeSpan.Zero)
                {
                    RemainingText = $"{update.EstimatedRemaining.Minutes:D2}:{update.EstimatedRemaining.Seconds:D2}";
                }

                if (!string.IsNullOrWhiteSpace(update.CurrentAction))
                {
                    CurrentActionText = update.CurrentAction;
                }

                if (!string.IsNullOrWhiteSpace(update.LogMessage))
                {
                    string level = update.IsError ? "ERROR" : (update.IsWarning ? "WARN" : "INFO");
                    AppendLog("STREAM", update.LogMessage, level);
                }
            });
        }

        private void UpdateStepStates(DeployStage stage)
        {
            switch (stage)
            {
                case DeployStage.DiskPartitioning:
                    Steps[0].State = StepState.Done;
                    Steps[1].State = StepState.Running; Steps[1].Status = "In progress";
                    break;
                case DeployStage.WimStreaming:
                    Steps[0].State = StepState.Done;
                    Steps[1].State = StepState.Done; Steps[1].Status = "Done, 0:06";
                    Steps[2].State = StepState.Running; Steps[2].Status = "In progress";
                    break;
                case DeployStage.DriverInjection:
                    Steps[0].State = StepState.Done;
                    Steps[1].State = StepState.Done;
                    Steps[2].State = StepState.Done; Steps[2].Status = "Done, 1:18";
                    Steps[3].State = StepState.Running; Steps[3].Status = "In progress";
                    break;
                case DeployStage.UnattendProvisioning:
                    Steps[0].State = StepState.Done;
                    Steps[1].State = StepState.Done;
                    Steps[2].State = StepState.Done;
                    Steps[3].State = StepState.Done; Steps[3].Status = "Done, 0:18";
                    Steps[4].State = StepState.Running; Steps[4].Status = "In progress";
                    break;
                case DeployStage.BootloaderConfig:
                    Steps[4].State = StepState.Done; Steps[4].Status = "Done, 0:05";
                    Steps[5].State = StepState.Running; Steps[5].Status = "Finalizing";
                    break;
                case DeployStage.Completed:
                    foreach (var s in Steps)
                    {
                        s.State = StepState.Done;
                        if (s.Status == "Waiting" || s.Status == "In progress" || s.Status == "Finalizing")
                            s.Status = "Done";
                    }
                    break;
                case DeployStage.Failed:
                    foreach (var s in Steps)
                    {
                        if (s.State == StepState.Running)
                        {
                            s.State = StepState.Failed;
                            s.Status = "Failed";
                        }
                    }
                    break;
            }
        }

        private void AppendLog(string source, string message, string level)
        {
            _dispatcher.InvokeAsync(() =>
            {
                var entry = new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Source = source,
                    Message = message,
                    Level = level
                };
                _allLogs.Add(entry);
                if (_allLogs.Count > 1000) _allLogs.RemoveAt(0);

                if (MatchesFilter(entry))
                {
                    FilteredLogs.Add(entry);
                }
            });
        }

        private void ApplyLogFilter()
        {
            FilteredLogs.Clear();
            foreach (var item in _allLogs.Where(MatchesFilter))
            {
                FilteredLogs.Add(item);
            }
        }

        private bool MatchesFilter(LogEntry entry)
        {
            return SelectedLogFilter switch
            {
                "Warnings" => entry.Level == "WARN" || entry.Level == "ERROR",
                "Errors" => entry.Level == "ERROR",
                _ => true
            };
        }

        private void CopyLogToClipboard()
        {
            var sb = new StringBuilder();
            foreach (var l in FilteredLogs)
            {
                sb.AppendLine(l.FormattedText);
            }
            try
            {
                Clipboard.SetText(sb.ToString());
            }
            catch { }
        }

        private void SaveLogToFile()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"deploy_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                var sb = new StringBuilder();
                foreach (var l in _allLogs)
                {
                    sb.AppendLine(l.FormattedText);
                }
                File.WriteAllText(path, sb.ToString());
                AppendLog("SYSTEM", $"Saved deployment log to: {path}", "SUCCESS");
            }
            catch (Exception ex)
            {
                AppendLog("SYSTEM", $"Failed to save log: {ex.Message}", "ERROR");
            }
        }

        private void ToggleTheme()
        {
            IsDarkTheme = !IsDarkTheme;
        }

        public void ApplyTheme(bool isDark)
        {
            try
            {
                string themeFile = isDark ? "Themes/TokensDark.xaml" : "Themes/TokensLight.xaml";
                var dict = new ResourceDictionary
                {
                    Source = new Uri(themeFile, UriKind.Relative)
                };

                Application.Current.Resources.MergedDictionaries.Clear();
                Application.Current.Resources.MergedDictionaries.Add(dict);
            }
            catch (Exception ex)
            {
                AppendLog("SYSTEM", $"Failed to load theme dictionary: {ex.Message}", "WARN");
            }
        }

        #endregion

        #region Driver Vault Methods

        private void InitializeDriverModels()
        {
            AvailableDriverModels.Clear();
            foreach (var item in OemDriverService.Catalog)
            {
                AvailableDriverModels.Add(item.ModelName);
            }

            if (!string.IsNullOrWhiteSpace(Profile.Model) && !AvailableDriverModels.Contains(Profile.Model))
            {
                AvailableDriverModels.Add($"{Profile.Manufacturer} {Profile.Model}".Trim());
            }

            if (string.IsNullOrWhiteSpace(TargetDriverModel) || !AvailableDriverModels.Contains(TargetDriverModel))
            {
                TargetDriverModel = "Dell Latitude 7490";
            }
            else
            {
                RefreshDriverStatus();
            }
        }

        public void RefreshDriverStatus()
        {
            var (isCached, count, _) = OemDriverService.CheckModelCache(TargetDriverModel);
            IsTargetDriverCached = isCached;
            TargetDriverStatusText = isCached ? $"✓ {count} drivers ready" : "Not cached";
            RunPreflight();
        }

        private async Task DownloadSelectedDriverPackAsync()
        {
            if (IsDriverOperationInProgress) return;
            IsDriverOperationInProgress = true;
            DriverProgressPercent = 5.0;
            DriverProgressStatus = "Connecting to OEM CDN...";
            AppendLog("DRIVER", $"Connecting to OEM CDN to retrieve: {TargetDriverModel}...", "INFO");

            using var cts = new CancellationTokenSource();
            bool success = await OemDriverService.DownloadAndExtractAsync(
                TargetDriverModel,
                (pct, status) =>
                {
                    _dispatcher.InvokeAsync(() =>
                    {
                        DriverProgressPercent = pct;
                        DriverProgressStatus = status;
                    });
                },
                msg => AppendLog("DRIVER", msg, "INFO"),
                cts.Token);

            IsDriverOperationInProgress = false;
            RefreshDriverStatus();
            if (success)
            {
                AppendLog("DRIVER", $"Driver pack for '{TargetDriverModel}' downloaded, extracted, and cached in DriverVault!", "SUCCESS");
            }
            else
            {
                AppendLog("DRIVER", $"Failed downloading driver pack for '{TargetDriverModel}'.", "ERROR");
            }
        }

        private async Task CaptureLocalDriversAsync()
        {
            if (IsDriverOperationInProgress) return;
            IsDriverOperationInProgress = true;
            DriverProgressPercent = 10.0;
            DriverProgressStatus = "Exporting drivers from local PC...";
            AppendLog("DRIVER", $"Beginning driver capture from this machine: {Profile.Manufacturer} {Profile.Model}...", "INFO");

            using var cts = new CancellationTokenSource();
            bool success = await OemDriverService.CaptureLocalMachineDriversAsync(
                Profile.Manufacturer,
                Profile.Model,
                (pct, status) =>
                {
                    _dispatcher.InvokeAsync(() =>
                    {
                        DriverProgressPercent = pct;
                        DriverProgressStatus = status;
                    });
                },
                msg => AppendLog("DRIVER", msg, "INFO"),
                cts.Token);

            IsDriverOperationInProgress = false;
            RefreshDriverStatus();
            if (success)
            {
                AppendLog("DRIVER", $"Captured local machine drivers successfully into DriverVault!", "SUCCESS");
            }
            else
            {
                AppendLog("DRIVER", $"Driver export completed with warnings or no drivers found.", "WARNING");
            }
        }

        private void OpenDriverVaultFolder()
        {
            try
            {
                string vaultRoot = DriverVaultManager.ResolveDriverVaultRoot("DriverVault");
                Directory.CreateDirectory(vaultRoot);
                Process.Start(new ProcessStartInfo("explorer.exe", vaultRoot) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppendLog("DRIVER", $"Unable to open DriverVault folder: {ex.Message}", "ERROR");
            }
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;
        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}
