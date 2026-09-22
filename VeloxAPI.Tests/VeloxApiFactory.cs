using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VeloxAPI.Data;

namespace VeloxAPI.Tests;

/// <summary>
/// Shared test factory — creates an in-process test server with a fresh in-memory SQLite DB per test class.
/// </summary>
public sealed class VeloxApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = "test_" + Guid.NewGuid().ToString("N");
    private (string TenantId, string ApiKey)? _cachedTenant;
    private readonly object _tenantLock = new();

    public VeloxApiFactory()
    {
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Replace real VeloxDb with an in-memory instance
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(VeloxDb));
            if (descriptor != null) services.Remove(descriptor);

            var dbName = _dbName; // capture for lambda
            services.AddSingleton(_ => VeloxDb.CreateInMemory(dbName));
        });

        builder.UseSetting("VELOX_JWT_SECRET",   "test-jwt-secret-that-is-long-enough-32c");
        builder.UseSetting("VELOX_ADMIN_SECRET", "test-admin-secret");
    }

    /// <summary>Convenience: creates (or returns cached) tenant + API key for test requests.</summary>
    public (string TenantId, string ApiKey) SetupDepot(string name = "TestDepot", string siteCode = "TEST")
    {
        lock (_tenantLock)
        {
            if (_cachedTenant != null) return _cachedTenant.Value;
            var db = Services.GetRequiredService<VeloxDb>();
            _cachedTenant = db.CreateTenantWithKey(name, siteCode + "_" + Guid.NewGuid().ToString("N")[..6], "test-key");
            return _cachedTenant.Value;
        }
    }
}
