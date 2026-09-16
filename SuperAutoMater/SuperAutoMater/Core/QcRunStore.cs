using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace SuperAutoMater.Wpf.Core
{
    /// <summary>
    /// Local system of record for QC runs. SQLite is deliberately separate from the
    /// UI so a later server sync can use the outbox without reading WPF state.
    /// </summary>
    public sealed class QcRunStore
    {
        private readonly string _connectionString;
        private readonly object _gate = new object();

        public QcRunStore(string databasePath = null)
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SuperAutoMater");
            Directory.CreateDirectory(root);
            var path = databasePath ?? Path.Combine(root, "superautomater-qc.db");
            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            }.ToString();
            Initialize();
        }

        public void Initialize()
        {
            lock (_gate)
            {
                using var connection = Open();
                Execute(connection, "PRAGMA journal_mode=WAL;");
                Execute(connection, "PRAGMA foreign_keys=ON;");
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
                AddOutboxEvent(connection, transaction, "QcRunStarted", runId, "{\"runId\":\"" + runId + "\"}", now);
                transaction.Commit();
                return runId;
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

        public bool TryCompleteRun(string runId, out string reason)
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
                using var complete = connection.CreateCommand();
                complete.Transaction = transaction;
                complete.CommandText = "UPDATE qc_runs SET status='Completed', completed_at_utc=$completed WHERE id=$id;";
                complete.Parameters.AddWithValue("$id", runId);
                complete.Parameters.AddWithValue("$completed", UtcNow());
                complete.ExecuteNonQuery();
                AddOutboxEvent(connection, transaction, "QcRunCompleted", runId, "{\"runId\":\"" + runId + "\"}", UtcNow());
                transaction.Commit();
                return true;
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

        private static string FindOrCreateAsset(SqliteConnection connection, SqliteTransaction transaction, QcRunIdentity identity, string now)
        {
            var serial = NormalizeIdentity(identity.SerialNumber);
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
                    update.CommandText = "UPDATE assets SET asset_tag=$tag, model=$model, updated_at_utc=$updated WHERE id=$id;";
                    update.Parameters.AddWithValue("$tag", NormalizeIdentity(identity.AssetTag));
                    update.Parameters.AddWithValue("$model", identity.Model ?? "");
                    update.Parameters.AddWithValue("$updated", now);
                    update.Parameters.AddWithValue("$id", found);
                    update.ExecuteNonQuery();
                    return found;
                }
            }
            var id = Guid.NewGuid().ToString("N");
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO assets(id, serial_number, asset_tag, model, created_at_utc, updated_at_utc) VALUES($id,$serial,$tag,$model,$created,$updated);";
            insert.Parameters.AddWithValue("$id", id);
            insert.Parameters.AddWithValue("$serial", serial);
            insert.Parameters.AddWithValue("$tag", NormalizeIdentity(identity.AssetTag));
            insert.Parameters.AddWithValue("$model", identity.Model ?? "");
            insert.Parameters.AddWithValue("$created", now);
            insert.Parameters.AddWithValue("$updated", now);
            insert.ExecuteNonQuery();
            return id;
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
    }
}
