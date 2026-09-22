using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Velox.Shared.Dtos;

namespace VeloxManager.Services;

public class LanMirrorServer
{
    private static LanMirrorServer? _instance;
    public static LanMirrorServer Instance => _instance ??= new LanMirrorServer();

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    public bool IsRunning { get; private set; }
    public string ServerAddress { get; private set; } = "Not Running";

    public event Action<string>? LocalBenchActivityReceived;

    public void Start(int port = 9000)
    {
        if (IsRunning) return;

        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://*:{port}/");
            _listener.Start();
            IsRunning = true;
            ServerAddress = $"http://0.0.0.0:{port}/";

            _cts = new CancellationTokenSource();
            _ = ListenLoopAsync(_cts.Token);
        }
        catch (HttpListenerException)
        {
            // If binding to * fails without elevation, bind to localhost
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{port}/");
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

        // CORS headers for LAN clients
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
            var path = req.Url?.AbsolutePath.ToLowerInvariant() ?? "";

            if (path == "/health")
            {
                await SendJsonAsync(res, 200, new { status = "healthy", server = "VeloxManager-LAN-Mirror", version = "1.0.0" });
                return;
            }

            if (path.StartsWith("/api/v1/assets/search") && req.HttpMethod == "GET")
            {
                var q = req.QueryString["q"] ?? "";
                var assets = await ManagerApiClient.Instance.SearchAssetsAsync(q);
                await SendJsonAsync(res, 200, assets);
                return;
            }

            if (path.StartsWith("/api/v1/assets/intake") && req.HttpMethod == "POST")
            {
                using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                var body = await reader.ReadToEndAsync();
                var intake = JsonSerializer.Deserialize<IntakeRequest>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (intake != null)
                {
                    LocalBenchActivityReceived?.Invoke($"Intake: {intake.SerialNumber} ({intake.Model}) from {req.RemoteEndPoint.Address}");
                    // Forward to cloud
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
