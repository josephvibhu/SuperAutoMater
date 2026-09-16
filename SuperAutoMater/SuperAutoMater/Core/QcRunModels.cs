using System;

namespace SuperAutoMater.Wpf.Core
{
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

    public sealed class QcRunIdentity
    {
        public string AssetTag { get; set; } = "";
        public string SerialNumber { get; set; } = "";
        public string Model { get; set; } = "";
        public string Technician { get; set; } = "";
        public string Station { get; set; } = "";
        public string PolicyVersion { get; set; } = "default-v1";
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
}
