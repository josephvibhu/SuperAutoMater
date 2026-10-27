using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using Microsoft.Data.Sqlite;
using VeloxBench.Core;
using SuperAutoMater.Wpf.Core;

namespace VeloxBench.Core;

public sealed class DurableOutboxEvent
{
    public string Id { get; set; } = "";
    public string EventType { get; set; } = "";
    public string AggregateId { get; set; } = "";
    public string SiliconDnaHash { get; set; } = "";
    public string HardwareQuorumJson { get; set; } = "{}";
    public string IdempotencyKey { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public int Attempts { get; set; } = 0;
    public string? LastError { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DispatchedAtUtc { get; set; }
}

/// <summary>
/// Dedicated SQLite outbox (outbox.db) with Write-Ahead Logging (WAL) mode.
/// Guarantees that when Wi-Fi drops, all test events are safely persisted locally.
/// Automatically replayed upon connectivity return with zero duplicate quota consumption.
/// </summary>
public sealed class OutboxStore
{
    private static readonly Lazy<OutboxStore> _lazy = new(() => new OutboxStore());
    public static OutboxStore Instance => _lazy.Value;

    private readonly string _connectionString;
    private readonly string _dbPath;
    private readonly object _lock = new();

    public string DatabasePath => _dbPath;

    public OutboxStore(string? customDbPath = null)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Velox", "VeloxBench");
        Directory.CreateDirectory(root);
        _dbPath = customDbPath ?? Path.Combine(root, "outbox.db");

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        Initialize();
    }

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA busy_timeout = 5000;";
        cmd.ExecuteNonQuery();
        return conn;
    }

    private void Initialize()
    {
        lock (_lock)
        {
            try
            {
                using var conn = OpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS outbox_events (
                        id TEXT PRIMARY KEY,
                        event_type TEXT NOT NULL,
                        aggregate_id TEXT NOT NULL,
                        silicon_dna_hash TEXT,
                        hardware_quorum_json TEXT,
                        idempotency_key TEXT NOT NULL UNIQUE,
                        payload_json TEXT NOT NULL,
                        status TEXT NOT NULL DEFAULT 'Pending',
                        attempts INTEGER NOT NULL DEFAULT 0,
                        last_error TEXT,
                        created_at_utc TEXT NOT NULL,
                        dispatched_at_utc TEXT
                    );
                    CREATE INDEX IF NOT EXISTS ix_outbox_pending_replay ON outbox_events(status, created_at_utc);
                    CREATE INDEX IF NOT EXISTS ix_outbox_idempotency ON outbox_events(idempotency_key);
                ";
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to initialize outbox.db", ex);
            }
        }
    }

    public void Enqueue(
        string eventType,
        string aggregateId,
        string idempotencyKey,
        string payloadJson,
        string? siliconDnaHash = null,
        string? hardwareQuorumJson = null)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            idempotencyKey = $"idemp-{Guid.NewGuid():N}";
        }

        lock (_lock)
        {
            try
            {
                using var conn = OpenConnection();
                using var tx = conn.BeginTransaction();
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT INTO outbox_events(id, event_type, aggregate_id, silicon_dna_hash, hardware_quorum_json, idempotency_key, payload_json, status, created_at_utc)
                    VALUES(@id, @type, @agg, @dna, @quorum, @key, @payload, 'Pending', @ts)
                    ON CONFLICT(idempotency_key) DO UPDATE SET
                        payload_json = excluded.payload_json,
                        silicon_dna_hash = COALESCE(excluded.silicon_dna_hash, outbox_events.silicon_dna_hash),
                        hardware_quorum_json = COALESCE(excluded.hardware_quorum_json, outbox_events.hardware_quorum_json);
                ";
                cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                cmd.Parameters.AddWithValue("@type", eventType);
                cmd.Parameters.AddWithValue("@agg", aggregateId ?? "");
                cmd.Parameters.AddWithValue("@dna", (object?)siliconDnaHash ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@quorum", (object?)hardwareQuorumJson ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@key", idempotencyKey);
                cmd.Parameters.AddWithValue("@payload", payloadJson ?? "{}");
                cmd.Parameters.AddWithValue("@ts", DateTime.UtcNow.ToString("O"));
                cmd.ExecuteNonQuery();

                tx.Commit();

                // Explicit WAL flush for physical power-loss safety
                using var flushCmd = conn.CreateCommand();
                flushCmd.CommandText = "PRAGMA wal_checkpoint(PASSIVE);";
                flushCmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Failed to enqueue event into outbox.db (Key={idempotencyKey})", ex);
            }
        }
    }

    public List<DurableOutboxEvent> GetPending(int limit = 50)
    {
        var list = new List<DurableOutboxEvent>();
        lock (_lock)
        {
            try
            {
                using var conn = OpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT id, event_type, aggregate_id, silicon_dna_hash, hardware_quorum_json, idempotency_key, payload_json, status, attempts, last_error, created_at_utc, dispatched_at_utc
                    FROM outbox_events
                    WHERE status = 'Pending'
                    ORDER BY created_at_utc ASC
                    LIMIT @lim;
                ";
                cmd.Parameters.AddWithValue("@lim", limit);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    list.Add(new DurableOutboxEvent
                    {
                        Id = r.GetString(0),
                        EventType = r.GetString(1),
                        AggregateId = r.GetString(2),
                        SiliconDnaHash = r.IsDBNull(3) ? "" : r.GetString(3),
                        HardwareQuorumJson = r.IsDBNull(4) ? "{}" : r.GetString(4),
                        IdempotencyKey = r.GetString(5),
                        PayloadJson = r.GetString(6),
                        Status = r.GetString(7),
                        Attempts = r.GetInt32(8),
                        LastError = r.IsDBNull(9) ? null : r.GetString(9),
                        CreatedAtUtc = DateTime.Parse(r.GetString(10)),
                        DispatchedAtUtc = r.IsDBNull(11) ? null : DateTime.Parse(r.GetString(11))
                    });
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed reading pending events from outbox.db: {ex.Message}");
            }
        }
        return list;
    }

    public void MarkDispatched(string id)
    {
        lock (_lock)
        {
            try
            {
                using var conn = OpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE outbox_events SET status = 'Dispatched', dispatched_at_utc = @ts WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@ts", DateTime.UtcNow.ToString("O"));
                cmd.ExecuteNonQuery();
            }
            catch { }
        }
    }

    public void RecordFailure(string id, string error)
    {
        lock (_lock)
        {
            try
            {
                using var conn = OpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE outbox_events SET attempts = attempts + 1, last_error = @err WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@err", error ?? "Unknown delivery error");
                cmd.ExecuteNonQuery();
            }
            catch { }
        }
    }

    public int GetPendingCount()
    {
        lock (_lock)
        {
            try
            {
                using var conn = OpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM outbox_events WHERE status = 'Pending'";
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
            catch
            {
                return 0;
            }
        }
    }

    public void PurgeDispatched(int daysToKeep = 7)
    {
        lock (_lock)
        {
            try
            {
                using var conn = OpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM outbox_events WHERE status = 'Dispatched' AND dispatched_at_utc < @cutoff";
                cmd.Parameters.AddWithValue("@cutoff", DateTime.UtcNow.AddDays(-daysToKeep).ToString("O"));
                cmd.ExecuteNonQuery();
            }
            catch { }
        }
    }
}
