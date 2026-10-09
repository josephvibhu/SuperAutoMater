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


