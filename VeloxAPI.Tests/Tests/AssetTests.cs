using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Velox.Shared.Dtos;
using Velox.Shared.Models;
using Xunit;

namespace VeloxAPI.Tests.Tests;

public sealed class AssetTests : IClassFixture<VeloxApiFactory>
{
    private readonly HttpClient _client;
    private readonly string     _apiKey;

    public AssetTests(VeloxApiFactory factory)
    {
        var (_, key) = factory.SetupDepot();
        _apiKey      = key;
        _client      = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
    }

    [Fact]
    public async Task Intake_NewAsset_Returns200WithId()
    {
        var req = new IntakeRequest
        {
            SerialNumber = "SN-TEST-001",
            Model        = "Dell Latitude 5520",
            Actor        = "TestRunner"
        };

        var resp = await _client.PostAsJsonAsync("/api/v1/assets/intake", req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var asset = await resp.Content.ReadFromJsonAsync<AssetDto>();
        Assert.NotNull(asset);
        Assert.NotEmpty(asset!.Id);
        Assert.Equal("SN-TEST-001", asset.SerialNumber);
        Assert.Equal(LifecycleQueue.IntakeStaging, asset.LifecycleQueue);
    }

    [Fact]
    public async Task Intake_SameSerial_Upserts()
    {
        var req = new IntakeRequest { SerialNumber = "SN-UPSERT-001", Model = "HP EliteBook" };
        var r1  = await _client.PostAsJsonAsync("/api/v1/assets/intake", req);
        var a1  = await r1.Content.ReadFromJsonAsync<AssetDto>();

        var req2 = new IntakeRequest { SerialNumber = "SN-UPSERT-001", Model = "HP EliteBook 840 G8" };
        var r2   = await _client.PostAsJsonAsync("/api/v1/assets/intake", req2);
        var a2   = await r2.Content.ReadFromJsonAsync<AssetDto>();

        Assert.Equal(a1!.Id, a2!.Id); // same asset, updated model
    }

    [Fact]
    public async Task Intake_NoIdentifier_Returns400()
    {
        var req  = new IntakeRequest { Model = "NoSerial" };
        var resp = await _client.PostAsJsonAsync("/api/v1/assets/intake", req);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetById_Existing_Returns200()
    {
        var intake = await _client.PostAsJsonAsync("/api/v1/assets/intake",
            new IntakeRequest { SerialNumber = "SN-GET-001" });
        var asset  = await intake.Content.ReadFromJsonAsync<AssetDto>();

        var resp = await _client.GetAsync($"/api/v1/assets/{asset!.Id}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task GetById_Missing_Returns404()
    {
        var resp = await _client.GetAsync("/api/v1/assets/ast-nonexistent");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Patch_UpdateGrade_Persists()
    {
        var intake = await _client.PostAsJsonAsync("/api/v1/assets/intake",
            new IntakeRequest { SerialNumber = "SN-PATCH-001" });
        var asset  = await intake.Content.ReadFromJsonAsync<AssetDto>();

        var patch  = new PatchAssetRequest { Grade = AssetGrade.A, Actor = "Tester" };
        var presp  = await _client.PatchAsJsonAsync($"/api/v1/assets/{asset!.Id}", patch);
        Assert.Equal(HttpStatusCode.OK, presp.StatusCode);

        var updated = await presp.Content.ReadFromJsonAsync<AssetDto>();
        Assert.Equal(AssetGrade.A, updated!.Grade);
    }

    [Fact]
    public async Task Patch_NonExistent_Returns404()
    {
        var patch = new PatchAssetRequest { Grade = AssetGrade.B };
        var resp  = await _client.PatchAsJsonAsync("/api/v1/assets/ast-missing", patch);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Search_BySerial_ReturnsMatch()
    {
        await _client.PostAsJsonAsync("/api/v1/assets/intake",
            new IntakeRequest { SerialNumber = "SN-SEARCH-UNIQUE-999", Model = "Lenovo ThinkPad" });

        var resp  = await _client.GetAsync("/api/v1/assets/search?q=SN-SEARCH-UNIQUE-999");
        var body  = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("total").GetInt32() >= 1);
    }

    [Fact]
    public async Task Search_NoResults_ReturnsZero()
    {
        var resp = await _client.GetAsync("/api/v1/assets/search?q=XYZNOTFOUND99999");
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Timeline_AfterIntake_HasEvent()
    {
        var intake = await _client.PostAsJsonAsync("/api/v1/assets/intake",
            new IntakeRequest { SerialNumber = "SN-TIMELINE-001", Actor = "Bot" });
        var asset  = await intake.Content.ReadFromJsonAsync<AssetDto>();

        var resp   = await _client.GetAsync($"/api/v1/assets/{asset!.Id}/timeline");
        var body   = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("count").GetInt32() >= 1);
    }
}
