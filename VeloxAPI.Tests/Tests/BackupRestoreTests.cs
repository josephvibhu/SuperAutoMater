using System.Net.Http.Json;
using Velox.Shared.Dtos;
using Velox.Shared.Models;
using Xunit;

namespace VeloxAPI.Tests.Tests;

public class BackupRestoreTests : IClassFixture<VeloxApiFactory>
{
    private readonly HttpClient _client;

    public BackupRestoreTests(VeloxApiFactory factory)
    {
        var (_, apiKey) = factory.SetupDepot();
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
    }

    [Fact]
    public async Task Backup_Purge_Restore_Workflow_WorksCleanly()
    {
        // 1. Intake a test asset
        var intakeRes = await _client.PostAsJsonAsync("/api/v1/assets/intake", new IntakeRequest
        {
            SerialNumber = "SN-BACKUP-TEST-999",
            AssetTag = "TAG-BK-999",
            Model = "Backup Verification Model",
            Location = "Shelf B-4",
            Actor = "Test Runner",
            Category = AssetCategory.Laptop
        });
        intakeRes.EnsureSuccessStatusCode();

        // 2. Fetch backup
        var backupRes = await _client.GetAsync("/api/v1/admin/backup");
        backupRes.EnsureSuccessStatusCode();
        var backup = await backupRes.Content.ReadFromJsonAsync<DatabaseBackupDto>();
        Assert.NotNull(backup);
        Assert.Contains(backup.Assets, a => a.SerialNumber == "SN-BACKUP-TEST-999");

        // 3. Purge all assets
        var purgeRes = await _client.PostAsJsonAsync("/api/v1/admin/purge-assets", new { });
        purgeRes.EnsureSuccessStatusCode();
        var purgeResp = await purgeRes.Content.ReadFromJsonAsync<PurgeAssetsResponse>();
        Assert.NotNull(purgeResp);
        Assert.True(purgeResp.Success);

        // Verify assets are 0
        var listAfterPurge = await _client.GetFromJsonAsync<AssetListWrapper>("/api/v1/assets");
        Assert.NotNull(listAfterPurge);
        Assert.Empty(listAfterPurge.Items);

        // 4. Restore from backup
        var restoreRes = await _client.PostAsJsonAsync("/api/v1/admin/restore", backup);
        restoreRes.EnsureSuccessStatusCode();
        var restoreResp = await restoreRes.Content.ReadFromJsonAsync<RestoreBackupResponse>();
        Assert.NotNull(restoreResp);
        Assert.True(restoreResp.Success);
        Assert.True(restoreResp.RestoredAssets >= 1);

        // Verify asset is back with its location
        var listAfterRestore = await _client.GetFromJsonAsync<AssetListWrapper>("/api/v1/assets");
        Assert.NotNull(listAfterRestore);
        var restoredAsset = listAfterRestore.Items.FirstOrDefault(a => a.SerialNumber == "SN-BACKUP-TEST-999");
        Assert.NotNull(restoredAsset);
        Assert.Equal("Shelf B-4", restoredAsset.CurrentLocation);
    }

    private sealed class AssetListWrapper
    {
        public int Count { get; set; }
        public List<AssetDto> Items { get; set; } = new();
    }
}
