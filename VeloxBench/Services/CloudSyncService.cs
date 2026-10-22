using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Velox.Shared.Dtos;
using Velox.Shared.Common;
using SuperAutoMater;
using SuperAutoMater.Core;
using SuperAutoMater.Wpf.Core;
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

    // ── Direct Synchronous Cloud Push (No Queue Ambiguity) ──────────────────

    public async Task<VeloxResult> PushAssetDirectAsync(AssetQueueRecord record)
    {
        if (record == null)
            return VeloxResult.Fail(VeloxErrorCode.ERR_CLOUD_BAD_REQUEST, "Asset record is empty or invalid.");

        var cfg = BenchConfigService.Current;
        var tag = string.IsNullOrWhiteSpace(record.Tag) ? record.Asset_Tag : record.Tag;
        var sn = string.IsNullOrWhiteSpace(record.Serial_Number) ? tag : record.Serial_Number;

        // High-throughput batch envelope mapped to /api/v1/bench/sync
        var payloadObj = new
        {
            Tag = tag,
            SerialNumber = sn,
            Model = record.Model ?? "Generic Laptop",
            Status = record.Status ?? "RTS",
            Grade = record.Physical_Grade ?? "A",
            Cpu = record.Processor ?? "",
            RamGb = record.Memory ?? "",
            StorageDetails = $"{record.Storage_Health}% Health",
            BatteryHealth = record.Battery_Health,
            StorageHealth = record.Storage_Health,
            Technician = record.Technician ?? Environment.UserName,
            Remarks = $"{record.Remarks} | WIP: {record.Work_In_Progress}",
            Timestamp = string.IsNullOrWhiteSpace(record.Timestamp) ? DateTime.UtcNow.ToString("o") : record.Timestamp
        };

        var batch = new BenchSyncBatchRequest
        {
            MachineName = cfg.BenchName,
            LicenseKey = cfg.LicenseKey,
            AppVersion = "1.6.10-alpha",
            Events = new List<BenchSyncItemDto>
            {
                new BenchSyncItemDto
                {
                    EventId = Guid.NewGuid().ToString("N"),
                    EventType = "QC_DISPATCH_COMPLETE",
                    AggregateId = tag,
                    IdempotencyKey = $"QC-{tag}-{sn}-{DateTime.UtcNow:yyyyMMddHHmmss}",
                    Timestamp = DateTime.UtcNow.ToString("o"),
                    PayloadJson = JsonSerializer.Serialize(payloadObj)
                }
            }
        };

        var json = JsonSerializer.Serialize(batch);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var req = new HttpRequestMessage(HttpMethod.Post, $"{cfg.ApiUrl}/api/v1/bench/sync")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            req.Headers.Add("X-API-Key", cfg.ApiKey);
            req.Headers.Add("X-License-Key", cfg.LicenseKey);

            var resp = await _http.SendAsync(req, cts.Token);
            var body = await resp.Content.ReadAsStringAsync();

            if (resp.IsSuccessStatusCode)
            {
                _isOnline = true;
                AppLogger.Info($"[DirectSync] [VLX-OK-200] Asset {tag} ({sn}) uploaded to Velox Cloud.");
                return VeloxResult.Ok($"Asset {tag} ({sn}) successfully uploaded to Velox Cloud.");
            }

            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                AppLogger.Warn($"[DirectSync] [VLX-AUTH-200] Unauthorized: Invalid API key or license key.");
                return VeloxResult.Fail(VeloxErrorCode.ERR_AUTH_INVALID_KEY, "Authentication failed: Invalid API Key or License Key.", body);
            }

            if (resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                AppLogger.Warn($"[DirectSync] [VLX-AUTH-201] Forbidden: License expired or quota exceeded.");
                return VeloxResult.Fail(VeloxErrorCode.ERR_AUTH_FORBIDDEN, "Cloud access denied: License expired or monthly quota exceeded.", body);
            }

            if (resp.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                AppLogger.Warn($"[DirectSync] [VLX-DATA-304] Validation Error: {body}");
                return VeloxResult.Fail(VeloxErrorCode.ERR_CLOUD_BAD_REQUEST, "Cloud server rejected asset payload validation.", body);
            }

            AppLogger.Warn($"[DirectSync] [VLX-SRV-300] Server error: {resp.StatusCode} - {body}");
            return VeloxResult.Fail(VeloxErrorCode.ERR_CLOUD_SERVER_ERROR, $"Cloud server returned error (HTTP {(int)resp.StatusCode}).", body);
        }
        catch (TaskCanceledException)
        {
            _isOnline = false;
            AppLogger.Warn($"[DirectSync] [VLX-NET-101] Cloud sync timed out after 5s for {tag}.");
            return VeloxResult.Fail(VeloxErrorCode.ERR_NET_TIMEOUT, "Cloud server timed out after 5 seconds.", "Check if cloud server or reverse proxy is running.");
        }
        catch (HttpRequestException httpEx)
        {
            _isOnline = false;
            AppLogger.Warn($"[DirectSync] [VLX-NET-100] Network unreachable for {tag}: {httpEx.Message}");
            return VeloxResult.Fail(VeloxErrorCode.ERR_NET_UNREACHABLE, "Cannot connect to Velox Cloud server (Network unreachable).", httpEx.Message);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[DirectSync] [VLX-ERR-999] Unexpected error: {ex.Message}");
            return VeloxResult.Fail(VeloxErrorCode.ERR_INTERNAL_EXCEPTION, "Unexpected internal upload exception.", ex.Message);
        }
    }

    public async Task<VeloxResult> TestCloudConnectionAsync()
    {
        var cfg = BenchConfigService.Current;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var resp = await _http.GetAsync($"{cfg.ApiUrl}/health", cts.Token);
            sw.Stop();
            if (resp.IsSuccessStatusCode)
            {
                _isOnline = true;
                return VeloxResult.Ok($"Connected to Velox Cloud API ({sw.ElapsedMilliseconds}ms latency)");
            }
            return VeloxResult.Fail(VeloxErrorCode.ERR_CLOUD_SERVER_ERROR, $"Cloud server returned HTTP {(int)resp.StatusCode} on health check.");
        }
        catch (TaskCanceledException)
        {
            _isOnline = false;
            return VeloxResult.Fail(VeloxErrorCode.ERR_NET_TIMEOUT, "Cloud server health check timed out after 4 seconds.");
        }
        catch (HttpRequestException ex)
        {
            _isOnline = false;
            return VeloxResult.Fail(VeloxErrorCode.ERR_NET_UNREACHABLE, $"Cannot reach cloud server at {cfg.ApiUrl}.", ex.Message);
        }
        catch (Exception ex)
        {
            return VeloxResult.Fail(VeloxErrorCode.ERR_INTERNAL_EXCEPTION, ex.Message);
        }
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

    public async Task<List<FittedComponentDto>> GetFittedComponentsAsync(string assetId)
    {
        if (string.IsNullOrWhiteSpace(assetId)) return new List<FittedComponentDto>();
        var cfg = BenchConfigService.Current;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var req = new HttpRequestMessage(HttpMethod.Get, $"{cfg.ApiUrl}/api/v1/assets/{assetId}/components");
            req.Headers.Add("X-API-Key", cfg.ApiKey);
            var resp = await _http.SendAsync(req, cts.Token);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("items", out var itemsElem))
                {
                    var items = JsonSerializer.Deserialize<List<FittedComponentDto>>(itemsElem.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    return items ?? new List<FittedComponentDto>();
                }
            }
        }
        catch { }
        return new List<FittedComponentDto>();
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
