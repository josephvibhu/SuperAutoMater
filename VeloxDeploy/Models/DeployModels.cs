using System;
using System.Collections.Generic;

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
        public string SizeFormatted => SizeBytes > 0 
            ? $"{(SizeBytes / (1024.0 * 1024.0 * 1024.0)):F0} GB" 
            : "Unknown";
        public bool IsNvme => InterfaceType.Contains("NVMe", StringComparison.OrdinalIgnoreCase) || Model.Contains("NVMe", StringComparison.OrdinalIgnoreCase);
        public bool IsUsb => InterfaceType.Contains("USB", StringComparison.OrdinalIgnoreCase);
        public bool IsBootMedia { get; set; } = false;
        public bool HasExistingData { get; set; } = true;
        public string SafetyBadge => IsBootMedia ? "Boot media (Excluded)" : (HasExistingData ? "Has data" : "Unformatted");
        public string DisplayText => $"Disk {DiskIndex}: {Model} ({SizeFormatted} {InterfaceType})";
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

    public class PreflightCheckItem
    {
        public string Name { get; set; } = string.Empty;
        public CheckStatus Status { get; set; } = CheckStatus.Passed;
        public string Message { get; set; } = string.Empty;
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
        public string DriverVaultPath { get; set; } = "DriverVault";
        public bool IsDryRun { get; set; } = true; // Always start in Dry run per handoff spec
        public string ApiEndpoint { get; set; } = "http://13.206.106.146";

        // Automation Toggles
        public bool SkipMicrosoftAccount { get; set; } = true;
        public bool AutoLogonUser { get; set; } = true;
        public bool LaunchVeloxBench { get; set; } = true;
        public bool AutoReboot { get; set; } = true;
        public int AutoRebootCountdownSecs { get; set; } = 15;
        public string AdminUsername { get; set; } = "User";
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
        public string LogMessage { get; set; } = string.Empty;
        public bool IsError { get; set; }
        public bool IsWarning { get; set; }
    }
}
