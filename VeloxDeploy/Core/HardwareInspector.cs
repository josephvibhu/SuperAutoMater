using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using VeloxDeploy.Models;

namespace VeloxDeploy.Core
{
    public static class HardwareInspector
    {
        [DllImport("kernel32.dll")]
        private static extern bool GetFirmwareType(out FirmwareType firmwareType);

        private enum FirmwareType
        {
            Unknown = 0,
            Bios = 1,
            Uefi = 2,
            Max = 3
        }

        public static HardwareProfile ProbeSystem()
        {
            var profile = new HardwareProfile();

            // 1. Motherboard & Computer System
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Model, SystemSKUNumber, TotalPhysicalMemory FROM Win32_ComputerSystem");
                foreach (ManagementObject obj in searcher.Get())
                {
                    profile.Manufacturer = CleanString(obj["Manufacturer"]?.ToString()) ?? "OEM";
                    profile.Model = CleanString(obj["Model"]?.ToString()) ?? "Depot Laptop";
                    profile.SystemSku = CleanString(obj["SystemSKUNumber"]?.ToString()) ?? profile.Model;

                    if (obj["TotalPhysicalMemory"] != null && double.TryParse(obj["TotalPhysicalMemory"]?.ToString(), out double ramBytes))
                    {
                        profile.RamGb = Math.Round(ramBytes / (1024.0 * 1024.0 * 1024.0), 1);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HardwareInspector] ComputerSystem query failed: {ex.Message}");
            }

            // 2. BIOS & Serial Number
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT SerialNumber, SMBIOSBIOSVersion FROM Win32_BIOS");
                foreach (ManagementObject obj in searcher.Get())
                {
                    profile.BiosSerial = CleanString(obj["SerialNumber"]?.ToString()) ?? "VLX-SERIAL";
                    profile.BiosVersion = CleanString(obj["SMBIOSBIOSVersion"]?.ToString()) ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HardwareInspector] BIOS query failed: {ex.Message}");
            }

            // 3. Processor Details
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
                foreach (ManagementObject obj in searcher.Get())
                {
                    profile.CpuName = CleanString(obj["Name"]?.ToString()) ?? "x86-64 Processor";
                    if (int.TryParse(obj["NumberOfCores"]?.ToString(), out int cores))
                        profile.CpuCores = cores;
                    if (int.TryParse(obj["NumberOfLogicalProcessors"]?.ToString(), out int threads))
                        profile.CpuThreads = threads;
                    break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HardwareInspector] Processor query failed: {ex.Message}");
            }

            // 4. Boot Mode (UEFI vs BIOS)
            try
            {
                if (GetFirmwareType(out FirmwareType fwType))
                {
                    profile.BootType = fwType switch
                    {
                        FirmwareType.Uefi => FirmwareBootType.Uefi,
                        FirmwareType.Bios => FirmwareBootType.Bios,
                        _ => FirmwareBootType.Unknown
                    };
                }
            }
            catch
            {
                profile.BootType = FirmwareBootType.Uefi;
            }

            // 5. ACPI OEM Key Extraction
            profile.AcpiKey = AcpiKeyReader.ReadEmbeddedKey();

            // 6. Connected Storage Disks
            profile.Disks = QueryStorageDisks();

            return profile;
        }

        public static List<TargetDiskInfo> QueryStorageDisks()
        {
            var disks = new List<TargetDiskInfo>();

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Index, Model, SerialNumber, InterfaceType, Size, MediaType FROM Win32_DiskDrive");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var disk = new TargetDiskInfo
                    {
                        DiskIndex = Convert.ToInt32(obj["Index"] ?? 0),
                        Model = CleanString(obj["Model"]?.ToString()) ?? "Generic Storage Disk",
                        SerialNumber = CleanString(obj["SerialNumber"]?.ToString()) ?? "SN-UNKNOWN",
                        InterfaceType = CleanString(obj["InterfaceType"]?.ToString()) ?? "SCSI",
                        SizeBytes = Convert.ToInt64(obj["Size"] ?? 0)
                    };

                    // Check for NVMe hint in model or interface
                    if (disk.Model.Contains("NVMe", StringComparison.OrdinalIgnoreCase) ||
                        disk.Model.Contains("SAMSUNG MZ", StringComparison.OrdinalIgnoreCase) ||
                        disk.Model.Contains("SKhynix", StringComparison.OrdinalIgnoreCase) ||
                        disk.Model.Contains("KIOXIA", StringComparison.OrdinalIgnoreCase) ||
                        disk.Model.Contains("Micron", StringComparison.OrdinalIgnoreCase) ||
                        disk.Model.Contains("WD Black", StringComparison.OrdinalIgnoreCase))
                    {
                        disk.InterfaceType = "NVMe";
                    }

                    disks.Add(disk);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HardwareInspector] DiskDrive query failed: {ex.Message}");
            }

            // If no disks found (e.g. specialized WinPE environment), try mock/fallback
            if (disks.Count == 0)
            {
                disks.Add(new TargetDiskInfo
                {
                    DiskIndex = 0,
                    Model = "Primary Internal NVMe SSD",
                    SerialNumber = "NVME-DEPOT-001",
                    InterfaceType = "NVMe",
                    SizeBytes = 256L * 1024 * 1024 * 1024
                });
            }

            return disks;
        }

        private static string? CleanString(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            return input.Trim().Replace("  ", " ");
        }
    }
}
