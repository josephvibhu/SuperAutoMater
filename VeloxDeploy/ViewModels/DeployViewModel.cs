using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using VeloxDeploy.Core;
using VeloxDeploy.Models;

namespace VeloxDeploy.ViewModels
{
    public class DeployViewModel : INotifyPropertyChanged
    {
        private readonly DeploymentController _controller = new();
        private CancellationTokenSource? _cts;
        private readonly Dispatcher _dispatcher;

        private HardwareProfile _profile = new();
        private ObservableCollection<TargetDiskInfo> _disks = new();
        private TargetDiskInfo? _selectedDisk;
        private DeployConfig _config = new();

        private DeployStage _currentStage = DeployStage.Idle;
        private double _progressPercent = 0.0;
        private double _speedMBps = 0.0;
        private string _bytesText = "0.0 / 14.5 GB";
        private string _elapsedText = "00:00";
        private string _remainingText = "--:--";
        private string _currentActionText = "Ready for deployment";
        private readonly StringBuilder _consoleBuilder = new();
        private string _consoleOutput = string.Empty;

        private bool _isRunning = false;
        private bool _isCompleted = false;
        private bool _isFailed = false;
        private bool _isDryRun = true; // Default to safe simulation mode for demo safety
        private bool _autoReboot = true;
        private int _countdownSeconds = 15;
        private DispatcherTimer? _rebootTimer;
        private string _cloudStatusText = "AWS LIGHTSAIL (13.206.106.146)";

        public DeployViewModel()
        {
            _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

            _controller.OnLog += AppendConsoleLog;
            _controller.OnProgress += HandleProgress;

            StartDeployCommand = new RelayCommand(async () => await StartPipelineAsync(false), () => !IsRunning && SelectedDisk != null);
            SimulateDeployCommand = new RelayCommand(async () => await StartPipelineAsync(true), () => !IsRunning && SelectedDisk != null);
            AbortCommand = new RelayCommand(AbortPipeline, () => IsRunning);
            RefreshDisksCommand = new RelayCommand(ProbeHardware, () => !IsRunning);

            // Initial hardware interrogation
            ProbeHardware();
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

        public string ConsoleOutput
        {
            get => _consoleOutput;
            set => SetField(ref _consoleOutput, value);
        }

        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (SetField(ref _isRunning, value))
                {
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

        public bool IsDryRun
        {
            get => _isDryRun;
            set
            {
                if (SetField(ref _isDryRun, value))
                {
                    Config.IsDryRun = value;
                }
            }
        }

        public bool AutoReboot
        {
            get => _autoReboot;
            set => SetField(ref _autoReboot, value);
        }

        public int CountdownSeconds
        {
            get => _countdownSeconds;
            set => SetField(ref _countdownSeconds, value);
        }

        public string CloudStatusText
        {
            get => _cloudStatusText;
            set => SetField(ref _cloudStatusText, value);
        }

        public ICommand StartDeployCommand { get; }
        public ICommand SimulateDeployCommand { get; }
        public ICommand AbortCommand { get; }
        public ICommand RefreshDisksCommand { get; }

        public void ProbeHardware()
        {
            AppendConsoleLog("[System] Interrogating hardware configuration & ACPI tables...");
            Profile = HardwareInspector.ProbeSystem();

            Disks.Clear();
            foreach (var d in Profile.Disks)
            {
                Disks.Add(d);
            }

            if (Disks.Count > 0)
            {
                // Auto-select fastest NVMe or first internal disk
                SelectedDisk = Disks[0];
            }

            AppendConsoleLog($"[System] Discovered: {Profile.Manufacturer} {Profile.Model} ({Profile.SystemSku})");
            AppendConsoleLog($"[System] CPU: {Profile.CpuName} ({Profile.CpuCores}C / {Profile.CpuThreads}T) | RAM: {Profile.RamGb} GB");
            AppendConsoleLog($"[System] ACPI OEM Key: {(Profile.AcpiKey.HasKey ? Profile.AcpiKey.ProductKey : "NO KEY PRESENT")}");
            AppendConsoleLog($"[System] Target Storage: {(SelectedDisk != null ? SelectedDisk.DisplayText : "None Detected")}");
        }

        private async Task StartPipelineAsync(bool dryRun)
        {
            if (SelectedDisk == null)
            {
                MessageBox.Show("Please select a target storage disk.", "Velox Deploy", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!dryRun)
            {
                var result = MessageBox.Show(
                    $"WARNING: LIVE DEPLOYMENT ARMED!\n\nThis will completely ERASE Disk {SelectedDisk.DiskIndex} ({SelectedDisk.Model}) and apply Windows.\n\nAre you sure you want to proceed?",
                    "Velox Deploy - Live Disk Confirmation",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Exclamation);

                if (result != MessageBoxResult.Yes) return;
            }

            IsDryRun = dryRun;
            Config.IsDryRun = dryRun;
            IsRunning = true;
            IsCompleted = false;
            IsFailed = false;
            ProgressPercent = 0;
            SpeedMBps = 0;
            _cts = new CancellationTokenSource();

            _consoleBuilder.Clear();
            ConsoleOutput = string.Empty;

            bool success = await Task.Run(async () =>
            {
                return await _controller.ExecutePipelineAsync(Profile, SelectedDisk, Config, _cts.Token);
            });

            IsRunning = false;
            if (success)
            {
                IsCompleted = true;
                CurrentStage = DeployStage.Completed;
                if (AutoReboot)
                {
                    StartRebootCountdown();
                }
            }
            else
            {
                IsFailed = true;
                CurrentStage = DeployStage.Failed;
            }
        }

        private void StartRebootCountdown()
        {
            CountdownSeconds = 15;
            _rebootTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _rebootTimer.Tick += (s, e) =>
            {
                CountdownSeconds--;
                CurrentActionText = $"Rebooting into newly provisioned Windows in {CountdownSeconds}s...";
                if (CountdownSeconds <= 0)
                {
                    _rebootTimer.Stop();
                    AppendConsoleLog("[System] Auto-reboot triggered. Handing off to Windows OOBE / VeloxBench.");
                }
            };
            _rebootTimer.Start();
        }

        private void AbortPipeline()
        {
            _cts?.Cancel();
            _rebootTimer?.Stop();
            AppendConsoleLog("[System] Cancellation requested by technician.");
        }

        private void AppendConsoleLog(string line)
        {
            _dispatcher.InvokeAsync(() =>
            {
                _consoleBuilder.AppendLine($"[{DateTime.Now:HH:mm:ss}] {line}");
                if (_consoleBuilder.Length > 20000)
                {
                    _consoleBuilder.Remove(0, 5000);
                }
                ConsoleOutput = _consoleBuilder.ToString();
            });
        }

        private void HandleProgress(DeployProgressUpdate update)
        {
            _dispatcher.InvokeAsync(() =>
            {
                if (update.Stage != DeployStage.Idle) CurrentStage = update.Stage;
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
                    AppendConsoleLog(update.LogMessage);
                }
            });
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}
