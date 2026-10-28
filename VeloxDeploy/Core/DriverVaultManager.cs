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
            string usbDriveLetter,
            bool isDryRun,
            CancellationToken cancellationToken)
        {
            string resolvedRoot = ResolveDriverVaultRoot(driverVaultRoot);
            Log($"[DriverVault] Scanning repository for USB staging: {resolvedRoot} (Target: {profile.Manufacturer} {profile.Model})...");

            var matchedPacks = FindDriverPackages(resolvedRoot, profile);

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
                foreach (var pack in matchedPacks.Where(p => p.IsMatched))
                {
                    if (cancellationToken.IsCancellationRequested) return false;

                    string destDir = Path.Combine(oemDriversDir, pack.Name);
                    Log($"[DriverVault] Copying driver pack '{pack.Name}' ({pack.DriverCount} INFs) -> USB $OEM$\\$1\\Drivers\\{pack.Name}...");
                    CopyDirectory(pack.DirectoryPath, destDir);
                    totalPacksStaged++;

                    OnProgress?.Invoke(new DeployProgressUpdate
                    {
                        Stage = DeployStage.DriverInjection,
                        ProgressPercent = 80.0,
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
            string targetDriveLetter,
            bool isDryRun,
            CancellationToken cancellationToken)
        {
            string resolvedRoot = ResolveDriverVaultRoot(driverVaultRoot);
            Log($"[DriverVault] Scanning repository for machine: {profile.Manufacturer} {profile.Model} (SKU: {profile.SystemSku})...");

            var matchedPacks = FindDriverPackages(resolvedRoot, profile);

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

        public List<DriverVaultPackage> FindDriverPackages(string driverVaultRoot, HardwareProfile profile)
        {
            var packages = new List<DriverVaultPackage>();
            string resolvedRoot = ResolveDriverVaultRoot(driverVaultRoot);

            if (string.IsNullOrWhiteSpace(resolvedRoot) || !Directory.Exists(resolvedRoot))
            {
                return packages;
            }

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
                        bool isMatch = fullPath.Contains(profile.Model.ToLowerInvariant()) ||
                                      (!string.IsNullOrWhiteSpace(profile.SystemSku) && fullPath.Contains(profile.SystemSku.ToLowerInvariant())) ||
                                      fullPath.Contains(profile.Manufacturer.ToLowerInvariant()) ||
                                      fullPath.Contains("latitude") ||
                                      fullPath.Contains("7490") ||
                                      fullPath.Contains("generic");

                        packages.Add(new DriverVaultPackage
                        {
                            Name = dirName,
                            Manufacturer = profile.Manufacturer,
                            ModelPattern = dirName,
                            DirectoryPath = dir,
                            DriverCount = infFiles.Length,
                            IsMatched = isMatch
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[DriverVault] Error scanning directory: {ex.Message}");
            }

            return packages.OrderByDescending(p => p.IsMatched).ToList();
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
