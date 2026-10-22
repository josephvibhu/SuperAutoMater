using Velox.Shared.Models;

namespace Velox.Shared.Dtos;

// ─── Asset ───────────────────────────────────────────────────────────────────

public sealed class AssetDto
{
    public string Id { get; init; } = "";
    public string TenantId { get; init; } = "";
    public string SerialNumber { get; init; } = "";
    public string AssetTag { get; init; } = "";
    public string AssetUuid { get; init; } = "";
    public string Model { get; init; } = "";
    public string CurrentLocation { get; set; } = "";
    public LifecycleQueue LifecycleQueue { get; set; } = LifecycleQueue.IntakeStaging;
    public AssetCategory Category { get; set; } = AssetCategory.Laptop;
    public int Quantity { get; set; } = 1;
    public AssetGrade Grade { get; set; } = AssetGrade.Unknown;
    public string Supplier { get; set; } = "";
    public string Customer { get; set; } = "";
    public string AssignedEmployeeId { get; set; } = "";
    public string AssignedEmployeeName { get; set; } = "";
    public double? PurchasePrice { get; set; }
    public double? SalePrice { get; set; }
    public string MissingComponents { get; set; } = "";
    public string CosmeticGrade { get; set; } = "";
    public int? BatteryHealth { get; set; }
    public int? StorageHealth { get; set; }
    public string Notes { get; set; } = "";
    public string SiliconDnaHash { get; set; } = "";
    public string HardwareQuorumJson { get; set; } = "{}";
    public string InDate { get; set; } = "";
    public string OutDate { get; set; } = "";
    public string LatestEventType { get; set; } = "";
    public string HoldCustomer { get; set; } = "";
    public string HoldSalesRep { get; set; } = "";
    public string? HoldExpiresAtUtc { get; set; }
    public bool IsOnHold => !string.IsNullOrWhiteSpace(HoldExpiresAtUtc) && DateTimeOffset.TryParse(HoldExpiresAtUtc, out var exp) && exp > DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

// ─── Employee ─────────────────────────────────────────────────────────────────

public sealed class EmployeeDto
{
    public string Id { get; init; } = "";
    public string TenantId { get; init; } = "";
    public string Name { get; init; } = "";
    public EmployeeRole Role { get; init; } = EmployeeRole.Technician;
    public bool IsActive { get; init; } = true;
    public string StationId { get; set; } = "BENCH-01";
    public int WorkloadCount { get; set; }
    public int FinishedCount { get; set; }
    public string CurrentActivity { get; set; } = "Standby";
    public DateTimeOffset CreatedAtUtc { get; init; }
}

// ─── Asset Event ──────────────────────────────────────────────────────────────

public sealed class AssetEventDto
{
    public string Id { get; init; } = "";
    public string AssetId { get; init; } = "";
    public string TenantId { get; init; } = "";
    public string EventType { get; init; } = "";
    public string Location { get; init; } = "";
    public string Actor { get; init; } = "";
    public string NotesJson { get; init; } = "{}";
    public string SourceBenchId { get; init; } = "";
    public DateTimeOffset RecordedAtUtc { get; init; }
}

// ─── Job ──────────────────────────────────────────────────────────────────────

public sealed class JobDto
{
    public string AssetId { get; init; } = "";
    public string AssetTag { get; init; } = "";
    public string SerialNumber { get; init; } = "";
    public string Model { get; init; } = "";
    public LifecycleQueue Queue { get; init; }
    public AssetGrade Grade { get; init; }
    public string Notes { get; init; } = "";
    public DateTimeOffset AssignedAtUtc { get; init; }
}

// ─── SSE Event ───────────────────────────────────────────────────────────────

public sealed class SseEventDto
{
    public SseEventType EventType { get; init; }
    public object? Payload { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

// ─── Request / Response bodies ───────────────────────────────────────────────

public sealed class IntakeRequest
{
    public string? SerialNumber { get; set; }
    public string? AssetTag { get; set; }
    public string? AssetUuid { get; set; }
    public string? Model { get; set; }
    public AssetCategory? Category { get; set; }
    public int? Quantity { get; set; }
    public double? PurchasePrice { get; set; }
    public double? SalePrice { get; set; }
    public string? Location { get; set; }
    public string? Supplier { get; set; }
    public string? Actor { get; set; }
    public string? Notes { get; set; }
    public string? InDate { get; set; }
}

public sealed class PatchAssetRequest
{
    public string? CurrentLocation { get; set; }
    public LifecycleQueue? LifecycleQueue { get; set; }
    public AssetCategory? Category { get; set; }
    public int? Quantity { get; set; }
    public AssetGrade? Grade { get; set; }
    public string? Supplier { get; set; }
    public string? Customer { get; set; }
    public string? MissingComponents { get; set; }
    public string? CosmeticGrade { get; set; }
    public int? BatteryHealth { get; set; }
    public int? StorageHealth { get; set; }
    public string? Notes { get; set; }
    public string? InDate { get; set; }
    public string? OutDate { get; set; }
    public double? PurchasePrice { get; set; }
    public double? SalePrice { get; set; }
    public string? HoldCustomer { get; set; }
    public string? HoldSalesRep { get; set; }
    public string? HoldExpiresAtUtc { get; set; }
    public bool? BypassQualityGate { get; set; }
    public string? Actor { get; set; }
}

public sealed class HoldAssetRequest
{
    public string CustomerName { get; init; } = "";
    public string SalesRep { get; init; } = "";
    public int DurationMinutes { get; init; } = 120; // Default: 2 hours
}

public sealed class AppendEventRequest
{
    public string EventType { get; init; } = "";
    public string? Location { get; init; }
    public string? Actor { get; init; }
    public string? NotesJson { get; init; }
    public string? SourceBenchId { get; init; }
}

public sealed class AssignRequest
{
    public string EmployeeId { get; init; } = "";
    public string? Actor { get; init; }
}

public sealed class CreateEmployeeRequest
{
    public string Name { get; init; } = "";
    public string Pin { get; init; } = "";
    public EmployeeRole Role { get; init; } = EmployeeRole.Technician;
    public string? StationId { get; init; } = "BENCH-01";
}

public sealed class PatchEmployeeRequest
{
    public string? Name { get; init; }
    public string? Pin { get; init; }
    public EmployeeRole? Role { get; init; }
    public bool? IsActive { get; init; }
    public string? StationId { get; init; }
}

public sealed class LoginRequest
{
    public string Name { get; init; } = "";
    public string Pin { get; init; } = "";
}

public sealed class LoginResponse
{
    public string Token { get; init; } = "";
    public string EmployeeId { get; init; } = "";
    public string Name { get; init; } = "";
    public EmployeeRole Role { get; init; }
    public string StationId { get; init; } = "BENCH-01";
    public DateTimeOffset ExpiresAtUtc { get; init; }
}

// ─── Refurbishment Fitted Components (Optional BOM) ──────────────────────────

public sealed class FittedComponentDto
{
    public string Id { get; init; } = "";
    public string TenantId { get; init; } = "";
    public string ParentAssetId { get; init; } = "";
    public string? ComponentAssetId { get; set; }
    public AssetCategory Category { get; set; } = AssetCategory.RAM;
    public string Description { get; set; } = "";
    public string? SerialNumber { get; set; }
    public double CostDelta { get; set; } = 0.0;
    public double PriceDelta { get; set; } = 0.0;
    public string? InstalledBy { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset InstalledAtUtc { get; init; }
}

public sealed class AttachComponentRequest
{
    public string? ComponentAssetId { get; set; } // Optional: ID of tracked inventory asset
    public AssetCategory Category { get; set; } = AssetCategory.RAM;
    public string Description { get; set; } = "";
    public string? SerialNumber { get; set; }
    public double CostDelta { get; set; } = 0.0;
    public double PriceDelta { get; set; } = 0.0;
    public string? InstalledBy { get; set; }
    public string? Notes { get; set; }
}

public sealed class DetachComponentRequest
{
    public bool ReturnToStock { get; set; } = false;
    public string? Condition { get; set; } = "Used / Tested";
    public double? RestockPurchaseCost { get; set; }
    public double? RestockSalePrice { get; set; }
}

// ─── Technician Performance & Tracking ────────────────────────────────────────

public sealed class TechnicianActivityDto
{
    public string Id { get; init; } = "";
    public string AssetId { get; init; } = "";
    public string AssetTag { get; init; } = "";
    public string SerialNumber { get; init; } = "";
    public string Model { get; init; } = "";
    public string EventType { get; init; } = "";
    public string Location { get; init; } = "";
    public string Actor { get; init; } = "";
    public string NotesJson { get; init; } = "{}";
    public DateTimeOffset RecordedAtUtc { get; init; }
}

public sealed class TechnicianPerformanceDto
{
    public string EmployeeId { get; init; } = "";
    public string Name { get; init; } = "";
    public EmployeeRole Role { get; init; } = EmployeeRole.Technician;
    public string StationId { get; init; } = "BENCH-01";
    public bool IsActive { get; init; } = true;
    public int ActiveAssignedCount { get; set; }
    public int CompletedTodayCount { get; set; }
    public int CompletedAllTimeCount { get; set; }
    public int TotalFixesCount { get; set; }
    public int ComponentsFittedCount { get; set; }
    public string CurrentStatusText { get; set; } = "Standby";
    public string LastActionText { get; set; } = "None";
    public DateTimeOffset? LastActiveUtc { get; set; }
    public List<TechnicianActivityDto> RecentActivity { get; set; } = new();
    public List<AssetDto> AssignedAssets { get; set; } = new();
}

// ─── Cloud Database Operations & Backup ───────────────────────────────────────

public sealed class DatabaseBackupDto
{
    public string Version { get; set; } = "1.0";
    public string ExportedAtUtc { get; set; } = "";
    public string TenantId { get; set; } = "";
    public List<AssetDto> Assets { get; set; } = new();
    public List<FittedComponentDto> Components { get; set; } = new();
    public List<AssetEventDto> Events { get; set; } = new();
    public List<EmployeeDto> Employees { get; set; } = new();
}

public sealed class RestoreBackupResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public int RestoredAssets { get; set; }
    public int RestoredComponents { get; set; }
    public int RestoredEvents { get; set; }
    public int RestoredEmployees { get; set; }
}

public sealed class PurgeAssetsResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public int PurgedAssets { get; set; }
}

// ─── Alpha Licensing System ──────────────────────────────────────────────────

public sealed class LicenseValidateRequest
{
    public string LicenseKey { get; set; } = "";
    public string MachineName { get; set; } = "";
    public string MachineGuid { get; set; } = "";
    public string AppVersion { get; set; } = "";
}

public sealed class LicenseValidateResponse
{
    public bool IsValid { get; set; }
    public string CompanyName { get; set; } = "";
    public string PlanTier { get; set; } = "Starter";
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public int DaysRemaining { get; set; }
    public int OfflineGraceDays { get; set; } = 14;
    public int DevicesUsedMonth { get; set; }
    public int MaxDevicesMonth { get; set; }
    public int Month1MigrationQuota { get; set; }
    public bool IsMonth1MigrationActive { get; set; }
    public int MaxWorkstationSeats { get; set; } = 3;
    public int WorkstationSeatsUsed { get; set; } = 1;
    public List<string> AllowedFeatures { get; set; } = new();
    public string SignedLeaseToken { get; set; } = "";
    public string Message { get; set; } = "";
}

public sealed class LicenseCreateRequest
{
    public string CompanyName { get; set; } = "";
    public string? CustomKey { get; set; }
    public string PlanTier { get; set; } = "Starter";
    public int MaxDevicesMonth { get; set; } = 100;
    public int Month1MigrationQuota { get; set; } = 1000;
    public int MaxWorkstationSeats { get; set; } = 3;
    public int ValidDays { get; set; } = 30;
    public List<string>? AllowedFeatures { get; set; }
    public string? Notes { get; set; }
}

public sealed class LicenseStatusDto
{
    public string Id { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string LicenseKey { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string PlanTier { get; set; } = "Starter";
    public int MaxDevicesMonth { get; set; }
    public int Month1MigrationQuota { get; set; }
    public bool IsMonth1MigrationActive { get; set; }
    public int MaxWorkstationSeats { get; set; }
    public int WorkstationSeatsUsed { get; set; }
    public int DevicesUsedMonth { get; set; }
    public string MonthPeriod { get; set; } = "";
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public bool IsActive { get; set; }
    public List<string> AllowedFeatures { get; set; } = new();
    public string Notes { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class TenantProvisionRequest
{
    public string CompanyName { get; set; } = "";
    public string? SiteCode { get; set; }
    public string PlanTier { get; set; } = "Starter"; // BenchSolo, Starter, Pro, Enterprise
    public string? CustomKey { get; set; }
    public int? CustomMonthlyQuota { get; set; }
    public int? CustomMonth1Quota { get; set; }
    public int? CustomSeats { get; set; }
    public int ValidDays { get; set; } = 30;
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? Notes { get; set; }
}

public sealed class TenantProvisionResponse
{
    public bool Success { get; set; }
    public string TenantId { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string LicenseKey { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string PlanTier { get; set; } = "";
    public int MonthlyQuota { get; set; }
    public int Month1MigrationQuota { get; set; }
    public int MaxWorkstationSeats { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public string TenantDbFile { get; set; } = "";
    public string Message { get; set; } = "";
}

// ─── Dynamic Auto-Update System ──────────────────────────────────────────────

public sealed class UpdateCheckRequest
{
    public string AppName { get; set; } = "VeloxBench";
    public string CurrentVersion { get; set; } = "1.0.0";
    public string Channel { get; set; } = "alpha";
}

public sealed class UpdateCheckResponse
{
    public bool UpdateAvailable { get; set; }
    public string LatestVersion { get; set; } = "";
    public string Channel { get; set; } = "alpha";
    public bool IsMandatory { get; set; }
    public string DownloadUrl { get; set; } = "";
    public string FileSha256 { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public string ReleaseNotes { get; set; } = "";
    public DateTimeOffset PublishedAtUtc { get; set; }
}

public sealed class PublishReleaseRequest
{
    public string AppName { get; set; } = "VeloxBench";
    public string Version { get; set; } = "";
    public string Channel { get; set; } = "alpha";
    public string DownloadUrl { get; set; } = "";
    public string FileSha256 { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public bool IsMandatory { get; set; }
    public string ReleaseNotes { get; set; } = "";
}

// ─── High-Throughput Bench Cloud Sync ────────────────────────────────────────

public sealed class BenchSyncBatchRequest
{
    public string LicenseKey { get; set; } = "";
    public string MachineName { get; set; } = "";
    public string AppVersion { get; set; } = "";
    public List<BenchSyncItemDto> Events { get; set; } = new();
}

public sealed class BenchSyncItemDto
{
    public string EventId { get; set; } = "";
    public string EventType { get; set; } = "";
    public string AggregateId { get; set; } = "";
    public string SiliconDnaHash { get; set; } = "";
    public string HardwareQuorumJson { get; set; } = "{}";
    public string IdempotencyKey { get; set; } = "";
    public string Timestamp { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";
}

public sealed class BenchSyncResponse
{
    public bool Success { get; set; }
    public int IngestedCount { get; set; }
    public string Message { get; set; } = "";
}



