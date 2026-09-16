using System.Text.Json.Serialization;

namespace ItamService.Models;

// ─── Tenant / Auth ───────────────────────────────────────────────────────────

public sealed class Tenant
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string SiteCode { get; init; } = "";
    public DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed class ApiKeyRecord
{
    public string Id { get; init; } = "";
    public string TenantId { get; init; } = "";
    public string KeyHash { get; init; } = "";
    public string Label { get; init; } = "";
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? RevokedAtUtc { get; init; }
    public bool IsActive => RevokedAtUtc is null;
}

// Populated by ApiKeyMiddleware and shared via DI
public sealed class TenantContext
{
    public string TenantId { get; set; } = "";
    public string SiteCode { get; set; } = "";
}

// ─── Asset ───────────────────────────────────────────────────────────────────

public sealed class ItamAsset
{
    public string Id { get; init; } = "";
    public string TenantId { get; init; } = "";
    public string SerialNumber { get; init; } = "";
    public string AssetTag { get; init; } = "";
    public string AssetUuid { get; init; } = "";
    public string Model { get; init; } = "";
    public string CurrentLocation { get; set; } = "";
    public string LifecycleQueue { get; set; } = "Intake";
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

// ─── Events ──────────────────────────────────────────────────────────────────

public sealed class AssetEvent
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string AssetId { get; init; } = "";
    public string TenantId { get; init; } = "";
    public string EventType { get; init; } = "";   // Intake, QcPassed, QcFailed, Moved, Held, Released, Disposed
    public string Location { get; init; } = "";
    public string Actor { get; init; } = "";
    public string NotesJson { get; init; } = "{}";
    public string SourceBenchId { get; init; } = "";
    public DateTimeOffset RecordedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

// ─── Component Observations ───────────────────────────────────────────────────

/// <summary>
/// Immutable snapshot of a single component attribute at a point in time.
/// Never updated after write — new observations append a new row.
/// </summary>
public sealed class ComponentObservation
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string AssetId { get; init; } = "";
    public string TenantId { get; init; } = "";
    /// <summary>Disk | RAM | Battery | Display | Motherboard | Firmware</summary>
    public string ComponentType { get; init; } = "";
    /// <summary>E.g. serial_number, capacity_gb, health_percent, firmware_version</summary>
    public string AttributeKey { get; init; } = "";
    public string AttributeValue { get; init; } = "";
    /// <summary>WmiQuery | SmartReport | DmiDecode | BenchManual</summary>
    public string Source { get; init; } = "";
    /// <summary>High | Medium | Low</summary>
    public string Confidence { get; init; } = "High";
    public string BenchId { get; init; } = "";
    public DateTimeOffset ObservedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

// ─── Differences ─────────────────────────────────────────────────────────────

/// <summary>
/// Neutral record of a detected difference between two observations of the same attribute.
/// Uses the term "difference detected" — never "theft" or accusatory language.
/// </summary>
public sealed class ComponentDifference
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string AssetId { get; init; } = "";
    public string TenantId { get; init; } = "";
    public string ComponentType { get; init; } = "";
    public string AttributeKey { get; init; } = "";
    public string ObservationAId { get; init; } = "";
    public string ObservationBId { get; init; } = "";
    public string ValueA { get; init; } = "";
    public string ValueB { get; init; } = "";
    public string SourceA { get; init; } = "";
    public string SourceB { get; init; } = "";
    public string ConfidenceA { get; init; } = "";
    public string ConfidenceB { get; init; } = "";
    public DateTimeOffset DetectedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public bool ApprovedReplacement { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
}

// ─── Reports ─────────────────────────────────────────────────────────────────

public sealed class AssetReturnReport
{
    public string ReportId { get; init; } = Guid.NewGuid().ToString("N");
    public string AssetId { get; init; } = "";
    public string TenantId { get; init; } = "";
    public string SiteCode { get; init; } = "";
    public ItamAsset? Asset { get; init; }
    public List<AssetEvent> Timeline { get; init; } = [];
    public List<ComponentObservation> LatestObservations { get; init; } = [];
    public List<ComponentDifference> Differences { get; init; } = [];
    public int UnresolvedDifferences => Differences.Count(d => !d.ApprovedReplacement);
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>HMAC-SHA256 over canonical JSON of this report (excluding this field).</summary>
    public string HmacSignature { get; set; } = "";
}

// ─── Request / Response DTOs ─────────────────────────────────────────────────

public sealed class IntakeRequest
{
    public string SerialNumber { get; set; } = "";
    [JsonPropertyName("serial_number")]
    public string? SerialNumberSnake { set => SerialNumber = value ?? SerialNumber; }

    public string AssetTag { get; set; } = "";
    [JsonPropertyName("asset_tag")]
    public string? AssetTagSnake { set => AssetTag = value ?? AssetTag; }

    public string? AssetUuid { get; set; }
    [JsonPropertyName("asset_uuid")]
    public string? AssetUuidSnake { set => AssetUuid = value ?? AssetUuid; }

    public string? Model { get; set; }

    public string? Location { get; set; }

    public string? Actor { get; set; }

    public string? Notes { get; set; }
}

public sealed class AppendEventRequest
{
    public string EventType { get; set; } = "";
    [JsonPropertyName("event_type")]
    public string? EventTypeSnake { set => EventType = value ?? EventType; }

    public string? Location { get; set; }

    public string? Actor { get; set; }

    public string? Notes { get; set; }

    public string? SourceBenchId { get; set; }
    [JsonPropertyName("source_bench_id")]
    public string? SourceBenchIdSnake { set => SourceBenchId = value ?? SourceBenchId; }
}

public sealed class ObservationBatchRequest
{
    public string? BenchId { get; set; }
    [JsonPropertyName("bench_id")]
    public string? BenchIdSnake { set => BenchId = value ?? BenchId; }

    public List<ObservationItem> Observations { get; set; } = [];
}

public sealed class ObservationItem
{
    public string ComponentType { get; set; } = "";
    [JsonPropertyName("component_type")]
    public string? ComponentTypeSnake { set => ComponentType = value ?? ComponentType; }

    public string AttributeKey { get; set; } = "";
    [JsonPropertyName("attribute_key")]
    public string? AttributeKeySnake { set => AttributeKey = value ?? AttributeKey; }

    public string AttributeValue { get; set; } = "";
    [JsonPropertyName("attribute_value")]
    public string? AttributeValueSnake { set => AttributeValue = value ?? AttributeValue; }

    public string? Source { get; set; }

    public string? Confidence { get; set; }
}

public sealed class ApproveReplacementRequest
{
    public string ApprovedBy { get; set; } = "";
    [JsonPropertyName("approved_by")]
    public string? ApprovedBySnake { set => ApprovedBy = value ?? ApprovedBy; }

    public string? Notes { get; set; }
}

public sealed class CreateTenantRequest
{
    public string Name { get; set; } = "";

    public string SiteCode { get; set; } = "";
    [JsonPropertyName("site_code")]
    public string? SiteCodeSnake { set => SiteCode = value ?? SiteCode; }

    public string FirstKeyLabel { get; set; } = "";
    [JsonPropertyName("first_key_label")]
    public string? FirstKeyLabelSnake { set => FirstKeyLabel = value ?? FirstKeyLabel; }
}

public sealed class IssueKeyRequest
{
    public string Label { get; set; } = "";
}

public sealed record ApiKeyIssuedResponse(
    string KeyId,
    string TenantId,
    string PlainTextKey,   // shown once only
    string Label,
    DateTimeOffset CreatedAtUtc
);
