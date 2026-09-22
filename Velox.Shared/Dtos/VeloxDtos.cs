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
    public string? SerialNumber { get; init; }
    public string? AssetTag { get; init; }
    public string? AssetUuid { get; init; }
    public string? Model { get; init; }
    public string? Location { get; init; }
    public string? Supplier { get; init; }
    public string? Actor { get; init; }
    public string? Notes { get; init; }
    public string? InDate { get; init; }
}

public sealed class PatchAssetRequest
{
    public string? CurrentLocation { get; init; }
    public LifecycleQueue? LifecycleQueue { get; init; }
    public AssetGrade? Grade { get; init; }
    public string? Supplier { get; init; }
    public string? Customer { get; init; }
    public string? MissingComponents { get; init; }
    public string? CosmeticGrade { get; init; }
    public int? BatteryHealth { get; init; }
    public int? StorageHealth { get; init; }
    public string? Notes { get; init; }
    public string? InDate { get; init; }
    public string? OutDate { get; init; }
    public double? PurchasePrice { get; init; }
    public double? SalePrice { get; init; }
    public string? Actor { get; init; }
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
