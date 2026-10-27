using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using VeloxBench.Core;
using SuperAutoMater.Wpf.Core;
using SuperAutoMater.Wpf.Services;

namespace VeloxBench.Services;

public sealed class SiliconDnaResult
{
    public string SiliconDnaHash { get; set; } = "";
    public string HardwareQuorumJson { get; set; } = "{}";
    public string PermanentMac { get; set; } = "";
    public string CpuId { get; set; } = "";
    public string EdidHash { get; set; } = "";
    public string DisplayCode { get; set; } = "";
    public string BatterySerial { get; set; } = "";
    public int QuorumConfidenceScore { get; set; } = 0; // 0 - 100%
    public bool IsGenericMotherboard { get; set; } = false;
}

/// <summary>
/// Silicon DNA Quorum Deduplication Engine.
/// For generic motherboards reporting Serial: "Default String", "None", or "To be filled by O.E.M.",
/// generates a collision-resistant composite hardware quorum (Permanent MAC + CPU ID + EDID + Battery S/N).
/// Prevents duplicate license quota consumption across 30-day component upgrades and re-tests.
/// </summary>
public sealed class SiliconDnaService
{
    private static readonly Lazy<SiliconDnaService> _lazy = new(() => new SiliconDnaService());
    public static SiliconDnaService Instance => _lazy.Value;

    private SiliconDnaResult? _cachedResult;
    private readonly object _lock = new();

    private SiliconDnaService() { }

    public SiliconDnaResult GetSiliconDna(bool forceRefresh = false)
    {
        lock (_lock)
        {
            if (_cachedResult != null && !forceRefresh)
            {
                return _cachedResult;
            }

            var result = ProbeHardwareQuorum();
            _cachedResult = result;
            return result;
        }
    }

    private SiliconDnaResult ProbeHardwareQuorum()
    {
        var result = new SiliconDnaResult();
        int confidenceSignals = 0;

        // 1. Probe Permanent Physical MAC Address
        string mac = ProbePermanentMac();
        if (!string.IsNullOrWhiteSpace(mac) && mac != "00:00:00:00:00:00")
        {
            result.PermanentMac = mac;
            confidenceSignals += 35; // Primary stable anchor
        }
        else
        {
            result.PermanentMac = "MAC-UNAVAILABLE";
        }

        // 2. Probe CPU Silicon ProcessorId & Model
        string cpuId = ProbeCpuSiliconId();
        if (!string.IsNullOrWhiteSpace(cpuId) && cpuId != "UNKNOWN_CPU")
        {
            result.CpuId = cpuId;
            confidenceSignals += 25;
        }
        else
        {
            result.CpuId = "CPU-GENERIC";
        }

        // 3. Probe Display Panel EDID Hardware Signature
        var (edidHash, displayCode) = ProbeDisplayEdid();
        result.EdidHash = edidHash;
        result.DisplayCode = displayCode;
        if (!string.IsNullOrWhiteSpace(edidHash) && edidHash != "NO_EDID")
        {
            confidenceSignals += 25;
        }

        // 4. Probe Battery Serial Number
        string batterySn = ProbeBatterySerial();
        result.BatterySerial = batterySn;
        if (!string.IsNullOrWhiteSpace(batterySn) && batterySn != "NO_BATTERY" && batterySn != "GENERIC_BATTERY")
        {
            confidenceSignals += 15;
        }

        result.QuorumConfidenceScore = Math.Min(100, confidenceSignals);

        // Build Quorum Dictionary
        var quorumDict = new Dictionary<string, string>
        {
            ["mac"] = result.PermanentMac,
            ["cpu_id"] = result.CpuId,
            ["edid"] = result.EdidHash,
            ["display"] = result.DisplayCode,
            ["battery_sn"] = result.BatterySerial,
            ["confidence"] = $"{result.QuorumConfidenceScore}%"
        };
        result.HardwareQuorumJson = JsonSerializer.Serialize(quorumDict);

        // Compute Composite SHA-256 Hash
        string compositeSeed = $"{result.PermanentMac}|{result.CpuId}|{result.EdidHash}|{result.BatterySerial}";
        using var sha = SHA256.Create();
        byte[] hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(compositeSeed));
        string hex = Convert.ToHexString(hashBytes).ToUpperInvariant();
        result.SiliconDnaHash = "DNA-" + hex.Substring(0, 16);

        AppLogger.Info($"[SiliconDNA] Generated Quorum: {result.SiliconDnaHash} (Confidence: {result.QuorumConfidenceScore}%, MAC: {result.PermanentMac}, CPU: {result.CpuId}, EDID: {result.EdidHash})");

