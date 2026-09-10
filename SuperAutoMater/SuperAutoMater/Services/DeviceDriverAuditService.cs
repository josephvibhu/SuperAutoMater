using System;
using System.Collections.Generic;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SuperAutoMater.Wpf.Services
{
    public class MissingDeviceEntry
    {
        public string DeviceName { get; set; } = "Unknown Device";
        public string DeviceId { get; set; } = "";
        public int ErrorCode { get; set; } = 0;
        public string ErrorDescription { get; set; } = "";
        public string HardwareId { get; set; } = "";
        public string Status { get; set; } = "Error";

        public string DisplayPill => $"⚠ [Code {ErrorCode}] {DeviceName} ({HardwareId})";
    }

    public class DriverAuditResult
    {
        public int TotalDevicesScanned { get; set; } = 0;
        public List<MissingDeviceEntry> MissingDevices { get; set; } = new List<MissingDeviceEntry>();
        public int MissingCount => MissingDevices.Count;
        public bool HasMissingDrivers => MissingCount > 0;

        public string SummaryText => HasMissingDrivers
            ? $"⚠ {MissingCount} HARDWARE DEVICE{(MissingCount > 1 ? "S" : "")} REQUIRE DRIVERS"
            : "✓ 0 MISSING DRIVERS (All PnP Controllers Online)";

        public string StatusBadge => HasMissingDrivers
            ? $"{MissingCount} MISSING DRIVERS"
            : "0 MISSING DRIVERS";

        public string AccentHex => HasMissingDrivers ? "#D29922" : "#3FB950";
    }

    public class DeviceDriverAuditService
    {
        private static readonly Lazy<DeviceDriverAuditService> _instance =
            new Lazy<DeviceDriverAuditService>(() => new DeviceDriverAuditService());
        public static DeviceDriverAuditService Instance => _instance.Value;

        private DriverAuditResult _lastResult = new DriverAuditResult();
        public DriverAuditResult LastResult => _lastResult;

        private DeviceDriverAuditService() { }

        public static string GetErrorDescription(int code)
        {
            return code switch
            {
                1 => "Code 1: Device is not configured correctly",
                10 => "Code 10: Device cannot start (Hardware/Firmware error)",
                14 => "Code 14: Device requires computer restart",
                18 => "Code 18: Reinstall drivers for this device",
                19 => "Code 19: Registry information is incomplete/damaged",
                21 => "Code 21: Windows is removing this device",
                22 => "Code 22: Device is disabled in Device Manager",
                28 => "Code 28: Driver not installed (Yellow Bang)",
                31 => "Code 31: Device is not working properly (Driver load failure)",
                32 => "Code 32: Driver for this device has been disabled",
                37 => "Code 37: Driver failed initialization code",
                38 => "Code 38: Previous instance of driver still loaded",
                39 => "Code 39: Driver corrupt or missing binary",
                43 => "Code 43: Windows stopped device because it reported problems",
                48 => "Code 48: Driver software blocked from starting",
                52 => "Code 52: Unsigned driver blocked by Windows integrity",
                _ => $"Code {code}: Configuration issue"
            };
        }

        public static string ExtractHardwareId(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return "UNKNOWN_HWID";

            var match = Regex.Match(deviceId, @"(VEN_[A-F0-9]{4}&DEV_[A-F0-9]{4}|VID_[A-F0-9]{4}&PID_[A-F0-9]{4}|ACPI\\[A-Z0-9_]+)", RegexOptions.IgnoreCase);
            if (match.Success) return match.Groups[1].Value.ToUpperInvariant();

            int firstSlash = deviceId.IndexOf('\\');
            if (firstSlash >= 0)
            {
                int secondSlash = deviceId.IndexOf('\\', firstSlash + 1);
                if (secondSlash > firstSlash)
                {
                    return deviceId.Substring(firstSlash + 1, secondSlash - firstSlash - 1);
                }
            }

            return deviceId.Length > 24 ? deviceId.Substring(0, 24) : deviceId;
        }

        public async Task<DriverAuditResult> RunAuditAsync()
        {
            return await Task.Run(() =>
            {
                var result = new DriverAuditResult();

                try
                {
                    // Fast indexed WQL query on Win32_PnPEntity
                    using var searcher = new ManagementObjectSearcher(
                        "SELECT Name, DeviceID, ConfigManagerErrorCode, Status FROM Win32_PnPEntity WHERE ConfigManagerErrorCode > 0");

                    foreach (ManagementObject obj in searcher.Get())
                    {
                        try
                        {
                            int errCode = Convert.ToInt32(obj["ConfigManagerErrorCode"]);
                            // Code 22 is simply user-disabled devices, omit unless critical
                            if (errCode == 22) continue;

                            string name = obj["Name"]?.ToString() ?? "Unknown PnP Device";
                            string devId = obj["DeviceID"]?.ToString() ?? "";
                            string status = obj["Status"]?.ToString() ?? "Error";

                            string desc = GetErrorDescription(errCode);
                            string hwId = ExtractHardwareId(devId);

                            result.MissingDevices.Add(new MissingDeviceEntry
                            {
                                DeviceName = name,
                                DeviceId = devId,
                                ErrorCode = errCode,
                                ErrorDescription = desc,
                                HardwareId = hwId,
                                Status = status
                            });
                        }
                        catch { }
                    }
                }
                catch { }

                _lastResult = result;
                return result;
            });
        }
    }
}
