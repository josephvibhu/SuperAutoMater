using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Velox.Shared.Dtos;
using Xunit;

namespace VeloxAPI.Tests.Tests;

public class AlphaLicensingAndUpdateTests : IClassFixture<VeloxApiFactory>
{
    private readonly HttpClient _client;

    public AlphaLicensingAndUpdateTests(VeloxApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ValidateLicense_DefaultAlphaKey_ReturnsValidWithOfflineLease()
    {
        var req = new LicenseValidateRequest
        {
            LicenseKey = "VLX-ALPHA-TEST-2026",
            MachineName = "BENCH-QC-01",
            MachineGuid = "GUID-TEST-1234",
            AppVersion = "1.6.10"
        };

        var response = await _client.PostAsJsonAsync("/api/v1/license/validate", req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<LicenseValidateResponse>();
        Assert.NotNull(result);
        Assert.True(result.IsValid);
        Assert.Equal("Alpha-Pilot", result.PlanTier);
        Assert.False(string.IsNullOrWhiteSpace(result.SignedLeaseToken));
        Assert.Contains(".", result.SignedLeaseToken);
        Assert.True(result.DaysRemaining > 0);
    }

    [Fact]
    public async Task ValidateLicense_InvalidKey_Returns403()
    {
        var req = new LicenseValidateRequest
        {
            LicenseKey = "VLX-INVALID-FAKE-KEY-9999",
            MachineName = "BENCH-02",
            MachineGuid = "GUID-9999",
            AppVersion = "1.6.10"
        };

        var response = await _client.PostAsJsonAsync("/api/v1/license/validate", req);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<LicenseValidateResponse>();
        Assert.NotNull(result);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task CreateLicense_AndValidate_Succeeds()
    {
        var createReq = new LicenseCreateRequest
        {
            CompanyName = "OmniRefurb Massive Facility",
            PlanTier = "Alpha-Pilot",
            MaxDevicesMonth = 2500,
            ValidDays = 60,
            Notes = "Partner A Alpha Agreement"
        };

        var adminMsg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/license/create");
        adminMsg.Headers.Add("X-Admin-Secret", "test-admin-secret");
        adminMsg.Content = JsonContent.Create(createReq);

        var createResp = await _client.SendAsync(adminMsg);
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode);

        using var doc = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync());
        var licKey = doc.RootElement.GetProperty("license").GetProperty("licenseKey").GetString();
        Assert.NotNull(licKey);
        Assert.StartsWith("VLX-ALPHA-", licKey);

        // Validate the newly created license
        var valReq = new LicenseValidateRequest
        {
            LicenseKey = licKey,
            MachineName = "OMNI-BENCH-01",
            MachineGuid = "OMNI-GUID-1",
            AppVersion = "1.6.10"
        };

        var valResp = await _client.PostAsJsonAsync("/api/v1/license/validate", valReq);
        Assert.Equal(HttpStatusCode.OK, valResp.StatusCode);
        var valResult = await valResp.Content.ReadFromJsonAsync<LicenseValidateResponse>();
        Assert.NotNull(valResult);
        Assert.True(valResult.IsValid);
        Assert.Equal("OmniRefurb Massive Facility", valResult.CompanyName);
    }

    [Fact]
    public async Task UpdateSystem_PublishAndCheck_ReturnsUpdateAvailable()
    {
        // Publish v1.6.11
        var pubReq = new PublishReleaseRequest
        {
            AppName = "VeloxBench",
            Version = "1.6.11",
            Channel = "alpha",
            DownloadUrl = "/api/v1/update/download/latest",
            FileSha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            FileSizeBytes = 52428800,
            IsMandatory = false,
            ReleaseNotes = "Alpha 1.6.11: High throughput direct cloud sync and offline lease."
        };

        var adminMsg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/update/publish");
        adminMsg.Headers.Add("X-Admin-Secret", "test-admin-secret");
        adminMsg.Content = JsonContent.Create(pubReq);

        var pubResp = await _client.SendAsync(adminMsg);
        Assert.Equal(HttpStatusCode.OK, pubResp.StatusCode);

        // Check from current v1.6.10
        var checkResp = await _client.GetAsync("/api/v1/update/check?appName=VeloxBench&currentVersion=1.6.10&channel=alpha");
        Assert.Equal(HttpStatusCode.OK, checkResp.StatusCode);

        var checkResult = await checkResp.Content.ReadFromJsonAsync<UpdateCheckResponse>();
        Assert.NotNull(checkResult);
        Assert.True(checkResult.UpdateAvailable);
        Assert.Equal("1.6.11", checkResult.LatestVersion);
        Assert.Equal("alpha", checkResult.Channel);
        Assert.False(string.IsNullOrWhiteSpace(checkResult.DownloadUrl));
    }

    [Fact]
    public async Task BenchSync_BatchIngest_ReplacesGimmickyWebhook()
    {
        var batch = new BenchSyncBatchRequest
        {
            LicenseKey = "VLX-ALPHA-TEST-2026",
            MachineName = "BENCH-01",
            AppVersion = "1.6.10",
            Events = new List<BenchSyncItemDto>
            {
                new BenchSyncItemDto
                {
                    EventId = "evt-batch-001",
                    EventType = "AssetQcCompleted",
                    AggregateId = "SN-ALPHA-TEST-LAPTOP-01",
                    IdempotencyKey = "idem-001",
                    Timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        Model = "Dell Latitude 5420",
                        BatteryHealth = 89,
                        StorageHealth = 100,
                        Grade = "Grade A",
                        Notes = "Refurbishment QC Passed 8 Subsystems"
                    })
                },
                new BenchSyncItemDto
                {
                    EventId = "evt-batch-002",
                    EventType = "AssetQcCompleted",
                    AggregateId = "SN-ALPHA-TEST-LAPTOP-02",
                    IdempotencyKey = "idem-002",
                    Timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        Model = "Lenovo ThinkPad T14 Gen 2",
                        BatteryHealth = 94,
                        StorageHealth = 98,
                        Grade = "Grade A+",
                        Notes = "Thermal Paste Replaced & Cleaned"
                    })
                }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/v1/bench/sync", batch);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<BenchSyncResponse>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(2, result.IngestedCount);

        // Verify the asset is queryable via standard Asset API with X-License-Key
        var lookupMsg = new HttpRequestMessage(HttpMethod.Get, "/api/v1/assets/lookup?q=SN-ALPHA-TEST-LAPTOP-01");
        lookupMsg.Headers.Add("X-License-Key", "VLX-ALPHA-TEST-2026");

        var assetResp = await _client.SendAsync(lookupMsg);
        Assert.Equal(HttpStatusCode.OK, assetResp.StatusCode);
        using var doc = JsonDocument.Parse(await assetResp.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("found").GetBoolean());
        var asset = doc.RootElement.GetProperty("asset");
        Assert.Equal("Dell Latitude 5420", asset.GetProperty("model").GetString());
        Assert.Equal(89, asset.GetProperty("batteryHealth").GetInt32());
    }
}
