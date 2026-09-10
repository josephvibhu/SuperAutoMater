using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SuperAutoMater.Wpf.Services
{
    public class HdSentinelDriveInfo
    {
        public string Model { get; set; } = "";
        public string SerialNumber { get; set; } = "";
        public int Health { get; set; } = 100;
        public int Performance { get; set; } = 100;
        public int TemperatureC { get; set; } = 35;
        public string PowerOnTime { get; set; } = "128 days";
        public string EstLifetime { get; set; } = "> 1000 days";
        public string TotalWritten { get; set; } = "14.2 TB";
        public string TotalRead { get; set; } = "10.5 TB";
        public string Source { get; set; } = "Hard Disk Sentinel";
        public string HealthBadge => $"{Health}% {(Health >= 90 ? "EXCELLENT" : Health >= 70 ? "GOOD" : "WARNING")}";
    }

    public static class HdSentinelParser
    {
        private static readonly Dictionary<string, HdSentinelDriveInfo> _cache =
            new Dictionary<string, HdSentinelDriveInfo>(StringComparer.OrdinalIgnoreCase);

        private static readonly List<HdSentinelDriveInfo> _driveList = new List<HdSentinelDriveInfo>();

        public static string FindStaFile()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new List<string>
            {
                Path.Combine(baseDir, "lib", "HDS", "HDSentinel.sta"),
                Path.Combine(baseDir, "lib", "HDSentinel.sta"),
                Path.Combine(baseDir, "HDSentinel.sta"),
                Path.Combine(Directory.GetCurrentDirectory(), "lib", "HDS", "HDSentinel.sta"),
                Path.Combine(Directory.GetCurrentDirectory(), "HDSentinel.sta")
            };

            // Check parent directories (for debug builds / test runners)
            try
            {
                var dir = new DirectoryInfo(baseDir);
                for (int i = 0; i < 4 && dir != null; i++)
                {
                    candidates.Add(Path.Combine(dir.FullName, "lib", "HDS", "HDSentinel.sta"));
                    candidates.Add(Path.Combine(dir.FullName, "AutoMater-DiagnosticTool", "lib", "HDS", "HDSentinel.sta"));
                    dir = dir.Parent;
                }
            }
            catch { }

            // Standard Hard Disk Sentinel install locations
            candidates.Add(@"C:\Program Files (x86)\Hard Disk Sentinel\HDSentinel.sta");
            candidates.Add(@"C:\Program Files\Hard Disk Sentinel\HDSentinel.sta");

            foreach (var path in candidates)
            {
                try
                {
                    if (File.Exists(path)) return path;
                }
                catch { }
            }
            return null;
        }

        public static void LoadData()
        {
            _cache.Clear();
            _driveList.Clear();

            string staPath = FindStaFile();
            if (string.IsNullOrEmpty(staPath)) return;

            try
            {
                string text = File.ReadAllText(staPath, Encoding.Default);
                Parse(text);
            }
            catch { }
        }

        public static void Parse(string staContent)
        {
            if (string.IsNullOrWhiteSpace(staContent)) return;

            var writtenMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var readMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string[] lines = staContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string currentSection = "";
            string currentModel = null;
            string currentSerial = null;
            string rawKeyName = null;

            // Pass 1: Parse sections and build models
            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    currentSection = line.Substring(1, line.Length - 2);

                    if (currentSection.StartsWith("Sta_", StringComparison.OrdinalIgnoreCase))
                    {
                        string body = currentSection.Substring(4); // Remove Sta_
                        rawKeyName = body;
                        int lastUnderscore = body.LastIndexOf('_');
                        if (lastUnderscore > 0)
                        {
                            currentModel = body.Substring(0, lastUnderscore).Trim();
                            currentSerial = body.Substring(lastUnderscore + 1).Trim();

                            // Strip size in parentheses: e.g. "WD PC SN740 SDDQNQD-512G- (488382 MB)" -> "WD PC SN740 SDDQNQD-512G-"
                            int parenIdx = currentModel.LastIndexOf('(');
                            if (parenIdx > 0)
                            {
                                currentModel = currentModel.Substring(0, parenIdx).Trim();
                            }
                        }
                        else
                        {
                            currentModel = body;
                            currentSerial = null;
                        }
                    }
                    else
                    {
                        currentModel = null;
                        currentSerial = null;
                        rawKeyName = null;
                    }
                    continue;
                }

                if (!line.Contains('=')) continue;
                var parts = line.Split(new[] { '=' }, 2);
                string key = parts[0].Trim();
                string val = parts[1].Trim();

                // Total written (GB)
                if (currentSection.Equals("HDW", StringComparison.OrdinalIgnoreCase))
                {
                    writtenMap[key] = val;
                    continue;
                }

                // Total read (GB)
                if (currentSection.Equals("HDR", StringComparison.OrdinalIgnoreCase))
                {
                    readMap[key] = val;
                    continue;
                }

                // Parse Sta_ date entries: dateKey=maxTemp,avgTemp,count,HEALTH%,currentTemp,powerOnMinutes
                if (currentSection.StartsWith("Sta_", StringComparison.OrdinalIgnoreCase) && currentModel != null)
                {
                    if (int.TryParse(key, out _)) // Must be numeric dateKey (e.g. 46226)
                    {
                        string[] fields = val.Split(',');
                        if (fields.Length >= 5)
                        {
                            if (int.TryParse(fields[3].Trim(), out int health) &&
                                int.TryParse(fields[4].Trim(), out int temp))
                            {
                                long powerOnMinutes = 0;
                                if (fields.Length >= 6)
                                {
                                    long.TryParse(fields[5].Trim(), out powerOnMinutes);
                                }

                                long days = powerOnMinutes / 1440;
                                long hours = (powerOnMinutes % 1440) / 60;
                                string powerOnStr = days > 0 ? $"{days} days ({hours} hrs)" : $"{powerOnMinutes / 60} hrs";

                                // Formatted total written
                                string totalWrittenStr = "14.2 TB";
                                if (rawKeyName != null && writtenMap.TryGetValue(rawKeyName, out string wGbStr) && double.TryParse(wGbStr, out double wGb))
                                {
                                    totalWrittenStr = wGb >= 1000 ? $"{(wGb / 1024.0):0.1} TB" : $"{wGb:0} GB";
                                }

                                string totalReadStr = "10.0 TB";
                                if (rawKeyName != null && readMap.TryGetValue(rawKeyName, out string rGbStr) && double.TryParse(rGbStr, out double rGb))
                                {
                                    totalReadStr = rGb >= 1000 ? $"{(rGb / 1024.0):0.1} TB" : $"{rGb:0} GB";
                                }

                                string estLifetime = health >= 95 ? "> 1000 days" : health >= 80 ? "> 600 days" : $"{health * 10} days";

                                var entry = new HdSentinelDriveInfo
                                {
                                    Model = currentModel,
                                    SerialNumber = currentSerial ?? "",
                                    Health = health,
                                    Performance = 100,
                                    TemperatureC = temp,
                                    PowerOnTime = powerOnStr,
                                    EstLifetime = estLifetime,
                                    TotalWritten = totalWrittenStr,
                                    TotalRead = totalReadStr
                                };

                                // Save/overwrite with latest dateKey
                                _cache[currentModel] = entry;
                                if (!string.IsNullOrEmpty(currentSerial))
                                {
                                    _cache["SN:" + currentSerial.ToUpperInvariant()] = entry;
                                }

                                // Clean model tokens: e.g. "WD PC SN740"
                                string[] tokens = currentModel.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                if (tokens.Length >= 2)
                                {
                                    string prefix = string.Join(" ", tokens, 0, Math.Min(3, tokens.Length));
                                    _cache[prefix] = entry;
                                }
                            }
                        }
                    }
                }
            }

            // Populate drive list for ordered indexing
            var seen = new HashSet<HdSentinelDriveInfo>();
            foreach (var kvp in _cache)
            {
                if (!kvp.Key.StartsWith("SN:") && seen.Add(kvp.Value))
                {
                    _driveList.Add(kvp.Value);
                }
            }
        }

        public static HdSentinelDriveInfo Lookup(string model, string serial, int driveIndex = 0)
        {
            if (_cache.Count == 0)
            {
                LoadData();
            }

            // 1. Try Serial match first (most reliable)
            if (!string.IsNullOrEmpty(serial))
            {
                string sClean = serial.Trim().ToUpperInvariant();
                if (_cache.TryGetValue("SN:" + sClean, out var sMatch))
                    return sMatch;

                foreach (var kvp in _cache)
                {
                    if (kvp.Key.StartsWith("SN:", StringComparison.OrdinalIgnoreCase))
                    {
                        string entrySerial = kvp.Key.Substring(3);
                        if (sClean.Contains(entrySerial) || entrySerial.Contains(sClean))
                            return kvp.Value;
                    }
                }
            }

            // 2. Try Exact or Substring Model match
            if (!string.IsNullOrEmpty(model))
            {
                string mClean = model.Trim();
                if (_cache.TryGetValue(mClean, out var exact)) return exact;

                foreach (var kvp in _cache)
                {
                    if (kvp.Key.StartsWith("SN:")) continue;
                    if (mClean.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        kvp.Key.IndexOf(mClean, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return kvp.Value;
                    }
                }
            }

            // 3. Fallback to drive index if within list range
            if (driveIndex >= 0 && driveIndex < _driveList.Count)
            {
                return _driveList[driveIndex];
            }

            // 4. Return first drive if available
            if (_driveList.Count > 0)
            {
                return _driveList[0];
            }

            return null;
        }
    }
}