        return result;
    }

    private string ProbePermanentMac()
    {
        try
        {
            // Priority 1: Physical network adapters via WMI (PhysicalAdapter = True)
            var candidateMacs = new List<string>();
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT MACAddress, PermanentAddress, PhysicalAdapter, Name, Description FROM Win32_NetworkAdapter WHERE PhysicalAdapter = True");
                using var col = searcher.Get();
                foreach (ManagementObject obj in col)
                {
                    using (obj)
                    {
                        string desc = obj["Description"]?.ToString() ?? "";
                        if (IsVirtualAdapter(desc)) continue;

                        string perm = obj["PermanentAddress"]?.ToString()?.Trim() ?? "";
                        string mac = obj["MACAddress"]?.ToString()?.Trim() ?? "";

                        string chosen = !string.IsNullOrWhiteSpace(perm) ? perm : mac;
                        if (!string.IsNullOrWhiteSpace(chosen))
                        {
                            candidateMacs.Add(NormalizeMac(chosen));
                        }
                    }
                }
            }
            catch { }

            // Priority 2: NetworkInterface.GetAllNetworkInterfaces() fallback
            if (candidateMacs.Count == 0)
            {
                var nics = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var nic in nics)
                {
                    if (nic.NetworkInterfaceType != NetworkInterfaceType.Ethernet &&
                        nic.NetworkInterfaceType != NetworkInterfaceType.Wireless80211)
                        continue;

                    if (IsVirtualAdapter(nic.Description)) continue;

                    var addr = nic.GetPhysicalAddress().ToString();
                    if (!string.IsNullOrWhiteSpace(addr) && addr.Length == 12)
                    {
                        candidateMacs.Add(NormalizeMac(addr));
                    }
                }
            }

            if (candidateMacs.Count > 0)
            {
                // Sort to ensure determinism across reboots
                candidateMacs.Sort(StringComparer.OrdinalIgnoreCase);
                return candidateMacs[0];
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Failed to probe permanent MAC: {ex.Message}");
        }

        return "00:00:00:00:00:00";
    }

    private static bool IsVirtualAdapter(string desc)
    {
        if (string.IsNullOrWhiteSpace(desc)) return false;
        string d = desc.ToLowerInvariant();
        return d.Contains("virtual") ||
               d.Contains("hyper-v") ||
               d.Contains("wsl") ||
               d.Contains("vethernet") ||
               d.Contains("virtualbox") ||
               d.Contains("vmware") ||
               d.Contains("tap-") ||
               d.Contains("vpn") ||
               d.Contains("bluetooth") ||
               d.Contains("loopback");
    }

    private static string NormalizeMac(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        string clean = new string(raw.Where(c => char.IsLetterOrDigit(c)).ToArray()).ToUpperInvariant();
        if (clean.Length == 12)
        {
            return string.Join(":", Enumerable.Range(0, 6).Select(i => clean.Substring(i * 2, 2)));
        }
        return raw.ToUpperInvariant();
    }

    private string ProbeCpuSiliconId()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ProcessorId, Name, NumberOfCores, MaxClockSpeed FROM Win32_Processor");
            using var col = searcher.Get();
            foreach (ManagementObject obj in col)
            {
                using (obj)
                {
                    string procId = obj["ProcessorId"]?.ToString()?.Trim() ?? "";
                    string name = obj["Name"]?.ToString()?.Trim() ?? "";
                    string cores = obj["NumberOfCores"]?.ToString() ?? "";
                    string speed = obj["MaxClockSpeed"]?.ToString() ?? "";

                    if (!string.IsNullOrWhiteSpace(procId) && procId != "0000000000000000" && procId != "FFFFFFFFFFFFFFFF")
                    {
                        return procId;
                    }

                    // Fallback: CPU model + cores + speed hash
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        using var sha = SHA256.Create();
                        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes($"{name}-{cores}-{speed}"));
                        return "CPUID-" + Convert.ToHexString(hash).Substring(0, 12);
                    }
                }
                break;
            }
        }
        catch { }

        return "UNKNOWN_CPU";
    }

    private (string EdidHash, string DisplayCode) ProbeDisplayEdid()
    {
        try
        {
            // Read active display EDID from Windows Registry
            using var displayKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\DISPLAY");
            if (displayKey != null)
            {
                foreach (string subkeyName in displayKey.GetSubKeyNames())
                {
                    using var subkey = displayKey.OpenSubKey(subkeyName);
                    if (subkey == null) continue;

                    foreach (string childName in subkey.GetSubKeyNames())
                    {
                        using var devParams = subkey.OpenSubKey($@"{childName}\Device Parameters");
                        if (devParams != null)
                        {
                            var edid = devParams.GetValue("EDID") as byte[];
                            if (edid != null && edid.Length >= 128)
                            {
                                using var sha = SHA256.Create();
                                byte[] h = sha.ComputeHash(edid);
                                string edidHex = Convert.ToHexString(h).Substring(0, 16);
                                return ($"EDID-{edidHex}", subkeyName);
                            }
                        }
                    }
                }
            }

            // Fallback: Win32_DesktopMonitor
            using (var monSearcher = new ManagementObjectSearcher("SELECT PNPDeviceID FROM Win32_DesktopMonitor"))
            using (var mCol = monSearcher.Get())
            {
                foreach (ManagementObject m in mCol)
                {
                    using (m)
                    {
                        string pnp = m["PNPDeviceID"]?.ToString()?.Trim() ?? "";
                        if (!string.IsNullOrWhiteSpace(pnp))
                        {
                            using var sha = SHA256.Create();
                            var h = sha.ComputeHash(Encoding.UTF8.GetBytes(pnp));
                            return ($"MON-{Convert.ToHexString(h).Substring(0, 12)}", pnp);
                        }
                    }
                }
            }
        }
        catch { }

        return ("NO_EDID", "GENERIC_DISPLAY");
    }

    private string ProbeBatterySerial()
    {
        try
        {
            // Check HardwareDiagnosticsService telemetry first
            var bt = HardwareDiagnosticsService.Instance.BatteryTelemetry;
            if (bt != null && !string.IsNullOrWhiteSpace(bt.SerialNumber) && bt.SerialNumber != "N/A" && bt.SerialNumber != "27225")
            {
                return bt.SerialNumber.Trim();
            }

            // Probe Win32_Battery
            using var searcher = new ManagementObjectSearcher("SELECT DeviceID, Name FROM Win32_Battery");
            using var col = searcher.Get();
            foreach (ManagementObject obj in col)
            {
                using (obj)
                {
                    string devId = obj["DeviceID"]?.ToString()?.Trim() ?? "";
                    if (!string.IsNullOrWhiteSpace(devId) && devId.Length >= 4)
                    {
                        return devId;
                    }
                }
                break;
            }
        }
        catch { }

        return "NO_BATTERY";
    }
}
