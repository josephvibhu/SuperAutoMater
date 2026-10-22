using System;
using System.IO;
using System.Management;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SuperAutoMater.Wpf.Core;
using Velox.Shared.Common;
using Velox.Shared.Dtos;
using VeloxBench.Core;

namespace VeloxBench.Services;

public sealed class LicenseService
{
    private static readonly Lazy<LicenseService> _lazy = new(() => new LicenseService());
    public static LicenseService Instance => _lazy.Value;

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public bool IsLicensed { get; private set; }
    public bool IsOfflineLeaseValid { get; private set; }
    public string LicenseKey { get; private set; } = "";
    public string CompanyName { get; private set; } = "Unlicensed";
    public string PlanTier { get; private set; } = "None";
    public int DaysRemaining { get; private set; }
    public int DevicesUsedMonth { get; private set; }
    public int MaxDevicesMonth { get; private set; }
    public string StatusMessage { get; private set; } = "Initializing license...";
    public VeloxErrorCode LastErrorCode { get; private set; } = VeloxErrorCode.OK_SUCCESS;

    public event Action? LicenseStatusChanged;

    public LicenseService()
    {
    }

    public static string GetExecutableDirectory()
    {
        var procPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(procPath))
        {
            var dir = Path.GetDirectoryName(procPath);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                return dir;
        }
        return AppDomain.CurrentDomain.BaseDirectory;
    }

    public static string GetPortableKeyPath() =>
        Path.Combine(GetExecutableDirectory(), "license.key");

    public static string GetLeasePath()
    {
        var exeDir = GetExecutableDirectory();
        var portableLease = Path.Combine(exeDir, "license.lease");

        // If portable lease already exists, always use it
        if (File.Exists(portableLease))
            return portableLease;

        // Test if directory is writable (e.g. USB flash drive or depot folder)
        try
        {
            var testFile = Path.Combine(exeDir, $".lease_test_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            return portableLease;
        }
        catch
        {
            // Fall back to LocalAppData if directory is read-only
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Velox", "VeloxBench", "license.lease");
        }
    }

    public string DiscoverLicenseKey()
    {
        // 1. Check license.key beside the portable executable (USB stick portable mode)
        try
        {
            var keyPath = GetPortableKeyPath();
            if (File.Exists(keyPath))
            {
                var key = File.ReadAllText(keyPath).Trim();
                if (!string.IsNullOrWhiteSpace(key))
                {
                    AppLogger.Info("License", $"Discovered portable license key file at {keyPath}");
                    return key;
                }
            }
        }
        catch { }

        // 2. Check BenchConfig
        var cfg = BenchConfigService.Current;
        if (!string.IsNullOrWhiteSpace(cfg.LicenseKey))
        {
            return cfg.LicenseKey.Trim();
        }

        return "VLX-ALPHA-TEST-2026";
    }

    public async Task<bool> InitializeAndValidateAsync()
    {
        var key = DiscoverLicenseKey();
        var result = await ActivateKeyAsync(key, persistToDisk: false);
        return result.Success;
    }

    public async Task<VeloxResult> ActivateKeyAsync(string newKey, bool persistToDisk = true)
    {
        if (string.IsNullOrWhiteSpace(newKey))
        {
            IsLicensed = false;
            LastErrorCode = VeloxErrorCode.ERR_LIC_NOT_FOUND;
            StatusMessage = "No activation key provided.";
            LicenseStatusChanged?.Invoke();
            return VeloxResult.Fail(VeloxErrorCode.ERR_LIC_NOT_FOUND, StatusMessage);
        }

        var key = newKey.Trim();
        LicenseKey = key;

        var cfg = BenchConfigService.Current;
        var request = new LicenseValidateRequest
        {
            LicenseKey = key,
            MachineName = Environment.MachineName,
            MachineGuid = GetMachineIdentifier(),
            AppVersion = "1.6.10"
        };

        // ── 1. Attempt Online Validation with Velox Cloud ───────────────────────
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var url = $"{cfg.ApiUrl.TrimEnd('/')}/api/v1/license/validate";
            var resp = await _http.PostAsync(url, content, cts.Token);
            var body = await resp.Content.ReadAsStringAsync(cts.Token);

            if (resp.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<LicenseValidateResponse>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (result != null && result.IsValid)
                {
                    IsLicensed = true;
                    IsOfflineLeaseValid = false;
                    CompanyName = result.CompanyName;
                    PlanTier = result.PlanTier;
                    DaysRemaining = result.DaysRemaining;
                    DevicesUsedMonth = result.DevicesUsedMonth;
                    MaxDevicesMonth = result.MaxDevicesMonth;
                    LastErrorCode = VeloxErrorCode.OK_SUCCESS;
                    StatusMessage = $"System Verified: {CompanyName} — {DaysRemaining} days remaining.";

                    // Cache verified signed lease for offline usage
                    SaveOfflineLease(result.SignedLeaseToken);

                    // Persist license key to disk if requested
                    if (persistToDisk)
                    {
                        PersistLicenseKey(key);
                    }

                    AppLogger.Info("License", $"[VLX-OK-200] Online license validated: {StatusMessage}");
                    LicenseStatusChanged?.Invoke();
                    return VeloxResult.Ok(StatusMessage);
                }
            }
            else
            {
                // Server rejected license (e.g. 403 Forbidden or 401 Unauthorized)
                string failMsg;
                try
                {
                    var failResult = JsonSerializer.Deserialize<LicenseValidateResponse>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    failMsg = failResult?.Message ?? $"License validation rejected by cloud (HTTP {resp.StatusCode}).";
                }
                catch
                {
                    failMsg = $"Cloud returned license rejection (HTTP {resp.StatusCode}).";
                }

                IsLicensed = false;
                LastErrorCode = resp.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    ? VeloxErrorCode.ERR_AUTH_INVALID_KEY
                    : VeloxErrorCode.ERR_AUTH_FORBIDDEN;
                StatusMessage = failMsg;

                AppLogger.Warn("License", $"[{LastErrorCode}] License rejected: {StatusMessage}");
                LicenseStatusChanged?.Invoke();
                return VeloxResult.Fail(LastErrorCode, StatusMessage, body);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("License", $"[VLX-NET-100] Online validation unreachable: {ex.Message}. Checking offline lease...");
        }

        // ── 2. Fallback: Verify Cryptographic Offline Lease (14-Day Grace) ────
        if (VerifyOfflineLease(key))
        {
            IsLicensed = true;
            IsOfflineLeaseValid = true;
            LastErrorCode = VeloxErrorCode.OK_SUCCESS;

            if (persistToDisk)
            {
                PersistLicenseKey(key);
            }

            LicenseStatusChanged?.Invoke();
            return VeloxResult.Ok($"Verified via Offline Grace ({CompanyName})");
        }

        IsLicensed = false;
        LastErrorCode = VeloxErrorCode.ERR_NET_UNREACHABLE;
        StatusMessage = "Authorization could not be verified online and no valid offline cache exists.";
        LicenseStatusChanged?.Invoke();
        return VeloxResult.Fail(LastErrorCode, StatusMessage);
    }

    private void PersistLicenseKey(string key)
    {
        try
        {
            // 1. Write to portable license.key beside the executable
            var portablePath = GetPortableKeyPath();
            File.WriteAllText(portablePath, key.Trim());
            AppLogger.Info("License", $"Persisted license key to {portablePath}");
        }
        catch { }

        try
        {
            // 2. Update config.json
            var cfg = BenchConfigService.Current;
            cfg.LicenseKey = key.Trim();
            BenchConfigService.Save(cfg);
        }
        catch { }
    }

    private void SaveOfflineLease(string signedToken)
    {
        if (string.IsNullOrWhiteSpace(signedToken)) return;
        try
        {
            var leasePath = GetLeasePath();
            var dir = Path.GetDirectoryName(leasePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(leasePath, signedToken.Trim());
            AppLogger.Info("License", $"Cached offline cryptographic lease to {leasePath}");
        }
        catch { }
    }

    private bool VerifyOfflineLease(string expectedKey)
    {
        try
        {
            var leasePath = GetLeasePath();
            if (!File.Exists(leasePath)) return false;
            var token = File.ReadAllText(leasePath).Trim();
            var parts = token.Split('.');
            if (parts.Length != 2) return false;

            var payloadBytes = Convert.FromBase64String(parts[0]);
            var payload = Encoding.UTF8.GetString(payloadBytes);

            // Recompute HMAC signature using the shared secret
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("velox-alpha-secret-seed-9821"));
            var expectedSig = Convert.ToBase64String(hmac.ComputeHash(payloadBytes));

            if (parts[1] != expectedSig)
            {
                AppLogger.Warn("License", "[VLX-LIC-204] Offline lease signature mismatch (tampering detected).");
                return false;
            }

            // Payload format: key|company|planTier|expiresAt|validUntilUtc|maxDevices|usedDevices
            var fields = payload.Split('|');
            if (fields.Length < 5) return false;

            var key = fields[0];
            var company = fields[1];
            var plan = fields[2];
            var expiresAt = DateTimeOffset.Parse(fields[3]);
            var validUntil = DateTimeOffset.Parse(fields[4]);

            if (!string.Equals(key, expectedKey, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var now = DateTimeOffset.UtcNow;
            if (now > validUntil || now > expiresAt)
            {
                StatusMessage = "Offline lease grace period has expired. Connect to internet to re-verify.";
                return false;
            }

            CompanyName = company;
            PlanTier = plan;
            DaysRemaining = (int)Math.Max(0, (expiresAt - now).TotalDays);
            int graceDaysLeft = (int)Math.Max(0, (validUntil - now).TotalDays);
            StatusMessage = $"Offline Verified ({company}) — {graceDaysLeft} offline grace days left.";
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("License", $"Failed to verify offline lease: {ex.Message}");
            return false;
        }
    }

    public static string GetMachineIdentifier()
    {
        try
        {
            // Query BIOS Serial Number or Motherboard UUID via WMI
            using var searcher = new ManagementObjectSearcher("SELECT UUID FROM Win32_ComputerSystemProduct");
            foreach (ManagementObject obj in searcher.Get())
            {
                var uuid = obj["UUID"]?.ToString();
                if (!string.IsNullOrWhiteSpace(uuid) && uuid.Length > 8 && !uuid.Contains("00000000"))
                {
                    return uuid.Trim();
                }
            }
        }
        catch { }

        try
        {
            var raw = Environment.MachineName + Environment.ProcessorCount;
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(raw)))[..16];
        }
        catch
        {
            return "MACHINE-" + Environment.MachineName;
        }
    }
}
