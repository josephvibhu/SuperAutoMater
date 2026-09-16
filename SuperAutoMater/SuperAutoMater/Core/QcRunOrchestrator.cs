using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SuperAutoMater.Wpf.Core
{
    /// <summary>
    /// Coordinates the QC run lifecycle, ensuring all diagnostic tests, policies,
    /// manual overrides, and cryptographic completion seals adhere to audit standards.
    /// </summary>
    public sealed class QcRunOrchestrator
    {
        private static readonly Lazy<QcRunOrchestrator> _instance =
            new Lazy<QcRunOrchestrator>(() => new QcRunOrchestrator());

        public static QcRunOrchestrator Instance => _instance.Value;

        private readonly QcRunStore _store;
        private readonly QcPolicy _policy;
        private readonly object _gate = new object();

        public string CurrentRunId { get; private set; }
        public QcRunIdentity CurrentIdentity { get; private set; }
        public QcPolicy Policy => _policy;
        public bool IsRunActive => !string.IsNullOrEmpty(CurrentRunId);

        public QcRunOrchestrator(QcRunStore store = null, QcPolicy policy = null)
        {
            _store = store ?? new QcRunStore();
            _policy = policy ?? QcPolicyConfig.Load();
        }

        public string InitializeRun(QcRunIdentity identity, bool forceFreshRun = false)
        {
            if (identity == null) throw new ArgumentNullException(nameof(identity));

            lock (_gate)
            {
                identity.PolicyVersion = _policy.PolicyId + "@v" + _policy.Version;
                CurrentIdentity = identity;

                // Check for interrupted run recovery unless forced fresh
                if (!forceFreshRun)
                {
                    var asset = _store.FindAssetBySerialOrTag(identity.SerialNumber);
                    if (asset == null && !string.IsNullOrEmpty(identity.AssetTag))
                    {
                        asset = _store.FindAssetBySerialOrTag(identity.AssetTag);
                    }

                    if (asset == null || asset.LifecycleQueue != AssetQueueStatus.Retest)
                    {
                        string existingRunId = _store.FindActiveRunForAsset(identity.SerialNumber);
                        if (string.IsNullOrEmpty(existingRunId) && !string.IsNullOrEmpty(identity.AssetTag))
                        {
                            existingRunId = _store.FindActiveRunForAsset(identity.AssetTag);
                        }

                        if (!string.IsNullOrEmpty(existingRunId))
                        {
                            CurrentRunId = existingRunId;
                            AppLogger.Info($"Resumed active QC run '{existingRunId}' for asset '{identity.SerialNumber}'");
                            return existingRunId;
                        }
                    }
                }

                CurrentRunId = _store.StartRun(identity);
                AppLogger.Info($"Initialized fresh QC run '{CurrentRunId}' with policy '{identity.PolicyVersion}'");
                return CurrentRunId;
            }
        }

        public void RecordTestStarted(string testKey, string testName, bool isAutomated = true)
        {
            EnsureRun();
            _store.RecordResult(CurrentRunId, new QcTestResultRecord
            {
                TestKey = testKey,
                TestName = testName ?? testKey,
                Status = QcTestStatus.Running,
                IsAutomated = isAutomated,
                RecordedAtUtc = DateTimeOffset.UtcNow
            });
        }

        public void RecordTestPassed(string testKey, string testName, bool isAutomated = true, string metricsJson = null)
        {
            EnsureRun();
            _store.RecordResult(CurrentRunId, new QcTestResultRecord
            {
                TestKey = testKey,
                TestName = testName ?? testKey,
                Status = QcTestStatus.Passed,
                IsAutomated = isAutomated,
                MetricsJson = metricsJson ?? "",
                RecordedAtUtc = DateTimeOffset.UtcNow
            });
        }

        public void RecordTestFailed(string testKey, string testName, string failureReason, string metricsJson = null)
        {
            EnsureRun();
            _store.RecordResult(CurrentRunId, new QcTestResultRecord
            {
                TestKey = testKey,
                TestName = testName ?? testKey,
                Status = QcTestStatus.Failed,
                IsAutomated = false,
                OverrideReason = failureReason ?? "Test failed verification criteria.",
                MetricsJson = metricsJson ?? "",
                RecordedAtUtc = DateTimeOffset.UtcNow
            });
        }

        public void RecordTestSkipped(string testKey, string testName, string reason)
        {
            EnsureRun();
            _store.RecordResult(CurrentRunId, new QcTestResultRecord
            {
                TestKey = testKey,
                TestName = testName ?? testKey,
                Status = QcTestStatus.Skipped,
                IsAutomated = false,
                OverrideReason = reason ?? "Skipped by technician.",
                RecordedAtUtc = DateTimeOffset.UtcNow
            });
        }

        public void RecordTestNotApplicable(string testKey, string testName, string reason)
        {
            EnsureRun();
            _store.RecordResult(CurrentRunId, new QcTestResultRecord
            {
                TestKey = testKey,
                TestName = testName ?? testKey,
                Status = QcTestStatus.NotApplicable,
                IsAutomated = true,
                OverrideReason = reason ?? "Hardware feature not present.",
                RecordedAtUtc = DateTimeOffset.UtcNow
            });
        }

        public bool RecordTestOverride(string testKey, string reason, string actor, string approver, out string error)
        {
            error = "";
            EnsureRun();

            if (string.IsNullOrWhiteSpace(reason))
            {
                error = "An explicit reason is required to override a diagnostic test.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(actor))
            {
                error = "Technician identification is required to record an override.";
                return false;
            }

            if (_policy.RequiresSupervisorApproval(testKey) && string.IsNullOrWhiteSpace(approver))
            {
                error = $"Policy requires supervisor sign-off to override test '{testKey}'.";
                return false;
            }

            var ov = new QcOverride
            {
                RunId = CurrentRunId,
                TestKey = testKey,
                Actor = actor,
                Reason = reason,
                Approver = approver ?? "",
                RecordedAtUtc = DateTimeOffset.UtcNow
            };

            _store.RecordOverride(CurrentRunId, ov);
            return true;
        }

        public void RecordEvidence(string testKey, string evidenceType, string filePath, string fileHash, string metadataJson = null)
        {
            EnsureRun();
            _store.RecordEvidence(CurrentRunId, new QcEvidence
            {
                RunId = CurrentRunId,
                TestKey = testKey,
                EvidenceType = evidenceType,
                FilePath = filePath,
                FileHash = fileHash,
                MetadataJson = metadataJson ?? "",
                RecordedAtUtc = DateTimeOffset.UtcNow
            });
        }

        public bool TryCompleteRun(
            int batteryHealthPercent,
            int storageHealthPercent,
            out string failureReason,
            out QcRunSummary completedSummary)
        {
            failureReason = "";
            completedSummary = null;

            EnsureRun();

            var current = _store.GetRunSummary(CurrentRunId);
            if (current == null)
            {
                failureReason = "QC Run record not found in local store.";
                return false;
            }

            // Check policy: Required tests
            var resultsByKey = current.Results.ToDictionary(r => r.TestKey, StringComparer.OrdinalIgnoreCase);

            foreach (var req in _policy.RequiredTests)
            {
                if (!resultsByKey.TryGetValue(req, out var rec))
                {
                    failureReason = $"Mandatory test '{req}' has not been executed.";
                    return false;
                }

                if (rec.Status == QcTestStatus.NotStarted || rec.Status == QcTestStatus.Running)
                {
                    failureReason = $"Mandatory test '{req}' is still in progress or incomplete.";
                    return false;
                }

                if (rec.Status == QcTestStatus.Failed || rec.Status == QcTestStatus.Skipped)
                {
                    failureReason = $"Test '{req}' is {rec.Status}. Must be resolved or manually overridden with approval.";
                    return false;
                }
            }

            bool hasOverrides = current.Overrides.Count > 0 || current.Results.Any(r => r.Status == QcTestStatus.ManualOverride);
            string calculatedGrade = _policy.EvaluateGrade(batteryHealthPercent, storageHealthPercent, hasOverrides);

            // Calculate immutable cryptographic verification hash
            string verificationHash = CalculateVerificationHash(current, calculatedGrade);

            if (!_store.TryCompleteRun(CurrentRunId, verificationHash, calculatedGrade, out failureReason))
            {
                return false;
            }

            completedSummary = _store.GetRunSummary(CurrentRunId);
            return true;
        }

        public QcRunSummary GetCurrentSummary()
        {
            if (string.IsNullOrEmpty(CurrentRunId)) return null;
            return _store.GetRunSummary(CurrentRunId);
        }

        public static string CalculateVerificationHash(QcRunSummary run, string grade)
        {
            if (run == null) return "";

            var sb = new StringBuilder();
            sb.Append(run.RunId).Append('|')
              .Append(run.AssetId).Append('|')
              .Append(run.SerialNumber ?? "").Append('|')
              .Append(run.PolicyVersion ?? "").Append('|')
              .Append(run.Technician ?? "").Append('|')
              .Append(run.Station ?? "").Append('|')
              .Append(grade ?? run.Grade ?? "").Append('|');

            // Ordered results
            var sortedResults = run.Results.OrderBy(r => r.TestKey, StringComparer.Ordinal).ToList();
            foreach (var r in sortedResults)
            {
                sb.Append(r.TestKey).Append(':')
                  .Append(r.Status).Append(':')
                  .Append(r.OverrideReason ?? "").Append(';');
            }
            sb.Append('|');

            // Ordered overrides
            var sortedOverrides = run.Overrides.OrderBy(o => o.TestKey, StringComparer.Ordinal).ToList();
            foreach (var o in sortedOverrides)
            {
                sb.Append(o.TestKey).Append(':')
                  .Append(o.Actor).Append(':')
                  .Append(o.Reason).Append(':')
                  .Append(o.Approver ?? "").Append(';');
            }

            using var sha = SHA256.Create();
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            return BitConverter.ToString(bytes).Replace("-", "").ToUpperInvariant();
        }

        public static bool VerifyRunTamper(QcRunSummary run)
        {
            if (run == null || string.IsNullOrWhiteSpace(run.VerificationHash)) return false;
            string expected = CalculateVerificationHash(run, run.Grade);
            return string.Equals(expected, run.VerificationHash, StringComparison.OrdinalIgnoreCase);
        }

        private void EnsureRun()
        {
            if (string.IsNullOrEmpty(CurrentRunId))
            {
                throw new InvalidOperationException("No active QC run initialized. Call InitializeRun first.");
            }
        }
    }
}
