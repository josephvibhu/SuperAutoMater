using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SuperAutoMater.Wpf.Core;
using Velox.Shared.Dtos;
using VeloxBench.Core;

namespace VeloxBench.Services;

public sealed class LicenseService
{
    private static readonly Lazy<LicenseService> _lazy = new(() => new LicenseService());
    public static LicenseService Instance => _lazy.Value;

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(6) };
    private static readonly string LeasePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Velox", "VeloxBench", "license.lease");

    private static readonly string PortableKeyPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "license.key");

    public bool IsLicensed { get; private set; }
    public bool IsOfflineLeaseValid { get; private set; }
    public string LicenseKey { get; private set; } = "";
    public string CompanyName { get; private set; } = "Unlicensed";
    public string PlanTier { get; private set; } = "None";
    public int DaysRemaining { get; private set; }
    public int DevicesUsedMonth { get; private set; }
    public int MaxDevicesMonth { get; private set; }
    public string StatusMessage { get; private set; } = "Initializing license...";

    public event Action? LicenseStatusChanged;

    public LicenseService()
    {
    }

    public string DiscoverLicenseKey()
    {
        // 1. Check license.key beside the portable executable (USB stick portable mode)
        try
        {
            if (File.Exists(PortableKeyPath))
            {
                var key = File.ReadAllText(PortableKeyPath).Trim();
                if (!string.IsNullOrWhiteSpace(key))
                {
                    AppLogger.Info("License", $"Discovered portable license key file at {PortableKeyPath}");
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
        LicenseKey = key;

        if (string.IsNullOrWhiteSpace(key))
        {
            IsLicensed = false;
            StatusMessage = "No Alpha license key found. Place license.key in depot folder.";
            LicenseStatusChanged?.Invoke();
            return false;
        }

        var cfg = BenchConfigService.Current;
        var request = new LicenseValidateRequest
        {
            LicenseKey = key,
            MachineName = Environment.MachineName,
            MachineGuid = GetMachineIdentifier(),
            AppVersion = "1.6.10"
        };

        // Try Online Validation with Velox Cloud
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
                    StatusMessage = $"Alpha Licensed to {CompanyName} ({PlanTier}) — {DaysRemaining} days remaining.";

                    // Cache verified signed lease for offline usage
                    SaveOfflineLease(result.SignedLeaseToken);

                    AppLogger.Info("License", $"Online license validated successfully: {StatusMessage}");
                    LicenseStatusChanged?.Invoke();
                    return true;
                }
            }
            else
            {
                // Server rejected license
                try
                {
                    var failResult = JsonSerializer.Deserialize<LicenseValidateResponse>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    StatusMessage = failResult?.Message ?? "License validation rejected by cloud.";
                }
                catch
                {
                    StatusMessage = $"Cloud returned license rejection (HTTP {resp.StatusCode}).";
                }

                IsLicensed = false;
                AppLogger.Warn("License", $"License rejected by cloud: {StatusMessage}");
                LicenseStatusChanged?.Invoke();
                return false;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("License", $"Online validation unreachable: {ex.Message}. Falling back to offline lease check.");
        }

        // Fallback: Verify local signed lease (Offline Grace Period)
        if (VerifyOfflineLease(key))
        {
            IsLicensed = true;
            IsOfflineLeaseValid = true;
            LicenseStatusChanged?.Invoke();
            return true;
        }

        IsLicensed = false;
        StatusMessage = "License could not be verified online and no valid offline lease exists.";
        LicenseStatusChanged?.Invoke();
        return false;
    }

    private void SaveOfflineLease(string signedToken)
    {
        if (string.IsNullOrWhiteSpace(signedToken)) return;
        try
        {
            var dir = Path.GetDirectoryName(LeasePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(LeasePath, signedToken.Trim());
        }
        catch { }
    }

    private bool VerifyOfflineLease(string expectedKey)
    {
        try
        {
            if (!File.Exists(LeasePath)) return false;
            var token = File.ReadAllText(LeasePath).Trim();
            var parts = token.Split('.');
            if (parts.Length != 2) return false;

            var payloadBytes = Convert.FromBase64String(parts[0]);
            var payload = Encoding.UTF8.GetString(payloadBytes);

            // Recompute HMAC signature
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("velox-alpha-secret-seed-9821"));
            var expectedSig = Convert.ToBase64String(hmac.ComputeHash(payloadBytes));

            if (parts[1] != expectedSig)
            {
                AppLogger.Warn("License", "Offline lease signature mismatch (tampering detected).");
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
            StatusMessage = $"Alpha Offline Grace Mode ({company}) — {graceDaysLeft} offline grace days left.";
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("License", $"Failed to verify offline lease: {ex.Message}");
            return false;
        }
    }

    private static string GetMachineIdentifier()
    {
        try
        {
            var raw = Environment.MachineName + Environment.UserName + Environment.ProcessorCount;
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(raw)))[..16];
        }
        catch
        {
            return "MACHINE-" + Environment.MachineName;
        }
    }
}
