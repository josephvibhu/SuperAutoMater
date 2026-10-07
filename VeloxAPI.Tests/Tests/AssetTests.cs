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

    [Fact]
    public async Task FittedComponents_AttachUntrackedAndTracked_Succeeds()
    {
        // 1. Create parent laptop
        var laptopResp = await _client.PostAsJsonAsync("/api/v1/assets/intake",
            new IntakeRequest { SerialNumber = "LAP-BOM-001", Model = "ThinkPad T14", Category = AssetCategory.Laptop });
        var laptop = await laptopResp.Content.ReadFromJsonAsync<AssetDto>();
        Assert.NotNull(laptop);

        // 2. Attach untracked free-form RAM spec (no stock needed)
        var ramAttach = await _client.PostAsJsonAsync($"/api/v1/assets/{laptop!.Id}/components", new AttachComponentRequest
        {
            Category = AssetCategory.RAM,
            Description = "Crucial 16GB DDR4 3200MHz",
            SerialNumber = "RAM-SN-001",
            PriceDelta = 2500
        });
        Assert.Equal(HttpStatusCode.OK, ramAttach.StatusCode);
        var fittedRam = await ramAttach.Content.ReadFromJsonAsync<FittedComponentDto>();
        Assert.NotNull(fittedRam);
        Assert.Equal("Crucial 16GB DDR4 3200MHz", fittedRam!.Description);
        Assert.Equal(2500, fittedRam.PriceDelta);

        // 3. Create tracked SSD in stock with quantity = 2
        var ssdResp = await _client.PostAsJsonAsync("/api/v1/assets/intake",
            new IntakeRequest { SerialNumber = "SSD-STOCK-001", Model = "Samsung 980 500GB NVMe", Category = AssetCategory.Storage, Quantity = 2 });
        var ssd = await ssdResp.Content.ReadFromJsonAsync<AssetDto>();
        Assert.NotNull(ssd);
        Assert.Equal(2, ssd!.Quantity);

        // 4. Attach tracked SSD to laptop -> should decrement stock to 1
        var ssdAttach = await _client.PostAsJsonAsync($"/api/v1/assets/{laptop.Id}/components", new AttachComponentRequest
        {
            ComponentAssetId = ssd.Id,
            Category = AssetCategory.Storage,
            PriceDelta = 3500
        });
        Assert.Equal(HttpStatusCode.OK, ssdAttach.StatusCode);
        var fittedSsd = await ssdAttach.Content.ReadFromJsonAsync<FittedComponentDto>();
        Assert.NotNull(fittedSsd);
        Assert.Equal(ssd.Id, fittedSsd!.ComponentAssetId);

        // Verify stock decremented
        var getSsd = await _client.GetAsync($"/api/v1/assets/{ssd.Id}");
        var updatedSsd = await getSsd.Content.ReadFromJsonAsync<AssetDto>();
        Assert.Equal(1, updatedSsd!.Quantity);

        // 5. List components for laptop
        var listResp = await _client.GetAsync($"/api/v1/assets/{laptop.Id}/components");
        var listBody = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, listBody.GetProperty("count").GetInt32());

        // 6. Detach SSD with returnToStock=true -> stock should restore to 2
        var detachResp = await _client.DeleteAsync($"/api/v1/assets/{laptop.Id}/components/{fittedSsd.Id}?returnToStock=true");
        Assert.Equal(HttpStatusCode.OK, detachResp.StatusCode);

        var getRestoredSsd = await _client.GetAsync($"/api/v1/assets/{ssd.Id}");
        var restoredSsd = await getRestoredSsd.Content.ReadFromJsonAsync<AssetDto>();
        Assert.Equal(2, restoredSsd!.Quantity);
    }
}
