using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Velox.Shared.Dtos;
using VeloxBench.Core;

namespace VeloxBench.Services;

public enum SyncSource
{
    None,
    LocalCache,
    LanManager,
    CloudAuthoritative
}

public sealed class LookupResult
{
    public bool Found { get; set; }
    public SyncSource Source { get; set; }
    public AssetDto? Asset { get; set; }
}

public sealed class CloudSyncService
{
    private static readonly Lazy<CloudSyncService> _lazy = new(() => new CloudSyncService());
    public static CloudSyncService Instance => _lazy.Value;

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(6) };
    private readonly Timer _outboxTimer;
    private bool _isOnline;
    private bool _isLanConnected;

    public bool IsOnline => _isOnline;
    public bool IsLanConnected => _isLanConnected;
    public event Action? ConnectivityChanged;

    public CloudSyncService()
    {
        _outboxTimer = new Timer(async _ => await FlushOutboxAsync(), null, 5000, 15000);
        _ = CheckConnectivityAsync();
    }

    public async Task CheckConnectivityAsync()
    {
        var cfg = BenchConfigService.Current;

        // Check Cloud
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var resp = await _http.GetAsync($"{cfg.ApiUrl}/health", cts.Token);
            _isOnline = resp.IsSuccessStatusCode;
        }
        catch
        {
            _isOnline = false;
        }

        // Check LAN
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
            var resp = await _http.GetAsync($"http://{cfg.ManagerLanIp}:{cfg.ManagerLanPort}/api/ping", cts.Token);
            _isLanConnected = resp.IsSuccessStatusCode;
        }
        catch
        {
            _isLanConnected = false;
        }

        ConnectivityChanged?.Invoke();
    }

    // ── 3-Tier Waterfall Lookup ───────────────────────────────────────────────

    public async Task<LookupResult> LookupAssetAsync(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return new LookupResult { Found = false, Source = SyncSource.None };

        var cfg = BenchConfigService.Current;
        var q = Uri.EscapeDataString(identifier.Trim());

        // Tier 1: LAN Mirror (ultra-fast, <400ms)
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
            var lanUrl = $"http://{cfg.ManagerLanIp}:{cfg.ManagerLanPort}/api/asset/lookup?q={q}";
            var resp = await _http.GetAsync(lanUrl, cts.Token);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("found", out var foundProp) && foundProp.GetBoolean())
                {
                    if (doc.RootElement.TryGetProperty("asset", out var assetEl))
                    {
                        var asset = JsonSerializer.Deserialize<AssetDto>(assetEl.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (asset != null)
                        {
                            _isLanConnected = true;
                            LocalBenchStore.Instance.CacheAsset(asset);
                            AppLogger.Info($"[Lookup] Found asset on LAN: {identifier}");
                            return new LookupResult { Found = true, Source = SyncSource.LanManager, Asset = asset };
                        }
                    }
                }
            }
        }
        catch { }

        // Tier 2: Cloud Authoritative (VeloxAPI on Railway)
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var req = new HttpRequestMessage(HttpMethod.Get, $"{cfg.ApiUrl}/api/v1/assets/lookup?q={q}");
            req.Headers.Add("X-API-Key", cfg.ApiKey);
            var resp = await _http.SendAsync(req, cts.Token);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("found", out var foundProp) && foundProp.GetBoolean())
                {
                    if (doc.RootElement.TryGetProperty("asset", out var assetEl))
                    {
                        var asset = JsonSerializer.Deserialize<AssetDto>(assetEl.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (asset != null)
                        {
                            _isOnline = true;
                            LocalBenchStore.Instance.CacheAsset(asset);
                            AppLogger.Info($"[Lookup] Found asset in Cloud: {identifier}");
                            return new LookupResult { Found = true, Source = SyncSource.CloudAuthoritative, Asset = asset };
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Cloud lookup failed: {ex.Message}");
        }

        // Tier 3: Local SQLite Cache
        var local = LocalBenchStore.Instance.FindLocalAsset(identifier);
        if (local != null)
        {
            AppLogger.Info($"[Lookup] Found asset in local cache: {identifier}");
            return new LookupResult { Found = true, Source = SyncSource.LocalCache, Asset = local };
        }

        return new LookupResult { Found = false, Source = SyncSource.None };
    }

    // ── Instant State Push ───────────────────────────────────────────────────

    public async Task PushIntakeOrUpdateAsync(IntakeRequest intake)
    {
        var cfg = BenchConfigService.Current;
        var json = JsonSerializer.Serialize(intake);

        // Try direct push to Cloud
        bool pushed = false;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var req = new HttpRequestMessage(HttpMethod.Post, $"{cfg.ApiUrl}/api/v1/assets/intake");
            req.Headers.Add("X-API-Key", cfg.ApiKey);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await _http.SendAsync(req, cts.Token);
            if (resp.IsSuccessStatusCode)
            {
                pushed = true;
                _isOnline = true;
                AppLogger.Info($"[Push] Live intake pushed to cloud for SN={intake.SerialNumber}");
            }
        }
        catch { }

        if (!pushed)
        {
            // Offline resilience: enqueue to local outbox
            LocalBenchStore.Instance.EnqueueOutbox("/api/v1/assets/intake", "POST", json);
            AppLogger.Warn($"[Push] Network unreachable, queued intake to local outbox: {intake.SerialNumber}");
        }
    }

    public async Task PushAssetPatchAsync(string assetId, PatchAssetRequest patch)
    {
        if (string.IsNullOrWhiteSpace(assetId)) return;
        var cfg = BenchConfigService.Current;
        var json = JsonSerializer.Serialize(patch);

        bool pushed = false;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var req = new HttpRequestMessage(HttpMethod.Patch, $"{cfg.ApiUrl}/api/v1/assets/{assetId}");
            req.Headers.Add("X-API-Key", cfg.ApiKey);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await _http.SendAsync(req, cts.Token);
            if (resp.IsSuccessStatusCode)
            {
                pushed = true;
                _isOnline = true;
                AppLogger.Info($"[Push] Live patch pushed to cloud for assetId={assetId}");
            }
        }
        catch { }

        if (!pushed)
        {
            LocalBenchStore.Instance.EnqueueOutbox($"/api/v1/assets/{assetId}", "PATCH", json);
            AppLogger.Warn($"[Push] Network unreachable, queued patch to local outbox: {assetId}");
        }
    }

    public async Task PushAssetEventAsync(string assetId, AppendEventRequest evt)
    {
        if (string.IsNullOrWhiteSpace(assetId)) return;
        var cfg = BenchConfigService.Current;
        var json = JsonSerializer.Serialize(evt);

        bool pushed = false;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var req = new HttpRequestMessage(HttpMethod.Post, $"{cfg.ApiUrl}/api/v1/assets/{assetId}/events");
            req.Headers.Add("X-API-Key", cfg.ApiKey);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await _http.SendAsync(req, cts.Token);
            if (resp.IsSuccessStatusCode)
            {
                pushed = true;
                _isOnline = true;
            }
        }
        catch { }

        if (!pushed)
        {
            LocalBenchStore.Instance.EnqueueOutbox($"/api/v1/assets/{assetId}/events", "POST", json);
        }
    }

    // ── Outbox Background Flush ───────────────────────────────────────────────

    private async Task FlushOutboxAsync()
    {
        var pending = LocalBenchStore.Instance.GetPendingOutbox(30);
        if (pending.Count == 0) return;

        var cfg = BenchConfigService.Current;

        foreach (var item in pending)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var method = item.HttpMethod.ToUpperInvariant() == "PATCH" ? HttpMethod.Patch : HttpMethod.Post;
                var req = new HttpRequestMessage(method, $"{cfg.ApiUrl}{item.TargetEndpoint}");
                req.Headers.Add("X-API-Key", cfg.ApiKey);
                req.Content = new StringContent(item.PayloadJson, Encoding.UTF8, "application/json");

                var resp = await _http.SendAsync(req, cts.Token);
                if (resp.IsSuccessStatusCode)
                {
                    LocalBenchStore.Instance.RemoveOutbox(item.Id);
                    AppLogger.Info($"[Outbox] Flushed item {item.Id} -> {item.TargetEndpoint}");
                }
            }
            catch
            {
                break; // still offline
            }
        }
    }
}
