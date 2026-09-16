using System;
using System.Collections.Generic;

namespace SuperAutoMater.Wpf.Core
{
    /// <summary>
    /// Lifecycle state of a QC run.
    /// </summary>
    public enum QcRunStatus
    {
        InProgress,
        Completed,
        Aborted
    }

    /// <summary>
    /// The persisted state of a diagnostic result. Do not use a successful-looking
    /// default for measurements that were not collected.
    /// </summary>
    public enum QcTestStatus
    {
        NotStarted,
        Running,
        Passed,
        Failed,
        NotApplicable,
        Skipped,
        ManualOverride
    }

    /// <summary>
    /// Confidence level in the extracted asset identifier.
    /// </summary>
    public enum AssetIdentifierConfidence
    {
        Authoritative,
        Fallback
    }

    public sealed class AssetIdentifier
    {
        public string AssetTag { get; set; } = "";
        public string SerialNumber { get; set; } = "";
        public string AssetUuid { get; set; } = "";
        public string Source { get; set; } = "Win32_BIOS";
        public AssetIdentifierConfidence Confidence { get; set; } = AssetIdentifierConfidence.Authoritative;
    }

    public sealed class QcRunIdentity
    {
        public string AssetTag { get; set; } = "";
        public string SerialNumber { get; set; } = "";
        public string AssetUuid { get; set; } = "";
        public string Model { get; set; } = "";
        public string Technician { get; set; } = "";
        public string Station { get; set; } = "";
        public string PolicyVersion { get; set; } = "default-v1";
        public AssetIdentifierConfidence Confidence { get; set; } = AssetIdentifierConfidence.Authoritative;
        public string IdentifierSource { get; set; } = "Win32_BIOS";
    }

    public sealed class QcOverride
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string RunId { get; set; } = "";
        public string TestKey { get; set; } = "";
        public string Actor { get; set; } = "";
        public string Reason { get; set; } = "";
        public string Approver { get; set; } = "";
        public DateTimeOffset RecordedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    public sealed class QcEvidence
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string RunId { get; set; } = "";
        public string TestKey { get; set; } = "";
        public string EvidenceType { get; set; } = "Log";
        public string FilePath { get; set; } = "";
        public string FileHash { get; set; } = "";
        public string MetadataJson { get; set; } = "";
        public DateTimeOffset RecordedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    public sealed class QcTestResultRecord
    {
        public string TestKey { get; set; } = "";
        public string TestName { get; set; } = "";
        public QcTestStatus Status { get; set; }
        public bool IsAutomated { get; set; }
        public string MetricsJson { get; set; } = "";
        public string OverrideReason { get; set; } = "";
        public string ApprovedBy { get; set; } = "";
        public DateTimeOffset RecordedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Immutable record of an entire QC run including results, overrides, and verification hash.
    /// </summary>
    public sealed class QcRunSummary
    {
        public string RunId { get; set; } = "";
        public string AssetId { get; set; } = "";
        public string SerialNumber { get; set; } = "";
        public string AssetTag { get; set; } = "";
        public string Model { get; set; } = "";
        public string Technician { get; set; } = "";
        public string Station { get; set; } = "";
        public string PolicyVersion { get; set; } = "";
        public QcRunStatus Status { get; set; }
        public string Grade { get; set; } = "PENDING";
        public string VerificationHash { get; set; } = "";
        public DateTimeOffset StartedAtUtc { get; set; }
        public DateTimeOffset? CompletedAtUtc { get; set; }
        public List<QcTestResultRecord> Results { get; set; } = new List<QcTestResultRecord>();
        public List<QcOverride> Overrides { get; set; } = new List<QcOverride>();
        public List<QcEvidence> EvidenceItems { get; set; } = new List<QcEvidence>();
    }
}
