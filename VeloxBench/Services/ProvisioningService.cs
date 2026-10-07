using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace VeloxBench.Services;

public class PreFlightCheckResult
{
    public bool BitLockerActive { get; set; }
    public string BitLockerStatus { get; set; } = "Unknown";
    public bool AcPowerConnected { get; set; }
    public int BatteryPercent { get; set; } = 100;
    public bool IsPowerSafe { get; set; } = true;
    public string WindowsActivation { get; set; } = "Probing...";
    public bool IsWindowsActivated { get; set; }
    public string RecommendedUsbDriverPath { get; set; } = "";
    public string OsVersion { get; set; } = "";
    public string HardwareModel { get; set; } = "";
}

public class UnattendOptions
{
    public string OemManufacturer { get; set; } = "Velox Certified Refurbished";
    public string OemSupportHours { get; set; } = "Mon-Sat 09:00 - 20:00";
    public string OemSupportPhone { get; set; } = "Depot Support";
    public string OemModel { get; set; } = "";
    public bool BypassMicrosoftAccount { get; set; } = true;
    public bool SkipEula { get; set; } = true;
    public bool SkipNetworkInOobe { get; set; } = true;
    public bool CreateDefaultUser { get; set; } = true;
    public string DefaultUserName { get; set; } = "User";
}

/// <summary>
/// Service providing automated driver backup (DISM), system sanitization,
/// Windows Sysprep OOBE staging, and Factory Reset automation.
/// </summary>
public class ProvisioningService
{
    private static readonly Lazy<ProvisioningService> _instance = new(() => new ProvisioningService());
    public static ProvisioningService Instance => _instance.Value;

    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    private const uint SHERB_NOCONFIRMATION = 0x00000001;
    private const uint SHERB_NOPROGRESSUI = 0x00000002;
    private const uint SHERB_NOSOUND = 0x00000004;

