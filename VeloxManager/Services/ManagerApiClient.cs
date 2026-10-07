using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Velox.Shared.Dtos;
using Velox.Shared.Models;

namespace VeloxManager.Services;

public class SearchResponseWrapper
{
    public int Total { get; set; }
    public List<AssetDto> Items { get; set; } = new();
}

public class EmployeeResponseWrapper
{
    public int Count { get; set; }
    public List<EmployeeDto> Employees { get; set; } = new();
}

public class FittedComponentsResponseWrapper
{
    public string AssetId { get; set; } = "";
    public int Count { get; set; }
    public List<FittedComponentDto> Items { get; set; } = new();
}

public class ManagerApiClient
{
    private static ManagerApiClient? _instance;
    public static ManagerApiClient Instance => _instance ??= new ManagerApiClient();

    private readonly HttpClient _http;
    private readonly HttpClient _sseHttp;
    private CancellationTokenSource? _sseCts;
    private CancellationTokenSource? _healthCts;

    public event Action<string>? AssetUpdated;
    public event Action<bool>? CloudConnectionChanged;

    public bool IsConnected { get; private set; }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ManagerApiClient()
    {
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        _sseHttp = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        ConfigureClient();
        StartPeriodicHealthCheck();
    }

    public void ConfigureClient()
    {
        var cfg = ManagerConfigService.Current;
        var baseUrl = cfg.ApiUrl.TrimEnd('/');
        
        _http.BaseAddress = new Uri(baseUrl + "/");
        _http.DefaultRequestHeaders.Clear();
        _http.DefaultRequestHeaders.Add("X-Velox-Api-Key", cfg.ApiKey);
        _http.DefaultRequestHeaders.Add("X-API-Key", cfg.ApiKey);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _sseHttp.BaseAddress = new Uri(baseUrl + "/");
        _sseHttp.DefaultRequestHeaders.Clear();
        _sseHttp.DefaultRequestHeaders.Add("X-Velox-Api-Key", cfg.ApiKey);
        _sseHttp.DefaultRequestHeaders.Add("X-API-Key", cfg.ApiKey);
    }

