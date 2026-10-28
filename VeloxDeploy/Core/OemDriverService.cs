using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace VeloxDeploy.Core
{
    public class OemModelDefinition
    {
        public string ModelName { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;
        public string RelativeVaultPath { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long EstimatedSizeBytes { get; set; }
        public string SizeFormatted => EstimatedSizeBytes > 0 ? $"{(EstimatedSizeBytes / (1024.0 * 1024.0)):F0} MB" : "Unknown";
    }

    public class OemDriverService
    {
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(30) };

        public static readonly List<OemModelDefinition> Catalog = new()
        {
            new OemModelDefinition
            {
                ModelName = "Dell Latitude 7490",
                Manufacturer = "Dell",
                RelativeVaultPath = Path.Combine("Dell", "Latitude 7490"),
                DownloadUrl = "https://downloads.dell.com/FOLDER11116639M/1/Latitude-7490-33GP2_Win10_1.0_A16.exe",
                FileName = "Latitude-7490-33GP2_Win10_1.0_A16.exe",
                EstimatedSizeBytes = 996943232
            },
            new OemModelDefinition
            {
                ModelName = "Dell Latitude 7480",
                Manufacturer = "Dell",
                RelativeVaultPath = Path.Combine("Dell", "Latitude 7480"),
                DownloadUrl = "https://downloads.dell.com/FOLDER05788981M/1/Latitude-7480-win10-A12-DMC4V.CAB",
                FileName = "Latitude-7480-win10-A12-DMC4V.CAB",
                EstimatedSizeBytes = 878000000
            },
            new OemModelDefinition
            {
                ModelName = "Dell Latitude 5490",
                Manufacturer = "Dell",
                RelativeVaultPath = Path.Combine("Dell", "Latitude 5490"),
                DownloadUrl = "https://downloads.dell.com/FOLDER11116613M/1/Latitude-5490-33GP2_Win10_1.0_A16.exe",
                FileName = "Latitude-5490-33GP2_Win10_1.0_A16.exe",
                EstimatedSizeBytes = 980000000
            },
            new OemModelDefinition
            {
                ModelName = "Lenovo ThinkPad T480",
                Manufacturer = "Lenovo",
                RelativeVaultPath = Path.Combine("Lenovo", "ThinkPad T480"),
                DownloadUrl = "https://download.lenovo.com/pccbbs/mobiles/tp_t480_w1064_202102.exe",
                FileName = "tp_t480_w1064_202102.exe",
                EstimatedSizeBytes = 750000000
            }
        };

        public static (bool IsCached, int DriverCount, string DirectoryPath) CheckModelCache(string modelName)
        {
            string vaultRoot = DriverVaultManager.ResolveDriverVaultRoot("DriverVault");
            var item = Catalog.FirstOrDefault(c => c.ModelName.Equals(modelName, StringComparison.OrdinalIgnoreCase));
            string targetDir = item != null 
                ? Path.Combine(vaultRoot, item.RelativeVaultPath)
                : Path.Combine(vaultRoot, modelName);

            if (Directory.Exists(targetDir))
            {
                int count = Directory.GetFiles(targetDir, "*.inf", SearchOption.AllDirectories).Length;
                return (count > 0, count, targetDir);
            }

            return (false, 0, targetDir);
        }

        public static async Task<bool> DownloadAndExtractAsync(
            string modelName,
            Action<double, string> onProgress,
            Action<string> onLog,
            CancellationToken cancellationToken)
        {
            var def = Catalog.FirstOrDefault(c => c.ModelName.Equals(modelName, StringComparison.OrdinalIgnoreCase))
                      ?? Catalog.First();

            string vaultRoot = DriverVaultManager.ResolveDriverVaultRoot("DriverVault");
            string targetDir = Path.Combine(vaultRoot, def.RelativeVaultPath);
            Directory.CreateDirectory(targetDir);

            string tempDownloadPath = Path.Combine(vaultRoot, def.FileName);

            onLog($"[DriverVault] Initiating 1-Click OEM download directly from Dell CDN: {def.DownloadUrl}");
            onLog($"[DriverVault] Target directory: {targetDir}");

            try
            {
                // Step 1: Download from OEM CDN
                using (var response = await _httpClient.GetAsync(def.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();
                    long totalBytes = response.Content.Headers.ContentLength ?? def.EstimatedSizeBytes;
                    long receivedBytes = 0;

                    using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    using var fileStream = new FileStream(tempDownloadPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

                    byte[] buffer = new byte[81920];
                    var sw = Stopwatch.StartNew();
                    long lastReportBytes = 0;
                    DateTime lastReportTime = DateTime.UtcNow;

                    int bytesRead;
                    while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                        receivedBytes += bytesRead;

                        var now = DateTime.UtcNow;
                        if ((now - lastReportTime).TotalMilliseconds >= 300)
                        {
                            double progressPct = totalBytes > 0 ? (receivedBytes / (double)totalBytes) * 100.0 : 0;
                            double speedMb = ((receivedBytes - lastReportBytes) / (1024.0 * 1024.0)) / (now - lastReportTime).TotalSeconds;
                            string status = $"{receivedBytes / (1024.0 * 1024.0):F0} MB / {totalBytes / (1024.0 * 1024.0):F0} MB ({speedMb:F1} MB/s)";
                            onProgress(progressPct, status);

                            lastReportBytes = receivedBytes;
                            lastReportTime = now;
                        }
                    }
                }

                onLog($"[DriverVault] Download complete. Extracting OEM driver payload to: {targetDir}...");
                onProgress(95.0, "Extracting drivers...");

                // Step 2: Extract driver payload
                bool extractOk = await ExtractDriverPackageAsync(tempDownloadPath, targetDir, onLog, cancellationToken);
                if (!extractOk)
                {
                    onLog("[DriverVault] Primary extraction fallback: attempting tar/extrac32...");
                    await FallbackExtractionAsync(tempDownloadPath, targetDir, onLog, cancellationToken);
                }

                // Clean up installer executable after extraction
                try
                {
                    if (File.Exists(tempDownloadPath))
                    {
                        File.Delete(tempDownloadPath);
                    }
                }
                catch { }

                int infCount = Directory.GetFiles(targetDir, "*.inf", SearchOption.AllDirectories).Length;
                onLog($"[DriverVault] SUCCESS: Extracted {infCount} driver packages into: {targetDir}");
                onProgress(100.0, $"Ready ({infCount} drivers)");
                return infCount > 0;
            }
            catch (OperationCanceledException)
            {
                onLog("[DriverVault] Driver download aborted by technician.");
                return false;
            }
            catch (Exception ex)
            {
                onLog($"[DriverVault] ERROR downloading driver pack: {ex.Message}");
                return false;
            }
        }

        public static async Task<bool> CaptureLocalMachineDriversAsync(
            string manufacturer,
            string model,
            Action<double, string> onProgress,
            Action<string> onLog,
            CancellationToken cancellationToken)
        {
            string vaultRoot = DriverVaultManager.ResolveDriverVaultRoot("DriverVault");
            string cleanModel = string.IsNullOrWhiteSpace(model) ? "LocalHost" : model;
            string cleanMfr = string.IsNullOrWhiteSpace(manufacturer) ? "Generic" : manufacturer;
            string targetDir = Path.Combine(vaultRoot, cleanMfr, cleanModel);
            Directory.CreateDirectory(targetDir);

            onLog($"[DriverVault] Capturing online drivers from current machine: {cleanMfr} {cleanModel}...");
            onLog($"[DriverVault] Target directory: {targetDir}");
            onProgress(10.0, "Exporting drivers from Windows driver store...");

            try
            {
                var psi = new ProcessStartInfo("dism.exe", $"/online /export-driver /destination:\"{targetDir}\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    onLog("[DriverVault] Failed to start DISM driver export process.");
                    return false;
                }

                int driverCount = 0;
                string? line;
                while ((line = await proc.StandardOutput.ReadLineAsync(cancellationToken)) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    if (line.Contains("Exporting", StringComparison.OrdinalIgnoreCase))
                    {
                        driverCount++;
                        onLog($"[DriverVault] {line.Trim()}");
                        onProgress(Math.Min(90.0, 10.0 + driverCount * 2.0), $"Exported {driverCount} drivers...");
                    }
                }

                await proc.WaitForExitAsync(cancellationToken);

                int finalInfCount = Directory.GetFiles(targetDir, "*.inf", SearchOption.AllDirectories).Length;
                onLog($"[DriverVault] Capture complete: Successfully stored {finalInfCount} driver packages into: {targetDir}");
                onProgress(100.0, $"Captured ({finalInfCount} drivers)");
                return finalInfCount > 0;
            }
            catch (Exception ex)
            {
                onLog($"[DriverVault] Error capturing local drivers: {ex.Message}");
                return false;
            }
        }

        private static async Task<bool> ExtractDriverPackageAsync(string packagePath, string destDir, Action<string> onLog, CancellationToken ct)
        {
            string ext = Path.GetExtension(packagePath).ToLowerInvariant();
            if (ext == ".cab")
            {
                var psi = new ProcessStartInfo("extrac32.exe", $"/Y /E /L \"{destDir}\" \"{packagePath}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    await proc.WaitForExitAsync(ct);
                    return true;
                }
            }
            else if (ext == ".exe")
            {
                // Dell DUP silent extraction switch: /s /e="destination"
                var psi = new ProcessStartInfo(packagePath, $"/s /e=\"{destDir}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                try
                {
                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        await proc.WaitForExitAsync(ct);
                        int count = Directory.GetFiles(destDir, "*.inf", SearchOption.AllDirectories).Length;
                        if (count > 0) return true;
                    }
                }
                catch (Exception ex)
                {
                    onLog($"[DriverVault] Executable self-extraction notice: {ex.Message}");
                }
            }

            return false;
        }

        private static async Task FallbackExtractionAsync(string packagePath, string destDir, Action<string> onLog, CancellationToken ct)
        {
            try
            {
                // Fallback using Windows built-in tar.exe to uncompress archive structures
                var psi = new ProcessStartInfo("tar.exe", $"-xf \"{packagePath}\" -C \"{destDir}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    await proc.WaitForExitAsync(ct);
                }
            }
            catch (Exception ex)
            {
                onLog($"[DriverVault] Tar fallback notice: {ex.Message}");
            }
        }
    }
}
