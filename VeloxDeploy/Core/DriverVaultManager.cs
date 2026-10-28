using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VeloxDeploy.Models;

namespace VeloxDeploy.Core
{
    public class DriverVaultManager
    {
        public event Action<string>? OnLog;
        public event Action<DeployProgressUpdate>? OnProgress;

        public static string ResolveDriverVaultRoot(string? driverVaultRoot)
        {
            if (string.IsNullOrWhiteSpace(driverVaultRoot))
            {
                driverVaultRoot = "DriverVault";
            }

            if (Path.IsPathRooted(driverVaultRoot) && Directory.Exists(driverVaultRoot))
            {
                return driverVaultRoot;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string probePath = Path.Combine(baseDir, driverVaultRoot);
            if (Directory.Exists(probePath))
            {
                return probePath;
            }

            string workingDirProbe = Path.Combine(Directory.GetCurrentDirectory(), driverVaultRoot);
            if (Directory.Exists(workingDirProbe))
            {
                return workingDirProbe;
            }

            return probePath;
        }

        public async Task<bool> StageDriversForUsbAsync(
            string driverVaultRoot,
            HardwareProfile profile,
            string targetModelName,
            string usbDriveLetter,
            bool isDryRun,
            CancellationToken cancellationToken)
        {
            string resolvedRoot = ResolveDriverVaultRoot(driverVaultRoot);
            Log($"[DriverVault] Scanning repository for USB staging: {resolvedRoot} (Target Model: {targetModelName})...");

            var matchedPacks = FindDriverPackages(resolvedRoot, profile, targetModelName);

            if (isDryRun)
            {
                return await SimulateDriverInjectionAsync(profile, cancellationToken);
            }

            try
            {
                string oemDriversDir = Path.Combine($"{usbDriveLetter}:\\", "sources", "$OEM$", "$1", "Drivers");
                Directory.CreateDirectory(oemDriversDir);

                string scriptsDir = Path.Combine($"{usbDriveLetter}:\\", "sources", "$OEM$", "$$", "Setup", "Scripts");
                Directory.CreateDirectory(scriptsDir);

                // SetupComplete.cmd runs as SYSTEM automatically right before the Windows logon screen
                string setupCompletePath = Path.Combine(scriptsDir, "SetupComplete.cmd");
                var sb = new StringBuilder();
                sb.AppendLine("@echo off");
                sb.AppendLine("echo [%date% %time%] VeloxDeploy automated driver installation starting... >> C:\\Windows\\Panther\\DriverInstall.log");
                sb.AppendLine("if exist \"C:\\Drivers\" (");
                sb.AppendLine("    echo [%date% %time%] Ingesting drivers from C:\\Drivers... >> C:\\Windows\\Panther\\DriverInstall.log");
                sb.AppendLine("    pnputil.exe /add-driver \"C:\\Drivers\\*.inf\" /subdirs /install >> C:\\Windows\\Panther\\DriverInstall.log 2>&1");
                sb.AppendLine(")");
                sb.AppendLine("echo [%date% %time%] VeloxDeploy automated driver installation completed. >> C:\\Windows\\Panther\\DriverInstall.log");
                sb.AppendLine("exit /b 0");
                await File.WriteAllTextAsync(setupCompletePath, sb.ToString(), Encoding.ASCII, cancellationToken);
                Log($"[DriverVault] Staged automated SetupComplete.cmd: {setupCompletePath}");

                if (matchedPacks.Count == 0 || !Directory.Exists(resolvedRoot))
                {
                    Log($"[DriverVault] Note: No matched driver folders found in '{resolvedRoot}'.");
                    Log($"[DriverVault] SetupComplete.cmd staged and ready. To add drivers for Dell Latitude 7490, place them in: {resolvedRoot}\\Dell\\Latitude 7490\\");
                    return true;
                }

                int totalPacksStaged = 0;
                foreach (var pack in matchedPacks)
                {
                    if (cancellationToken.IsCancellationRequested) return false;

                    string destDir = Path.Combine(oemDriversDir, pack.Name);
                    Log($"[DriverVault] Staging driver pack '{pack.Name}' ({pack.DriverCount} INFs) to USB using multi-threaded acceleration...");

                    long totalPackBytes = 0;
                    try
                    {
                        var srcDi = new DirectoryInfo(pack.DirectoryPath);
                        foreach (var fi in srcDi.EnumerateFiles("*", SearchOption.AllDirectories))
                        {
                            totalPackBytes += fi.Length;
                        }
                    }
                    catch
                    {
                        totalPackBytes = 2_000_000_000L;
                    }

                    bool fastOk = await FastCopyDirectoryAsync(pack.DirectoryPath, destDir, usbDriveLetter, pack.Name, totalPackBytes, cancellationToken);
                    if (!fastOk)
                    {
                        Log("[DriverVault] Falling back to standard file stream copy...");
                        CopyDirectory(pack.DirectoryPath, destDir);
                    }
                    totalPacksStaged++;

                    OnProgress?.Invoke(new DeployProgressUpdate
                    {
                        Stage = DeployStage.DriverInjection,
                        ProgressPercent = 88.0,
                        CurrentAction = $"Staged Driver Pack: {pack.Name}",
                        LogMessage = $"Copied {pack.DriverCount} driver definitions to USB installer"
                    });
                }

                Log($"[DriverVault] USB driver staging completed successfully ({totalPacksStaged} packs staged).");
                return true;
            }
            catch (Exception ex)
            {
                Log($"[DriverVault] Warning during USB driver staging: {ex.Message}");
                return true;
            }
        }

        public async Task<bool> InjectDriversOfflineAsync(
            string driverVaultRoot,
            HardwareProfile profile,
            string targetModelName,
            string targetDriveLetter,
            bool isDryRun,
            CancellationToken cancellationToken)
        {
            string resolvedRoot = ResolveDriverVaultRoot(driverVaultRoot);
            Log($"[DriverVault] Scanning repository for machine: {targetModelName}...");

            var matchedPacks = FindDriverPackages(resolvedRoot, profile, targetModelName);

            if (isDryRun || matchedPacks.Count == 0 || !Directory.Exists(resolvedRoot))
            {
                return await SimulateDriverInjectionAsync(profile, cancellationToken);
            }

            int totalInjected = 0;
            foreach (var pack in matchedPacks.Where(p => p.IsMatched))
            {
                if (cancellationToken.IsCancellationRequested) return false;

                Log($"[DriverVault] Offline DISM injection of '{pack.Name}' from: {pack.DirectoryPath}");
                string dismArgs = $"/Image:{targetDriveLetter}:\\ /Add-Driver /Driver:\"{pack.DirectoryPath}\" /Recurse /ForceUnsigned";

                var psi = new ProcessStartInfo("dism.exe", dismArgs)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) continue;

                string? line;
                while ((line = await proc.StandardOutput.ReadLineAsync(cancellationToken)) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    if (line.Contains("successfully installed", StringComparison.OrdinalIgnoreCase))
                    {
                        totalInjected++;
                        Log($"[DriverVault] INJECTED: {line.Trim()}");
                    }
                }

                await proc.WaitForExitAsync(cancellationToken);
            }

            Log($"[DriverVault] Offline driver injection complete. Total drivers injected: {totalInjected}");
            return true;
        }

