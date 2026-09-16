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

    /// <summary>
    /// Seven-state warehouse lifecycle queue.
    /// </summary>
    public enum AssetQueueStatus
    {
        ReadyForTest,
        InTest,
        Hold,
        Repair,
        Retest,
        ReadyForRelease,
        Disposed
    }

    /// <summary>
    /// Source stream for intake batching.
    /// </summary>
    public enum IntakeSourceStream
    {
        LeaseReturn,
        TradeIn,
        OfficeDecommission,
        EWasteRecycle,
        WarrantyRMA,
        Other
    }

    /// <summary>
    /// Power adapter and charging accessories intake confirmation.
    /// </summary>
    public enum ChargerConfirmationStatus
    {
        OemChargerPresent,
        ThirdPartyCharger,
        NoChargerMissing,
        UsbCPowerDeliveryBenchTested
    }

    /// <summary>
    /// Audit-tracked custody lifecycle transitions.
    /// </summary>
    public enum CustodyEventType
    {
        Receive,
        Move,
        Hold,
        RepairStart,
        RepairComplete,
        RetestQueued,
        ReleaseStaged,
        Disposed
    }

    /// <summary>
    /// Scan-first intake parameters for newly arrived assets.
    /// </summary>
    public sealed class AssetIntakeRequest
    {
        public string AssetTag { get; set; } = "";
        public string SerialNumber { get; set; } = "";
        public string AssetUuid { get; set; } = "";
        public string Model { get; set; } = "";
        public string IntakeBatchId { get; set; } = "";
        public IntakeSourceStream SourceStream { get; set; } = IntakeSourceStream.TradeIn;
        public string InitialLocation { get; set; } = "INTAKE-STAGING";
        public ChargerConfirmationStatus ChargerStatus { get; set; } = ChargerConfirmationStatus.NoChargerMissing;
        public string TestProfileId { get; set; } = "standard-refurb-v1";
        public string Technician { get; set; } = "OPERATOR";
        public string Notes { get; set; } = "";
    }

    /// <summary>
    /// Append-only custody and location audit record.
    /// </summary>
    public sealed class CustodyEventRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string AssetId { get; set; } = "";
        public CustodyEventType EventType { get; set; } = CustodyEventType.Receive;
        public string Location { get; set; } = "";
        public string Actor { get; set; } = "";
        public string ReasonCode { get; set; } = "";
        public string BatchId { get; set; } = "";
        public string Notes { get; set; } = "";
        public bool ScanConfirmed { get; set; } = true;
        public DateTimeOffset RecordedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Live WIP record representing an asset on the floor with its current queue and location.
    /// </summary>
    public sealed class AssetWipRecord
    {
        public string AssetId { get; set; } = "";
        public string SerialNumber { get; set; } = "";
        public string AssetTag { get; set; } = "";
        public string Model { get; set; } = "";
        public string CurrentLocation { get; set; } = "";
        public AssetQueueStatus LifecycleQueue { get; set; } = AssetQueueStatus.ReadyForTest;
        public string IntakeBatchId { get; set; } = "";
        public string SourceStream { get; set; } = "";
        public string ChargerStatus { get; set; } = "";
        public string TestProfileId { get; set; } = "";
        public string LatestRunId { get; set; } = "";
        public string LatestRunGrade { get; set; } = "";
        public string LatestRunStatus { get; set; } = "";
        public string LatestVerificationHash { get; set; } = "";
        public int StorageHealth { get; set; } = 100;
        public string WorkInProgress { get; set; } = "All Okay";
        public string Supplier { get; set; } = "";
        public string Customer { get; set; } = "";
        public string InDate { get; set; } = "";
        public string OutDate { get; set; } = "";
        public string Remarks { get; set; } = "";
        public DateTimeOffset CreatedAtUtc { get; set; }
        public DateTimeOffset UpdatedAtUtc { get; set; }
    }

    /// <summary>
    /// Complete historical device journey combining intake, QC runs, overrides, photos, and custody events.
    /// </summary>
    public sealed class AssetJourneySummary
    {
        public AssetWipRecord Asset { get; set; }
        public List<QcRunSummary> Runs { get; set; } = new List<QcRunSummary>();
        public List<CustodyEventRecord> CustodyEvents { get; set; } = new List<CustodyEventRecord>();
        public List<QcEvidence> EvidenceItems { get; set; } = new List<QcEvidence>();
    }
}

