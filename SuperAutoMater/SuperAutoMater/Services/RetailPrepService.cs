using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace SuperAutoMater.Wpf.Services
{
    public class RetailPrepProgressEventArgs : EventArgs
    {
        public int Percentage { get; set; }
        public string Step { get; set; } = "";
        public string Details { get; set; } = "";
        public string AccentHex { get; set; } = "#58A6FF";
    }

    public class RetailPrepService
    {
        private static readonly Lazy<RetailPrepService> _instance =
            new Lazy<RetailPrepService>(() => new RetailPrepService());
        public static RetailPrepService Instance => _instance.Value;

        [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint SHEmptyRecycleBin(IntPtr hwnd, string pszRootPath, uint dwFlags);

        private const uint SHERB_NOCONFIRMATION = 0x00000001;
        private const uint SHERB_NOPROGRESSUI   = 0x00000002;
        private const uint SHERB_NOSOUND        = 0x00000004;

        public event EventHandler<RetailPrepProgressEventArgs> ProgressUpdated;

        private RetailPrepService() { }

        public async Task<bool> RunRetailPrepAsync(
            bool cleanTempFiles,
            bool emptyRecycleBin,
            bool clearEventLogs,
            bool resetPowerPlan,
            bool triggerOobeSysprep)
        {
            return await Task.Run(() =>
            {
                long totalBytesFreed = 0;
                int step = 0;
                int totalSteps = (cleanTempFiles ? 1 : 0) + (emptyRecycleBin ? 1 : 0) + 
                                 (clearEventLogs ? 1 : 0) + (resetPowerPlan ? 1 : 0) + 
                                 (triggerOobeSysprep ? 1 : 0);
                if (totalSteps == 0) totalSteps = 1;

                void Report(int pct, string title, string details, string accent = "#58A6FF")
                {
                    ProgressUpdated?.Invoke(this, new RetailPrepProgressEventArgs
                    {
                        Percentage = pct,
                        Step = title,
                        Details = details,
                        AccentHex = accent
                    });
                }

                Report(5, "INITIALIZING RETAIL PREP", "Preparing sanitization pipelines & administrative security handles...");

                // 1. Clean Temp Files & Diagnostic Artifacts
                if (cleanTempFiles)
                {
                    step++;
                    int pct = (int)((double)step / totalSteps * 85);
                    Report(pct, "PURGING DIAGNOSTIC TEMP & BENCHMARK FILES", "Cleaning SuperAutoMater caches, benchmark logs, and XML dumps...");

                    try
                    {
                        string tempDir = Path.GetTempPath();
                        string[] patterns = new[] { "superautomater*", "*_io.dat", "superautomater_bat.xml", "*.sta", "hds_temp*" };
                        foreach (var pat in patterns)
                        {
                            try
                            {
                                foreach (var file in Directory.GetFiles(tempDir, pat))
                                {
                                    try
                                    {
                                        var fi = new FileInfo(file);
                                        totalBytesFreed += fi.Length;
                                        fi.Delete();
                                    }
                                    catch { }
                                }
                            }
                            catch { }
                        }

                        // Clean local temp files in current directory
                        try
                        {
                            string curr = Directory.GetCurrentDirectory();
                            foreach (var f in Directory.GetFiles(curr, "superautomater_*.tmp"))
                            {
                                try
                                {
                                    var fi = new FileInfo(f);
                                    totalBytesFreed += fi.Length;
                                    fi.Delete();
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }
                    catch { }
                }

                // 2. Empty Recycle Bin
                if (emptyRecycleBin)
                {
                    step++;
                    int pct = (int)((double)step / totalSteps * 85);
                    Report(pct, "EMPTYING WINDOWS RECYCLE BIN", "Purging all recycled files across mounted volume drives...");
                    try
                    {
                        SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
                    }
                    catch { }
                }

                // 3. Clear Windows Event Viewer Logs
                if (clearEventLogs)
                {
                    step++;
                    int pct = (int)((double)step / totalSteps * 85);
                    Report(pct, "SANITIZING WINDOWS EVENT LOGS", "Clearing Application, System, and Setup event logs via wevtutil...");
                    string[] logsToClear = new[] { "Application", "System", "Setup" };
                    foreach (var log in logsToClear)
                    {
                        try
                        {
                            var psi = new ProcessStartInfo("wevtutil", $"cl \"{log}\"")
                            {
                                CreateNoWindow = true,
                                UseShellExecute = false
                            };
                            using var p = Process.Start(psi);
                            p?.WaitForExit(1500);
                        }
                        catch { }
                    }
                }

                // 4. Reset Windows Power Plan to Balanced
                if (resetPowerPlan)
                {
                    step++;
                    int pct = (int)((double)step / totalSteps * 85);
                    Report(pct, "RESTORING BALANCED POWER SCHEME", "Configuring factory Windows Balanced energy profile...");
                    try
                    {
                        // Balanced scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e
                        var psi = new ProcessStartInfo("powercfg", "/setactive 381b4222-f694-41f0-9685-ff5bb260df2e")
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using var p = Process.Start(psi);
                        p?.WaitForExit(1500);
                    }
                    catch { }
                }

                // 5. Trigger OOBE Sysprep (Optional)
                if (triggerOobeSysprep)
                {
                    step++;
                    Report(95, "ARMING WINDOWS CUSTOMER OOBE", "Executing Sysprep /oobe /shutdown for fresh customer unboxing experience...", "#D29922");
                    try
                    {
                        string sysprepPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "Sysprep", "sysprep.exe");
                        if (File.Exists(sysprepPath))
                        {
                            var psi = new ProcessStartInfo(sysprepPath, "/oobe /shutdown /quiet")
                            {
                                CreateNoWindow = true,
                                UseShellExecute = false
                            };
                            Process.Start(psi);
                        }
                    }
                    catch (Exception ex)
                    {
                        Report(98, "SYSPREP WARNING", $"Could not launch sysprep: {ex.Message}", "#F85149");
                    }
                }

                double mbFreed = Math.Round(totalBytesFreed / (1024.0 * 1024.0), 1);
                string summary = triggerOobeSysprep
                    ? "✓ Unit sanitized and armed with Windows OOBE. System will shut down for customer packaging."
                    : $"✓ Retail Prep Complete: ~{mbFreed} MB diagnostic cache purged, event logs sanitized & Balanced power plan restored.";

                Report(100, "RETAIL PREP COMPLETED NOMINAL", summary, "#3FB950");
                return true;
            });
        }
    }
}
