using System.Net;
using Velox.Shared.Models;
using VeloxAPI.Realtime;
using Xunit;

namespace VeloxAPI.Tests.Tests;

public sealed class SseHubTests : IClassFixture<VeloxApiFactory>
{
    private readonly HttpClient _client;
    private readonly VeloxApiFactory _factory;

    public SseHubTests(VeloxApiFactory factory)
    {
        _factory = factory;
        var (_, key) = factory.SetupDepot("SseTestDepot", "SSE");
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-API-Key", key);
    }

    [Fact]
    public async Task StreamEndpoint_WithoutAuth_Returns401()
    {
        var unauthClient = _factory.CreateClient();
        var resp = await unauthClient.GetAsync("/api/v1/events/stream");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task BroadcastAsync_NoClients_DoesNotThrow()
    {
        var hub = new SseHub();
        Assert.Equal(0, hub.ConnectedCount);

        var ex = await Record.ExceptionAsync(() =>
            hub.BroadcastAsync(SseEventType.AssetUpdated, new { test = "payload" }));

        Assert.Null(ex);
    }
}
