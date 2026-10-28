using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VeloxDeploy.Models;

namespace VeloxDeploy.Core
{
    public class DiskGeometryManager
    {
        public event Action<string>? OnLog;

        public async Task<bool> PartitionDiskAsync(int diskIndex, bool isDryRun, CancellationToken cancellationToken)
        {
            Log($"[DiskGeometry] Initializing GPT partition layout for Target Disk {diskIndex} (DryRun={isDryRun})...");

            if (isDryRun)
            {
                Log("[DiskGeometry] [SIMULATION] Selecting Disk " + diskIndex);
                await Task.Delay(400, cancellationToken);
                Log("[DiskGeometry] [SIMULATION] Executing CLEAN (Zeroing GPT/MBR partition tables)...");
                await Task.Delay(500, cancellationToken);
                Log("[DiskGeometry] [SIMULATION] Converting to GPT (GUID Partition Table)...");
                await Task.Delay(300, cancellationToken);
                Log("[DiskGeometry] [SIMULATION] Creating EFI System Partition (300 MB, FAT32) -> Mounted as S:");
                await Task.Delay(350, cancellationToken);
                Log("[DiskGeometry] [SIMULATION] Creating Microsoft Reserved Partition (16 MB, MSR)...");
                await Task.Delay(250, cancellationToken);
                Log("[DiskGeometry] [SIMULATION] Creating Windows OS Partition (NTFS, 4K Cluster Aligned) -> Mounted as W:");
                await Task.Delay(400, cancellationToken);
                Log("[DiskGeometry] [SIMULATION] Creating Windows Recovery Partition (900 MB, WinRE Type ID)...");
                await Task.Delay(300, cancellationToken);
                Log("[DiskGeometry] [SIMULATION] Storage geometry prepared successfully.");
                return true;
            }

            // Live Diskpart Execution
            string scriptPath = Path.Combine(Path.GetTempPath(), $"velox_diskpart_{diskIndex}_{DateTime.Now.Ticks}.txt");
            var script = new StringBuilder();
            script.AppendLine($"select disk {diskIndex}");
            script.AppendLine("clean");
            script.AppendLine("convert gpt");
            script.AppendLine("create partition efi size=300");
            script.AppendLine("format quick fs=fat32 label=\"System\"");
            script.AppendLine("assign letter=S");
            script.AppendLine("create partition msr size=16");
            script.AppendLine("create partition primary");
            script.AppendLine("shrink minimum=900");
            script.AppendLine("format quick fs=ntfs label=\"Windows\"");
            script.AppendLine("assign letter=W");
            script.AppendLine("create partition primary");
            script.AppendLine("format quick fs=ntfs label=\"Recovery\"");
            script.AppendLine("set id=\"de94bba4-06d1-4d40-a16a-bfd50179d6ac\"");
            script.AppendLine("gpt attributes=0x8000000000000001");
            script.AppendLine("exit");

            await File.WriteAllTextAsync(scriptPath, script.ToString(), cancellationToken);

            try
            {
                var psi = new ProcessStartInfo("diskpart.exe", $"/s \"{scriptPath}\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    Log("[DiskGeometry] ERROR: Failed to launch diskpart.exe");
                    return false;
                }

                string? line;
                while ((line = await proc.StandardOutput.ReadLineAsync(cancellationToken)) != null)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        Log($"[Diskpart] {line.Trim()}");
                    }
                }

                await proc.WaitForExitAsync(cancellationToken);
                if (proc.ExitCode != 0)
                {
                    string err = await proc.StandardError.ReadToEndAsync(cancellationToken);
                    Log($"[DiskGeometry] ERROR: Diskpart exited with code {proc.ExitCode}: {err}");
                    return false;
                }

                Log("[DiskGeometry] Disk partitioning complete. Target OS mounted at W:\\, EFI at S:\\");
                return true;
            }
            finally
            {
                if (File.Exists(scriptPath))
                {
                    try { File.Delete(scriptPath); } catch { }
                }
            }
        }

        public async Task<bool> PartitionUsbDriveAsync(int diskIndex, bool isDryRun, CancellationToken cancellationToken)
        {
            Log($"[DiskGeometry] Initializing USB Pendrive layout for Disk {diskIndex} (DryRun={isDryRun})...");

            if (isDryRun)
            {
                Log("[DiskGeometry] [SIMULATION] Selecting USB Disk " + diskIndex);
                await Task.Delay(300, cancellationToken);
                Log("[DiskGeometry] [SIMULATION] Executing CLEAN (Zeroing partition table)...");
                await Task.Delay(400, cancellationToken);
                Log("[DiskGeometry] [SIMULATION] Initializing MBR bootable scheme...");
                await Task.Delay(200, cancellationToken);
                Log("[DiskGeometry] [SIMULATION] Creating Primary Bootable Partition (NTFS) -> Mounted as U:");
                await Task.Delay(400, cancellationToken);
                Log("[DiskGeometry] [SIMULATION] Marking partition as Active (Boot flag armed).");
                await Task.Delay(250, cancellationToken);
                Log("[DiskGeometry] [SIMULATION] USB Pendrive storage geometry ready.");
                return true;
            }

            // Live Diskpart Execution for USB
            string scriptPath = Path.Combine(Path.GetTempPath(), $"velox_usb_diskpart_{diskIndex}_{DateTime.Now.Ticks}.txt");
            var script = new StringBuilder();
            script.AppendLine($"select disk {diskIndex}");
            script.AppendLine("clean");
            script.AppendLine("convert mbr");
            script.AppendLine("create partition primary");
            script.AppendLine("format quick fs=ntfs label=\"VELOX_BOOT\"");
            script.AppendLine("assign letter=U");
            script.AppendLine("active");
            script.AppendLine("exit");

            await File.WriteAllTextAsync(scriptPath, script.ToString(), cancellationToken);

            try
            {
                var psi = new ProcessStartInfo("diskpart.exe", $"/s \"{scriptPath}\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    Log("[DiskGeometry] ERROR: Failed to launch diskpart.exe for USB");
                    return false;
                }

                string? line;
                while ((line = await proc.StandardOutput.ReadLineAsync(cancellationToken)) != null)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        Log($"[Diskpart] {line.Trim()}");
                    }
                }

                await proc.WaitForExitAsync(cancellationToken);
                if (proc.ExitCode != 0)
                {
                    string err = await proc.StandardError.ReadToEndAsync(cancellationToken);
                    Log($"[DiskGeometry] ERROR: Diskpart exited with code {proc.ExitCode}: {err}");
                    return false;
                }

                Log("[DiskGeometry] USB Pendrive formatted as VELOX_BOOT and mounted as U:\\");
                return true;
            }
            finally
            {
                if (File.Exists(scriptPath))
                {
                    try { File.Delete(scriptPath); } catch { }
                }
            }
        }

        private void Log(string message)
        {
            OnLog?.Invoke(message);
        }
    }
}
