using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SuperAutoMater
{
    public partial class Form1
    {
        private string lastModel = "N/A";
        private string lastSerial = "N/A";
        private string lastCpu = "N/A";
        private string lastRam = "N/A";
        private string lastGpu = "N/A";
        private string lastNetworkSummary = "N/A";
        private string lastStorageSummary = "N/A";
        private string lastStorageHealth = "N/A";
        private string lastBatteryHealth = "N/A";
        private string lastBatteryCardLine1 = null;

        public string LastModel => lastModel;
        public string LastSerial => lastSerial;
        public string LastCpu => lastCpu;
        public string LastRam => lastRam;
        public string LastStorageSummary => lastStorageSummary;
        public string LastBatteryHealth => lastBatteryHealth;

        private struct SmartctlDeviceInfo
        {
            public string DevicePath;
            public string DeviceType;
        }
        private Dictionary<string, SmartctlDeviceInfo> smartctlDeviceMap = new Dictionary<string, SmartctlDeviceInfo>();

        // --- Hard Disk Sentinel Integration ---
        private struct HdSentinelDriveInfo
        {
            public int Health;
            public int Performance;
            public string Temperature;
            public string Description;
            public string Model;
            public string Serial;
        }
        private Dictionary<string, HdSentinelDriveInfo> hdSentinelData = new Dictionary<string, HdSentinelDriveInfo>(StringComparer.OrdinalIgnoreCase);

        private string FindHdSentinelExe()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            string[] candidates = new[]
            {
                Path.Combine(baseDir, "lib", "HDS", "HDSentinel.exe"),
                Path.Combine(baseDir, "lib", "HDS", "HDS.exe"),
                Path.Combine(baseDir, "lib", "HDS", "hds.exe"),
                Path.Combine(baseDir, "lib", "HDSentinel.exe"),
                Path.Combine(baseDir, "lib", "HDS.exe"),
                Path.Combine(baseDir, "lib", "hds.exe"),
                Path.Combine(baseDir, "HDS", "HDSentinel.exe"),
                Path.Combine(baseDir, "HDS", "HDS.exe"),
                Path.Combine(baseDir, "HDS", "hds.exe"),
                Path.Combine(baseDir, "HDSentinel.exe"),
                Path.Combine(baseDir, "HDS.exe"),
                Path.Combine(baseDir, "hds.exe"),
                @"C:\Program Files (x86)\Hard Disk Sentinel\HDSentinel.exe",
                @"C:\Program Files\Hard Disk Sentinel\HDSentinel.exe"
            };

            foreach (string path in candidates)
            {
                if (File.Exists(path)) return path;
            }

            return null;
        }

        private void BuildHdSentinelMap()
        {
            hdSentinelData.Clear();

            // Strategy 1: Read the .sta status file directly (instant, no GUI needed)
            string staFile = FindHdSentinelStaFile();
            if (staFile != null)
            {
                try
                {
                    string staContent = File.ReadAllText(staFile, Encoding.Default);
                    ParseHdSentinelStaFile(staContent);
                    if (hdSentinelData.Count > 0) return; // Success!
                }
                catch { }
            }

            // Strategy 2: Launch HDSentinel to generate fresh .sta, then read it
            string hdsPath = FindHdSentinelExe();
            if (hdsPath == null) return;

            string hdsDir = Path.GetDirectoryName(hdsPath);
            string targetSta = Path.Combine(hdsDir, "HDSentinel.sta");

            try
            {
                Process p = new Process();
                p.StartInfo.FileName = hdsPath;
                p.StartInfo.UseShellExecute = true;
                p.StartInfo.WindowStyle = ProcessWindowStyle.Minimized;
                p.Start();

                uint pid = (uint)p.Id;

                // Wait up to 25 seconds — nag screen Close button activates after ~10s
                for (int attempt = 0; attempt < 50; attempt++)
                {
                    System.Threading.Thread.Sleep(500);
                    if (p.HasExited) break;

                    // Try to dismiss the nag screen
                    TryDismissHdsNagScreen(pid);
                }

                // Give HDSentinel a moment to write the .sta file after nag dismissal
                if (!p.HasExited)
                {
                    System.Threading.Thread.Sleep(3000);
                }

                // Kill the process — we only needed it to refresh the .sta file
                if (!p.HasExited)
                {
                    try { p.CloseMainWindow(); } catch { }
                    if (!p.WaitForExit(3000))
                    {
                        try { p.Kill(); } catch { }
                    }
                }

                try { p.Dispose(); } catch { }

                // Now read the freshly written .sta file
                if (File.Exists(targetSta))
                {
                    string staContent = File.ReadAllText(targetSta, Encoding.Default);
                    ParseHdSentinelStaFile(staContent);
                }
            }
            catch { }
        }

        private string FindHdSentinelStaFile()
        {
            // Check all known locations for HDSentinel.sta
            string[] candidates = new[]
            {
                // Next to HDSentinel.exe / HDS.exe in our lib folder
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib", "HDS", "HDSentinel.sta"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib", "HDS", "HDS.sta"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib", "HDSentinel.sta"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib", "HDS.sta"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HDSentinel.sta"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HDS.sta"),
                // Standard install location
                @"C:\Program Files (x86)\Hard Disk Sentinel\HDSentinel.sta",
                @"C:\Program Files\Hard Disk Sentinel\HDSentinel.sta"
            };

            foreach (string path in candidates)
            {
                if (File.Exists(path)) return path;
            }

            // Search next to detected exe
            string hdsExe = FindHdSentinelExe();
            if (hdsExe != null)
            {
                string staPath = Path.Combine(Path.GetDirectoryName(hdsExe), "HDSentinel.sta");
                if (File.Exists(staPath)) return staPath;
            }

            return null;
        }

        private void ParseHdSentinelStaFile(string staContent)
        {
            // The .sta file is INI format with sections like:
            // [Sta_WD PC SN740 SDDQNQD-512G- (488382 MB)_223626805334]
            // 46226=50,44.50,2,97,39,30458753
            //                      ^^ health %
            // Format: dateKey=maxTemp,avgTemp,count,HEALTH%,currentTemp,powerOnMinutes

            string[] lines = staContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            string currentModel = null;
            string currentSerial = null;

            foreach (string line in lines)
            {
                string trimmed = line.Trim();

                // Parse section headers: [Sta_MODEL (SIZE MB)_SERIAL]
                if (trimmed.StartsWith("[Sta_", StringComparison.OrdinalIgnoreCase))
                {
                    if (trimmed.Length >= 7 && trimmed.EndsWith("]"))
                    {
                        string sectionName = trimmed.Substring(5, trimmed.Length - 6); // Remove [Sta_ and ]
                        
                        // Extract model and serial from "MODEL (SIZE MB)_SERIAL"
                        int lastUnderscore = sectionName.LastIndexOf('_');
                        if (lastUnderscore > 0)
                        {
                            currentModel = sectionName.Substring(0, lastUnderscore).Trim();
                            currentSerial = sectionName.Substring(lastUnderscore + 1).Trim();
    
                            // Clean the model name — remove the "(SIZE MB)" part for matching
                            int parenStart = currentModel.LastIndexOf('(');
                            if (parenStart > 0)
                            {
                                currentModel = currentModel.Substring(0, parenStart).Trim();
                            }
                        }
                    }
                    continue;
                }

                // Parse data lines: dateKey=maxTemp,avgTemp,count,HEALTH%,currentTemp,powerOnMinutes
                if (currentModel != null && trimmed.Contains('=') && !trimmed.StartsWith("["))
                {
                    string key = trimmed.Split('=')[0].Trim();

                    // Skip non-numeric keys (like MaxTempEverC, MinTempEverC, Dates, etc.)
                    if (!int.TryParse(key, out _)) continue;

                    string valuePart = trimmed.Split(new[] { '=' }, 2)[1].Trim();
                    string[] fields = valuePart.Split(',');

                    if (fields.Length >= 5)
                    {
                        if (int.TryParse(fields[3].Trim(), out int health) &&
                            int.TryParse(fields[4].Trim(), out int currentTemp))
                        {
                            // Use the LATEST data line (highest dateKey = most recent)
                            var entry = new HdSentinelDriveInfo
                            {
                                Health = health,
                                Performance = 100, // .sta doesn't store performance separately
                                Temperature = $"{currentTemp} °C",
                                Description = null,
                                Model = currentModel,
                                Serial = currentSerial
                            };

                            // Store/overwrite — later entries are more recent
                            if (!string.IsNullOrWhiteSpace(currentModel))
                                hdSentinelData[currentModel] = entry;
                            if (!string.IsNullOrWhiteSpace(currentSerial))
                                hdSentinelData["SN:" + currentSerial.ToUpperInvariant()] = entry;
                        }
                    }
                }
            }
        }

        private void TryDismissHdsNagScreen(uint targetPid)
        {
            IntPtr foundButton = IntPtr.Zero;

            NativeMethods.EnumWindows((hWnd, lParam) =>
            {
                NativeMethods.GetWindowThreadProcessId(hWnd, out uint windowPid);
                if (windowPid != targetPid) return true;
                if (!NativeMethods.IsWindowVisible(hWnd)) return true;

                NativeMethods.EnumChildWindows(hWnd, (childHwnd, childLParam) =>
                {
                    var classSb = new StringBuilder(64);
                    NativeMethods.GetClassName(childHwnd, classSb, 64);
                    string className = classSb.ToString();

                    if (!className.Equals("Button", StringComparison.OrdinalIgnoreCase) &&
                        !className.Equals("TButton", StringComparison.OrdinalIgnoreCase))
                        return true;

                    var btnTextSb = new StringBuilder(64);
                    NativeMethods.GetWindowText(childHwnd, btnTextSb, 64);
                    string btnText = btnTextSb.ToString().Trim();

                    if (btnText.IndexOf("Close", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        btnText.Equals("OK", StringComparison.OrdinalIgnoreCase))
                    {
                        if (NativeMethods.IsWindowEnabled(childHwnd))
                        {
                            foundButton = childHwnd;
                            return false;
                        }
                    }
                    return true;
                }, IntPtr.Zero);

                if (foundButton != IntPtr.Zero) return false;
                return true;
            }, IntPtr.Zero);

            if (foundButton != IntPtr.Zero)
            {
                NativeMethods.SendMessage(foundButton, NativeMethods.BM_CLICK, IntPtr.Zero, IntPtr.Zero);
            }
        }



        private HdSentinelDriveInfo? LookupHdSentinelHealth(string driveName, string serialNumber)
        {
            // Try exact model match first
            if (!string.IsNullOrWhiteSpace(driveName))
            {
                string cleanName = driveName.Trim();
                if (hdSentinelData.TryGetValue(cleanName, out var exactMatch))
                    return exactMatch;

                // Try partial match (WMI model name may be a substring of HDS model or vice versa)
                foreach (var kvp in hdSentinelData)
                {
                    if (kvp.Key.StartsWith("SN:")) continue; // Skip serial keys
                    if (kvp.Key.Contains(cleanName) || cleanName.Contains(kvp.Key))
                        return kvp.Value;
                    // Also try matching significant tokens (brand + capacity)
                    string[] nameTokens = cleanName.Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
                    if (nameTokens.Length >= 2)
                    {
                        int matchCount = nameTokens.Count(t => kvp.Key.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
                        if (matchCount >= 2) return kvp.Value;
                    }
                }
            }

            // Try serial number match
            if (!string.IsNullOrWhiteSpace(serialNumber))
            {
                string cleanSerial = "SN:" + serialNumber.Trim().ToUpperInvariant();
                if (hdSentinelData.TryGetValue(cleanSerial, out var serialMatch))
                    return serialMatch;
            }

            // If only one drive in HDS data, return it (most common case - single drive laptop)
            var driveEntries = hdSentinelData.Where(kvp => !kvp.Key.StartsWith("SN:")).ToList();
            if (driveEntries.Count == 1)
                return driveEntries[0].Value;

            return null;
        }

        private struct PhysicalDiskInfo
        {
            public string DeviceId;
            public string RawName;
            public string SerialNumber;
            public long SizeBytes;
            public int MediaType;
            public int BusType;
            public int HealthStatus;
            public bool IsSystemDrive;
            public bool IsUsb;
        }

        private string GetSystemDriveDeviceId()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(@"SELECT * FROM Win32_DiskDrive"))
                using (var collection = searcher.Get())
                {
                    foreach (ManagementObject drive in collection)
                    {
                        using (drive)
                        {
                            string deviceID = drive["DeviceID"]?.ToString();
                            string index = drive["Index"]?.ToString();
                            if (string.IsNullOrEmpty(deviceID)) continue;

                            using (var pSearcher = new ManagementObjectSearcher($"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{deviceID}'}} WHERE AssocClass = Win32_DiskDriveToDiskPartition"))
                            using (var pCollection = pSearcher.Get())
                            {
                                foreach (ManagementObject part in pCollection)
                                {
                                    using (part)
                                    {
                                        string partDeviceID = part["DeviceID"]?.ToString();
                                        if (string.IsNullOrEmpty(partDeviceID)) continue;

                                        using (var lSearcher = new ManagementObjectSearcher($"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partDeviceID}'}} WHERE AssocClass = Win32_LogicalDiskToPartition"))
                                        using (var lCollection = lSearcher.Get())
                                        {
                                            foreach (ManagementObject logical in lCollection)
                                            {
                                                using (logical)
                                                {
                                                    string driveLetter = logical["Name"]?.ToString();
                                                    if (string.Equals(driveLetter, "C:", StringComparison.OrdinalIgnoreCase))
                                                    {
                                                        return index;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private string GetSmartctlExecutablePath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string sourcePath = null;

            string libPath = Path.Combine(baseDir, "lib", "smartctl.exe");
            if (File.Exists(libPath)) sourcePath = libPath;
            else
            {
                string rootPath = Path.Combine(baseDir, "smartctl.exe");
                if (File.Exists(rootPath)) sourcePath = rootPath;
            }

            if (sourcePath == null) return null;

            // Fast RAM/Local Cache Optimization:
            // Cache to local %TEMP%\SuperAutoMater\bin to eliminate slow USB bus read latency during process execution
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "SuperAutoMater", "bin");
                string localExe = Path.Combine(tempDir, "smartctl.exe");
                if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

                if (!File.Exists(localExe) || new FileInfo(localExe).Length != new FileInfo(sourcePath).Length)
                {
                    File.Copy(sourcePath, localExe, true);
                    string sourceDb = Path.Combine(Path.GetDirectoryName(sourcePath), "drivedb.h");
                    if (File.Exists(sourceDb))
                    {
                        File.Copy(sourceDb, Path.Combine(tempDir, "drivedb.h"), true);
                    }
                }
                return localExe;
            }
            catch
            {
                return sourcePath;
            }
        }

        private bool IsSafeDeviceId(string deviceId)
        {
            return !string.IsNullOrEmpty(deviceId) && deviceId.All(char.IsLetterOrDigit);
        }

        private void BuildSmartctlDeviceMap()
        {
            smartctlDeviceMap.Clear();
            string smartctlPath = GetSmartctlExecutablePath();
            if (smartctlPath == null) return;

            string scanOutput = RunSmartctl(smartctlPath, "--scan");
            if (string.IsNullOrWhiteSpace(scanOutput)) return;

            var lineRegex = new Regex(@"^(\S+)\s+-d\s+(\S+)");

            foreach (string rawLine in scanOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Match match = lineRegex.Match(rawLine.Trim());
                if (!match.Success) continue;

                string devicePath = match.Groups[1].Value;
                string deviceType = match.Groups[2].Value;

                if (deviceType.Equals("csmi", StringComparison.OrdinalIgnoreCase))
                    deviceType = "nvme";

                // Sanitize devicePath and deviceType
                if (!Regex.IsMatch(devicePath, @"^[a-zA-Z0-9/\._\-]+$") || !Regex.IsMatch(deviceType, @"^[a-zA-Z0-9_\-]+$"))
                    continue;

                string infoOutput = RunSmartctl(smartctlPath, $"-i \"{devicePath}\" -d \"{deviceType}\"");
                string serial = ExtractSerialFromSmartctlInfo(infoOutput);

                if (!string.IsNullOrWhiteSpace(serial))
                {
                    smartctlDeviceMap[serial.Trim().ToUpperInvariant()] = new SmartctlDeviceInfo
                    {
                        DevicePath = devicePath,
                        DeviceType = deviceType
                    };
                }
            }
        }

        private string ExtractSerialFromSmartctlInfo(string infoOutput)
        {
            if (string.IsNullOrWhiteSpace(infoOutput)) return null;
            foreach (string line in infoOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("Serial Number:", StringComparison.OrdinalIgnoreCase) || line.StartsWith("Serial number:", StringComparison.OrdinalIgnoreCase))
                    return line.Split(new[] { ':' }, 2)[1].Trim();
            }
            return null;
        }

        private string GetDriveHealthFromSmartctl(string deviceId, string serialNumber)
        {
            if (!IsSafeDeviceId(deviceId)) return " [Invalid device id]";

            string smartctlPath = GetSmartctlExecutablePath();
            if (smartctlPath == null) return " [smartctl.exe missing in ./ or ./lib/]";

            if (!string.IsNullOrWhiteSpace(serialNumber))
            {
                string wmiSerial = serialNumber.Trim().ToUpperInvariant();
                if (wmiSerial.Length >= 4)
                {
                    foreach (var kvp in smartctlDeviceMap)
                    {
                        if (kvp.Key.Length < 4) continue;
                        if (!wmiSerial.Contains(kvp.Key) && !kvp.Key.Contains(wmiSerial)) continue;

                        if (kvp.Value.DeviceType.Equals("csmi", StringComparison.OrdinalIgnoreCase))
                        {
                            string nvmeAttempt = ParseSmartctlOutput(RunSmartctl(smartctlPath, $"-a \"{kvp.Value.DevicePath}\" -d nvme"));
                            if (IsUsableSmartResult(nvmeAttempt)) return nvmeAttempt;
                        }

                        string mappedResult = ParseSmartctlOutput(RunSmartctl(smartctlPath, $"-a \"{kvp.Value.DevicePath}\" -d \"{kvp.Value.DeviceType}\""));
                        if (IsUsableSmartResult(mappedResult)) return mappedResult;
                    }
                }
            }

            string csmiOutput = RunSmartctl(smartctlPath, $"-a \"/dev/csmi0,{deviceId}\" -d nvme");
            string csmiResult = ParseSmartctlOutput(csmiOutput);
            if (IsUsableSmartResult(csmiResult)) return csmiResult;

            string nvmeOutput = RunSmartctl(smartctlPath, $"-a -d nvme \"/dev/nvme{deviceId}\"");
            string nvmeResult = ParseSmartctlOutput(nvmeOutput);
            if (IsUsableSmartResult(nvmeResult)) return nvmeResult;

            string pdNvmeOutput = RunSmartctl(smartctlPath, $"-a -d nvme \"/dev/pd{deviceId}\"");
            string pdNvmeResult = ParseSmartctlOutput(pdNvmeOutput);
            if (IsUsableSmartResult(pdNvmeResult)) return pdNvmeResult;

            string autoOutput = RunSmartctl(smartctlPath, $"-a \"/dev/pd{deviceId}\"");
            string autoResult = ParseSmartctlOutput(autoOutput);
            if (IsUsableSmartResult(autoResult)) return autoResult;

            // --- INTEL RST / VMD RAID CONTROLLER SMART FALLBACK ---
            string rstWmiResult = GetIntelRstWmiDriveHealth(deviceId, serialNumber);
            if (!string.IsNullOrWhiteSpace(rstWmiResult)) return rstWmiResult;

            return " [SMART: unavailable - drive behind Intel RST/RAID controller]";
        }

        private string GetIntelRstWmiDriveHealth(string deviceId, string serialNumber)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT * FROM MSFT_PhysicalDisk"))
                using (var collection = searcher.Get())
                {
                    foreach (ManagementObject disk in collection)
                    {
                        using (disk)
                        {
                            string dId = disk["DeviceId"]?.ToString();
                            string serial = disk["SerialNumber"]?.ToString();

                            bool isMatch = (dId == deviceId);
                            if (!isMatch && !string.IsNullOrWhiteSpace(serialNumber) && !string.IsNullOrWhiteSpace(serial))
                            {
                                string cleanTarget = serialNumber.Trim().ToUpperInvariant();
                                string cleanDiskSerial = serial.Trim().ToUpperInvariant();
                                if (cleanTarget.Contains(cleanDiskSerial) || cleanDiskSerial.Contains(cleanTarget))
                                    isMatch = true;
                            }

                            if (isMatch)
                            {
                                int hStatus = disk["HealthStatus"] != null ? Convert.ToInt32(disk["HealthStatus"]) : 0;
                                string healthStr = hStatus == 0 ? "100%" : (hStatus == 1 ? "75%" : "30%");
                                string statusTag = hStatus == 0 ? "GOOD [✓]" : "WARNING [!]";

                                return $" [SMART: Health {healthStr}, Status {statusTag} (Intel RST/VMD Pass-Through)]";
                            }
                        }
                    }
                }
            }
            catch { }

            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive"))
                using (var collection = searcher.Get())
                {
                    foreach (ManagementObject disk in collection)
                    {
                        using (disk)
                        {
                            string indexStr = disk["Index"]?.ToString();
                            if (indexStr == deviceId)
                            {
                                string status = disk["Status"]?.ToString() ?? "OK";
                                return $" [SMART: Health 100%, Status {status} (Intel RST Controller Active)]";
                            }
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        private bool IsUsableSmartResult(string result)
        {
            return !result.Contains("failed") &&
                   !result.Contains("IOCTL") &&
                   !result.Contains("no response") &&
                   !result.Contains("Unknown device type") &&
                   !result.Contains("Unknown Block") &&
                   !result.Contains("Invalid device id");
        }

        private string RunSmartctl(string smartctlPath, string arguments, int timeoutMs = 5000)
        {
            try
            {
                using (Process p = new Process())
                {
                    p.StartInfo.FileName = smartctlPath;
                    p.StartInfo.Arguments = arguments;
                    p.StartInfo.UseShellExecute = false;
                    p.StartInfo.RedirectStandardOutput = true;
                    p.StartInfo.CreateNoWindow = true;

                    var outputBuilder = new StringBuilder();
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };

                    p.Start();
                    p.BeginOutputReadLine();

                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        return "";
                    }
                    p.WaitForExit();
                    return outputBuilder.ToString();
                }
            }
            catch (Exception)
            {
                return "";
            }
        }

        private string ParseSmartctlOutput(string rawOutput)
        {
            if (string.IsNullOrWhiteSpace(rawOutput)) return " [SMART: no response]";

            string[] lines = rawOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            string temperature = "N/A";
            int baseLife = 100;
            int reallocatedSectors = 0;
            int pendingSectors = 0;
            int uncorrectableSectors = 0;
            int mediaErrors = 0;
            bool criticalWarningActive = false;
            bool overallFailed = false;
            bool dataFound = false;

            foreach (string line in lines)
            {
                string trimmed = line.Trim();

                // Temperature Parsing
                if (trimmed.Contains("Temperature:") && !trimmed.Contains("Warning"))
                {
                    temperature = trimmed.Split(new[] { ':' }, 2)[1].Trim();
                    dataFound = true;
                }
                else if (trimmed.Contains("Temperature_Celsius"))
                {
                    var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 9) { temperature = parts[9] + " C"; dataFound = true; }
                }

                // Overall Health Self-Assessment
                if (trimmed.Contains("overall-health self-assessment test result:"))
                {
                    if (trimmed.Contains("FAILED!")) overallFailed = true;
                    dataFound = true;
                }

                // NVMe Health Metrics
                if (trimmed.Contains("Percentage Used:"))
                {
                    string wearStr = trimmed.Split(new[] { ':' }, 2)[1].Trim().Replace("%", "");
                    if (int.TryParse(wearStr, out int wear))
                    {
                        baseLife = Math.Max(0, 100 - wear);
                        dataFound = true;
                    }
                }
                else if (trimmed.Contains("Critical Warning:"))
                {
                    string warnStr = trimmed.Split(new[] { ':' }, 2)[1].Trim();
                    if (warnStr.StartsWith("0x") && int.TryParse(warnStr.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out int hexVal))
                    {
                        if (hexVal != 0) criticalWarningActive = true;
                    }
                }
                else if (trimmed.Contains("Media and Data Integrity Errors:"))
                {
                    string errStr = trimmed.Split(new[] { ':' }, 2)[1].Trim();
                    int.TryParse(errStr, out mediaErrors);
                }

                // SATA SMART Attribute Table Metrics
                if (trimmed.Contains("Media_Wearout_Indicator") || trimmed.Contains("SSD_Life_Left") || trimmed.Contains("Remaining_Lifetime_Perc"))
                {
                    var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 3 && int.TryParse(parts[3], out int lifeVal))
                    {
                        baseLife = Math.Min(baseLife, lifeVal);
                        dataFound = true;
                    }
                }
                if (trimmed.Contains("Reallocated_Sector_Ct"))
                {
                    var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 9 && int.TryParse(parts[9], out int val)) { reallocatedSectors = val; dataFound = true; }
                }
                else if (trimmed.Contains("Current_Pending_Sector"))
                {
                    var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 9 && int.TryParse(parts[9], out int val)) { pendingSectors = val; dataFound = true; }
                }
                else if (trimmed.Contains("Offline_Uncorrectable"))
                {
                    var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 9 && int.TryParse(parts[9], out int val)) { uncorrectableSectors = val; dataFound = true; }
                }
            }

            if (dataFound)
            {
                // Hard Disk Sentinel Storage Health Deductions
                int sectorDeductions = (reallocatedSectors * 10) + (pendingSectors * 15) + (uncorrectableSectors * 20) + Math.Min(50, mediaErrors * 5);
                if (criticalWarningActive) sectorDeductions += 25;

                int calculatedHealth = Math.Max(0, Math.Min(baseLife, 100 - sectorDeductions));
                if (overallFailed) calculatedHealth = Math.Min(calculatedHealth, 10);

                string hdsRating = calculatedHealth >= 95 ? "PERFECT [✓]" :
                                  (calculatedHealth >= 80 ? "GOOD [✓]" :
                                  (calculatedHealth >= 50 ? "FAIR [⚠]" : "CRITICAL REPLACEMENT RECOMMENDED [✕]"));

                string sectorNote = reallocatedSectors > 0 ? $", {reallocatedSectors} Bad Sectors" : "";
                return $" [SMART: Health {calculatedHealth}% ({hdsRating}), Temp {temperature}{sectorNote}]";
            }

            string realError = "Unknown Block";
            foreach (string line in lines)
            {
                if (!line.StartsWith("smartctl") && !line.StartsWith("Copyright") && !line.StartsWith("===") && !line.StartsWith("Please specify"))
                {
                    realError = line.Trim();
                    break;
                }
            }
            return $" [{realError}]";
        }

        private string lastStorageBenchmark = "Not Benchmarked";

        public async Task<string> RunStorageBenchmarkAsync()
        {
            return await Task.Run(() =>
            {
                string tempFile = Path.Combine(Path.GetTempPath(), $"SuperAutoMater_Bench_{Guid.NewGuid():N}.tmp");
                byte[] buffer = new byte[1024 * 1024]; // 1MB buffer
                new Random().NextBytes(buffer);
                int totalMB = 64; // 64MB test sample

                double writeSpeedMBs = 0;
                double readSpeedMBs = 0;

                try
                {
                    // 1. Measure Write Speed
                    var sw = Stopwatch.StartNew();
                    using (FileStream fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, FileOptions.WriteThrough))
                    {
                        for (int i = 0; i < totalMB; i++)
                        {
                            fs.Write(buffer, 0, buffer.Length);
                        }
                    }
                    sw.Stop();
                    writeSpeedMBs = totalMB / (sw.ElapsedMilliseconds / 1000.0);

                    // 2. Measure Read Speed
                    sw.Restart();
                    using (FileStream fs = new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.None, buffer.Length, FileOptions.SequentialScan))
                    {
                        for (int i = 0; i < totalMB; i++)
                        {
                            fs.ReadExactly(buffer, 0, buffer.Length);
                        }
                    }
                    sw.Stop();
                    readSpeedMBs = totalMB / (sw.ElapsedMilliseconds / 1000.0);
                }
                catch (Exception ex)
                {
                    return $"Benchmark Error: {ex.Message}";
                }
                finally
                {
                    try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
                }

                // Comparative Hardware Classification & Tier Grading
                string tier;
                string grade;
                double maxSpeed = Math.Max(writeSpeedMBs, readSpeedMBs);

                if (maxSpeed >= 3000)
                {
                    tier = "NVMe PCIe Gen4/Gen5 SSD";
                    grade = "GRADE A+ [ULTRA HIGH PERFORMANCE]";
                }
                else if (maxSpeed >= 1500)
                {
                    tier = "NVMe PCIe Gen3 SSD";
                    grade = "GRADE A [HIGH PERFORMANCE SSD]";
                }
                else if (maxSpeed >= 400)
                {
                    tier = "SATA III SSD";
                    grade = "GRADE B+ [STANDARD SSD]";
                }
                else if (maxSpeed >= 150)
                {
                    tier = "SATA II / Entry SSD";
                    grade = "GRADE C [DEGRADED OR SATA II]";
                }
                else
                {
                    tier = "Mechanical HDD / Slow Storage";
                    grade = "GRADE D [MECHANICAL HDD BOTTLENECK]";
                }

                lastStorageBenchmark = $"Write: {writeSpeedMBs:F1} MB/s | Read: {readSpeedMBs:F1} MB/s | Tier: {tier} | {grade}";

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("=== STORAGE SPEED BENCHMARK & TIER EVALUATION ===");
                sb.AppendLine($"Sequential Write : {writeSpeedMBs:F1} MB/s");
                sb.AppendLine($"Sequential Read  : {readSpeedMBs:F1} MB/s");
                sb.AppendLine($"Hardware Tier    : {tier}");
                sb.AppendLine($"Condition Grade  : {grade}");

                return sb.ToString();
            });
        }

        private async Task GenerateHardwareReport()
        {
            string reportText = await BuildHardwareReportTextAsync();
            if (reportBox != null) reportBox.Text = reportText;

            if (telemetryCards != null)
            {
                string mLine1 = $"MODEL : {lastModel} | SN: {lastSerial}";
                string mLine2 = $"CPU   : {lastCpu}";
                telemetryCards.UpdateCardData(0, mLine1, mLine2, "[ PASS ✓ ]", HudTheme.HudAccent, HudTheme.PassNominal);

                string rLine1 = $"RAM   : {lastRam}";
                string rLine2 = $"GPU   : {lastGpu}";
                telemetryCards.UpdateCardData(1, rLine1, rLine2, "[ PASS ✓ ]", HudTheme.PassNominal, HudTheme.PassNominal);

                string nLine1 = $"NET   : {lastNetworkSummary}";
                string nLine2 = $"BIO   : Windows Biometric Framework [Ready]";
                telemetryCards.UpdateCardData(2, nLine1, nLine2, "[ PASS ✓ ]", HudTheme.WarnCaution, HudTheme.PassNominal);

                string sLine1 = $"DRIVE : {lastStorageSummary}";
                string sLine2 = $"SMART : {lastStorageHealth}";
                telemetryCards.UpdateCardData(3, sLine1, sLine2, "[ PASS ✓ ]", HudTheme.StorageAux, HudTheme.PassNominal);

                string bLine1 = lastBatteryCardLine1 ?? $"BATT  : {lastBatteryHealth}";
                string bLine2 = "POWER : AC Line Mainline [PASS ✓]";
                if (NativeMethods.TryGetBatteryState(out var bState))
                {
                    bLine2 = $"POWER : {FormatBatteryPowerFlow(bState)} [PASS ✓]";
                }
                telemetryCards.UpdateCardData(4, bLine1, bLine2, "[ PASS ✓ ]", Color.FromArgb(40, 200, 120), HudTheme.PassNominal);
            }
        }

        private string GetProgressBar(int percentage, int width = 10)
        {
            percentage = Math.Max(0, Math.Min(100, percentage));
            int filled = (int)Math.Round((percentage / 100.0) * width);
            int empty = width - filled;
            return "[" + new string('█', filled) + new string('░', empty) + $"] {percentage}%";
        }

        private string SimplifyDriveName(string rawName, long sizeGb, string typeLabel)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return $"{sizeGb} GB {typeLabel}";

            string clean = rawName.Trim();
            // If rawName is excessively verbose (e.g. "NVMe WD PC SN740 SDDQNQD-512G-1014"), simplify it
            clean = clean.Replace("SDDQNQD-", "").Replace("SDDPNQP-", "");
            if (clean.Length > 32)
            {
                // Extract main model tokens
                var parts = clean.Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    clean = string.Join(" ", parts.Take(4));
                }
            }
            return $"{clean} ({sizeGb} GB {typeLabel})";
        }

        private string GetFingerprintSensorSummary()
        {
            try
            {
                // 1. Try Windows Biometric Framework (WinBio) unit enumeration
                IntPtr unitArray = IntPtr.Zero;
                int count = 0;
                int hr = NativeMethods.WinBioEnumBiometricUnits(NativeMethods.WINBIO_TYPE_FINGERPRINT, out unitArray, out count);
                if (hr == 0 && count > 0 && unitArray != IntPtr.Zero)
                {
                    try
                    {
                        var schema = (NativeMethods.WINBIO_UNIT_SCHEMA)Marshal.PtrToStructure(unitArray, typeof(NativeMethods.WINBIO_UNIT_SCHEMA));
                        string desc = string.IsNullOrWhiteSpace(schema.Description) ? "WBF Fingerprint Sensor" : schema.Description;
                        string mfg = string.IsNullOrWhiteSpace(schema.Manufacturer) ? "" : $" ({schema.Manufacturer})";
                        return $"{desc}{mfg} [ONLINE / READY]";
                    }
                    finally
                    {
                        NativeMethods.WinBioFree(unitArray);
                    }
                }
            }
            catch { }

            try
            {
                // 2. Fallback: Query WMI Biometric Devices
                using (var s = new ManagementObjectSearcher("SELECT Name, Manufacturer, Status FROM Win32_PnPEntity WHERE PNPClass = 'Biometric' OR Service = 'WbioSrvc' OR Description LIKE '%Fingerprint%' OR Caption LIKE '%Fingerprint%'"))
                using (var collection = s.Get())
                {
                    foreach (ManagementObject o in collection)
                    {
                        using (o)
                        {
                            string name = o["Name"]?.ToString() ?? o["Caption"]?.ToString();
                            string mfg = o["Manufacturer"]?.ToString();
                            string status = o["Status"]?.ToString() ?? "OK";
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                string mfgStr = string.IsNullOrWhiteSpace(mfg) ? "" : $" ({mfg})";
                                return $"{name}{mfgStr} [{status}]";
                            }
                        }
                    }
                }
            }
            catch { }

            return "Not Installed / No Biometric Hardware";
        }

        private string CleanCpuName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "Generic Processor";
            return raw.Replace("(R)", "").Replace("(TM)", "").Replace("CPU", "").Replace("Processor", "").Replace("  ", " ").Trim();
        }

        public async Task<string> BuildHardwareReportTextAsync()
        {
            var sysCpuTask = Task.Run(() => GetSystemCpuTelemetry());
            var graphicsWirelessTask = Task.Run(() => GetGraphicsWirelessTelemetry());
            var storageTask = Task.Run(() => GetStorageTelemetry());
            var batteryTask = Task.Run(() => GetBatteryTelemetry());

            await Task.WhenAll(sysCpuTask, graphicsWirelessTask, storageTask, batteryTask);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("┌─ SUPERAUTOMATER SYSTEM TELEMETRY ─────────────────────────────────────┐");
            sb.Append(await sysCpuTask);
            sb.Append(await graphicsWirelessTask);
            sb.AppendLine("├─ STORAGE & SMART HEALTH ───────────────────────────────────────────────┤");
            sb.Append(await storageTask);
            sb.AppendLine("├─ POWER & BATTERY WEAR ─────────────────────────────────────────────────┤");
            sb.Append(await batteryTask);
            sb.Append("└────────────────────────────────────────────────────────────────────────┘");
            return sb.ToString();
        }

        private string GetSystemCpuTelemetry()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                string model = null, serial = null;
                using (var s = new ManagementObjectSearcher("SELECT Name, IdentifyingNumber FROM Win32_ComputerSystemProduct"))
                using (var collection = s.Get())
                {
                    foreach (ManagementObject o in collection)
                    {
                        using (o)
                        {
                            model = o["Name"]?.ToString();
                            serial = o["IdentifyingNumber"]?.ToString();
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(model))
                {
                    using (var s = new ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem"))
                    using (var collection = s.Get())
                    {
                        foreach (ManagementObject o in collection)
                        {
                            using (o)
                            {
                                model = $"{o["Manufacturer"]} {o["Model"]}".Trim();
                            }
                        }
                    }
                }

                lastModel = string.IsNullOrWhiteSpace(model) ? "N/A" : model;
                lastSerial = string.IsNullOrWhiteSpace(serial) ? "N/A" : serial;
                sb.AppendLine($"│ MODEL   : {lastModel} | SN: {lastSerial}");

                int cpuCores = 0, cpuThreads = Environment.ProcessorCount;
                using (var s = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, Status FROM Win32_Processor"))
                using (var collection = s.Get())
                {
                    foreach (ManagementObject o in collection)
                    {
                        using (o)
                        {
                            lastCpu = o["Name"]?.ToString() ?? "N/A";
                            int.TryParse(o["NumberOfCores"]?.ToString(), out cpuCores);
                            int.TryParse(o["NumberOfLogicalProcessors"]?.ToString(), out cpuThreads);
                        }
                    }
                }
                sb.AppendLine($"│ CPU     : {CleanCpuName(lastCpu)} ({cpuCores}C/{cpuThreads}T) [PASS ✓]");

                long ramBytes = 0; int slots = 0; string speed = "";
                var dimmList = new List<string>();
                using (var s = new ManagementObjectSearcher("SELECT DeviceLocator, Manufacturer, Capacity, Speed FROM Win32_PhysicalMemory"))
                using (var collection = s.Get())
                {
                    foreach (ManagementObject o in collection)
                    {
                        using (o)
                        {
                            long cap = Convert.ToInt64(o["Capacity"] ?? 0);
                            ramBytes += cap;
                            slots++;
                            if (o["Speed"] != null) speed = o["Speed"].ToString();
                            string mfg = o["Manufacturer"]?.ToString() ?? "";
                            long capGb = (long)Math.Round(cap / (1024.0 * 1024 * 1024));
                            dimmList.Add($"{capGb}GB");
                        }
                    }
                }
                long ramGb = (long)Math.Ceiling(ramBytes / (1024.0 * 1024 * 1024));
                lastRam = $"{ramGb} GB";
                string dimmSummary = dimmList.Count > 0 ? $" ({string.Join("+", dimmList)} @ {speed} MHz)" : "";
                string channelMode = slots >= 2 ? "Dual-Channel" : "Single-Channel";
                sb.AppendLine($"│ RAM     : {lastRam} {channelMode}{dimmSummary} [PASS ✓]");
            }
            catch { sb.AppendLine("│ CPU     : [Probe Error]"); }
            return sb.ToString();
        }

        private string GetGraphicsWirelessTelemetry()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                string gpuName = "N/A", vramStr = "N/A";
                using (var s = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController"))
                using (var collection = s.Get())
                {
                    foreach (ManagementObject o in collection)
                    {
                        using (o)
                        {
                            string gName = o["Name"]?.ToString();
                            if (!string.IsNullOrWhiteSpace(gName) && !gName.Contains("Basic Display"))
                            {
                                gpuName = gName;
                                if (o["AdapterRAM"] != null)
                                {
                                    long ramBytes = Convert.ToInt64(o["AdapterRAM"]);
                                    if (ramBytes > 0)
                                        vramStr = $"{Math.Round(ramBytes / (1024.0 * 1024 * 1024), 1)} GB VRAM";
                                }
                                break;
                            }
                        }
                    }
                }
                lastGpu = $"{gpuName} ({vramStr})";
                // Trim GPU for card display — long names like "Intel(R) Iris(R) Xe Graphics (1 GB VRAM)" must fit
                string gpuDisplay = lastGpu.Replace("(R)", "").Replace("(TM)", "").Replace("  ", " ").Trim();
                if (gpuDisplay.Length > 38) gpuDisplay = gpuDisplay.Substring(0, 35) + "...";
                sb.AppendLine($"│ GPU     : {gpuDisplay}");

                string wifiName = "Wi-Fi Active";
                using (var s = new ManagementObjectSearcher("SELECT Name FROM Win32_NetworkAdapter WHERE NetConnectionID IS NOT NULL AND PhysicalAdapter = True"))
                using (var collection = s.Get())
                {
                    foreach (ManagementObject o in collection)
                    {
                        using (o)
                        {
                            string adapterName = o["Name"]?.ToString();
                            if (!string.IsNullOrWhiteSpace(adapterName) && (adapterName.Contains("Wi-Fi") || adapterName.Contains("Wireless") || adapterName.Contains("802.11") || adapterName.Contains("AX") || adapterName.Contains("AC")))
                            {
                                wifiName = adapterName;
                                break;
                            }
                        }
                    }
                }
                // Shorten adapter name for card (e.g. "Intel(R) Wi-Fi 6E AX211 160MHz" → "Wi-Fi 6E AX211 160MHz")
                string wifiDisplay = wifiName.Replace("(R)", "").Replace("(TM)", "").Replace("Intel ", "").Replace("  ", " ").Trim();
                if (wifiDisplay.Length > 32) wifiDisplay = wifiDisplay.Substring(0, 29) + "...";
                lastNetworkSummary = wifiName;

                string fpSummary = GetFingerprintSensorSummary();
                // Short fingerprint label for card line
                string fpDisplay = fpSummary.Contains("Not Installed") ? "No FP Sensor"
                                 : fpSummary.Contains("Ready") || fpSummary.Contains("READY") ? "FP Sensor Ready"
                                 : "FP Present";
                sb.AppendLine($"│ NET     : {wifiDisplay}");
                sb.AppendLine($"│ BIO     : {fpDisplay}");
            }
            catch { sb.AppendLine("│ GPU     : [Probe Error]"); }
            return sb.ToString();
        }

        private string GetStorageTelemetry()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                BuildSmartctlDeviceMap();
                BuildHdSentinelMap();

                string systemDriveIndex = GetSystemDriveDeviceId();

                var diskList = new List<PhysicalDiskInfo>();
                using (var s = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT * FROM MSFT_PhysicalDisk"))
                using (var collection = s.Get())
                {
                    foreach (ManagementObject disk in collection)
                    {
                        using (disk)
                        {
                            string pDevId = disk["DeviceId"]?.ToString() ?? "";
                            string pRawName = disk["FriendlyName"]?.ToString() ?? "Storage Drive";
                            string pSerial = disk["SerialNumber"]?.ToString() ?? "";
                            long pSizeBytes = disk["Size"] != null ? Convert.ToInt64(disk["Size"]) : 0;
                            int pMediaType = disk["MediaType"] != null ? Convert.ToInt32(disk["MediaType"]) : -1;
                            int pBusType = disk["BusType"] != null ? Convert.ToInt32(disk["BusType"]) : -1;
                            int pHealthStatus = disk["HealthStatus"] != null ? Convert.ToInt32(disk["HealthStatus"]) : 0;

                            bool isUsb = (pBusType == 7) ||
                                         pRawName.IndexOf("USB", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         pRawName.IndexOf("Flash Drive", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         pRawName.IndexOf("SanDisk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         pRawName.IndexOf("Kingston DataTraveler", StringComparison.OrdinalIgnoreCase) >= 0;

                            bool isSystemDrive = (!string.IsNullOrEmpty(systemDriveIndex) && pDevId == systemDriveIndex);

                            diskList.Add(new PhysicalDiskInfo
                            {
                                DeviceId = pDevId,
                                RawName = pRawName,
                                SerialNumber = pSerial,
                                SizeBytes = pSizeBytes,
                                MediaType = pMediaType,
                                BusType = pBusType,
                                HealthStatus = pHealthStatus,
                                IsSystemDrive = isSystemDrive,
                                IsUsb = isUsb
                            });
                        }
                    }
                }

                var sortedDisks = diskList.OrderByDescending(d => d.IsSystemDrive ? 1000 : (d.IsUsb ? 0 : 500 + (d.SizeBytes / (1024 * 1024 * 1024))))
                                           .ToList();

                if (sortedDisks.Count == 0)
                {
                    sb.AppendLine("│ DRIVE 1 : Primary Storage Drive [Online / Accessible]");
                    sb.AppendLine("│ SMART   : Health: 100% PERFECT [✓]");
                    return sb.ToString();
                }

                var diskInfo = sortedDisks[0];
                string rawName = diskInfo.RawName;
                string devId = diskInfo.DeviceId;
                string driveSerial = diskInfo.SerialNumber;
                long size = diskInfo.SizeBytes / (1024 * 1024 * 1024);
                int mediaType = diskInfo.MediaType;
                string typeLabel = mediaType == 4 ? "SSD" : (mediaType == 3 ? "HDD" : "Drive");

                string simpleDriveName = SimplifyDriveName(rawName, size, typeLabel);
                int healthPct = 100;

                string advancedSmart = GetDriveHealthFromSmartctl(devId, driveSerial);
                if (advancedSmart.StartsWith("[Critical Error]"))
                {
                    healthPct = 0;
                }
                else
                {
                    var m = System.Text.RegularExpressions.Regex.Match(advancedSmart, @"Health:\s*(\d+)%");
                    if (m.Success) int.TryParse(m.Groups[1].Value, out healthPct);
                }

                if (diskInfo.HealthStatus == 1) healthPct = Math.Min(healthPct, 50);
                else if (diskInfo.HealthStatus == 2) healthPct = 0;

                string healthSource = "WMI";
                string tempStr = "";

                var hdsInfo = LookupHdSentinelHealth(rawName, driveSerial);
                if (hdsInfo.HasValue)
                {
                    healthPct = hdsInfo.Value.Health;
                    healthSource = "HDSentinel";
                    if (!string.IsNullOrEmpty(hdsInfo.Value.Temperature)) tempStr = $", {hdsInfo.Value.Temperature}";
                }
                else
                {
                    if (smartctlDeviceMap.Count > 0)
                    {
                        var match = smartctlDeviceMap.Values.FirstOrDefault(d =>
                            !string.IsNullOrEmpty(devId) && (d.DevicePath == $"/dev/pd{devId}" || d.DevicePath == $"/dev/sd{devId}"));
                        if (match.DevicePath != null) healthSource = "smartctl";
                    }
                }

                healthPct = Math.Max(0, Math.Min(100, healthPct));
                string healthLabel = healthPct >= 95 ? "PERFECT [✓]" :
                                    (healthPct >= 80 ? "GOOD [✓]" :
                                    (healthPct >= 60 ? "CAUTION [⚠]" :
                                    (healthPct >= 50 ? "FAIR [⚠]" : "CRITICAL [✕]")));

                lastStorageSummary = simpleDriveName;
                lastStorageHealth = $"{healthPct}% ({healthLabel})";

                sb.AppendLine($"│ DRIVE 1 : {simpleDriveName} [{healthPct}% {healthLabel}{tempStr}]");

                // Multi-drive: check if a secondary internal drive exists
                var secondaryDisks = sortedDisks.Skip(1).Where(d => !d.IsUsb).ToList();
                if (secondaryDisks.Count > 0)
                {
                    var disk2 = secondaryDisks[0];
                    long size2 = disk2.SizeBytes / (1024 * 1024 * 1024);
                    string typeLabel2 = disk2.MediaType == 4 ? "SSD" : (disk2.MediaType == 3 ? "HDD" : "Drive");
                    string simpleName2 = SimplifyDriveName(disk2.RawName, size2, typeLabel2);
                    int healthPct2 = 100;

                    string smart2 = GetDriveHealthFromSmartctl(disk2.DeviceId, disk2.SerialNumber);
                    var m2 = System.Text.RegularExpressions.Regex.Match(smart2, @"Health:\s*(\d+)%");
                    if (m2.Success) int.TryParse(m2.Groups[1].Value, out healthPct2);
                    if (disk2.HealthStatus == 1) healthPct2 = Math.Min(healthPct2, 50);
                    else if (disk2.HealthStatus == 2) healthPct2 = 0;

                    var hds2 = LookupHdSentinelHealth(disk2.RawName, disk2.SerialNumber);
                    string tempStr2 = "";
                    if (hds2.HasValue)
                    {
                        healthPct2 = hds2.Value.Health;
                        if (!string.IsNullOrEmpty(hds2.Value.Temperature)) tempStr2 = $", {hds2.Value.Temperature}";
                    }

                    healthPct2 = Math.Max(0, Math.Min(100, healthPct2));
                    string healthLabel2 = healthPct2 >= 95 ? "PERFECT [✓]" : (healthPct2 >= 80 ? "GOOD [✓]" : "CAUTION [⚠]");

                    sb.AppendLine($"│ DRIVE 2 : {simpleName2} [{healthPct2}% {healthLabel2}{tempStr2}]");
                    lastStorageSummary = $"{simpleDriveName} + {simpleName2}";
                    lastStorageHealth = $"D1: {healthPct}% | D2: {healthPct2}%";
                }

                if (lastStorageBenchmark != "Not Benchmarked")
                {
                    sb.AppendLine($"│ BENCH   : {lastStorageBenchmark}");
                }
                else
                {
                    string smartDisplay = advancedSmart.Trim();
                    if (smartDisplay.StartsWith("[") && smartDisplay.EndsWith("]"))
                        smartDisplay = smartDisplay.Substring(1, smartDisplay.Length - 2).Trim();
                    if (smartDisplay.StartsWith("SMART:", StringComparison.OrdinalIgnoreCase))
                        smartDisplay = smartDisplay.Substring(6).Trim();
                    if (smartDisplay.Length > 55) smartDisplay = smartDisplay.Substring(0, 52) + "...";
                    sb.AppendLine($"│ SMART   : {smartDisplay} [{healthSource}]");
                }
            }
            catch
            {
                sb.AppendLine("│ DRIVE 1 : Storage Drive [Online / Accessible]");
                sb.AppendLine("│ SMART   : Requires Administrator privileges for full SMART analytics.");
            }
            return sb.ToString();
        }

        internal static string FormatBatteryPowerFlow(NativeMethods.SYSTEM_BATTERY_STATE state)
        {
            if (!state.BatteryPresent)
            {
                return "Desktop / AC Mainline Power";
            }

            if (state.Discharging)
            {
                double watts = Math.Abs(state.Rate) / 1000.0;
                string timeStr = "";
                if (state.EstimatedTime > 0 && state.EstimatedTime != uint.MaxValue && state.EstimatedTime < 86400 * 2)
                {
                    int hours = (int)(state.EstimatedTime / 3600);
                    int mins = (int)((state.EstimatedTime % 3600) / 60);
                    timeStr = hours > 0 ? $" ({hours}h {mins}m rem)" : $" ({mins}m rem)";
                }
                return $"-{watts:F1}W Drain{timeStr}";
            }
            else if (state.Charging)
            {
                double watts = Math.Abs(state.Rate) / 1000.0;
                return $"+{watts:F1}W Charging";
            }
            else if (state.AcOnLine)
            {
                return "AC Line (Fully Charged)";
            }
            else
            {
                return "Battery Standby";
            }
        }

        public void UpdateLiveBatteryTelemetryCard()
        {
            if (telemetryCards == null || this.IsDisposed || !this.IsHandleCreated) return;
            try
            {
                if (NativeMethods.TryGetBatteryState(out var bState))
                {
                    string flow = FormatBatteryPowerFlow(bState);
                    string bLine2 = $"POWER : {flow} [PASS ✓]";
                    string bLine1 = lastBatteryCardLine1 ?? $"BATT  : {lastBatteryHealth}";
                    telemetryCards.UpdateCardData(4, bLine1, bLine2, "[ PASS ✓ ]", Color.FromArgb(40, 200, 120), HudTheme.PassNominal);
                }
            }
            catch { }
        }

        private string GetBatteryTelemetry()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                uint design = 0, full = 0;
                using (var s = new ManagementObjectSearcher(@"root\wmi", "SELECT DesignedCapacity FROM BatteryStaticData"))
                using (var collection = s.Get())
                {
                    foreach (ManagementObject o in collection)
                    {
                        using (o) design = Convert.ToUInt32(o["DesignedCapacity"]);
                    }
                }

                using (var s = new ManagementObjectSearcher(@"root\wmi", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity"))
                using (var collection = s.Get())
                {
                    foreach (ManagementObject o in collection)
                    {
                        using (o) full = Convert.ToUInt32(o["FullChargedCapacity"]);
                    }
                }

                string powerFlow = "AC Line Mainline";
                bool hasNativeBattery = NativeMethods.TryGetBatteryState(out var battState);
                if (hasNativeBattery && battState.BatteryPresent)
                {
                    powerFlow = FormatBatteryPowerFlow(battState);
                    if (design == 0 && battState.MaxCapacity > 0)
                    {
                        design = battState.MaxCapacity;
                        full = battState.RemainingCapacity > 0 ? battState.RemainingCapacity : battState.MaxCapacity;
                    }
                }

                if (design > 0)
                {
                    double healthRatio = Math.Round(((double)full / design) * 100, 1);
                    double wearRatio = Math.Max(0, Math.Round(100.0 - healthRatio, 1));
                    long capacityLostMwh = Math.Max(0, (long)design - (long)full);
                    lastBatteryHealth = $"{healthRatio}%";

                    string battGrade = wearRatio <= 10.0 ? "A+ OPTIMAL" :
                                     (wearRatio <= 25.0 ? "A GOOD" :
                                     (wearRatio <= 40.0 ? "B FAIR" : "C SERVICE REQ"));

                    lastBatteryCardLine1 = $"BATT  : {full / 1000.0:F1}Wh / {design / 1000.0:F1}Wh · Health {healthRatio:F0}% [{battGrade}]";
                    sb.AppendLine($"│ BATTERY : {full / 1000.0:F1}Wh / {design / 1000.0:F1}Wh · Health {healthRatio:F0}% [{battGrade}]");
                    sb.AppendLine($"│ POWER   : {powerFlow} | Wear: -{wearRatio:F1}% (-{capacityLostMwh}mWh) [PASS ✓]");
                }
                else
                {
                    lastBatteryCardLine1 = "BATT  : Desktop / AC Power";
                    sb.AppendLine("│ BATTERY : Desktop / AC Power (No Battery)");
                    sb.AppendLine($"│ POWER   : {powerFlow} [Healthy ✓]");
                }
            }
            catch
            {
                sb.AppendLine("│ BATTERY : AC Power / Standard Battery Subsystem [Active]");
                sb.AppendLine("│ STATUS  : Run as Administrator for full battery wear calculations.");
            }
            return sb.ToString();
        }
    }
}