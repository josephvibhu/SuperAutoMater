using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.Json;
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

        public static int GetHostSystemDiskIndex()
        {
            try
            {
                string sysDrive = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\', ':') ?? "C";
                var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -Command \"(Get-Partition -DriveLetter {sysDrive}).DiskNumber\"")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string output = proc.StandardOutput.ReadToEnd().Trim();
                    proc.WaitForExit(1500);
                    if (int.TryParse(output, out int num)) return num;
                }
            }
            catch { }
            return 0; // Default host disk index fallback
        }

        public static List<TargetDiskInfo> QueryStorageDisks()
        {
            var disks = new List<TargetDiskInfo>();
            int hostDiskIndex = GetHostSystemDiskIndex();

            // 1. Try modern PowerShell Get-Disk query
            try
            {
                var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command \"Get-Disk | Select-Object Number, FriendlyName, SerialNumber, BusType, Size, IsBoot, IsSystem, OperationalStatus | ConvertTo-Json -Compress\"")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string json = proc.StandardOutput.ReadToEnd().Trim();
                    proc.WaitForExit(2500);

                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        using var doc = JsonDocument.Parse(json);
                        var elements = doc.RootElement.ValueKind == JsonValueKind.Array
                            ? doc.RootElement.EnumerateArray()
                            : new[] { doc.RootElement }.AsEnumerable();

                        foreach (var el in elements)
                        {
                            int diskNumber = el.TryGetProperty("Number", out var numProp) ? numProp.GetInt32() : 0;
                            string friendlyName = el.TryGetProperty("FriendlyName", out var nameProp) ? (CleanString(nameProp.GetString()) ?? "Generic Disk") : "Generic Disk";
                            string serial = el.TryGetProperty("SerialNumber", out var snProp) ? (CleanString(snProp.GetString()) ?? "SN-UNKNOWN") : "SN-UNKNOWN";
                            string busType = el.TryGetProperty("BusType", out var busProp) ? (busProp.GetString() ?? "NVMe") : "NVMe";
                            long size = el.TryGetProperty("Size", out var sizeProp) ? sizeProp.GetInt64() : 0L;
                            bool isBoot = el.TryGetProperty("IsBoot", out var bootProp) && bootProp.GetBoolean();
                            bool isSys = el.TryGetProperty("IsSystem", out var sysProp) && sysProp.GetBoolean();

                            if (diskNumber == hostDiskIndex)
                            {
                                isBoot = true;
                                isSys = true;
                            }

                            var disk = new TargetDiskInfo
                            {
                                DiskIndex = diskNumber,
                                Model = friendlyName,
                                SerialNumber = serial,
                                InterfaceType = busType,
                                SizeBytes = size,
                                IsBoot = isBoot,
                                IsSystem = isSys,
                                HasExistingData = size > 0
                            };

                            disks.Add(disk);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HardwareInspector] PowerShell Get-Disk query failed: {ex.Message}");
            }

            // 2. WMI Fallback if PowerShell returned nothing
            if (disks.Count == 0)
            {
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT Index, Model, SerialNumber, InterfaceType, Size, MediaType FROM Win32_DiskDrive");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        int index = Convert.ToInt32(obj["Index"] ?? 0);
                        var disk = new TargetDiskInfo
                        {
                            DiskIndex = index,
                            Model = CleanString(obj["Model"]?.ToString()) ?? "Generic Storage Disk",
                            SerialNumber = CleanString(obj["SerialNumber"]?.ToString()) ?? "SN-UNKNOWN",
                            InterfaceType = CleanString(obj["InterfaceType"]?.ToString()) ?? "SCSI",
                            SizeBytes = Convert.ToInt64(obj["Size"] ?? 0),
                            IsBoot = index == hostDiskIndex,
                            IsSystem = index == hostDiskIndex
                        };

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
                    Debug.WriteLine($"[HardwareInspector] DiskDrive WMI query failed: {ex.Message}");
                }
            }

            // 3. If only host disk exists, add simulation targets for Dry Run testing
            bool hasRealNonHostTargets = disks.Any(d => !d.IsHostSystemDisk);
            if (!hasRealNonHostTargets)
            {
                disks.Add(new TargetDiskInfo
                {
                    DiskIndex = 1,
                    Model = "Virtual 256 GB NVMe SSD (Simulated Target)",
                    SerialNumber = "DEPOT-SIM-NVME",
                    InterfaceType = "NVMe",
                    SizeBytes = 256L * 1024 * 1024 * 1024,
                    IsSimulated = true,
                    HasExistingData = false
                });

                disks.Add(new TargetDiskInfo
                {
                    DiskIndex = 2,
                    Model = "Virtual 32 GB USB 3.2 Flash Drive (Simulated Target)",
                    SerialNumber = "DEPOT-SIM-USB",
                    InterfaceType = "USB",
                    SizeBytes = 32L * 1024 * 1024 * 1024,
                    IsSimulated = true,
                    HasExistingData = false
                });
            }

            // Sort so real external targets appear first, then simulated, then protected host disk
            return disks
                .OrderBy(d => d.IsHostSystemDisk ? 2 : (d.IsSimulated ? 1 : 0))
                .ThenBy(d => d.DiskIndex)
                .ToList();
        }

        private static string? CleanString(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            return input.Trim().Replace("  ", " ");
        }
    }
}
