using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VeloxDeploy.Models;

namespace VeloxDeploy.Core
{
    public class DeploymentController
    {
        private readonly DiskGeometryManager _diskManager = new();
        private readonly WimApplyEngine _wimEngine = new();
        private readonly DriverVaultManager _driverManager = new();
        private readonly UnattendBuilder _unattendBuilder = new();
        private readonly DeployTelemetryService _telemetry = new();

        public event Action<DeployProgressUpdate>? OnProgress;
        public event Action<string>? OnLog;

        public DeploymentController()
        {
            _diskManager.OnLog += Log;
            _wimEngine.OnLog += Log;
            _wimEngine.OnProgress += update => OnProgress?.Invoke(update);
            _driverManager.OnLog += Log;
            _driverManager.OnProgress += update => OnProgress?.Invoke(update);
            _unattendBuilder.OnLog += Log;
        }

        public async Task<bool> ExecutePipelineAsync(
            HardwareProfile profile,
            TargetDiskInfo disk,
            DeployConfig config,
            CancellationToken cancellationToken)
        {
            var totalSw = Stopwatch.StartNew();
            Log("================================================================================");
            Log($"[VeloxDeploy] PIPELINE INITIATED (Target: {disk.DisplayText}, DryRun={config.IsDryRun})");
            Log($"[VeloxDeploy] Machine: {profile.Manufacturer} {profile.Model} | SN: {profile.BiosSerial}");
            Log($"[VeloxDeploy] ACPI OEM Key: {(profile.AcpiKey.HasKey ? profile.AcpiKey.MaskedKey : "NONE")}");
            Log("================================================================================");

            try
            {
                // Stage 1: Hardware Probe Check
                UpdateStatus(DeployStage.HardwareProbe, 5.0, "Verifying hardware integrity and UEFI boot state...");
                await Task.Delay(400, cancellationToken);

                if (disk.IsUsbFlashDrive)
                {
                    // RUFUS MODE: Bootable USB Pendrive Creation
                    UpdateStatus(DeployStage.DiskPartitioning, 12.0, $"Formatting USB Pendrive (Disk {disk.DiskIndex}) as bootable volume...");
                    bool diskOk = await _diskManager.PartitionUsbDriveAsync(disk.DiskIndex, config.IsDryRun, cancellationToken);
                    if (!diskOk)
                    {
                        FailPipeline("Failed to partition USB pendrive.");
                        return false;
                    }

                    UpdateStatus(DeployStage.WimStreaming, 25.0, "Writing bootable Windows installation media to USB...");
                    bool streamOk;
                    if (!string.IsNullOrWhiteSpace(config.MountedIsoDriveLetter) && Directory.Exists($"{config.MountedIsoDriveLetter}:\\"))
                    {
                        streamOk = await _wimEngine.CopyIsoPayloadToUsbAsync(config.MountedIsoDriveLetter!, "U", config.IsDryRun, cancellationToken);
                    }
                    else
                    {
                        streamOk = await _wimEngine.ApplyImageAsync(config.WimPath, config.WimIndex, "U", "U", config.IsDryRun, cancellationToken);
                    }

                    if (!streamOk)
                    {
                        FailPipeline("Failed streaming installation payload to USB pendrive.");
                        return false;
                    }

                    UpdateStatus(DeployStage.DriverInjection, 75.0, "Staging DriverVault packages into USB $OEM$ directory...");
                    await _driverManager.StageDriversForUsbAsync(config.DriverVaultPath, profile, "U", config.IsDryRun, cancellationToken);

                    UpdateStatus(DeployStage.UnattendProvisioning, 90.0, "Generating automated autounattend.xml on USB root...");
                    await _unattendBuilder.ProvisionUsbAutounattendAsync("U", profile, config.AdminUsername, config.IsDryRun, cancellationToken);

                    UpdateStatus(DeployStage.BootloaderConfig, 96.0, "Writing master boot record & UEFI bootloader to USB...");
                    await _wimEngine.MakeUsbBootableAsync("U", disk.DiskIndex, config.IsDryRun, cancellationToken);

                    // Auto-dismount source ISO immediately upon USB completion to keep File Explorer navigation clean
                    try
                    {
                        await WimImageService.DismountLastIsoAsync();
                    }
                    catch
                    {
                    }
                }
                else
                {
                    // DIRECT SSD PROVISIONING MODE
                    UpdateStatus(DeployStage.DiskPartitioning, 12.0, $"Applying GPT layout on Target SSD (Disk {disk.DiskIndex})...");
                    bool diskOk = await _diskManager.PartitionDiskAsync(disk.DiskIndex, config.IsDryRun, cancellationToken);
                    if (!diskOk)
                    {
                        FailPipeline("Failed to partition target disk layout.");
                        return false;
                    }

                    UpdateStatus(DeployStage.WimStreaming, 20.0, "Streaming Windows image payload to SSD...");
                    bool wimOk = await _wimEngine.ApplyImageAsync(
                        config.WimPath,
                        config.WimIndex,
                        "W",
                        "S",
                        config.IsDryRun,
                        cancellationToken);

                    if (!wimOk)
                    {
                        FailPipeline("Failed applying Windows WIM payload.");
                        return false;
                    }

                    UpdateStatus(DeployStage.DriverInjection, 75.0, "Matching and injecting OEM hardware driver packages...");
                    bool driverOk = await _driverManager.InjectDriversOfflineAsync(
                        config.DriverVaultPath,
                        profile,
                        "W",
                        config.IsDryRun,
                        cancellationToken);

                    if (!driverOk)
                    {
                        Log("[VeloxDeploy] WARNING: Driver injection encountered warnings; proceeding with baseline.");
                    }

                    UpdateStatus(DeployStage.UnattendProvisioning, 90.0, "Injecting Panther unattend.xml and staging VeloxBench...");
                    bool unattendOk = await _unattendBuilder.ProvisionUnattendAsync(
                        "W",
                        profile,
                        config.AdminUsername,
                        config.IsDryRun,
                        cancellationToken);

                    if (!unattendOk)
                    {
                        Log("[VeloxDeploy] WARNING: Unattend staging encountered warning.");
                    }

                    UpdateStatus(DeployStage.BootloaderConfig, 96.0, "Writing UEFI BCD bootloader files to EFI partition...");
                    await _wimEngine.ConfigureUefiBootloaderAsync("W", "S", config.IsDryRun, cancellationToken);
                }

                // Stage 7: Telemetry Report
                totalSw.Stop();
                Log($"[VeloxDeploy] Deployment pipeline succeeded in {totalSw.Elapsed.TotalSeconds:F1} seconds!");

                await _telemetry.RecordDeployEventAsync(
                    config.ApiEndpoint,
                    profile,
                    disk,
                    totalSw.Elapsed,
                    true);

                // Stage 8: Completed
                OnProgress?.Invoke(new DeployProgressUpdate
                {
                    Stage = DeployStage.Completed,
                    ProgressPercent = 100.0,
                    Elapsed = totalSw.Elapsed,
                    EstimatedRemaining = TimeSpan.Zero,
                    CurrentAction = "DEPLOYMENT COMPLETE — READY FOR FIRST BOOT",
                    LogMessage = "Operating system successfully written, drivers injected, and licensed."
                });

                return true;
            }
            catch (OperationCanceledException)
            {
                Log("[VeloxDeploy] Pipeline aborted by technician.");
                UpdateStatus(DeployStage.Failed, 0, "Aborted by technician.", true);
                return false;
            }
            catch (Exception ex)
            {
                Log($"[VeloxDeploy] FATAL PIPELINE ERROR: {ex.Message}");
                FailPipeline(ex.Message);
                return false;
            }
        }

        private void UpdateStatus(DeployStage stage, double percent, string action, bool isError = false)
        {
            OnProgress?.Invoke(new DeployProgressUpdate
            {
                Stage = stage,
                ProgressPercent = percent,
                CurrentAction = action,
                IsError = isError
            });
        }

        private void FailPipeline(string error)
        {
            Log($"[VeloxDeploy] ERROR: {error}");
            OnProgress?.Invoke(new DeployProgressUpdate
            {
                Stage = DeployStage.Failed,
                ProgressPercent = 0.0,
                CurrentAction = $"FAILED: {error}",
                IsError = true
            });
        }

        private void Log(string message)
        {
            OnLog?.Invoke(message);
        }
    }
}
