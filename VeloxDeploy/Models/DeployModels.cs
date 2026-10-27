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
            ? "NOT DETECTED"
            : ProductKey[..5] + "-XXXXX-XXXXX-XXXXX-" + ProductKey[^5..];
        public string TableSignature { get; set; } = "MSDM";
        public string KeyEdition { get; set; } = "Windows Pro (OEM)";
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
            ? $"{(SizeBytes / (1024.0 * 1024.0 * 1024.0)):F1} GB" 
            : "Unknown";
        public bool IsNvme => InterfaceType.Contains("NVMe", StringComparison.OrdinalIgnoreCase) || Model.Contains("NVMe", StringComparison.OrdinalIgnoreCase);
        public bool IsUsb => InterfaceType.Contains("USB", StringComparison.OrdinalIgnoreCase);
        public string DisplayText => $"Disk {DiskIndex}: {Model} ({SizeFormatted} - {InterfaceType})";
    }

    public class HardwareProfile
    {
        public string Manufacturer { get; set; } = "Unknown";
        public string Model { get; set; } = "Generic Depot Device";
        public string SystemSku { get; set; } = "Default SKU";
        public string BiosSerial { get; set; } = "UNKNOWN-SN";
        public string BiosVersion { get; set; } = string.Empty;
        public string CpuName { get; set; } = "CPU";
        public int CpuCores { get; set; }
        public int CpuThreads { get; set; }
        public double RamGb { get; set; }
        public FirmwareBootType BootType { get; set; } = FirmwareBootType.Uefi;
        public AcpiKeyInfo AcpiKey { get; set; } = new();
        public List<TargetDiskInfo> Disks { get; set; } = new();
    }

    public class DriverVaultPackage
    {
        public string Name { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;
        public string ModelPattern { get; set; } = string.Empty;
        public string DirectoryPath { get; set; } = string.Empty;
        public int DriverCount { get; set; }
        public bool IsMatched { get; set; }
    }

    public class DeployConfig
    {
        public int TargetDiskIndex { get; set; } = 0;
        public string WimPath { get; set; } = string.Empty;
        public int WimIndex { get; set; } = 1;
        public string DriverVaultPath { get; set; } = string.Empty;
        public bool EnableAutoReboot { get; set; } = true;
        public int AutoRebootCountdownSecs { get; set; } = 15;
        public string AdminUsername { get; set; } = "User";
        public bool IsDryRun { get; set; } = true; // Default to safe simulation
        public string ApiEndpoint { get; set; } = "http://13.206.106.146";
        public string DepotLocation { get; set; } = "Depot-Station-01";
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
    }
}
