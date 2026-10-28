using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VeloxDeploy.Models
{
    public enum DeployStage
    {
        Idle,
        HardwareProbe,
        DiskPartitioning,
        WimStreaming,
        DriverInjection,
        UnattendProvisioning,
        BootloaderConfig,
        Completed,
        Failed,
        Aborted
    }

    public enum StepState
    {
        Waiting,
        Running,
        Done,
        Failed
    }

    public class StepItem : INotifyPropertyChanged
    {
        private string _status = "Waiting";
        private StepState _state = StepState.Waiting;

        public int Index { get; set; }
        public string Name { get; set; } = string.Empty;

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public StepState State
        {
            get => _state;
            set { _state = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? prop = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }
    }

    public enum CheckStatus
    {
        Passed,
        Warning,
        Failed
    }

    public enum FirmwareBootType
    {
        Unknown,
        Bios,
        Uefi
    }

    public class AcpiKeyInfo
    {
        public bool HasKey { get; set; }
        public string ProductKey { get; set; } = string.Empty;
        public string MaskedKey => string.IsNullOrWhiteSpace(ProductKey) || ProductKey.Length < 10
            ? "Not detected"
            : ProductKey[..5] + "-XXXXX-XXXXX-XXXXX-" + ProductKey[^5..];
        public string TableSignature { get; set; } = "MSDM";
        public string KeyEdition { get; set; } = "Windows 10/11 Pro (OEM)";
        public string SourceMethod { get; set; } = "ACPI Firmware Table";
    }

    public class TargetDiskInfo
    {
        public int DiskIndex { get; set; }
        public string Model { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string InterfaceType { get; set; } = "NVMe";
        public long SizeBytes { get; set; }
        public bool IsBoot { get; set; } = false;
        public bool IsSystem { get; set; } = false;
        public bool IsHostSystemDisk => IsBoot || IsSystem;
        public bool IsSimulated { get; set; } = false;
        public bool HasExistingData { get; set; } = true;

        public string SizeFormatted => SizeBytes > 0 
            ? $"{(SizeBytes / (1024.0 * 1024.0 * 1024.0)):F0} GB" 
            : "Unknown";

        public bool IsNvme => InterfaceType.Contains("NVMe", StringComparison.OrdinalIgnoreCase) || Model.Contains("NVMe", StringComparison.OrdinalIgnoreCase);
        public bool IsUsb => InterfaceType.Contains("USB", StringComparison.OrdinalIgnoreCase);

        // Smart Target Identification
        public bool IsUsbFlashDrive => !IsSimulated && IsUsb && (SizeBytes < (135L * 1024 * 1024 * 1024));
        public bool IsDirectTargetSsd => !IsUsbFlashDrive && !IsSimulated;

        public string TypeBadge => IsUsbFlashDrive ? "USB" : (IsHostSystemDisk ? "Host" : (IsNvme ? "NVMe" : "SSD"));

        public string TargetModeBadge => IsSimulated 
            ? "Dry Run Simulation" 
            : (IsHostSystemDisk ? "Host Windows (Protected)" : (IsUsbFlashDrive ? "Bootable USB Pendrive" : "Direct SSD Target"));

        public string SafetyBadge => IsHostSystemDisk 
            ? "Host OS (Protected)" 
            : (IsSimulated ? "Simulated Target" : (HasExistingData ? "Will be erased" : "Unformatted"));

        public string DisplayText
        {
            get
            {
                if (IsSimulated) return $"[SIMULATION] {Model} ({SizeFormatted})";
                string tag = IsHostSystemDisk ? " [Host Windows - Protected]" : (IsUsbFlashDrive ? " [USB Media]" : " [Target SSD]");
                return $"Disk {DiskIndex}: {Model} ({SizeFormatted} {InterfaceType}){tag}";
            }
        }
    }

    public class HardwareProfile
    {
        public string Manufacturer { get; set; } = "Unknown";
        public string Model { get; set; } = "Depot Laptop";
        public string SystemSku { get; set; } = "Default SKU";
        public string BiosSerial { get; set; } = "VLX-SERIAL-01";
        public string BiosVersion { get; set; } = string.Empty;
        public string CpuName { get; set; } = "x86-64 Processor";
        public int CpuCores { get; set; } = 4;
        public int CpuThreads { get; set; } = 8;
        public double RamGb { get; set; } = 16.0;
        public FirmwareBootType BootType { get; set; } = FirmwareBootType.Uefi;
        public AcpiKeyInfo AcpiKey { get; set; } = new();
        public List<TargetDiskInfo> Disks { get; set; } = new();
    }

    public class PreflightCheckItem : INotifyPropertyChanged
    {
        private CheckStatus _status = CheckStatus.Passed;
        private string _message = string.Empty;

        public string Name { get; set; } = string.Empty;

        public CheckStatus Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public string Message
        {
            get => _message;
            set { _message = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? prop = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }
    }

    public class DriverVaultPackage
    {
        public string Name { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;
        public string ModelPattern { get; set; } = string.Empty;
        public string DirectoryPath { get; set; } = string.Empty;
        public int DriverCount { get; set; } = 18;
        public bool IsMatched { get; set; } = true;
    }

    public class DeployConfig
    {
        public int TargetDiskIndex { get; set; } = 0;
        public string WimPath { get; set; } = "install.wim";
        public int WimIndex { get; set; } = 1;
        public string? MountedIsoDriveLetter { get; set; }
        public string DriverVaultPath { get; set; } = "DriverVault";
        public bool IsDryRun { get; set; } = false; // Always live write to target disk by default
        public string ApiEndpoint { get; set; } = "http://13.206.106.146";

        // Automation Toggles (Target Laptop & Bench Workflow)
        public bool SkipMicrosoftAccount { get; set; } = true;
        public bool AutoLogonUser { get; set; } = true;
        public bool LaunchVeloxBench { get; set; } = true;
        public bool SafelyEjectOnComplete { get; set; } = true;
        public string AdminUsername { get; set; } = "User";

        // Image display details
        public string SelectedImageName { get; set; } = "Windows 11 Pro 24H2 (x64)";
        public string SelectedImageDetails { get; set; } = "14.5 GB, streamed from local repository";
    }

    public class DeployProgressUpdate
    {
        public DeployStage Stage { get; set; }
        public double ProgressPercent { get; set; }
        public double SpeedMBps { get; set; }
        public long BytesProcessed { get; set; }
        public long TotalBytes { get; set; }
        public TimeSpan Elapsed { get; set; }
        public TimeSpan EstimatedRemaining { get; set; }
        public string CurrentAction { get; set; } = string.Empty;
        public string? LogMessage { get; set; }
        public bool IsError { get; set; }
        public bool IsWarning { get; set; }
    }
}