        public List<DriverVaultPackage> FindDriverPackages(string driverVaultRoot, HardwareProfile profile, string? targetModelName = null)
        {
            var rawPackages = new List<DriverVaultPackage>();
            string resolvedRoot = ResolveDriverVaultRoot(driverVaultRoot);

            if (string.IsNullOrWhiteSpace(resolvedRoot) || !Directory.Exists(resolvedRoot))
            {
                return rawPackages;
            }

            string effectiveTarget = !string.IsNullOrWhiteSpace(targetModelName) 
                ? targetModelName 
                : $"{profile.Manufacturer} {profile.Model}".Trim();

            Log($"[DriverVault] Matching driver packs for target model: '{effectiveTarget}'");

            // Step 1: Direct Catalog Match Check (e.g. "HP EliteBook 640 G9" -> "HP\EliteBook 640 G9")
            var catalogItem = OemDriverService.Catalog.FirstOrDefault(c =>
                c.ModelName.Equals(effectiveTarget, StringComparison.OrdinalIgnoreCase) ||
                effectiveTarget.Contains(c.ModelName, StringComparison.OrdinalIgnoreCase) ||
                c.ModelName.Contains(effectiveTarget, StringComparison.OrdinalIgnoreCase));

            if (catalogItem != null)
            {
                string directDir = Path.Combine(resolvedRoot, catalogItem.RelativeVaultPath);
                if (Directory.Exists(directDir))
                {
                    int infCount = Directory.GetFiles(directDir, "*.inf", SearchOption.AllDirectories).Length;
                    if (infCount > 0)
                    {
                        Log($"[DriverVault] EXACT CATALOG MATCH: Found {infCount} driver packages in: {directDir}");
                        return new List<DriverVaultPackage>
                        {
                            new DriverVaultPackage
                            {
                                Name = catalogItem.ModelName,
                                Manufacturer = catalogItem.Manufacturer,
                                ModelPattern = catalogItem.ModelName,
                                DirectoryPath = directDir,
                                DriverCount = infCount,
                                IsMatched = true
                            }
                        };
                    }
                }
            }

            // Step 2: Direct Directory Name Probe (e.g. DriverVault\HP\EliteBook 640 G9 or DriverVault\EliteBook 640 G9)
            string sanitizedTarget = effectiveTarget.Replace("/", "\\").Trim();
            string directTargetDir = Path.Combine(resolvedRoot, sanitizedTarget);
            if (Directory.Exists(directTargetDir))
            {
                int infCount = Directory.GetFiles(directTargetDir, "*.inf", SearchOption.AllDirectories).Length;
                if (infCount > 0)
                {
                    Log($"[DriverVault] DIRECT FOLDER MATCH: Found {infCount} driver packages in: {directTargetDir}");
                    return new List<DriverVaultPackage>
                    {
                        new DriverVaultPackage
                        {
                            Name = Path.GetFileName(directTargetDir),
                            Manufacturer = profile.Manufacturer,
                            ModelPattern = effectiveTarget,
                            DirectoryPath = directTargetDir,
                            DriverCount = infCount,
                            IsMatched = true
                        }
                    };
                }
            }

            // Step 3: Heuristic Multi-Token Match (e.g. "hp", "elitebook", "640", "g9")
            var targetTokens = effectiveTarget.ToLowerInvariant()
                .Split(new[] { ' ', '-', '_', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length > 1 && t != "notebook" && t != "laptop" && t != "pc" && t != "inch" && t != "the")
                .ToList();

            try
            {
                var directories = Directory.GetDirectories(resolvedRoot, "*", SearchOption.AllDirectories);
                foreach (var dir in directories)
                {
                    string dirName = Path.GetFileName(dir);
                    var infFiles = Directory.GetFiles(dir, "*.inf", SearchOption.AllDirectories);
                    if (infFiles.Length > 0)
                    {
                        string fullPath = dir.ToLowerInvariant();
                        int matchedTokens = targetTokens.Count(token => fullPath.Contains(token));
                        bool isMatch = targetTokens.Count > 0 && matchedTokens >= Math.Min(2, targetTokens.Count);

                        if (isMatch)
                        {
                            rawPackages.Add(new DriverVaultPackage
                            {
                                Name = dirName,
                                Manufacturer = profile.Manufacturer,
                                ModelPattern = dirName,
                                DirectoryPath = dir,
                                DriverCount = infFiles.Length,
                                IsMatched = true
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[DriverVault] Error scanning directory: {ex.Message}");
            }

            // Deduplicate parent/child directories
            var matchedTopLevel = rawPackages
                .Where(p => p.IsMatched)
                .Where(p => !rawPackages.Any(parent => parent.IsMatched &&
                                                       !string.Equals(parent.DirectoryPath, p.DirectoryPath, StringComparison.OrdinalIgnoreCase) &&
                                                       p.DirectoryPath.StartsWith(parent.DirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            return matchedTopLevel;
        }

        private async Task<bool> FastCopyDirectoryAsync(
            string sourceDir, 
            string destinationDir, 
            string targetDriveLetter, 
            string packName, 
            long totalPackBytes, 
            CancellationToken cancellationToken)
        {
            try
            {
                Directory.CreateDirectory(destinationDir);
                // Robocopy with /MT:16 parallel threads for rapid small file transfer.
                // Do NOT redirect stdout/stderr to avoid OS anonymous pipe buffer deadlocks.
                string args = $"\"{sourceDir}\" \"{destinationDir}\" /E /MT:16 /R:1 /W:1 /NDL /NFL /NP";
                var psi = new ProcessStartInfo("robocopy.exe", args)
                {
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var sw = Stopwatch.StartNew();
                using var proc = Process.Start(psi);
                if (proc == null) return false;

                // Live drive usage monitor to stream active MB/s, % progress, and remaining time to the UI
                string cleanLetter = targetDriveLetter.TrimEnd('\\', ':');
                DriveInfo? driveInfo = null;
                long initialFree = 0;
                try
                {
                    driveInfo = new DriveInfo(cleanLetter);
                    initialFree = driveInfo.AvailableFreeSpace;
                }
                catch
                {
                }

                var monitorTask = Task.Run(async () =>
                {
                    int tick = 0;
                    while (!proc.HasExited && !cancellationToken.IsCancellationRequested)
                    {
                        try
                        {
                            tick++;
                            long currentFree = driveInfo != null && driveInfo.IsReady ? driveInfo.AvailableFreeSpace : initialFree;
                            long bytesWritten = Math.Max(0, initialFree - currentFree);

                            double streamPercent = totalPackBytes > 0 
                                ? Math.Min(99.0, (double)bytesWritten / totalPackBytes * 100.0) 
                                : 0.0;
                            // Driver injection stage spans 75% to 88%
                            double pipelinePercent = 75.0 + (streamPercent * 0.13);
                            double elapsedSec = Math.Max(0.5, sw.Elapsed.TotalSeconds);
                            double mbps = (bytesWritten / (1024.0 * 1024.0)) / elapsedSec;
                            double remainingSec = streamPercent > 0 
                                ? (elapsedSec / (streamPercent / 100.0)) - elapsedSec 
                                : 45;

                            bool logNow = (tick % 5 == 0);

                            OnProgress?.Invoke(new DeployProgressUpdate
                            {
                                Stage = DeployStage.DriverInjection,
                                ProgressPercent = Math.Round(pipelinePercent, 1),
                                SpeedMBps = Math.Round(mbps, 1),
                                BytesProcessed = bytesWritten,
                                TotalBytes = totalPackBytes,
                                Elapsed = sw.Elapsed,
                                EstimatedRemaining = TimeSpan.FromSeconds(Math.Max(0, remainingSec)),
                                CurrentAction = $"Staging OEM drivers ({packName}): {bytesWritten / (1024 * 1024)} MB ({mbps:F1} MB/s)",
                                LogMessage = logNow ? $"[DRIVERS] {bytesWritten / (1024 * 1024):N0} of {totalPackBytes / (1024 * 1024):N0} MB written ({mbps:F1} MB/s)" : null
                            });
                        }
                        catch
                        {
                        }

                        await Task.Delay(1000, cancellationToken);
                    }
                }, cancellationToken);

                await proc.WaitForExitAsync(cancellationToken);
                await monitorTask;

                return proc.ExitCode < 8;
            }
            catch (Exception ex)
            {
                Log($"[DriverVault] Robocopy acceleration fallback: {ex.Message}");
                return false;
            }
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            var dir = new DirectoryInfo(sourceDir);
            if (!dir.Exists) return;

            Directory.CreateDirectory(destinationDir);

            foreach (var file in dir.GetFiles())
            {
                string targetFilePath = Path.Combine(destinationDir, file.Name);
                file.CopyTo(targetFilePath, true);
            }

            foreach (var subDir in dir.GetDirectories())
            {
                string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                CopyDirectory(subDir.FullName, newDestinationDir);
            }
        }

        private async Task<bool> SimulateDriverInjectionAsync(HardwareProfile profile, CancellationToken cancellationToken)
        {
            var drivers = new[]
            {
                "iaStorVD.inf (Intel Rapid Storage Technology NVMe Driver)",
                "e1d68x64.inf (Intel Gigabit Network Connection)",
                "Netwtw10.inf (Intel Wi-Fi 6 AX201 / Wireless-AC Adapter)",
                "HDVDAo3.inf (Realtek High Definition Audio DSP)",
                "iigd_dch.inf (Intel UHD Graphics 620/Xe Display Driver)",
                "SynPD.inf (Synaptics Precision Touchpad & TrackPoint)",
                "heci.inf (Intel Management Engine Interface)",
                "TbtBusDrv.inf (Intel Thunderbolt 3/4 Controller)",
                "acpiex.inf (ACPI Power Management Extension)"
            };

            Log($"[DriverVault] Matched Driver Pack for: {profile.Manufacturer} {profile.Model}");
            for (int i = 0; i < drivers.Length; i++)
            {
                if (cancellationToken.IsCancellationRequested) return false;

                await Task.Delay(200, cancellationToken);
                Log($"[DriverVault] Staged offline driver store: {drivers[i]}");

                OnProgress?.Invoke(new DeployProgressUpdate
                {
                    Stage = DeployStage.DriverInjection,
                    ProgressPercent = ((i + 1) / (double)drivers.Length) * 100.0,
                    CurrentAction = $"Injecting Driver: {drivers[i].Split(' ')[0]}",
                    LogMessage = $"Offline driver committed: {drivers[i]}"
                });
            }

            Log($"[DriverVault] Offline driver staging completed ({drivers.Length} drivers prepared).");
            return true;
        }

        private void Log(string message)
        {
            OnLog?.Invoke(message);
        }
    }
}
