using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using Velox.Shared.Dtos;

namespace VeloxBench.Core;

public sealed class OutboxRecord
{
    public long Id { get; set; }
    public string TargetEndpoint { get; set; } = "";
    public string HttpMethod { get; set; } = "POST";
    public string PayloadJson { get; set; } = "";
    public int AttemptCount { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class LocalBenchStore
{
    private static readonly Lazy<LocalBenchStore> _lazy = new(() => new LocalBenchStore());
    public static LocalBenchStore Instance => _lazy.Value;

    private readonly string _connectionString;
    private readonly object _lock = new();

    public LocalBenchStore()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Velox", "VeloxBench");
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "velox_bench.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        Initialize();
    }

    private void Initialize()
    {
        lock (_lock)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                PRAGMA journal_mode = WAL;

                CREATE TABLE IF NOT EXISTS local_assets (
                    serial_number TEXT PRIMARY KEY COLLATE NOCASE,
                    asset_tag TEXT COLLATE NOCASE,
                    model TEXT,
                    specs_summary TEXT,
                    grade TEXT,
                    battery_health INTEGER,
                    storage_health INTEGER,
                    qc_status TEXT,
                    assigned_to TEXT,
                    supplier TEXT,
                    customer TEXT,
                    in_date TEXT,
                    updated_at_utc TEXT
                );
                CREATE INDEX IF NOT EXISTS ix_local_tag ON local_assets(asset_tag);

                CREATE TABLE IF NOT EXISTS outbox_queue (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    endpoint TEXT NOT NULL,
                    method TEXT NOT NULL,
                    payload_json TEXT NOT NULL,
                    attempts INTEGER NOT NULL DEFAULT 0,
                    created_at_utc TEXT NOT NULL
                );
            ";
            cmd.ExecuteNonQuery();
        }
    }

    public void CacheAsset(AssetDto asset)
    {
        if (string.IsNullOrWhiteSpace(asset.SerialNumber)) return;
        lock (_lock)
        {
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO local_assets(serial_number, asset_tag, model, grade, battery_health, storage_health, assigned_to, supplier, customer, in_date, updated_at_utc)
                    VALUES(@sn, @tag, @model, @grade, @bh, @sh, @assign, @sup, @cust, @ind, @ts)
                    ON CONFLICT(serial_number) DO UPDATE SET
                        asset_tag = COALESCE(excluded.asset_tag, local_assets.asset_tag),
                        model = COALESCE(excluded.model, local_assets.model),
                        grade = excluded.grade,
                        battery_health = excluded.battery_health,
                        storage_health = excluded.storage_health,
                        assigned_to = excluded.assigned_to,
                        supplier = excluded.supplier,
                        customer = excluded.customer,
                        in_date = excluded.in_date,
                        updated_at_utc = excluded.updated_at_utc;
                ";
                cmd.Parameters.AddWithValue("@sn", asset.SerialNumber);
                cmd.Parameters.AddWithValue("@tag", (object?)asset.AssetTag ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@model", (object?)asset.Model ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@grade", asset.Grade.ToString());
                cmd.Parameters.AddWithValue("@bh", (object?)asset.BatteryHealth ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@sh", (object?)asset.StorageHealth ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@assign", (object?)asset.AssignedEmployeeName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@sup", (object?)asset.Supplier ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@cust", (object?)asset.Customer ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ind", (object?)asset.InDate ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ts", DateTime.UtcNow.ToString("o"));
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed caching asset in local bench store", ex);
            }
        }
    }

    public AssetDto? FindLocalAsset(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return null;
        lock (_lock)
        {
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT serial_number, asset_tag, model, grade, battery_health, storage_health, assigned_to, supplier, customer, in_date FROM local_assets WHERE serial_number = @id COLLATE NOCASE OR asset_tag = @id COLLATE NOCASE LIMIT 1";
                cmd.Parameters.AddWithValue("@id", identifier.Trim());
                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    return new AssetDto
                    {
                        SerialNumber = r.IsDBNull(0) ? "" : r.GetString(0),
                        AssetTag = r.IsDBNull(1) ? "" : r.GetString(1),
                        Model = r.IsDBNull(2) ? "" : r.GetString(2),
                        BatteryHealth = r.IsDBNull(4) ? null : r.GetInt32(4),
                        StorageHealth = r.IsDBNull(5) ? null : r.GetInt32(5),
                        AssignedEmployeeName = r.IsDBNull(6) ? "" : r.GetString(6),
                        Supplier = r.IsDBNull(7) ? "" : r.GetString(7),
                        Customer = r.IsDBNull(8) ? "" : r.GetString(8),
                        InDate = r.IsDBNull(9) ? "" : r.GetString(9)
                    };
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Local asset lookup failed", ex);
            }
            return null;
        }
    }

    public void EnqueueOutbox(string endpoint, string method, string payloadJson)
    {
        lock (_lock)
        {
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO outbox_queue(endpoint, method, payload_json, created_at_utc) VALUES(@ep, @method, @payload, @ts)";
                cmd.Parameters.AddWithValue("@ep", endpoint);
                cmd.Parameters.AddWithValue("@method", method);
                cmd.Parameters.AddWithValue("@payload", payloadJson);
                cmd.Parameters.AddWithValue("@ts", DateTime.UtcNow.ToString("o"));
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed queuing outbox item", ex);
            }
        }
    }

    public List<OutboxRecord> GetPendingOutbox(int limit = 50)
    {
        var list = new List<OutboxRecord>();
        lock (_lock)
        {
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT id, endpoint, method, payload_json, attempts, created_at_utc FROM outbox_queue ORDER BY id ASC LIMIT @lim";
                cmd.Parameters.AddWithValue("@lim", limit);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    list.Add(new OutboxRecord
                    {
                        Id = r.GetInt64(0),
                        TargetEndpoint = r.GetString(1),
                        HttpMethod = r.GetString(2),
                        PayloadJson = r.GetString(3),
                        AttemptCount = r.GetInt32(4),
                        CreatedAtUtc = DateTime.Parse(r.GetString(5))
                    });
                }
            }
            catch { }
        }
        return list;
    }

    public void RemoveOutbox(long id)
    {
        lock (_lock)
        {
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM outbox_queue WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }
            catch { }
        }
    }
}
