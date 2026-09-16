using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace SuperAutoMater.Wpf.Core
{
    public sealed class OutboxEventRecord
    {
        public string Id { get; set; } = "";
        public string EventType { get; set; } = "";
        public string AggregateId { get; set; } = "";
        public string IdempotencyKey { get; set; } = "";
        public string PayloadJson { get; set; } = "";
        public DateTimeOffset CreatedAtUtc { get; set; }
        public DateTimeOffset? DispatchedAtUtc { get; set; }
        public int Attempts { get; set; }
        public string LastError { get; set; }
    }

    /// <summary>
    /// Local system of record for QC runs. SQLite is deliberately separate from the
    /// UI so a later server sync can use the outbox without reading WPF state.
    /// </summary>
    public sealed class QcRunStore
    {
        private static readonly HashSet<string> GenericSerialBlacklist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "default string",
            "to be filled by o.e.m.",
            "to be filled by oem",
            "none",
            "system serial number",
            "chassis serial number",
            "base board serial number",
            "1234567890",
            "00000000",
            "0123456789",
            "not applicable",
            "unknown",
            "detecting...",
            "all okay",
            "n/a"
        };

        private readonly string _connectionString;
        private readonly string _dbPath;
        private readonly object _gate = new object();

        public string DatabasePath => _dbPath;

        public QcRunStore(string databasePath = null)
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SuperAutoMater");
            if (!Directory.Exists(root))
            {
                Directory.CreateDirectory(root);
            }
            _dbPath = databasePath ?? Path.Combine(root, "superautomater-qc.db");
            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = _dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            }.ToString();
            Initialize();
        }

        public static QcRunStore CreateForTest(string testDbPath)
        {
            return new QcRunStore(testDbPath);
        }

        public static QcRunStore CreateInMemory(string inMemoryDbName = null)
        {
            string name = inMemoryDbName ?? ("test_" + Guid.NewGuid().ToString("N"));
            return new QcRunStore($"Data Source={name};Mode=Memory;Cache=Shared");
        }

        public void Initialize()
        {
            lock (_gate)
            {
                using var connection = Open();
                Execute(connection, "PRAGMA journal_mode=WAL;");
                Execute(connection, "PRAGMA foreign_keys=ON;");

                // Migration 1: Base Tables
                Execute(connection, @"
CREATE TABLE IF NOT EXISTS schema_migrations (version INTEGER PRIMARY KEY, applied_at_utc TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS assets (
    id TEXT PRIMARY KEY,
    serial_number TEXT NULL COLLATE NOCASE,
    asset_tag TEXT NULL COLLATE NOCASE,
    model TEXT NULL,
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_assets_serial_nonempty ON assets(serial_number) WHERE serial_number IS NOT NULL AND serial_number <> '';
CREATE TABLE IF NOT EXISTS qc_runs (
    id TEXT PRIMARY KEY,
    asset_id TEXT NOT NULL,
    technician TEXT NULL,
    station TEXT NULL,
    policy_version TEXT NOT NULL,
    status TEXT NOT NULL,
    started_at_utc TEXT NOT NULL,
    completed_at_utc TEXT NULL,
    FOREIGN KEY(asset_id) REFERENCES assets(id)
);
CREATE INDEX IF NOT EXISTS ix_qc_runs_asset_started ON qc_runs(asset_id, started_at_utc DESC);
CREATE TABLE IF NOT EXISTS qc_test_results (
    id TEXT PRIMARY KEY,
    qc_run_id TEXT NOT NULL,
    test_key TEXT NOT NULL,
    test_name TEXT NOT NULL,
    status TEXT NOT NULL,
    is_automated INTEGER NOT NULL,
    metrics_json TEXT NULL,
    override_reason TEXT NULL,
    approved_by TEXT NULL,
    recorded_at_utc TEXT NOT NULL,
    FOREIGN KEY(qc_run_id) REFERENCES qc_runs(id),
    UNIQUE(qc_run_id, test_key)
);
CREATE TABLE IF NOT EXISTS sync_outbox (
    id TEXT PRIMARY KEY,
    event_type TEXT NOT NULL,
    aggregate_id TEXT NOT NULL,
    idempotency_key TEXT NOT NULL UNIQUE,
    payload_json TEXT NOT NULL,
    created_at_utc TEXT NOT NULL,
    dispatched_at_utc TEXT NULL,
    attempts INTEGER NOT NULL DEFAULT 0,
    last_error TEXT NULL
);
INSERT OR IGNORE INTO schema_migrations(version, applied_at_utc) VALUES(1, CURRENT_TIMESTAMP);");

                // Migration 2: Evidence, Overrides, Custody & Audit Enhancements
                Execute(connection, @"
CREATE TABLE IF NOT EXISTS evidence (
    id TEXT PRIMARY KEY,
    qc_run_id TEXT NOT NULL,
    test_key TEXT NULL,
    evidence_type TEXT NOT NULL,
    file_path TEXT NULL,
    file_hash TEXT NULL,
    metadata_json TEXT NULL,
    recorded_at_utc TEXT NOT NULL,
    FOREIGN KEY(qc_run_id) REFERENCES qc_runs(id)
);
CREATE INDEX IF NOT EXISTS ix_evidence_run ON evidence(qc_run_id);

CREATE TABLE IF NOT EXISTS overrides (
    id TEXT PRIMARY KEY,
    qc_run_id TEXT NOT NULL,
    test_key TEXT NOT NULL,
    actor TEXT NOT NULL,
    reason TEXT NOT NULL,
    approver TEXT NULL,
    recorded_at_utc TEXT NOT NULL,
    FOREIGN KEY(qc_run_id) REFERENCES qc_runs(id)
);
CREATE INDEX IF NOT EXISTS ix_overrides_run ON overrides(qc_run_id);

CREATE TABLE IF NOT EXISTS custody_events (
    id TEXT PRIMARY KEY,
    asset_id TEXT NOT NULL,
    event_type TEXT NOT NULL,
    location TEXT NULL,
    actor TEXT NOT NULL,
    notes TEXT NULL,
    recorded_at_utc TEXT NOT NULL,
    FOREIGN KEY(asset_id) REFERENCES assets(id)
);
CREATE INDEX IF NOT EXISTS ix_custody_asset ON custody_events(asset_id);
");

                // Safe Alter Table for SQLite columns
                AddColumnIfNotExists(connection, "assets", "asset_uuid", "TEXT NULL");
                AddColumnIfNotExists(connection, "assets", "serial_confidence", "TEXT NULL DEFAULT 'Authoritative'");
                AddColumnIfNotExists(connection, "assets", "serial_source", "TEXT NULL DEFAULT 'Win32_BIOS'");
                AddColumnIfNotExists(connection, "qc_runs", "verification_hash", "TEXT NULL");
                AddColumnIfNotExists(connection, "qc_runs", "grade", "TEXT NULL");

                Execute(connection, "INSERT OR IGNORE INTO schema_migrations(version, applied_at_utc) VALUES(2, CURRENT_TIMESTAMP);");
            }
        }

        public void BackupDatabase(string destinationPath)
        {
            if (string.IsNullOrWhiteSpace(destinationPath))
                throw new ArgumentException("Destination path is required.", nameof(destinationPath));

            lock (_gate)
            {
                string destDir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                using var source = Open();
                var destBuilder = new SqliteConnectionStringBuilder
                {
                    DataSource = destinationPath,
                    Mode = SqliteOpenMode.ReadWriteCreate
                };
                using var dest = new SqliteConnection(destBuilder.ToString());
                dest.Open();
                source.BackupDatabase(dest);
                AppLogger.Info($"SQLite database successfully backed up to {destinationPath}");
            }
        }

        public string StartRun(QcRunIdentity identity)
        {
            if (identity == null) throw new ArgumentNullException(nameof(identity));
            lock (_gate)
            {
                using var connection = Open();
                using var transaction = connection.BeginTransaction();
                var now = UtcNow();
                var assetId = FindOrCreateAsset(connection, transaction, identity, now);
                var runId = Guid.NewGuid().ToString("N");
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"INSERT INTO qc_runs(id, asset_id, technician, station, policy_version, status, started_at_utc)
VALUES($id, $assetId, $technician, $station, $policy, 'InProgress', $started);";
                    command.Parameters.AddWithValue("$id", runId);
                    command.Parameters.AddWithValue("$assetId", assetId);
                    command.Parameters.AddWithValue("$technician", identity.Technician ?? "");
                    command.Parameters.AddWithValue("$station", identity.Station ?? "");
                    command.Parameters.AddWithValue("$policy", string.IsNullOrWhiteSpace(identity.PolicyVersion) ? "default-v1" : identity.PolicyVersion);
                    command.Parameters.AddWithValue("$started", now);
                    command.ExecuteNonQuery();
                }
                AddOutboxEvent(connection, transaction, "QcRunStarted", runId, "{\"runId\":\"" + runId + "\",\"assetId\":\"" + assetId + "\"}", now);
                transaction.Commit();
                AppLogger.Info($"Started new QC run {runId} for asset {assetId}");
                return runId;
            }
        }

        public string FindActiveRunForAsset(string serialOrTag)
        {
            if (string.IsNullOrWhiteSpace(serialOrTag)) return null;
            lock (_gate)
            {
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = @"
SELECT r.id FROM qc_runs r
INNER JOIN assets a ON r.asset_id = a.id
WHERE (a.serial_number = $id COLLATE NOCASE OR a.asset_tag = $id COLLATE NOCASE OR a.asset_uuid = $id)
  AND r.status = 'InProgress'
ORDER BY r.started_at_utc DESC LIMIT 1;";
                command.Parameters.AddWithValue("$id", serialOrTag.Trim());
                return command.ExecuteScalar() as string;
            }
        }

        public void RecordResult(string runId, QcTestResultRecord result)
        {
            if (string.IsNullOrWhiteSpace(runId)) throw new ArgumentException("A QC run ID is required.", nameof(runId));
            if (result == null || string.IsNullOrWhiteSpace(result.TestKey)) throw new ArgumentException("A diagnostic test key is required.", nameof(result));
            lock (_gate)
            {
                using var connection = Open();
                using var transaction = connection.BeginTransaction();
                EnsureActiveRun(connection, transaction, runId);
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"INSERT INTO qc_test_results(id, qc_run_id, test_key, test_name, status, is_automated, metrics_json, override_reason, approved_by, recorded_at_utc)
VALUES($id, $runId, $key, $name, $status, $automated, $metrics, $reason, $approvedBy, $recorded)
ON CONFLICT(qc_run_id, test_key) DO UPDATE SET
test_name=excluded.test_name, status=excluded.status, is_automated=excluded.is_automated, metrics_json=excluded.metrics_json,
override_reason=excluded.override_reason, approved_by=excluded.approved_by, recorded_at_utc=excluded.recorded_at_utc;";
                    command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
                    command.Parameters.AddWithValue("$runId", runId);
                    command.Parameters.AddWithValue("$key", result.TestKey);
                    command.Parameters.AddWithValue("$name", result.TestName ?? result.TestKey);
                    command.Parameters.AddWithValue("$status", result.Status.ToString());
                    command.Parameters.AddWithValue("$automated", result.IsAutomated ? 1 : 0);
                    command.Parameters.AddWithValue("$metrics", result.MetricsJson ?? "");
                    command.Parameters.AddWithValue("$reason", result.OverrideReason ?? "");
                    command.Parameters.AddWithValue("$approvedBy", result.ApprovedBy ?? "");
                    command.Parameters.AddWithValue("$recorded", result.RecordedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
                    command.ExecuteNonQuery();
                }
                AddOutboxEvent(connection, transaction, "QcTestResultRecorded", runId,
                    "{\"runId\":\"" + runId + "\",\"testKey\":\"" + EscapeJson(result.TestKey) + "\",\"status\":\"" + result.Status + "\"}", UtcNow());
                transaction.Commit();
            }
        }

        public void RecordOverride(string runId, QcOverride ov)
        {
            if (string.IsNullOrWhiteSpace(runId)) throw new ArgumentException("A QC run ID is required.", nameof(runId));
            if (ov == null || string.IsNullOrWhiteSpace(ov.TestKey)) throw new ArgumentException("Valid override details are required.", nameof(ov));

            lock (_gate)
            {
                using var connection = Open();
                using var transaction = connection.BeginTransaction();
                EnsureActiveRun(connection, transaction, runId);

                // Insert into overrides table
                using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"INSERT INTO overrides(id, qc_run_id, test_key, actor, reason, approver, recorded_at_utc)
VALUES($id, $runId, $key, $actor, $reason, $approver, $recorded);";
                    cmd.Parameters.AddWithValue("$id", string.IsNullOrEmpty(ov.Id) ? Guid.NewGuid().ToString("N") : ov.Id);
                    cmd.Parameters.AddWithValue("$runId", runId);
                    cmd.Parameters.AddWithValue("$key", ov.TestKey);
                    cmd.Parameters.AddWithValue("$actor", ov.Actor ?? "Technician");
                    cmd.Parameters.AddWithValue("$reason", ov.Reason ?? "");
                    cmd.Parameters.AddWithValue("$approver", ov.Approver ?? "");
                    cmd.Parameters.AddWithValue("$recorded", ov.RecordedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
                    cmd.ExecuteNonQuery();
                }

                // Update qc_test_results table to ManualOverride
                using (var cmdUpdate = connection.CreateCommand())
                {
                    cmdUpdate.Transaction = transaction;
                    cmdUpdate.CommandText = @"INSERT INTO qc_test_results(id, qc_run_id, test_key, test_name, status, is_automated, metrics_json, override_reason, approved_by, recorded_at_utc)
VALUES($id, $runId, $key, $key, 'ManualOverride', 0, '', $reason, $approver, $recorded)
ON CONFLICT(qc_run_id, test_key) DO UPDATE SET
status='ManualOverride', override_reason=excluded.override_reason, approved_by=excluded.approved_by, recorded_at_utc=excluded.recorded_at_utc;";
                    cmdUpdate.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
                    cmdUpdate.Parameters.AddWithValue("$runId", runId);
                    cmdUpdate.Parameters.AddWithValue("$key", ov.TestKey);
                    cmdUpdate.Parameters.AddWithValue("$reason", ov.Reason ?? "");
                    cmdUpdate.Parameters.AddWithValue("$approver", ov.Approver ?? "");
                    cmdUpdate.Parameters.AddWithValue("$recorded", ov.RecordedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
                    cmdUpdate.ExecuteNonQuery();
                }

                AddOutboxEvent(connection, transaction, "QcTestOverridden", runId,
                    "{\"runId\":\"" + runId + "\",\"testKey\":\"" + EscapeJson(ov.TestKey) + "\",\"actor\":\"" + EscapeJson(ov.Actor) + "\",\"reason\":\"" + EscapeJson(ov.Reason) + "\"}", UtcNow());

                transaction.Commit();
                AppLogger.Info($"Manual override recorded for test '{ov.TestKey}' in run '{runId}' by '{ov.Actor}'");
            }
        }

        public void RecordEvidence(string runId, QcEvidence ev)
        {
            if (string.IsNullOrWhiteSpace(runId)) throw new ArgumentException("A QC run ID is required.", nameof(runId));
            if (ev == null) throw new ArgumentNullException(nameof(ev));

            lock (_gate)
            {
                using var connection = Open();
                using var transaction = connection.BeginTransaction();
                EnsureActiveRun(connection, transaction, runId);

                using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"INSERT INTO evidence(id, qc_run_id, test_key, evidence_type, file_path, file_hash, metadata_json, recorded_at_utc)
VALUES($id, $runId, $key, $type, $path, $hash, $meta, $recorded);";
                    cmd.Parameters.AddWithValue("$id", string.IsNullOrEmpty(ev.Id) ? Guid.NewGuid().ToString("N") : ev.Id);
                    cmd.Parameters.AddWithValue("$runId", runId);
                    cmd.Parameters.AddWithValue("$key", ev.TestKey ?? "");
                    cmd.Parameters.AddWithValue("$type", ev.EvidenceType ?? "Log");
                    cmd.Parameters.AddWithValue("$path", ev.FilePath ?? "");
                    cmd.Parameters.AddWithValue("$hash", ev.FileHash ?? "");
                    cmd.Parameters.AddWithValue("$meta", ev.MetadataJson ?? "");
                    cmd.Parameters.AddWithValue("$recorded", ev.RecordedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
                    cmd.ExecuteNonQuery();
                }

                AddOutboxEvent(connection, transaction, "QcEvidenceRecorded", runId,
                    "{\"runId\":\"" + runId + "\",\"evidenceType\":\"" + EscapeJson(ev.EvidenceType) + "\",\"hash\":\"" + EscapeJson(ev.FileHash) + "\"}", UtcNow());

                transaction.Commit();
            }
        }

        public bool TryCompleteRun(string runId, string verificationHash, string grade, out string reason)
        {
            reason = "";
            if (string.IsNullOrWhiteSpace(runId)) { reason = "No active QC run exists."; return false; }
            lock (_gate)
            {
                using var connection = Open();
                using var transaction = connection.BeginTransaction();
                EnsureActiveRun(connection, transaction, runId);
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "SELECT COUNT(*) FROM qc_test_results WHERE qc_run_id=$runId AND status IN ('NotStarted','Running','Failed','Skipped');";
                command.Parameters.AddWithValue("$runId", runId);
                var incomplete = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
                if (incomplete > 0)
                {
                    reason = "All required tests must be passed, marked not applicable, or explicitly overridden before certification.";
                    transaction.Rollback();
                    return false;
                }

                string now = UtcNow();
                using var complete = connection.CreateCommand();
                complete.Transaction = transaction;
                complete.CommandText = "UPDATE qc_runs SET status='Completed', completed_at_utc=$completed, verification_hash=$hash, grade=$grade WHERE id=$id;";
                complete.Parameters.AddWithValue("$id", runId);
                complete.Parameters.AddWithValue("$completed", now);
                complete.Parameters.AddWithValue("$hash", verificationHash ?? "");
                complete.Parameters.AddWithValue("$grade", grade ?? "GRADE A");
                complete.ExecuteNonQuery();

                AddOutboxEvent(connection, transaction, "QcRunCompleted", runId,
                    "{\"runId\":\"" + runId + "\",\"hash\":\"" + EscapeJson(verificationHash) + "\",\"grade\":\"" + EscapeJson(grade) + "\"}", now);
                transaction.Commit();
                AppLogger.Info($"Completed QC run '{runId}' with verification hash '{verificationHash}' and grade '{grade}'");
                return true;
            }
        }

        public QcRunSummary GetRunSummary(string runId)
        {
            if (string.IsNullOrWhiteSpace(runId)) return null;

            lock (_gate)
            {
                using var connection = Open();
                var summary = new QcRunSummary { RunId = runId };

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
SELECT r.id, r.asset_id, r.technician, r.station, r.policy_version, r.status, r.verification_hash, r.grade, r.started_at_utc, r.completed_at_utc,
       a.serial_number, a.asset_tag, a.model
FROM qc_runs r
INNER JOIN assets a ON r.asset_id = a.id
WHERE r.id = $id LIMIT 1;";
                    cmd.Parameters.AddWithValue("$id", runId);
                    using var reader = cmd.ExecuteReader();
                    if (!reader.Read()) return null;

                    summary.AssetId = reader.GetString(1);
                    summary.Technician = reader.IsDBNull(2) ? "" : reader.GetString(2);
                    summary.Station = reader.IsDBNull(3) ? "" : reader.GetString(3);
                    summary.PolicyVersion = reader.IsDBNull(4) ? "default-v1" : reader.GetString(4);
                    summary.Status = Enum.TryParse<QcRunStatus>(reader.GetString(5), out var st) ? st : QcRunStatus.InProgress;
                    summary.VerificationHash = reader.IsDBNull(6) ? "" : reader.GetString(6);
                    summary.Grade = reader.IsDBNull(7) ? "PENDING" : reader.GetString(7);
                    summary.StartedAtUtc = DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture);
                    summary.CompletedAtUtc = reader.IsDBNull(9) ? (DateTimeOffset?)null : DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture);
                    summary.SerialNumber = reader.IsDBNull(10) ? "" : reader.GetString(10);
                    summary.AssetTag = reader.IsDBNull(11) ? "" : reader.GetString(11);
                    summary.Model = reader.IsDBNull(12) ? "" : reader.GetString(12);
                }

                // Results
                using (var cmdResults = connection.CreateCommand())
                {
                    cmdResults.CommandText = @"SELECT test_key, test_name, status, is_automated, metrics_json, override_reason, approved_by, recorded_at_utc FROM qc_test_results WHERE qc_run_id = $id ORDER BY test_key ASC;";
                    cmdResults.Parameters.AddWithValue("$id", runId);
                    using var reader = cmdResults.ExecuteReader();
                    while (reader.Read())
                    {
                        summary.Results.Add(new QcTestResultRecord
                        {
                            TestKey = reader.GetString(0),
                            TestName = reader.GetString(1),
                            Status = Enum.TryParse<QcTestStatus>(reader.GetString(2), out var s) ? s : QcTestStatus.NotStarted,
                            IsAutomated = reader.GetInt32(3) == 1,
                            MetricsJson = reader.IsDBNull(4) ? "" : reader.GetString(4),
                            OverrideReason = reader.IsDBNull(5) ? "" : reader.GetString(5),
                            ApprovedBy = reader.IsDBNull(6) ? "" : reader.GetString(6),
                            RecordedAtUtc = DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture)
                        });
                    }
                }

                // Overrides
                using (var cmdOverrides = connection.CreateCommand())
                {
                    cmdOverrides.CommandText = @"SELECT id, test_key, actor, reason, approver, recorded_at_utc FROM overrides WHERE qc_run_id = $id ORDER BY recorded_at_utc ASC;";
                    cmdOverrides.Parameters.AddWithValue("$id", runId);
                    using var reader = cmdOverrides.ExecuteReader();
                    while (reader.Read())
                    {
                        summary.Overrides.Add(new QcOverride
                        {
                            Id = reader.GetString(0),
                            RunId = runId,
                            TestKey = reader.GetString(1),
                            Actor = reader.GetString(2),
                            Reason = reader.GetString(3),
                            Approver = reader.IsDBNull(4) ? "" : reader.GetString(4),
                            RecordedAtUtc = DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture)
                        });
                    }
                }

                // Evidence
                using (var cmdEvidence = connection.CreateCommand())
                {
                    cmdEvidence.CommandText = @"SELECT id, test_key, evidence_type, file_path, file_hash, metadata_json, recorded_at_utc FROM evidence WHERE qc_run_id = $id ORDER BY recorded_at_utc ASC;";
                    cmdEvidence.Parameters.AddWithValue("$id", runId);
                    using var reader = cmdEvidence.ExecuteReader();
                    while (reader.Read())
                    {
                        summary.EvidenceItems.Add(new QcEvidence
                        {
                            Id = reader.GetString(0),
                            RunId = runId,
                            TestKey = reader.IsDBNull(1) ? "" : reader.GetString(1),
                            EvidenceType = reader.GetString(2),
                            FilePath = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            FileHash = reader.IsDBNull(4) ? "" : reader.GetString(4),
                            MetadataJson = reader.IsDBNull(5) ? "" : reader.GetString(5),
                            RecordedAtUtc = DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture)
                        });
                    }
                }

                return summary;
            }
        }

        public int GetPendingOutboxCount()
        {
            lock (_gate)
            {
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM sync_outbox WHERE dispatched_at_utc IS NULL;";
                return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }

        public List<OutboxEventRecord> GetPendingOutboxEvents(int maxCount = 50)
        {
            var list = new List<OutboxEventRecord>();
            lock (_gate)
            {
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = @"
SELECT id, event_type, aggregate_id, idempotency_key, payload_json, created_at_utc, attempts, last_error
FROM sync_outbox
WHERE dispatched_at_utc IS NULL
ORDER BY created_at_utc ASC
LIMIT $limit;";
                command.Parameters.AddWithValue("$limit", maxCount);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new OutboxEventRecord
                    {
                        Id = reader.GetString(0),
                        EventType = reader.GetString(1),
                        AggregateId = reader.GetString(2),
                        IdempotencyKey = reader.GetString(3),
                        PayloadJson = reader.GetString(4),
                        CreatedAtUtc = DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
                        Attempts = reader.GetInt32(6),
                        LastError = reader.IsDBNull(7) ? null : reader.GetString(7)
                    });
                }
            }
            return list;
        }

        public void MarkOutboxEventDispatched(string eventId)
        {
            if (string.IsNullOrWhiteSpace(eventId)) return;
            lock (_gate)
            {
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE sync_outbox SET dispatched_at_utc=$now WHERE id=$id;";
                command.Parameters.AddWithValue("$now", UtcNow());
                command.Parameters.AddWithValue("$id", eventId);
                command.ExecuteNonQuery();
            }
        }

        public void RecordOutboxAttemptFailure(string eventId, string error)
        {
            if (string.IsNullOrWhiteSpace(eventId)) return;
            lock (_gate)
            {
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE sync_outbox SET attempts=attempts+1, last_error=$err WHERE id=$id;";
                command.Parameters.AddWithValue("$err", error ?? "Unknown error");
                command.Parameters.AddWithValue("$id", eventId);
                command.ExecuteNonQuery();
            }
        }

        public void EnqueueOutboxEvent(string eventType, string aggregateId, string idempotencyKey, string payloadJson)
        {
            lock (_gate)
            {
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = @"INSERT OR IGNORE INTO sync_outbox(id, event_type, aggregate_id, idempotency_key, payload_json, created_at_utc)
VALUES($id, $type, $agg, $key, $payload, $now);";
                command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
                command.Parameters.AddWithValue("$type", eventType ?? "CustomEvent");
                command.Parameters.AddWithValue("$agg", aggregateId ?? "");
                command.Parameters.AddWithValue("$key", idempotencyKey ?? Guid.NewGuid().ToString("N"));
                command.Parameters.AddWithValue("$payload", payloadJson ?? "{}");
                command.Parameters.AddWithValue("$now", UtcNow());
                command.ExecuteNonQuery();
            }
        }

        private SqliteConnection Open()
        {
            var connection = new SqliteConnection(_connectionString);
            connection.Open();
            return connection;
        }

        private static void Execute(SqliteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private static void AddColumnIfNotExists(SqliteConnection connection, string table, string column, string typeDef)
        {
            try
            {
                using var pragma = connection.CreateCommand();
                pragma.CommandText = $"PRAGMA table_info({table});";
                using var reader = pragma.ExecuteReader();
                while (reader.Read())
                {
                    string colName = reader.GetString(1);
                    if (string.Equals(colName, column, StringComparison.OrdinalIgnoreCase))
                    {
                        return; // column exists
                    }
                }
                reader.Close();

                using var alter = connection.CreateCommand();
                alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {typeDef};";
                alter.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"AddColumnIfNotExists failed for {table}.{column}", ex);
            }
        }

        private static string FindOrCreateAsset(SqliteConnection connection, SqliteTransaction transaction, QcRunIdentity identity, string now)
        {
            string rawSerial = identity.SerialNumber ?? "";
            bool isGeneric = IsGenericSerial(rawSerial);
            string serial = isGeneric ? "" : rawSerial.Trim();

            // Handle Generic or Missing serial: compute stable fallback hardware UUID
            string assetUuid = identity.AssetUuid;
            if (string.IsNullOrWhiteSpace(assetUuid))
            {
                if (isGeneric)
                {
                    identity.Confidence = AssetIdentifierConfidence.Fallback;
                    identity.IdentifierSource = "HardwareFingerprintFallback";
                    // Stable fallback generated from Model and Tag, or random GUID if neither
                    string seed = $"{identity.Model}:{identity.AssetTag}";
                    assetUuid = "FALLBACK-" + (string.IsNullOrWhiteSpace(seed.Trim(':'))
                        ? Guid.NewGuid().ToString("N").Substring(0, 12).ToUpperInvariant()
                        : GenerateStableHash(seed).Substring(0, 12).ToUpperInvariant());
                    identity.AssetUuid = assetUuid;
                }
                else
                {
                    assetUuid = "AUTH-" + GenerateStableHash(serial).Substring(0, 12).ToUpperInvariant();
                    identity.AssetUuid = assetUuid;
                }
            }

            // 1. If we have an Authoritative serial number, match by serial
            if (!string.IsNullOrEmpty(serial))
            {
                using var find = connection.CreateCommand();
                find.Transaction = transaction;
                find.CommandText = "SELECT id FROM assets WHERE serial_number=$serial LIMIT 1;";
                find.Parameters.AddWithValue("$serial", serial);
                var found = find.ExecuteScalar() as string;
                if (!string.IsNullOrEmpty(found))
                {
                    using var update = connection.CreateCommand();
                    update.Transaction = transaction;
                    update.CommandText = "UPDATE assets SET asset_tag=$tag, model=$model, asset_uuid=$uuid, serial_confidence='Authoritative', updated_at_utc=$updated WHERE id=$id;";
                    update.Parameters.AddWithValue("$tag", NormalizeIdentity(identity.AssetTag));
                    update.Parameters.AddWithValue("$model", identity.Model ?? "");
                    update.Parameters.AddWithValue("$uuid", assetUuid);
                    update.Parameters.AddWithValue("$updated", now);
                    update.Parameters.AddWithValue("$id", found);
                    update.ExecuteNonQuery();
                    return found;
                }
            }
            // 2. If fallback/generic serial, match by asset_uuid
            else if (!string.IsNullOrEmpty(assetUuid))
            {
                using var findUuid = connection.CreateCommand();
                findUuid.Transaction = transaction;
                findUuid.CommandText = "SELECT id FROM assets WHERE asset_uuid=$uuid LIMIT 1;";
                findUuid.Parameters.AddWithValue("$uuid", assetUuid);
                var found = findUuid.ExecuteScalar() as string;
                if (!string.IsNullOrEmpty(found))
                {
                    using var update = connection.CreateCommand();
                    update.Transaction = transaction;
                    update.CommandText = "UPDATE assets SET asset_tag=$tag, model=$model, updated_at_utc=$updated WHERE id=$id;";
                    update.Parameters.AddWithValue("$tag", NormalizeIdentity(identity.AssetTag));
                    update.Parameters.AddWithValue("$model", identity.Model ?? "");
                    update.Parameters.AddWithValue("$updated", now);
                    update.Parameters.AddWithValue("$id", found);
                    update.ExecuteNonQuery();
                    return found;
                }
            }

            // 3. Create new asset
            var id = Guid.NewGuid().ToString("N");
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = @"INSERT INTO assets(id, serial_number, asset_tag, model, asset_uuid, serial_confidence, serial_source, created_at_utc, updated_at_utc)
VALUES($id, $serial, $tag, $model, $uuid, $conf, $src, $created, $updated);";
            insert.Parameters.AddWithValue("$id", id);
            insert.Parameters.AddWithValue("$serial", string.IsNullOrEmpty(serial) ? (object)DBNull.Value : serial);
            insert.Parameters.AddWithValue("$tag", NormalizeIdentity(identity.AssetTag));
            insert.Parameters.AddWithValue("$model", identity.Model ?? "");
            insert.Parameters.AddWithValue("$uuid", assetUuid);
            insert.Parameters.AddWithValue("$conf", identity.Confidence.ToString());
            insert.Parameters.AddWithValue("$src", identity.IdentifierSource ?? "Win32_BIOS");
            insert.Parameters.AddWithValue("$created", now);
            insert.Parameters.AddWithValue("$updated", now);
            insert.ExecuteNonQuery();
            return id;
        }

        public static bool IsGenericSerial(string serial)
        {
            if (string.IsNullOrWhiteSpace(serial)) return true;
            string clean = serial.Trim();
            return GenericSerialBlacklist.Contains(clean);
        }

        private static void EnsureActiveRun(SqliteConnection connection, SqliteTransaction transaction, string runId)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT status FROM qc_runs WHERE id=$id;";
            command.Parameters.AddWithValue("$id", runId);
            var status = command.ExecuteScalar() as string;
            if (status == null) throw new InvalidOperationException("The QC run does not exist.");
            if (!string.Equals(status, "InProgress", StringComparison.Ordinal)) throw new InvalidOperationException("The QC run has already been completed and cannot be modified.");
        }

        private static void AddOutboxEvent(SqliteConnection connection, SqliteTransaction transaction, string type, string aggregateId, string payload, string now)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO sync_outbox(id,event_type,aggregate_id,idempotency_key,payload_json,created_at_utc) VALUES($id,$type,$aggregate,$key,$payload,$created);";
            command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("$type", type);
            command.Parameters.AddWithValue("$aggregate", aggregateId);
            command.Parameters.AddWithValue("$key", Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("$payload", payload);
            command.Parameters.AddWithValue("$created", now);
            command.ExecuteNonQuery();
        }

        private static string NormalizeIdentity(string value) => string.IsNullOrWhiteSpace(value) || value.Equals("Detecting...", StringComparison.OrdinalIgnoreCase) ? "" : value.Trim();
        private static string UtcNow() => DateTimeOffset.UtcNow.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        private static string EscapeJson(string value) => (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static string GenerateStableHash(string input)
        {
            using var sha = SHA256.Create();
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input ?? ""));
            return BitConverter.ToString(bytes).Replace("-", "").ToUpperInvariant();
        }
    }
}
