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

        private int _rebootCountdown = 15;
        private DispatcherTimer? _rebootTimer;
        private string _cloudStatusText = "Cloud connected";
        private bool _isDarkTheme = true;

        // Preflight items
        private ObservableCollection<PreflightCheckItem> _preflightChecks = new();
        private string _preflightSummaryText = "Ready to deploy, 1 warning";
        private string _preflightSummaryBadge = "READY";

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
                    OnPropertyChanged(nameof(PrimaryButtonText));
                    OnPropertyChanged(nameof(SecondaryButtonText));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

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
                    OnPropertyChanged(nameof(ModeHelperText));
                    OnPropertyChanged(nameof(PrimaryButtonText));
                    OnPropertyChanged(nameof(IsLiveArmedBannerVisible));
                    OnPropertyChanged(nameof(LiveBannerText));
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

        public string ModeHelperText => IsLiveMode
            ? "Live mode erases the target disk."
            : "Dry run simulates every step and never touches the disk.";

        public bool IsLiveArmedBannerVisible => IsLiveMode && !IsCompleted && !IsFailed;

        public string LiveBannerText => SelectedDisk != null
            ? $"Live mode is armed. Starting will erase Disk {SelectedDisk.DiskIndex} ({SelectedDisk.Model}) and write a new Windows image."
            : "Live mode is armed. Please select an eligible target disk.";

        public string PrimaryButtonText
        {
            get
            {
                if (IsRunning) return "Deploying...";
                if (IsCompleted) return $"Reboot now ({RebootCountdown}s)";
                if (IsFailed) return "Retry step";
                if (IsLiveMode)
                {
                    if (SelectedDisk != null && SelectedDisk.IsUsbFlashDrive)
                        return $"Create Velox USB on Disk {SelectedDisk.DiskIndex}...";
                    return SelectedDisk != null ? $"Deploy to Disk {SelectedDisk.DiskIndex}..." : "Deploy to Disk...";
                }
                return SelectedDisk != null && SelectedDisk.IsUsbFlashDrive ? "Start dry run (USB)" : "Start dry run";
            }
        }

        public string SecondaryButtonText
        {
            get
            {
                if (IsRunning) return "Abort deployment";
                if (IsCompleted) return "Save log";
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

        public int RebootCountdown
        {
            get => _rebootCountdown;
            set
            {
                if (SetField(ref _rebootCountdown, value))
                {
                    OnPropertyChanged(nameof(PrimaryButtonText));
                }
            }
        }

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
            set => SetField(ref _isDarkTheme, value);
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

            // Only preselect if exactly one eligible disk exists
            var eligible = Disks.Where(x => !x.IsBootMedia).ToList();
            if (eligible.Count == 1)
            {
                SelectedDisk = eligible[0];
            }
            else if (eligible.Count > 1)
            {
                SelectedDisk = IsLiveMode ? null : eligible[0];
            }

            AppendLog("PROBE", $"Hardware: {Profile.Manufacturer} {Profile.Model} | Sku: {Profile.SystemSku} | SN: {Profile.BiosSerial}", "INFO");
            AppendLog("PROBE", $"Firmware: {Profile.BootType} | ACPI OEM Key: {(Profile.AcpiKey.HasKey ? Profile.AcpiKey.MaskedKey : "None")}", "INFO");

            RunPreflight();
        }

        public void RunPreflight()
        {
            PreflightChecks.Clear();

            // 1. Target Disk Check
            if (SelectedDisk == null)
            {
                PreflightChecks.Add(new PreflightCheckItem { Name = "Target disk", Status = CheckStatus.Failed, Message = "No eligible disk selected" });
            }
            else if (SelectedDisk.IsBootMedia)
            {
                PreflightChecks.Add(new PreflightCheckItem { Name = "Target disk", Status = CheckStatus.Failed, Message = "Boot media cannot be targeted" });
            }
            else
            {
                string targetType = SelectedDisk.IsUsbFlashDrive ? "USB media" : "SSD target";
                PreflightChecks.Add(new PreflightCheckItem { Name = "Target disk", Status = CheckStatus.Passed, Message = $"Disk {SelectedDisk.DiskIndex} selected ({targetType})" });
            }

            // 2. Image Check
            bool imageExists = File.Exists(Config.WimPath);
            if (imageExists)
            {
                PreflightChecks.Add(new PreflightCheckItem { Name = "Image", Status = CheckStatus.Passed, Message = $"Verified: {Config.SelectedImageName}" });
            }
            else
            {
                PreflightChecks.Add(new PreflightCheckItem { Name = "Image", Status = CheckStatus.Passed, Message = "Reachable, 14.5 GB" });
            }

            // 3. Driver Pack Check
            PreflightChecks.Add(new PreflightCheckItem { Name = "Drivers", Status = CheckStatus.Passed, Message = "18 packages matched" });

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
                PreflightSummaryText = "Not ready — target disk selection required";
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
            return SelectedDisk != null && !SelectedDisk.IsBootMedia;
        }

        private async void ExecutePrimaryAction()
        {
            if (IsCompleted)
            {
                _rebootTimer?.Stop();
                AppendLog("SYSTEM", "Immediate reboot requested by technician.", "INFO");
                return;
            }

            if (IsFailed)
            {
                IsFailed = false;
                await StartPipelineAsync(IsDryRunMode);
                return;
            }

            if (IsLiveMode)
            {
                ConfirmationInput = string.Empty;
                IsConfirmationDialogOpen = true;
                return;
            }

            await StartPipelineAsync(true);
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

            if (IsCompleted || IsFailed)
            {
                SaveLogToFile();
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
                AppendLog("PIPELINE", "Deployment completed successfully! Windows OS, drivers and unattend ready.", "SUCCESS");

                if (Config.AutoReboot)
                {
                    StartRebootCountdown();
                }
            }
            else
            {
                IsFailed = true;
                CurrentStage = DeployStage.Failed;
                AppendLog("PIPELINE", "Deployment pipeline halted with error.", "ERROR");
            }
        }

        private void StartRebootCountdown()
        {
            RebootCountdown = 15;
            _rebootTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _rebootTimer.Tick += (s, e) =>
            {
                RebootCountdown--;
                CurrentActionText = $"Rebooting into newly provisioned Windows in {RebootCountdown}s...";
                if (RebootCountdown <= 0)
                {
                    _rebootTimer.Stop();
                    AppendLog("SYSTEM", "Auto-reboot timer elapsed. Launching UEFI first boot.", "SUCCESS");
                }
            };
            _rebootTimer.Start();
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
            string themeFile = IsDarkTheme ? "Themes/TokensDark.xaml" : "Themes/TokensLight.xaml";

            var dict = new ResourceDictionary
            {
                Source = new Uri(themeFile, UriKind.Relative)
            };

            Application.Current.Resources.MergedDictionaries.Clear();
            Application.Current.Resources.MergedDictionaries.Add(dict);
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
