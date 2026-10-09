using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Velox.Shared.Dtos;
using Velox.Shared.Models;
using VeloxManager.ViewModels;

namespace VeloxManager.Services;

public class LanMirrorServer
{
    private static LanMirrorServer? _instance;
    public static LanMirrorServer Instance => _instance ??= new LanMirrorServer();

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    public bool IsRunning { get; private set; }
    public int Port { get; private set; } = 9000;
    public string ServerAddress { get; private set; } = "Not Running";

    public event Action<string>? LocalBenchActivityReceived;

    public Func<IEnumerable<AssetItemViewModel>>? AssetProvider { get; set; }
    public Func<string, HoldAssetRequest, Task<bool>>? HoldAssetHandler { get; set; }
    public Func<string, Task<bool>>? ReleaseHoldHandler { get; set; }

    private static byte[]? _showroomHtmlBytes;

    public static string GetLocalIpAddress()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(ip) &&
                    !ip.ToString().StartsWith("169.254."))
                {
                    return ip.ToString();
                }
            }
        }
        catch { }
        return "127.0.0.1";
    }

    public void Start(int port = 9000)
    {
        if (IsRunning) return;
        Port = port;

        // Attempt 1: Bind wildcard * (requires admin or registered URL reservation)
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://*:{port}/");
            _listener.Start();
            IsRunning = true;
            ServerAddress = $"http://0.0.0.0:{port}/";
            _cts = new CancellationTokenSource();
            _ = ListenLoopAsync(_cts.Token);
            return;
        }
        catch { }

        // Attempt 2: Bind wildcard +
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://+:{port}/");
            _listener.Start();
            IsRunning = true;
            ServerAddress = $"http://0.0.0.0:{port}/";
            _cts = new CancellationTokenSource();
            _ = ListenLoopAsync(_cts.Token);
            return;
        }
        catch { }

        // Attempt 3: Bind localhost, 127.0.0.1, and local LAN IP
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{port}/");
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            var localIp = GetLocalIpAddress();
            if (localIp != "127.0.0.1")
            {
                _listener.Prefixes.Add($"http://{localIp}:{port}/");
            }
            _listener.Start();
            IsRunning = true;
            ServerAddress = $"http://{localIp}:{port}/";
            _cts = new CancellationTokenSource();
            _ = ListenLoopAsync(_cts.Token);
            return;
        }
        catch { }

        // Attempt 4: Bind localhost and 127.0.0.1 ONLY (100% guaranteed to succeed without admin permissions)
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{port}/");
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            IsRunning = true;
            ServerAddress = $"http://localhost:{port}/";
            _cts = new CancellationTokenSource();
            _ = ListenLoopAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            IsRunning = false;
            ServerAddress = $"Error: {ex.Message}";
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        try
        {
            _listener?.Stop();
            _listener?.Close();
        }
        catch { }
        IsRunning = false;
        ServerAddress = "Stopped";
    }

    private async Task ListenLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener != null && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = ProcessRequestAsync(context);
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch { }
        }
    }

    private async Task ProcessRequestAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;

        // CORS headers for LAN clients & browser fetch
        res.Headers.Add("Access-Control-Allow-Origin", "*");
        res.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        res.Headers.Add("Access-Control-Allow-Headers", "*");

        if (req.HttpMethod == "OPTIONS")
        {
            res.StatusCode = 204;
            res.Close();
            return;
        }

        try
        {
            var rawPath = req.Url?.AbsolutePath.ToLowerInvariant() ?? "";
            var path = rawPath.TrimEnd('/');

            // ── Health check ──
            if (path == "/health")
            {
                await SendJsonAsync(res, 200, new { status = "healthy", server = "VeloxManager-LAN-Mirror", version = "1.0.0" });
                return;
            }

            // ── VeloxShowroom Web Portal (HTML redirect or serve) ──
            if (string.IsNullOrEmpty(path) || path == "/showroom" || rawPath == "/showroom/index.html")
            {
                var htmlBytes = GetShowroomHtmlBytes();
                res.ContentType = "text/html; charset=utf-8";
                res.StatusCode = 200;
                res.ContentLength64 = htmlBytes.Length;
                await res.OutputStream.WriteAsync(htmlBytes, 0, htmlBytes.Length);
                res.Close();
                return;
            }

            // ── VeloxShowroom API: Search & List Assets ──
            if (rawPath.StartsWith("/api/v1/showroom/assets") && req.HttpMethod == "GET")
            {
                await HandleShowroomAssetsAsync(req, res);
                return;
            }

            // ── VeloxShowroom API: Dashboard Metrics Summary ──
            if (rawPath.StartsWith("/api/v1/showroom/summary") && req.HttpMethod == "GET")
            {
                await HandleShowroomSummaryAsync(res);
                return;
            }

            // ── VeloxShowroom API: Place Hold Reservation ──
            if (rawPath.StartsWith("/api/v1/showroom/assets/") && rawPath.EndsWith("/hold") && req.HttpMethod == "POST")
            {
                await HandleShowroomHoldAsync(req, res, rawPath);
                return;
            }

            // ── VeloxShowroom API: Release Hold Reservation ──
            if (rawPath.StartsWith("/api/v1/showroom/assets/") && rawPath.EndsWith("/release-hold") && req.HttpMethod == "POST")
            {
                await HandleShowroomReleaseHoldAsync(req, res, rawPath);
                return;
            }

            // ── LAN Bench: Assets Search ──
            if (rawPath.StartsWith("/api/v1/assets/search") && req.HttpMethod == "GET")
            {
                var q = req.QueryString["q"] ?? "";
                var assets = await ManagerApiClient.Instance.SearchAssetsAsync(q);
                await SendJsonAsync(res, 200, assets);
                return;
            }

            // ── LAN Bench: Intake ──
            if (rawPath.StartsWith("/api/v1/assets/intake") && req.HttpMethod == "POST")
            {
                using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                var body = await reader.ReadToEndAsync();
                var intake = JsonSerializer.Deserialize<IntakeRequest>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (intake != null)
                {
                    LocalBenchActivityReceived?.Invoke($"Intake: {intake.SerialNumber} ({intake.Model}) from {req.RemoteEndPoint.Address}");
                    var asset = await ManagerApiClient.Instance.IntakeAssetAsync(intake);
                    await SendJsonAsync(res, 200, asset ?? new object());
                    return;
                }
            }

            await SendJsonAsync(res, 404, new { error = "Endpoint not found on LAN mirror" });
        }
        catch (Exception ex)
        {
            await SendJsonAsync(res, 500, new { error = ex.Message });
        }
    }

    private async Task HandleShowroomAssetsAsync(HttpListenerRequest req, HttpListenerResponse res)
    {
        var all = AssetProvider?.Invoke()?.ToList() ?? new List<AssetItemViewModel>();
        var filtered = all.AsEnumerable();

        var statusParam = req.QueryString["status"] ?? "ReadyForSale";
        if (!string.Equals(statusParam, "all", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(statusParam, "ready", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(statusParam, "rts", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(statusParam, "ReadyForSale", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(a => a.Status == LifecycleQueue.ReadyForSale);
            }
            else if (Enum.TryParse<LifecycleQueue>(statusParam, true, out var lq))
            {
                filtered = filtered.Where(a => a.Status == lq);
            }
        }

        var catParam = req.QueryString["category"];
        if (!string.IsNullOrWhiteSpace(catParam) && Enum.TryParse<AssetCategory>(catParam, true, out var cat))
        {
            filtered = filtered.Where(a => a.Category == cat);
        }

        var gradeParam = req.QueryString["grade"];
        if (!string.IsNullOrWhiteSpace(gradeParam))
        {
            filtered = filtered.Where(a => (a.GradeBadge ?? "").Contains(gradeParam, StringComparison.OrdinalIgnoreCase));
        }

        var locParam = req.QueryString["location"];
        if (!string.IsNullOrWhiteSpace(locParam))
        {
            filtered = filtered.Where(a => (a.CurrentLocation ?? "").Contains(locParam, StringComparison.OrdinalIgnoreCase));
        }

        var qParam = req.QueryString["q"];
        if (!string.IsNullOrWhiteSpace(qParam))
        {
            filtered = filtered.Where(a =>
                (a.ModelName ?? "").Contains(qParam, StringComparison.OrdinalIgnoreCase) ||
                (a.AssetTag ?? "").Contains(qParam, StringComparison.OrdinalIgnoreCase) ||
                (a.SerialNumber ?? "").Contains(qParam, StringComparison.OrdinalIgnoreCase) ||
                (a.Notes ?? "").Contains(qParam, StringComparison.OrdinalIgnoreCase));
        }

        var dtos = filtered.Select(a => new
        {
            id = a.Id,
            assetTag = a.AssetTag,
            serialNumber = a.SerialNumber,
            model = a.ModelName,
            manufacturer = ExtractBrand(a.ModelName),
            category = a.Category.ToString(),
            lifecycleQueue = a.Status.ToString(),
            currentLocation = a.CurrentLocation,
            grade = a.Grade.ToString(),
            cosmeticGrade = a.GradeBadge,
            isOnHold = a.IsOnHold,
            holdCustomer = a.HoldCustomer,
            holdSalesRep = a.HoldSalesRep,
            holdExpiresAtUtc = a.HoldExpiresAtUtc,
            salePrice = a.SalePrice,
            purchasePrice = a.PurchasePrice,
            notes = a.Notes
        }).ToList();

        await SendJsonAsync(res, 200, new
        {
            count = dtos.Count,
            filter = new { status = statusParam, category = catParam, grade = gradeParam, location = locParam, q = qParam },
            assets = dtos
        });
    }

    private async Task HandleShowroomSummaryAsync(HttpListenerResponse res)
    {
        var all = AssetProvider?.Invoke()?.ToList() ?? new List<AssetItemViewModel>();
        var ready = all.Where(a => a.Status == LifecycleQueue.ReadyForSale).ToList();

        var byBrand = ready
            .GroupBy(a => ExtractBrand(a.ModelName))
            .ToDictionary(g => g.Key, g => g.Count());

        var byGrade = ready
            .GroupBy(a => string.IsNullOrWhiteSpace(a.GradeBadge) ? "Ungraded" : a.GradeBadge)
            .ToDictionary(g => g.Key, g => g.Count());

        var topLocations = ready
            .GroupBy(a => string.IsNullOrWhiteSpace(a.CurrentLocation) ? "Unassigned" : a.CurrentLocation)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .ToDictionary(g => g.Key, g => g.Count());

        var onHold = ready.Count(a => a.IsOnHold);

        await SendJsonAsync(res, 200, new
        {
            totalReadyForSale = ready.Count,
            pristineGradeCount = ready.Count(a => a.GradeBadge.Contains("A")),
            onHoldCount = onHold,
            byBrand,
            byGrade,
            topLocations,
            updatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private async Task HandleShowroomHoldAsync(HttpListenerRequest req, HttpListenerResponse res, string rawPath)
    {
        var parts = rawPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // e.g. ["api", "v1", "showroom", "assets", "{id}", "hold"]
        if (parts.Length >= 6)
        {
            var assetId = parts[4];
            using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
            var body = await reader.ReadToEndAsync();
            var holdReq = JsonSerializer.Deserialize<HoldAssetRequest>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (holdReq == null || string.IsNullOrWhiteSpace(holdReq.CustomerName))
            {
                await SendJsonAsync(res, 400, new { error = "Customer name is required to reserve an asset." });
                return;
            }

            if (HoldAssetHandler != null)
            {
                var ok = await HoldAssetHandler.Invoke(assetId, holdReq);
                if (ok)
                {
                    LocalBenchActivityReceived?.Invoke($"Showroom: Asset {assetId} reserved for {holdReq.CustomerName} by {holdReq.SalesRep ?? "Sales"}");
                    await SendJsonAsync(res, 200, new { success = true, assetId, message = $"Reserved for {holdReq.CustomerName}" });
                    return;
                }
            }
        }

        await SendJsonAsync(res, 500, new { error = "Unable to apply showroom reservation" });
    }

    private async Task HandleShowroomReleaseHoldAsync(HttpListenerRequest req, HttpListenerResponse res, string rawPath)
    {
        var parts = rawPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // e.g. ["api", "v1", "showroom", "assets", "{id}", "release-hold"]
        if (parts.Length >= 6)
        {
            var assetId = parts[4];
            if (ReleaseHoldHandler != null)
            {
                var ok = await ReleaseHoldHandler.Invoke(assetId);
                if (ok)
                {
                    LocalBenchActivityReceived?.Invoke($"Showroom: Reservation released on Asset {assetId}");
                    await SendJsonAsync(res, 200, new { success = true, assetId, message = "Reservation released" });
                    return;
                }
            }
        }

        await SendJsonAsync(res, 500, new { error = "Unable to release showroom reservation" });
    }

    private static string ExtractBrand(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return "Other";
        var upper = model.ToUpperInvariant();
        if (upper.Contains("HP") || upper.Contains("ELITEBOOK") || upper.Contains("PROBOOK")) return "HP";
        if (upper.Contains("DELL") || upper.Contains("LATITUDE") || upper.Contains("PRECISION")) return "Dell";
        if (upper.Contains("LENOVO") || upper.Contains("THINKPAD") || upper.Contains("YOGA")) return "Lenovo";
        if (upper.Contains("MICROSOFT") || upper.Contains("SURFACE")) return "Microsoft";
        if (upper.Contains("APPLE") || upper.Contains("MACBOOK")) return "Apple";
        return "Other";
    }

    private static byte[] GetShowroomHtmlBytes()
    {
        if (_showroomHtmlBytes != null) return _showroomHtmlBytes;

        // 1. Check relative development paths
        var devPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "VeloxAPI", "wwwroot", "showroom", "index.html");
        if (File.Exists(devPath))
        {
            _showroomHtmlBytes = File.ReadAllBytes(devPath);
            return _showroomHtmlBytes;
        }

        var localWwwroot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "showroom", "index.html");
        if (File.Exists(localWwwroot))
        {
            _showroomHtmlBytes = File.ReadAllBytes(localWwwroot);
            return _showroomHtmlBytes;
        }

        // 2. Read from assembly embedded resources
        var asm = Assembly.GetExecutingAssembly();
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("showroom_index.html", StringComparison.OrdinalIgnoreCase) ||
                                 n.EndsWith("index.html", StringComparison.OrdinalIgnoreCase));

        if (resourceName != null)
        {
            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                _showroomHtmlBytes = ms.ToArray();
                return _showroomHtmlBytes;
            }
        }

        // 3. Fallback inline HTML
        var fallback = "<!DOCTYPE html><html><head><title>VeloxShowroom</title></head><body style='background:#0D1117;color:#F0F6FC;font-family:sans-serif;padding:40px'><h1>⚡ VELOX SHOWROOM</h1><p>Active on LAN mirror.</p></body></html>";
        _showroomHtmlBytes = Encoding.UTF8.GetBytes(fallback);
        return _showroomHtmlBytes;
    }

    private static async Task SendJsonAsync(HttpListenerResponse res, int statusCode, object data)
    {
        try
        {
            var json = JsonSerializer.Serialize(data);
            var bytes = Encoding.UTF8.GetBytes(json);
            res.ContentType = "application/json";
            res.StatusCode = statusCode;
            res.ContentLength64 = bytes.Length;
            await res.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            res.Close();
        }
        catch { }
    }
}