    public async Task<PreFlightCheckResult> RunPreFlightChecksAsync(string manufacturer, string model, string serial)
    {
        return await Task.Run(() =>
        {
            var res = new PreFlightCheckResult
            {
                HardwareModel = $"{manufacturer} {model}".Trim(),
                OsVersion = Environment.OSVersion.VersionString
            };

            // 1. BitLocker Check
            try
            {
                var (code, stdout, _) = RunCliSynchronous("manage-bde.exe", "-status C:");
                if (stdout.Contains("Protection On", StringComparison.OrdinalIgnoreCase))
                {
                    res.BitLockerActive = true;
                    res.BitLockerStatus = "ACTIVE (Protection ON) — Decrypt/Suspend recommended";
                }
                else if (stdout.Contains("Protection Off", StringComparison.OrdinalIgnoreCase))
                {
                    res.BitLockerActive = false;
                    res.BitLockerStatus = "SUSPENDED / OFF";
                }
                else if (stdout.Contains("Fully Decrypted", StringComparison.OrdinalIgnoreCase))
                {
                    res.BitLockerActive = false;
                    res.BitLockerStatus = "OFF (Decrypted)";
                }
                else
                {
                    res.BitLockerActive = false;
                    res.BitLockerStatus = "OFF or Not Configured";
                }
            }
            catch
            {
                res.BitLockerStatus = "Unable to query (manage-bde)";
            }

            // 2. Battery & AC Power Check
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT EstimatedChargeRemaining, BatteryStatus FROM Win32_Battery");
                using var coll = searcher.Get();
                bool hasBattery = false;
                foreach (ManagementObject mo in coll)
                {
                    hasBattery = true;
                    if (mo["EstimatedChargeRemaining"] != null)
                        res.BatteryPercent = Convert.ToInt32(mo["EstimatedChargeRemaining"]);

                    // BatteryStatus 2 = AC Power (Charging or Connected)
                    if (mo["BatteryStatus"] != null)
                    {
                        int status = Convert.ToInt32(mo["BatteryStatus"]);
                        res.AcPowerConnected = (status == 2 || status == 6 || status == 7 || status == 8 || status == 9);
                    }
                }

                if (!hasBattery)
                {
                    // Desktop / All-in-One without battery
                    res.AcPowerConnected = true;
                    res.BatteryPercent = 100;
                    res.IsPowerSafe = true;
                }
                else
                {
                    res.IsPowerSafe = res.AcPowerConnected || res.BatteryPercent >= 35;
                }
            }
            catch
            {
                res.IsPowerSafe = true;
            }

            // 3. Windows Activation Check
            try
            {
                using var licSearcher = new ManagementObjectSearcher("SELECT LicenseStatus FROM SoftwareLicensingProduct WHERE PartialProductKey IS NOT NULL AND ApplicationId = '55c92734-d682-4d71-9662-e1901a16ea05'");
                using var licColl = licSearcher.Get();
                foreach (ManagementObject mo in licColl)
                {
                    if (mo["LicenseStatus"] != null && Convert.ToInt32(mo["LicenseStatus"]) == 1)
                    {
                        res.IsWindowsActivated = true;
                        res.WindowsActivation = "ACTIVATED (Permanent / OEM Digital License)";
                        break;
                    }
                }
                if (!res.IsWindowsActivated)
                {
                    res.WindowsActivation = "Notification / Grace Period / Not Activated";
                }
            }
            catch
            {
                res.WindowsActivation = "Query skipped";
            }

            // 4. Recommended USB Driver Export Path
            string appDir = AppContext.BaseDirectory;
            string root = Path.GetPathRoot(appDir) ?? "C:\\";
            string cleanMfg = Regex.Replace(manufacturer, @"[^a-zA-Z0-9_\-]", "_");
            string cleanModel = Regex.Replace(model, @"[^a-zA-Z0-9_\-]", "_");
            string cleanSerial = Regex.Replace(serial, @"[^a-zA-Z0-9_\-]", "_");
            if (string.IsNullOrWhiteSpace(cleanMfg)) cleanMfg = "GenericOEM";
            if (string.IsNullOrWhiteSpace(cleanModel)) cleanModel = "Model";

            string subFolder = $"{cleanMfg}_{cleanModel}_{cleanSerial}".Trim('_');
            res.RecommendedUsbDriverPath = Path.Combine(root, "VeloxDrivers", subFolder);

            return res;
        });
    }

    public async Task<bool> SuspendBitLockerAsync(Action<string> log)
    {
        log("Suspending BitLocker protection on C:...");
        var (code, stdout, stderr) = await RunCliAsync("manage-bde.exe", "-protectors -disable C:", log);
        if (code == 0)
        {
            log("✓ BitLocker protection successfully suspended on C:.");
            return true;
        }
        else
        {
            log($"⚠ manage-bde returned code {code}: {stderr}");
            return false;
        }
    }

    public async Task<bool> ExportDriversAsync(string destinationDir, Action<string> log, IProgress<int>? progress = null)
    {
        return await Task.Run(async () =>
        {
            try
            {
                log($"Preparing driver export destination directory: {destinationDir}");
                Directory.CreateDirectory(destinationDir);

                log("Launching DISM 3rd-party driver harvest (dism /online /export-driver)...");
                log("Note: Proprietary Wi-Fi, audio, display, chipset and trackpad drivers are being extracted.");

                int driverCount = 0;
                var (code, _, _) = await RunCliAsync("dism.exe", $"/online /export-driver /destination:\"{destinationDir}\"", line =>
                {
                    if (line.Contains(".inf", StringComparison.OrdinalIgnoreCase) || line.Contains("Exporting", StringComparison.OrdinalIgnoreCase))
                    {
                        driverCount++;
                        progress?.Report(driverCount);
                    }
                    log(line);
                });

                if (code == 0)
                {
                    log($"=======================================================");
                    log($"✓ DRIVER HARVEST COMPLETED! Total drivers packaged: {driverCount}");
                    log($"Stored in: {destinationDir}");
                    log($"=======================================================");
                    return true;
                }
                else
                {
                    log($"⚠ DISM driver export completed with exit code {code}. Check log output above.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                log($"ERROR during driver export: {ex.Message}");
                return false;
            }
        });
    }

    public async Task<bool> InjectDriversAsync(string sourceDir, Action<string> log)
    {
        return await Task.Run(async () =>
        {
            try
            {
                if (!Directory.Exists(sourceDir))
                {
                    log($"ERROR: Driver source directory does not exist: {sourceDir}");
                    return false;
                }

                log($"Injecting 3rd-party drivers from {sourceDir} into running OS (dism /online /add-driver /recurse)...");
                var (code, _, _) = await RunCliAsync("dism.exe", $"/online /add-driver /driver:\"{sourceDir}\" /recurse", log);
                if (code == 0)
                {
                    log("✓ All drivers injected and staged successfully.");
                    return true;
                }
                else
                {
                    log($"⚠ DISM driver injection exited with code {code}.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                log($"ERROR during driver injection: {ex.Message}");
                return false;
            }
        });
    }

    public async Task<bool> SanitizeDepotArtifactsAsync(Action<string> log)
    {
        return await Task.Run(async () =>
        {
            log("=======================================================");
            log("INITIATING DEPOT ARTIFACT & TELEMETRY SANITIZATION");
            log("=======================================================");

            // 1. Clean VeloxBench bundle cache in %TEMP%\VeloxBench
            try
            {
                string veloxTemp = Path.Combine(Path.GetTempPath(), "VeloxBench");
                if (Directory.Exists(veloxTemp))
                {
                    Directory.Delete(veloxTemp, true);
                    log("✓ Purged VeloxBench temporary extraction bundle directory.");
                }
            }
            catch (Exception ex)
            {
                log($"Notice: Could not purge active VeloxBench temp folder (in use): {ex.Message}");
            }

            // 2. Clean Windows Event Logs
            string[] eventLogs = ["Application", "System", "Security", "Setup"];
            foreach (var logName in eventLogs)
            {
                try
                {
                    var (code, _, _) = await RunCliAsync("wevtutil.exe", $"cl {logName}", null);
                    if (code == 0) log($"✓ Cleared Windows Event Log: {logName}");
                }
                catch { }
            }

            // 3. Flush DNS Cache
            try
            {
                await RunCliAsync("ipconfig.exe", "/flushdns", null);
                log("✓ Flushed DNS resolver cache.");
            }
            catch { }

            // 4. Clean System Temp & User Temp
            try
            {
                CleanDirectoryContents(Path.GetTempPath(), log, "User Temp");
                CleanDirectoryContents("C:\\Windows\\Temp", log, "System Temp");
            }
            catch { }

            // 5. Clean Crash Dumps
            try
            {
                CleanDirectoryContents("C:\\Windows\\Minidump", log, "Minidumps");
                CleanDirectoryContents("C:\\ProgramData\\Microsoft\\Windows\\WER\\ReportQueue", log, "WER Crash Reports");
            }
            catch { }

            // 6. Empty Recycle Bin
            try
            {
                SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
                log("✓ Emptied Windows Recycle Bin across all drives.");
            }
            catch { }

            // 7. Apply BypassNRO Registry Flag (Permits offline setup during Windows 11 OOBE)
            try
            {
                await RunCliAsync("reg.exe", "add \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\OOBE\" /v BypassNRO /t REG_DWORD /d 1 /f", null);
                log("✓ Applied Windows 11 OOBE BypassNRO flag (offline setup enabled).");
            }
            catch { }

            log("=======================================================");
            log("✓ SANITIZATION AUDIT COMPLETE — OS is clean for packaging.");
            log("=======================================================");
            return true;
        });
    }

    public async Task<bool> GenerateUnattendXmlAsync(string targetFilePath, UnattendOptions options, Action<string> log)
    {
        return await Task.Run(() =>
        {
            try
            {
                log($"Generating certified depot unattended answer file: {targetFilePath}...");
                string dir = Path.GetDirectoryName(targetFilePath)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var sb = new StringBuilder();
                sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
                sb.AppendLine("<unattend xmlns=\"urn:schemas-microsoft-com:unattend\">");

                // Specialize Pass: OEM Branding
                sb.AppendLine("    <settings pass=\"specialize\">");
                sb.AppendLine("        <component name=\"Microsoft-Windows-Shell-Setup\" processorArchitecture=\"amd64\" publicKeyToken=\"31bf3856ad364e35\" language=\"neutral\" versionScope=\"nonSxS\">");
                sb.AppendLine("            <OEMInformation>");
                sb.AppendLine($"                <Manufacturer>{EscapeXml(options.OemManufacturer)}</Manufacturer>");
                if (!string.IsNullOrWhiteSpace(options.OemModel))
                    sb.AppendLine($"                <Model>{EscapeXml(options.OemModel)}</Model>");
                sb.AppendLine($"                <SupportHours>{EscapeXml(options.OemSupportHours)}</SupportHours>");
                sb.AppendLine($"                <SupportPhone>{EscapeXml(options.OemSupportPhone)}</SupportPhone>");
                sb.AppendLine("            </OEMInformation>");
                sb.AppendLine("        </component>");
                sb.AppendLine("    </settings>");

                // OOBE Pass
                sb.AppendLine("    <settings pass=\"oobeSystem\">");
                sb.AppendLine("        <component name=\"Microsoft-Windows-Shell-Setup\" processorArchitecture=\"amd64\" publicKeyToken=\"31bf3856ad364e35\" language=\"neutral\" versionScope=\"nonSxS\">");
                sb.AppendLine("            <OOBE>");
                if (options.SkipEula)
                    sb.AppendLine("                <HideEULAPage>true</HideEULAPage>");
                sb.AppendLine("                <HideOEMRegistrationScreen>true</HideOEMRegistrationScreen>");
                if (options.BypassMicrosoftAccount)
                    sb.AppendLine("                <HideOnlineAccountScreens>true</HideOnlineAccountScreens>");
                if (options.SkipNetworkInOobe)
                    sb.AppendLine("                <HideWirelessSetupInOOBE>true</HideWirelessSetupInOOBE>");
                sb.AppendLine("                <ProtectYourPC>1</ProtectYourPC>");
                sb.AppendLine("                <NetworkLocation>Work</NetworkLocation>");
                sb.AppendLine("            </OOBE>");

                if (options.CreateDefaultUser && !string.IsNullOrWhiteSpace(options.DefaultUserName))
                {
                    sb.AppendLine("            <UserAccounts>");
                    sb.AppendLine("                <LocalAccounts>");
                    sb.AppendLine("                    <LocalAccount action=\"AddNext\">");
                    sb.AppendLine($"                        <Name>{EscapeXml(options.DefaultUserName)}</Name>");
                    sb.AppendLine($"                        <DisplayName>{EscapeXml(options.DefaultUserName)}</DisplayName>");
                    sb.AppendLine("                        <Group>Administrators</Group>");
                    sb.AppendLine("                        <Password>");
                    sb.AppendLine("                            <Value></Value>");
                    sb.AppendLine("                            <PlainText>true</PlainText>");
                    sb.AppendLine("                        </Password>");
                    sb.AppendLine("                    </LocalAccount>");
                    sb.AppendLine("                </LocalAccounts>");
                    sb.AppendLine("            </UserAccounts>");
                    sb.AppendLine("            <AutoLogon>");
                    sb.AppendLine("                <Enabled>true</Enabled>");
                    sb.AppendLine($"                <Username>{EscapeXml(options.DefaultUserName)}</Username>");
                    sb.AppendLine("                <Password><Value></Value><PlainText>true</PlainText></Password>");
                    sb.AppendLine("                <LogonCount>1</LogonCount>");
                    sb.AppendLine("            </AutoLogon>");
                }

                sb.AppendLine("        </component>");
                sb.AppendLine("    </settings>");
                sb.AppendLine("</unattend>");

                File.WriteAllText(targetFilePath, sb.ToString(), Encoding.UTF8);
                log("✓ Unattended answer file written successfully.");
                return true;
            }
            catch (Exception ex)
            {
                log($"ERROR writing unattend.xml: {ex.Message}");
                return false;
            }
        });
    }

    public async Task<bool> ExecuteSysprepAsync(bool generalize, bool shutdown, string? unattendPath, Action<string> log)
    {
        return await Task.Run(async () =>
        {
            string sysprepExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "Sysprep", "sysprep.exe");
            if (!File.Exists(sysprepExe))
            {
                log($"ERROR: Sysprep executable not found at {sysprepExe}");
                return false;
            }

            var args = new StringBuilder("/oobe");
            if (generalize) args.Append(" /generalize");
            if (shutdown) args.Append(" /shutdown");
            else args.Append(" /reboot");

            if (!string.IsNullOrWhiteSpace(unattendPath) && File.Exists(unattendPath))
            {
                args.Append($" /unattend:\"{unattendPath}\"");
                log($"Binding unattended answer file: {unattendPath}");
            }

            log($"Invoking Windows Sysprep: sysprep.exe {args}");
            log("WARNING: Windows will now strip unique system SIDs, generalize drivers, and prepare for clean out-of-box experience.");

            var (code, stdout, stderr) = await RunCliAsync(sysprepExe, args.ToString(), log);
            if (code == 0)
            {
                log("✓ Sysprep triggered successfully.");
                return true;
            }
            else
            {
                log($"⚠ Sysprep returned code {code}.");
                return false;
            }
        });
    }

    public async Task<bool> LaunchFactoryResetAsync(Action<string> log)
    {
        return await Task.Run(async () =>
        {
            try
            {
                log("Launching Windows Factory Reset wizard (systemreset -factoryreset)...");
                var (code, _, _) = await RunCliAsync("systemreset.exe", "-factoryreset", log);
                return code == 0;
            }
            catch (Exception ex)
            {
                log($"ERROR launching Factory Reset: {ex.Message}");
                return false;
            }
        });
    }

    public async Task<bool> RebootToWinReAsync(Action<string> log)
    {
        return await Task.Run(async () =>
        {
            try
            {
                log("Configuring one-time boot to Windows Recovery Environment (reagentc /boottore)...");
                var (code, stdout, stderr) = await RunCliAsync("reagentc.exe", "/boottore", log);
                if (code == 0 || stdout.Contains("successful", StringComparison.OrdinalIgnoreCase))
                {
                    log("✓ WinRE boot flag set. Scheduling immediate restart to Recovery Environment...");
                    await RunCliAsync("shutdown.exe", "/r /t 5 /c \"Velox Auto-Provisioning: Rebooting into Windows Recovery Environment\"", log);
                    return true;
                }
                else
                {
                    log($"⚠ reagentc error: {stdout} {stderr}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                log($"ERROR initiating WinRE reboot: {ex.Message}");
                return false;
            }
        });
    }

    private static void CleanDirectoryContents(string dirPath, Action<string> log, string label)
    {
        try
        {
            if (!Directory.Exists(dirPath)) return;
            var di = new DirectoryInfo(dirPath);
            int filesDeleted = 0;
            foreach (var file in di.GetFiles())
            {
                try { file.Delete(); filesDeleted++; } catch { }
            }
            foreach (var sub in di.GetDirectories())
            {
                try { sub.Delete(true); } catch { }
            }
            if (filesDeleted > 0)
                log($"✓ Purged {filesDeleted} files from {label} ({dirPath}).");
        }
        catch { }
    }

    private static (int exitCode, string stdout, string stderr) RunCliSynchronous(string filename, string arguments)
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = filename,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(5000);
            return (proc.ExitCode, stdout, stderr);
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }

    private static async Task<(int exitCode, string stdout, string stderr)> RunCliAsync(string filename, string arguments, Action<string>? logLine, CancellationToken ct = default)
    {
        var stdoutSb = new StringBuilder();
        var stderrSb = new StringBuilder();

        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = filename,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        proc.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                stdoutSb.AppendLine(e.Data);
                logLine?.Invoke(e.Data);
            }
        };

        proc.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                stderrSb.AppendLine(e.Data);
                logLine?.Invoke(e.Data);
            }
        };

        try
        {
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            await proc.WaitForExitAsync(ct);
            return (proc.ExitCode, stdoutSb.ToString(), stderrSb.ToString());
        }
        catch (Exception ex)
        {
            logLine?.Invoke($"Execution exception: {ex.Message}");
            return (-1, "", ex.Message);
        }
    }

    private static string EscapeXml(string val)
    {
        if (string.IsNullOrEmpty(val)) return "";
        return val.Replace("&", "&amp;")
                  .Replace("<", "&lt;")
                  .Replace(">", "&gt;")
                  .Replace("\"", "&quot;")
                  .Replace("'", "&apos;");
    }
}
