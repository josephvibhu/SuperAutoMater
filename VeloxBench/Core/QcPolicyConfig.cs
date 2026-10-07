using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SuperAutoMater.Wpf.Core
{
    public sealed class QcPolicy
    {
        public string PolicyId { get; set; } = "standard-refurb-v1";
        public string PolicyName { get; set; } = "Standard Refurbishment & ITAD Policy";
        public int Version { get; set; } = 1;
        public int MinBatteryHealthGradeA { get; set; } = 80;
        public int MinBatteryHealthGradeB { get; set; } = 60;
        public int MinStorageHealthPercent { get; set; } = 80;
        public List<string> RequiredTests { get; set; } = new List<string>
        {
            "Display",
            "Audio",
            "Camera",
            "Keyboard",
            "Cpu",
            "Battery",
            "Gpu",
            "Storage",
            "Usb",
            "Bluetooth"
        };
        public List<string> ConditionalTests { get; set; } = new List<string>
        {
            "Touchscreen",
            "Fingerprint"
        };
        public List<string> SupervisorRequiredOverrides { get; set; } = new List<string>
        {
            "Cpu",
            "Battery",
            "Storage"
        };

        public bool RequiresSupervisorApproval(string testKey)
        {
            if (string.IsNullOrWhiteSpace(testKey)) return false;
            foreach (var item in SupervisorRequiredOverrides)
            {
                if (string.Equals(item, testKey, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public string EvaluateGrade(int batteryHealth, int storageHealth, bool hasManualOverrides)
        {
            if (batteryHealth <= 0 || storageHealth <= 0) return "GRADE C"; // Incomplete/degraded metrics
            if (batteryHealth >= MinBatteryHealthGradeA && storageHealth >= MinStorageHealthPercent && !hasManualOverrides)
            {
                return "GRADE A";
            }
            if (batteryHealth >= MinBatteryHealthGradeB && storageHealth >= MinStorageHealthPercent)
            {
                return "GRADE B";
            }
            return "GRADE C";
        }

        public string ComputePolicyHash()
        {
            try
            {
                string json = JsonSerializer.Serialize(this);
                using var sha = SHA256.Create();
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
            catch
            {
                return "policy-hash-unknown";
            }
        }
    }

    public static class QcPolicyConfig
    {
        private static readonly object _lock = new object();
        private static QcPolicy _cachedPolicy;

        public static string PolicyFilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SuperAutoMater", "qc_policy.json");

        public static QcPolicy Load(string customPath = null)
        {
            lock (_lock)
            {
                if (_cachedPolicy != null && customPath == null)
                    return _cachedPolicy;

                string path = customPath ?? PolicyFilePath;
                try
                {
                    if (File.Exists(path))
                    {
                        string json = File.ReadAllText(path, Encoding.UTF8);
                        var loaded = JsonSerializer.Deserialize<QcPolicy>(json);
                        if (loaded != null)
                        {
                            if (customPath == null) _cachedPolicy = loaded;
                            return loaded;
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Failed to load qc_policy.json from {path}. Falling back to default.", ex);
                }

                var defaultPolicy = new QcPolicy();
                try
                {
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    string serialized = JsonSerializer.Serialize(defaultPolicy, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(path, serialized, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Failed to persist default qc_policy.json to {path}", ex);
                }

                if (customPath == null) _cachedPolicy = defaultPolicy;
                return defaultPolicy;
            }
        }

        public static void ResetCache()
        {
            lock (_lock)
            {
                _cachedPolicy = null;
            }
        }
    }
}