    private void StartPeriodicHealthCheck()
    {
        _healthCts?.Cancel();
        _healthCts = new CancellationTokenSource();
        var token = _healthCts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                await CheckHealthAsync();
                try
                {
                    await Task.Delay(20000, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, token);
    }

    public async Task<bool> CheckHealthAsync()
    {
        try
        {
            var res = await _http.GetAsync("health");
            IsConnected = res.IsSuccessStatusCode;
            CloudConnectionChanged?.Invoke(IsConnected);
            return IsConnected;
        }
        catch
        {
            IsConnected = false;
            CloudConnectionChanged?.Invoke(false);
            return false;
        }
    }

    public async Task<List<AssetDto>> SearchAssetsAsync(string? query = null, LifecycleQueue? queue = null, int limit = 200)
    {
        try
        {
            var url = $"api/v1/assets/search?limit={limit}";
            if (!string.IsNullOrWhiteSpace(query)) url += $"&q={Uri.EscapeDataString(query)}";
            if (queue.HasValue) url += $"&queue={queue.Value}";

            var res = await _http.GetAsync(url);
            if (res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadAsStringAsync();
                var wrapper = JsonSerializer.Deserialize<SearchResponseWrapper>(json, JsonOpts);
                IsConnected = true;
                return wrapper?.Items ?? new List<AssetDto>();
            }
        }
        catch (Exception)
        {
            IsConnected = false;
        }
        return new List<AssetDto>();
    }

    public async Task<AssetDto?> IntakeAssetAsync(IntakeRequest req)
    {
        try
        {
            var json = JsonSerializer.Serialize(req);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var res = await _http.PostAsync("api/v1/assets/intake", content);
            if (res.IsSuccessStatusCode)
            {
                var resJson = await res.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<AssetDto>(resJson, JsonOpts);
            }
        }
        catch { }
        return null;
    }

    public async Task<bool> UpdateAssetStatusAsync(string assetId, LifecycleQueue newStatus, string? notes = null, string? actor = null)
    {
        return await PatchAssetAsync(assetId, new PatchAssetRequest
        {
            LifecycleQueue = newStatus,
            Notes = notes,
            Actor = actor ?? ManagerConfigService.Current.ManagerName
        });
    }

    public async Task<bool> PatchAssetAsync(string assetId, PatchAssetRequest patch)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(patch.Actor))
            {
                patch.Actor = ManagerConfigService.Current.ManagerName;
            }
            var content = new StringContent(JsonSerializer.Serialize(patch, JsonOpts), Encoding.UTF8, "application/json");
            var res = await _http.PatchAsync($"api/v1/assets/{assetId}", content);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> AssignAssetAsync(string assetId, string employeeId, string? actor = null)
    {
        try
        {
            var assign = new AssignRequest
            {
                EmployeeId = employeeId,
                Actor = actor ?? ManagerConfigService.Current.ManagerName
            };
            var content = new StringContent(JsonSerializer.Serialize(assign), Encoding.UTF8, "application/json");
            var res = await _http.PostAsync($"api/v1/assets/{assetId}/assign", content);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<EmployeeDto>> GetEmployeesAsync()
    {
        try
        {
            var res = await _http.GetAsync("api/v1/employees");
            if (res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadAsStringAsync();
                var wrapper = JsonSerializer.Deserialize<EmployeeResponseWrapper>(json, JsonOpts);
                return wrapper?.Employees ?? new List<EmployeeDto>();
            }
        }
        catch { }
        return new List<EmployeeDto>();
    }

    public async Task<EmployeeDto?> CreateEmployeeAsync(string name, EmployeeRole role, string? pin = null, string stationId = "BENCH-01")
    {
        try
        {
            var body = new CreateEmployeeRequest
            {
                Name = name,
                Role = role,
                Pin = !string.IsNullOrWhiteSpace(pin) ? pin : "1234",
                StationId = !string.IsNullOrWhiteSpace(stationId) ? stationId : "BENCH-01"
            };
            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            var res = await _http.PostAsync("api/v1/employees", content);
            if (res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<EmployeeDto>(json, JsonOpts);
            }
        }
        catch { }
        return null;
    }

    public async Task<EmployeeDto?> PatchEmployeeAsync(string id, PatchEmployeeRequest patch)
    {
        try
        {
            var content = new StringContent(JsonSerializer.Serialize(patch), Encoding.UTF8, "application/json");
            var res = await _http.PatchAsync($"api/v1/employees/{id}", content);
            if (res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<EmployeeDto>(json, JsonOpts);
            }
        }
        catch { }
        return null;
    }

    public async Task<bool> ResetEmployeePinAsync(string id, string newPin)
    {
        var updated = await PatchEmployeeAsync(id, new PatchEmployeeRequest { Pin = newPin });
        return updated != null;
    }

    public async Task<bool> UpdateEmployeeStationAsync(string id, string stationId)
    {
        var updated = await PatchEmployeeAsync(id, new PatchEmployeeRequest { StationId = stationId });
        return updated != null;
    }

    // ── Fitted Components (BOM) ──────────────────────────────────────────────
    public async Task<List<FittedComponentDto>> GetFittedComponentsAsync(string assetId)
    {
        try
        {
            var res = await _http.GetAsync($"api/v1/assets/{assetId}/components");
            if (res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadAsStringAsync();
                var wrap = JsonSerializer.Deserialize<FittedComponentsResponseWrapper>(json, JsonOpts);
                return wrap?.Items ?? new List<FittedComponentDto>();
            }
        }
        catch { }
        return new List<FittedComponentDto>();
    }

    public async Task<FittedComponentDto?> AttachComponentAsync(string assetId, AttachComponentRequest req)
    {
        try
        {
            var jsonBody = JsonSerializer.Serialize(req, JsonOpts);
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            var res = await _http.PostAsync($"api/v1/assets/{assetId}/components", content);
            if (res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<FittedComponentDto>(json, JsonOpts);
            }
        }
        catch { }
        return null;
    }

    public async Task<bool> DetachComponentAsync(string assetId, string componentId, bool returnToStock, string? condition = null)
    {
        try
        {
            var url = $"api/v1/assets/{assetId}/components/{componentId}?returnToStock={returnToStock.ToString().ToLowerInvariant()}";
            if (!string.IsNullOrWhiteSpace(condition))
                url += $"&condition={Uri.EscapeDataString(condition)}";

            var res = await _http.DeleteAsync(url);
            return res.IsSuccessStatusCode;
        }
        catch { }
        return false;
    }

    // ── Real-Time Zero-Click SSE Event Stream ──────────────────────────────
    public void StartRealtimeEvents()
    {
        _sseCts?.Cancel();
        _sseCts = new CancellationTokenSource();
        var token = _sseCts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/events/stream");
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

                    using var response = await _sseHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                    if (response.IsSuccessStatusCode)
                    {
                        IsConnected = true;
                        CloudConnectionChanged?.Invoke(true);

                        using var stream = await response.Content.ReadAsStreamAsync(token);
                        using var reader = new StreamReader(stream);

                        while (!token.IsCancellationRequested)
                        {
                            var line = await reader.ReadLineAsync(token);
                            if (line == null) break;
                            if (string.IsNullOrWhiteSpace(line)) continue;

                            if (line.StartsWith("data:"))
                            {
                                var data = line.Substring(5).Trim();
                                AssetUpdated?.Invoke(data);
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // If SSE disconnects, let background health check monitor connectivity
                    // and re-establish in 3 seconds
                }

                // Wait before reconnecting
                try
                {
                    await Task.Delay(3000, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, token);
    }

    public void StopRealtimeEvents()
    {
        _sseCts?.Cancel();
    }
}
