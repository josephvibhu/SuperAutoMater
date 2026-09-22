using System.Collections.Concurrent;
using System.Text.Json;
using Velox.Shared.Dtos;
using Velox.Shared.Models;

namespace VeloxAPI.Realtime;

/// <summary>
/// Server-Sent Events hub. Manages all active SSE connections and broadcasts
/// asset lifecycle events to every connected client (VeloxBench, VeloxManager, VeloxLink).
/// Thread-safe — concurrent dictionary + per-client write semaphore.
/// </summary>
public sealed class SseHub
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly ConcurrentDictionary<string, SseClient> _clients = new();

    public int ConnectedCount => _clients.Count;

    /// <summary>Adds a client and blocks until the connection is closed.</summary>
    public async Task ServeAsync(HttpContext ctx, CancellationToken ct)
    {
        var clientId = Guid.NewGuid().ToString("N");
        var client   = new SseClient(ctx.Response, ct);

        ctx.Response.ContentType = "text/event-stream; charset=utf-8";
        ctx.Response.Headers["Cache-Control"]     = "no-cache";
        ctx.Response.Headers["X-Accel-Buffering"] = "no"; // Nginx passthrough
        ctx.Response.Headers["Connection"]        = "keep-alive";
        await ctx.Response.Body.FlushAsync(ct);

        _clients.TryAdd(clientId, client);

        // Send an initial connected event
        await client.SendAsync("connected", new { clientId, serverTime = DateTimeOffset.UtcNow }, ct);

        try
        {
            // Keep the connection open — heartbeat every 25s to prevent proxy timeouts
            while (!ct.IsCancellationRequested && !ctx.RequestAborted.IsCancellationRequested)
            {
                await Task.Delay(25_000, ct);
                await client.SendAsync(":heartbeat", null, ct); // SSE comment = keepalive
            }
        }
        catch (OperationCanceledException) { /* normal disconnect */ }
        finally
        {
            _clients.TryRemove(clientId, out _);
        }
    }

    /// <summary>Broadcasts an event to ALL connected SSE clients.</summary>
    public async Task BroadcastAsync(SseEventType eventType, object payload)
    {
        var dto = new SseEventDto { EventType = eventType, Payload = payload };
        var deadClients = new List<string>();

        foreach (var (id, client) in _clients)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await client.SendAsync(eventType.ToString(), payload, cts.Token);
            }
            catch
            {
                deadClients.Add(id);
            }
        }

        foreach (var id in deadClients)
            _clients.TryRemove(id, out _);
    }

    // ── Inner SSE client ─────────────────────────────────────────────────────

    private sealed class SseClient
    {
        private readonly HttpResponse _response;
        private readonly CancellationToken _ct;
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        public SseClient(HttpResponse response, CancellationToken ct)
        {
            _response = response;
            _ct = ct;
        }

        public async Task SendAsync(string eventName, object? data, CancellationToken ct)
        {
            await _writeLock.WaitAsync(ct);
            try
            {
                string line;
                if (eventName.StartsWith(':'))
                {
                    // SSE comment/heartbeat
                    line = $": heartbeat\n\n";
                }
                else
                {
                    var json = data != null ? JsonSerializer.Serialize(data, JsonOpts) : "{}";
                    line = $"event: {eventName}\ndata: {json}\n\n";
                }
                await _response.WriteAsync(line, ct);
                await _response.Body.FlushAsync(ct);
            }
            finally
            {
                _writeLock.Release();
            }
        }
    }
}
