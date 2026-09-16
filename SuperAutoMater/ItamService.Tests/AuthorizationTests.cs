using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ItamService.Data;
using ItamService.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ItamService.Tests;

/// <summary>
/// Authorization and tenant isolation integration tests.
/// Uses WebApplicationFactory with an in-memory SQLite database.
/// </summary>
public sealed class AuthorizationTests : IClassFixture<ItamTestFactory>
{
    private readonly ItamTestFactory _factory;

    public AuthorizationTests(ItamTestFactory factory)
    {
        _factory = factory;
    }

    // ── 1. Missing key returns 401 ────────────────────────────────────────────
    [Fact]
    public async Task Request_WithNoApiKey_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/assets");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── 2. Invalid key returns 401 ────────────────────────────────────────────
    [Fact]
    public async Task Request_WithInvalidApiKey_Returns401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "this-is-not-valid");
        var response = await client.GetAsync("/api/v1/assets");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── 3. Valid key returns 200 ─────────────────────────────────────────────
    [Fact]
    public async Task Request_WithValidKey_ReturnsSuccess()
    {
        var (client, _) = await _factory.CreateTenantClientAsync("SITE-A", "Test Site A");
        var response = await client.GetAsync("/api/v1/assets");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── 4. Cross-tenant isolation: Tenant A cannot see Tenant B assets ────────
    [Fact]
    public async Task TenantA_CannotAccess_TenantB_Asset()
    {
        var (clientA, _) = await _factory.CreateTenantClientAsync("SITE-ISO-A", "Isolation Site A");
        var (clientB, _) = await _factory.CreateTenantClientAsync("SITE-ISO-B", "Isolation Site B");

        // Site B intakes an asset
        var intakeResp = await clientB.PostAsJsonAsync("/api/v1/assets/intake", new
        {
            serial_number = "SN-CROSS-TENANT-TEST",
            asset_tag     = "TAG-B-001",
            location      = "SITE-B-WAREHOUSE"
        });
        Assert.Equal(HttpStatusCode.OK, intakeResp.StatusCode);
        var asset = await intakeResp.Content.ReadFromJsonAsync<ItamAsset>();
        Assert.NotNull(asset);

        // Site A tries to fetch Site B''s asset by ID — should not find it
        var resp = await clientA.GetAsync($"/api/v1/assets/{asset!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);

        // Site A should also get empty list (not see Site B''s asset)
        var listResp = await clientA.GetAsync("/api/v1/assets");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var body = await listResp.Content.ReadFromJsonAsync<AssetListResponse>();
        Assert.DoesNotContain(body!.Assets, a => a.Id == asset.Id);
    }

    // ── 5. Revoked key returns 401 ────────────────────────────────────────────
    [Fact]
    public async Task RevokedKey_Returns401()
    {
        var (client, keyId) = await _factory.CreateTenantClientAsync("SITE-REVOKE", "Revoke Test Site");

        // Confirm it works first
        var before = await client.GetAsync("/api/v1/assets");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        // Revoke via admin
        var adminClient = _factory.CreateAdminClient();
        var tenantId = _factory.GetTenantIdBySiteCode("SITE-REVOKE");
        var revokeResp = await adminClient.DeleteAsync($"/api/v1/admin/tenants/{tenantId}/keys/{keyId}");
        Assert.Equal(HttpStatusCode.OK, revokeResp.StatusCode);

        // Now the same key should be rejected
        var after = await client.GetAsync("/api/v1/assets");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    // ── 6. Cross-tenant observation injection is rejected ─────────────────────
    [Fact]
    public async Task TenantA_CannotInjectObservation_IntoTenantB_Asset()
    {
        var (clientA, _) = await _factory.CreateTenantClientAsync("SITE-OBS-A", "Obs Site A");
        var (clientB, _) = await _factory.CreateTenantClientAsync("SITE-OBS-B", "Obs Site B");

        // Site B creates asset
        var intakeResp = await clientB.PostAsJsonAsync("/api/v1/assets/intake", new
        {
            serial_number = "SN-OBS-INJECT-TEST",
            asset_tag     = "TAG-OBS-B"
        });
        var assetB = await intakeResp.Content.ReadFromJsonAsync<ItamAsset>();

        // Site A tries to post observations to Site B''s asset
        var obsResp = await clientA.PostAsJsonAsync($"/api/v1/assets/{assetB!.Id}/observations", new
        {
            observations = new[] { new { component_type = "Disk", attribute_key = "serial_number", attribute_value = "INJECTED" } }
        });

        // Must return 404 (asset not visible to Tenant A) — not 200
        Assert.Equal(HttpStatusCode.NotFound, obsResp.StatusCode);
    }

    // ── 7. Full lifecycle end-to-end: intake → events → observations → diff → approve → report ──
    [Fact]
    public async Task FullLifecycle_IntakeToReport_WithApprovedDifference()
    {
        var (client, _) = await _factory.CreateTenantClientAsync("SITE-E2E", "E2E Test Site");

        // 1. Intake
        var intake = await client.PostAsJsonAsync("/api/v1/assets/intake", new
        {
            serial_number = "SN-LIFECYCLE-001",
            model         = "ThinkPad X1 Carbon",
            asset_tag     = "TAG-E2E-001",
            location      = "INTAKE-STAGING",
            actor         = "tech-01"
        });
        Assert.Equal(HttpStatusCode.OK, intake.StatusCode);
        var asset = await intake.Content.ReadFromJsonAsync<ItamAsset>();
        Assert.NotNull(asset);

        // 2. Append lifecycle events
        await client.PostAsJsonAsync($"/api/v1/assets/{asset!.Id}/events", new { event_type = "QcStarted", location = "BENCH-03", actor = "tech-01" });
        await client.PostAsJsonAsync($"/api/v1/assets/{asset.Id}/events", new { event_type = "QcPassed",  location = "BENCH-03", actor = "tech-01" });
        await client.PostAsJsonAsync($"/api/v1/assets/{asset.Id}/events", new { event_type = "Moved",     location = "RELEASE-SHELF", actor = "mgr-01" });

        // 3. First observation set (initial component baseline)
        var obs1 = await client.PostAsJsonAsync($"/api/v1/assets/{asset.Id}/observations", new
        {
            bench_id = "BENCH-03",
            observations = new[]
            {
                new { component_type = "Disk",        attribute_key = "serial_number",   attribute_value = "WD-ORIGINAL-SN",   source = "SmartReport", confidence = "High" },
                new { component_type = "Disk",        attribute_key = "capacity_gb",     attribute_value = "512",              source = "SmartReport", confidence = "High" },
                new { component_type = "RAM",         attribute_key = "total_gb",        attribute_value = "16",               source = "WmiQuery",    confidence = "High" },
                new { component_type = "Battery",     attribute_key = "health_percent",  attribute_value = "92",               source = "WmiQuery",    confidence = "Medium" },
                new { component_type = "Motherboard", attribute_key = "uuid",            attribute_value = "4C4C4544-ORIG",    source = "DmiDecode",   confidence = "High" }
            }
        });
        var obs1Body = await obs1.Content.ReadFromJsonAsync<ObsBatchResponse>();
        Assert.Equal(0, obs1Body!.DifferencesDetected); // No previous — no diffs

        // 4. Second observation — one attribute changed (disk serial)
        var obs2 = await client.PostAsJsonAsync($"/api/v1/assets/{asset.Id}/observations", new
        {
            bench_id = "BENCH-07",
            observations = new[]
            {
                new { component_type = "Disk",        attribute_key = "serial_number",   attribute_value = "WD-REPLACED-SN",   source = "SmartReport", confidence = "High" },
                new { component_type = "Disk",        attribute_key = "capacity_gb",     attribute_value = "512",              source = "SmartReport", confidence = "High" },
                new { component_type = "Battery",     attribute_key = "health_percent",  attribute_value = "91",               source = "WmiQuery",    confidence = "Medium" }
            }
        });
        var obs2Body = await obs2.Content.ReadFromJsonAsync<ObsBatchResponse>();
        // disk serial_number changed → 1 diff; capacity unchanged → 0; battery changed → 1 diff
        Assert.Equal(2, obs2Body!.DifferencesDetected);

        // 5. Check differences
        var diffsResp = await client.GetAsync($"/api/v1/assets/{asset.Id}/differences");
        var diffsBody = await diffsResp.Content.ReadFromJsonAsync<DiffsResponse>();
        Assert.Equal(2, diffsBody!.Unresolved);

        // 6. Approve the disk serial replacement
        var diskDiff = diffsBody.Differences.First(d => d.AttributeKey == "serial_number");
        var approve  = await client.PostAsJsonAsync(
            $"/api/v1/assets/{asset.Id}/differences/{diskDiff.Id}/approve",
            new { approved_by = "mgr-01", notes = "HDD swapped during repair." });
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        // 7. Verify only 1 unresolved remains
        var diffsAfter = await client.GetAsync($"/api/v1/assets/{asset.Id}/differences");
        var diffsAfterBody = await diffsAfter.Content.ReadFromJsonAsync<DiffsResponse>();
        Assert.Equal(1, diffsAfterBody!.Unresolved);
        Assert.Equal(1, diffsAfterBody.Resolved);

        // 8. Timeline should have all events
        var timeline = await client.GetAsync($"/api/v1/assets/{asset.Id}/timeline");
        var tlBody = await timeline.Content.ReadFromJsonAsync<TimelineResponse>();
        Assert.True(tlBody!.Count >= 4);  // intake + 3 events

        // 9. Report is generated with HMAC signature
        var report = await client.GetAsync($"/api/v1/assets/{asset.Id}/report");
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        var reportBody = await report.Content.ReadFromJsonAsync<ReportResponse>();
        Assert.NotEmpty(reportBody!.HmacSignature);
        Assert.Equal(1, reportBody.UnresolvedDifferences);

        // 10. HTML report is served
        var html = await client.GetAsync($"/api/v1/assets/{asset.Id}/report/html");
        Assert.Equal(HttpStatusCode.OK, html.StatusCode);
        Assert.Equal("text/html; charset=utf-8", html.Content.Headers.ContentType?.ToString());
    }

    // ── Response shape helpers ────────────────────────────────────────────────
    private sealed record AssetListResponse(List<ItamAsset> Assets, int Count);
    private sealed record ObsBatchResponse(int DifferencesDetected);
    private sealed record DiffsResponse(int Total, int Unresolved, int Resolved, List<ComponentDifference> Differences);
    private sealed record TimelineResponse(int Count, List<AssetEvent> Events);
    private sealed record ReportResponse(string HmacSignature, int UnresolvedDifferences);
}

/// <summary>
/// WebApplicationFactory that substitutes an in-memory ItamDb.
/// </summary>
public sealed class ItamTestFactory : WebApplicationFactory<Program>
{
    private readonly ItamDb _db = ItamDb.CreateInMemory();
    private const string AdminKey = "test-admin-key-12345";

    // Track issued keys per site code for revocation tests
    private readonly Dictionary<string, (string TenantId, string KeyId, string PlainKey)> _siteMap = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Replace the singleton ItamDb with our shared in-memory instance
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ItamDb));
            if (descriptor is not null) services.Remove(descriptor);
            services.AddSingleton(_db);
        });

        builder.UseEnvironment("Testing");
        builder.UseSetting("ITAM_ADMIN_KEY", AdminKey);

        // Set env var so middleware can read it
        Environment.SetEnvironmentVariable("ITAM_ADMIN_KEY", AdminKey);
    }

    public HttpClient CreateAdminClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AdminKey);
        return client;
    }

    public async Task<(HttpClient Client, string KeyId)> CreateTenantClientAsync(string siteCode, string name)
    {
        if (_siteMap.TryGetValue(siteCode, out var cached))
        {
            var c = CreateClient();
            c.DefaultRequestHeaders.Add("X-Api-Key", cached.PlainKey);
            return (c, cached.KeyId);
        }

        // Create via admin endpoint
        var admin = CreateAdminClient();
        var resp  = await admin.PostAsJsonAsync("/api/v1/admin/tenants", new
        {
            name, site_code = siteCode, first_key_label = "test-key"
        });
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadFromJsonAsync<TenantCreateResponse>();
        _siteMap[siteCode] = (body!.Tenant.Id, body.ApiKey.KeyId, body.ApiKey.PlainTextKey);

        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", body.ApiKey.PlainTextKey);
        return (client, body.ApiKey.KeyId);
    }

    public string GetTenantIdBySiteCode(string siteCode) =>
        _siteMap.TryGetValue(siteCode, out var val) ? val.TenantId : "";

    private sealed record TenantCreateResponse(Tenant Tenant, ApiKeyIssuedResponse ApiKey);
}
