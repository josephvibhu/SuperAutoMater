using System.Security.Cryptography;
using System.Text;
using ItamService.Models;
using Microsoft.Data.Sqlite;

namespace ItamService.Data;

/// <summary>
/// SQLite data access layer for the ITAM service.
/// All writes are append-only or additive — existing raw observations are never mutated.
/// </summary>
public sealed class ItamDb
{
    private readonly string _connectionString;
    private readonly string _dbPath;
    private readonly object _gate = new();

    public string DbPath => _dbPath;

    public ItamDb(string? dbPath = null)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                "SuperAutoMater", "ItamService");
        Directory.CreateDirectory(root);
        _dbPath = dbPath ?? Path.Combine(root, "itam.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        Initialize();
    }

    // ── Public factory for tests ─────────────────────────────────────────────

    public static ItamDb CreateInMemory(string? name = null)
    {
        string tag = name ?? ("test_" + Guid.NewGuid().ToString("N"));
        return new ItamDb($"Data Source={tag};Mode=Memory;Cache=Shared");
    }

    // ── Schema bootstrap ─────────────────────────────────────────────────────

    public void Initialize()
    {
        lock (_gate)
        {
            using var conn = Open();
            Execute(conn, "PRAGMA journal_mode=WAL;");
            Execute(conn, "PRAGMA foreign_keys=ON;");

            Execute(conn, @"
CREATE TABLE IF NOT EXISTS schema_migrations (version INTEGER PRIMARY KEY, applied_at_utc TEXT NOT NULL);

CREATE TABLE IF NOT EXISTS tenants (
    id         TEXT PRIMARY KEY,
    name       TEXT NOT NULL,
    site_code  TEXT NOT NULL UNIQUE COLLATE NOCASE,
    created_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS api_keys (
    id             TEXT PRIMARY KEY,
    tenant_id      TEXT NOT NULL,
    key_hash       TEXT NOT NULL UNIQUE,
    label          TEXT NOT NULL,
    created_at_utc TEXT NOT NULL,
    revoked_at_utc TEXT NULL,
    FOREIGN KEY(tenant_id) REFERENCES tenants(id)
);
CREATE INDEX IF NOT EXISTS ix_api_keys_hash ON api_keys(key_hash);

CREATE TABLE IF NOT EXISTS assets (
    id               TEXT PRIMARY KEY,
    tenant_id        TEXT NOT NULL,
    serial_number    TEXT NULL COLLATE NOCASE,
    asset_tag        TEXT NULL COLLATE NOCASE,
    asset_uuid       TEXT NULL,
    model            TEXT NULL,
    current_location TEXT NULL,
    lifecycle_queue  TEXT NOT NULL DEFAULT 'Intake',
    created_at_utc   TEXT NOT NULL,
    updated_at_utc   TEXT NOT NULL,
    FOREIGN KEY(tenant_id) REFERENCES tenants(id)
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_assets_tenant_serial
    ON assets(tenant_id, serial_number)
    WHERE serial_number IS NOT NULL AND serial_number <> '';
CREATE INDEX IF NOT EXISTS ix_assets_tenant ON assets(tenant_id);

CREATE TABLE IF NOT EXISTS asset_events (
    id              TEXT PRIMARY KEY,
    asset_id        TEXT NOT NULL,
    tenant_id       TEXT NOT NULL,
    event_type      TEXT NOT NULL,
    location        TEXT NULL,
    actor           TEXT NULL,
    notes_json      TEXT NOT NULL DEFAULT '{}',
    source_bench_id TEXT NULL,
    recorded_at_utc TEXT NOT NULL,
    FOREIGN KEY(asset_id) REFERENCES assets(id)
);
CREATE INDEX IF NOT EXISTS ix_events_asset_time ON asset_events(asset_id, recorded_at_utc);

CREATE TABLE IF NOT EXISTS component_observations (
    id              TEXT PRIMARY KEY,
    asset_id        TEXT NOT NULL,
    tenant_id       TEXT NOT NULL,
    component_type  TEXT NOT NULL,
    attribute_key   TEXT NOT NULL,
    attribute_value TEXT NOT NULL,
    source          TEXT NOT NULL DEFAULT 'Unknown',
    confidence      TEXT NOT NULL DEFAULT 'High',
    bench_id        TEXT NULL,
    observed_at_utc TEXT NOT NULL,
    FOREIGN KEY(asset_id) REFERENCES assets(id)
);
CREATE INDEX IF NOT EXISTS ix_obs_asset_comp_time
    ON component_observations(asset_id, component_type, attribute_key, observed_at_utc DESC);

CREATE TABLE IF NOT EXISTS component_differences (
    id                 TEXT PRIMARY KEY,
    asset_id           TEXT NOT NULL,
    tenant_id          TEXT NOT NULL,
    component_type     TEXT NOT NULL,
    attribute_key      TEXT NOT NULL,
    observation_a_id   TEXT NOT NULL,
    observation_b_id   TEXT NOT NULL,
    value_a            TEXT NOT NULL,
    value_b            TEXT NOT NULL,
    source_a           TEXT NOT NULL,
    source_b           TEXT NOT NULL,
    confidence_a       TEXT NOT NULL,
    confidence_b       TEXT NOT NULL,
    detected_at_utc    TEXT NOT NULL,
    approved_replacement INTEGER NOT NULL DEFAULT 0,
    approved_by        TEXT NULL,
    approved_at_utc    TEXT NULL,
    UNIQUE(observation_a_id, observation_b_id, attribute_key),
    FOREIGN KEY(asset_id) REFERENCES assets(id)
);
CREATE INDEX IF NOT EXISTS ix_diffs_asset ON component_differences(asset_id);

INSERT OR IGNORE INTO schema_migrations(version, applied_at_utc) VALUES(1, CURRENT_TIMESTAMP);");
        }
    }

    // ── Tenant / API Key operations ──────────────────────────────────────────

    public Tenant CreateTenant(string name, string siteCode)
    {
        var tenant = new Tenant
        {
            Id        = "ten-" + Guid.NewGuid().ToString("N")[..12],
            Name      = name,
            SiteCode  = siteCode.ToUpperInvariant(),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        lock (_gate)
        {
            using var conn = Open();
            Execute(conn,
                "INSERT INTO tenants(id,name,site_code,created_at_utc) VALUES(@id,@name,@site,@ts)",
                P("@id", tenant.Id), P("@name", tenant.Name),
                P("@site", tenant.SiteCode), P("@ts", Iso(tenant.CreatedAtUtc)));
        }
        return tenant;
    }

    public Tenant? GetTenantById(string tenantId)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id,name,site_code,created_at_utc FROM tenants WHERE id=@id";
            cmd.Parameters.AddWithValue("@id", tenantId);
            using var r = cmd.ExecuteReader();
            return r.Read() ? MapTenant(r) : null;
        }
    }

    /// <summary>Returns (record, plainTextKey) — plain text shown once only.</summary>
    public (ApiKeyRecord Record, string PlainKey) IssueApiKey(string tenantId, string label)
    {
        string plain = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        string hash  = HashKey(plain);
        var rec = new ApiKeyRecord
        {
            Id = "key-" + Guid.NewGuid().ToString("N")[..12],
            TenantId      = tenantId,
            KeyHash       = hash,
            Label         = label,
            CreatedAtUtc  = DateTimeOffset.UtcNow
        };
        lock (_gate)
        {
            using var conn = Open();
            Execute(conn,
                "INSERT INTO api_keys(id,tenant_id,key_hash,label,created_at_utc) VALUES(@id,@tid,@hash,@lbl,@ts)",
                P("@id", rec.Id), P("@tid", tenantId), P("@hash", hash),
                P("@lbl", label), P("@ts", Iso(rec.CreatedAtUtc)));
        }
        return (rec, plain);
    }

    public bool RevokeApiKey(string tenantId, string keyId)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE api_keys SET revoked_at_utc=@ts WHERE id=@id AND tenant_id=@tid AND revoked_at_utc IS NULL";
            cmd.Parameters.AddWithValue("@ts", Iso(DateTimeOffset.UtcNow));
            cmd.Parameters.AddWithValue("@id", keyId);
            cmd.Parameters.AddWithValue("@tid", tenantId);
            return cmd.ExecuteNonQuery() > 0;
        }
    }

    /// <summary>Validates an inbound plain-text key; returns the associated tenant or null.</summary>
    public Tenant? ValidateApiKey(string plainKey)
    {
        string hash = HashKey(plainKey);
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT t.id, t.name, t.site_code, t.created_at_utc
                FROM api_keys k
                JOIN tenants t ON t.id = k.tenant_id
                WHERE k.key_hash = @hash AND k.revoked_at_utc IS NULL";
            cmd.Parameters.AddWithValue("@hash", hash);
            using var r = cmd.ExecuteReader();
            return r.Read() ? MapTenant(r) : null;
        }
    }

    // ── Asset operations ─────────────────────────────────────────────────────

    public ItamAsset UpsertAsset(string tenantId, string serial, string tag, string? uuid, string? model, string? location)
    {
        lock (_gate)
        {
            using var conn = Open();
            // Check for existing by serial (tenant-scoped)
            ItamAsset? existing = null;
            if (!string.IsNullOrWhiteSpace(serial))
            {
                using var chk = conn.CreateCommand();
                chk.CommandText = "SELECT * FROM assets WHERE tenant_id=@tid AND serial_number=@sn LIMIT 1";
                chk.Parameters.AddWithValue("@tid", tenantId);
                chk.Parameters.AddWithValue("@sn", serial);
                using var cr = chk.ExecuteReader();
                if (cr.Read()) existing = MapAsset(cr);
            }

            if (existing is not null)
            {
                // Update mutable fields only
                Execute(conn,
                    "UPDATE assets SET model=COALESCE(@model,model), current_location=COALESCE(@loc,current_location), updated_at_utc=@ts WHERE id=@id",
                    P("@model", model ?? (object)DBNull.Value), P("@loc", location ?? (object)DBNull.Value),
                    P("@ts", Iso(DateTimeOffset.UtcNow)), P("@id", existing.Id));
                existing = GetAssetById(tenantId, existing.Id)!;
                return existing;
            }

            var asset = new ItamAsset
            {
                Id             = "ast-" + Guid.NewGuid().ToString("N")[..16],
                TenantId       = tenantId,
                SerialNumber   = serial,
                AssetTag       = tag,
                AssetUuid      = uuid ?? "",
                Model          = model ?? "",
                CurrentLocation = location ?? "INTAKE-STAGING",
                LifecycleQueue = "Intake",
                CreatedAtUtc   = DateTimeOffset.UtcNow,
                UpdatedAtUtc   = DateTimeOffset.UtcNow
            };
            Execute(conn, @"
                INSERT INTO assets(id,tenant_id,serial_number,asset_tag,asset_uuid,model,current_location,lifecycle_queue,created_at_utc,updated_at_utc)
                VALUES(@id,@tid,@sn,@tag,@uuid,@model,@loc,@queue,@ca,@ua)",
                P("@id", asset.Id), P("@tid", tenantId),
                P("@sn",   string.IsNullOrWhiteSpace(serial) ? (object)DBNull.Value : serial),
                P("@tag",  string.IsNullOrWhiteSpace(tag)    ? (object)DBNull.Value : tag),
                P("@uuid", string.IsNullOrWhiteSpace(uuid)   ? (object)DBNull.Value : uuid),
                P("@model", model ?? (object)DBNull.Value),
                P("@loc",   asset.CurrentLocation),
                P("@queue", asset.LifecycleQueue),
                P("@ca", Iso(asset.CreatedAtUtc)), P("@ua", Iso(asset.UpdatedAtUtc)));
            return asset;
        }
    }

    public ItamAsset? GetAssetById(string tenantId, string assetId)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM assets WHERE id=@id AND tenant_id=@tid LIMIT 1";
            cmd.Parameters.AddWithValue("@id", assetId);
            cmd.Parameters.AddWithValue("@tid", tenantId);
            using var r = cmd.ExecuteReader();
            return r.Read() ? MapAsset(r) : null;
        }
    }

    public List<ItamAsset> ListAssets(string tenantId, int limit = 50, int offset = 0)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM assets WHERE tenant_id=@tid ORDER BY created_at_utc DESC LIMIT @lim OFFSET @off";
            cmd.Parameters.AddWithValue("@tid", tenantId);
            cmd.Parameters.AddWithValue("@lim", limit);
            cmd.Parameters.AddWithValue("@off", offset);
            using var r = cmd.ExecuteReader();
            var list = new List<ItamAsset>();
            while (r.Read()) list.Add(MapAsset(r));
            return list;
        }
    }

    // ── Event operations ─────────────────────────────────────────────────────

    public AssetEvent AppendEvent(string tenantId, string assetId, string eventType,
        string? location, string? actor, string? notes, string? sourceBenchId)
    {
        var ev = new AssetEvent
        {
            Id            = Guid.NewGuid().ToString("N"),
            AssetId       = assetId,
            TenantId      = tenantId,
            EventType     = eventType,
            Location      = location ?? "",
            Actor         = actor ?? "system",
            NotesJson     = notes ?? "{}",
            SourceBenchId = sourceBenchId ?? "",
            RecordedAtUtc = DateTimeOffset.UtcNow
        };
        lock (_gate)
        {
            using var conn = Open();
            Execute(conn, @"
                INSERT INTO asset_events(id,asset_id,tenant_id,event_type,location,actor,notes_json,source_bench_id,recorded_at_utc)
                VALUES(@id,@aid,@tid,@etype,@loc,@actor,@notes,@bench,@ts)",
                P("@id", ev.Id), P("@aid", assetId), P("@tid", tenantId),
                P("@etype", eventType), P("@loc", ev.Location), P("@actor", ev.Actor),
                P("@notes", ev.NotesJson), P("@bench", ev.SourceBenchId), P("@ts", Iso(ev.RecordedAtUtc)));

            // Update asset location and queue state
            if (!string.IsNullOrWhiteSpace(location))
                Execute(conn, "UPDATE assets SET current_location=@loc, updated_at_utc=@ts WHERE id=@id",
                    P("@loc", location), P("@ts", Iso(DateTimeOffset.UtcNow)), P("@id", assetId));

            string? newQueue = eventType switch
            {
                "Intake"      => "Intake",
                "QcStarted"   => "InTest",
                "QcPassed"    => "ReadyForRelease",
                "QcFailed"    => "Hold",
                "Held"        => "Hold",
                "RepairStart" => "Repair",
                "Released"    => "Released",
                "Disposed"    => "Disposed",
                _             => null
            };
            if (newQueue is not null)
                Execute(conn, "UPDATE assets SET lifecycle_queue=@q, updated_at_utc=@ts WHERE id=@id",
                    P("@q", newQueue), P("@ts", Iso(DateTimeOffset.UtcNow)), P("@id", assetId));
        }
        return ev;
    }

    public List<AssetEvent> GetTimeline(string tenantId, string assetId)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM asset_events WHERE asset_id=@aid AND tenant_id=@tid ORDER BY recorded_at_utc ASC";
            cmd.Parameters.AddWithValue("@aid", assetId);
            cmd.Parameters.AddWithValue("@tid", tenantId);
            using var r = cmd.ExecuteReader();
            var list = new List<AssetEvent>();
            while (r.Read()) list.Add(MapEvent(r));
            return list;
        }
    }

    // ── Observation operations ────────────────────────────────────────────────

    public ComponentObservation AppendObservation(string tenantId, string assetId,
        string componentType, string attrKey, string attrValue,
        string source, string confidence, string? benchId)
    {
        var obs = new ComponentObservation
        {
            Id             = Guid.NewGuid().ToString("N"),
            AssetId        = assetId,
            TenantId       = tenantId,
            ComponentType  = componentType,
            AttributeKey   = attrKey,
            AttributeValue = attrValue,
            Source         = source,
            Confidence     = confidence,
            BenchId        = benchId ?? "",
            ObservedAtUtc  = DateTimeOffset.UtcNow
        };
        lock (_gate)
        {
            using var conn = Open();
            Execute(conn, @"
                INSERT INTO component_observations(id,asset_id,tenant_id,component_type,attribute_key,attribute_value,source,confidence,bench_id,observed_at_utc)
                VALUES(@id,@aid,@tid,@ct,@ak,@av,@src,@conf,@bench,@ts)",
                P("@id", obs.Id), P("@aid", assetId), P("@tid", tenantId),
                P("@ct", componentType), P("@ak", attrKey), P("@av", attrValue),
                P("@src", source), P("@conf", confidence),
                P("@bench", benchId ?? (object)DBNull.Value), P("@ts", Iso(obs.ObservedAtUtc)));
        }
        return obs;
    }

    public List<ComponentObservation> GetObservations(string tenantId, string assetId)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM component_observations WHERE asset_id=@aid AND tenant_id=@tid ORDER BY component_type, attribute_key, observed_at_utc ASC";
            cmd.Parameters.AddWithValue("@aid", assetId);
            cmd.Parameters.AddWithValue("@tid", tenantId);
            using var r = cmd.ExecuteReader();
            var list = new List<ComponentObservation>();
            while (r.Read()) list.Add(MapObservation(r));
            return list;
        }
    }

    /// <summary>Gets the two most recent observations for a given (asset, type, key) pair.</summary>
    public (ComponentObservation? Previous, ComponentObservation? Latest) GetPreviousAndLatest(
        string tenantId, string assetId, string componentType, string attrKey)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT * FROM component_observations
                WHERE asset_id=@aid AND tenant_id=@tid AND component_type=@ct AND attribute_key=@ak
                ORDER BY observed_at_utc DESC LIMIT 2";
            cmd.Parameters.AddWithValue("@aid", assetId);
            cmd.Parameters.AddWithValue("@tid", tenantId);
            cmd.Parameters.AddWithValue("@ct", componentType);
            cmd.Parameters.AddWithValue("@ak", attrKey);
            using var r = cmd.ExecuteReader();
            var rows = new List<ComponentObservation>();
            while (r.Read()) rows.Add(MapObservation(r));
            // Latest is [0], previous is [1] (DESC order)
            return rows.Count >= 2 ? (rows[1], rows[0]) : (null, rows.Count == 1 ? rows[0] : null);
        }
    }

    // ── Difference operations ────────────────────────────────────────────────

    public ComponentDifference? UpsertDifference(ComponentDifference diff)
    {
        lock (_gate)
        {
            using var conn = Open();
            // Idempotent: UNIQUE(obs_a, obs_b, attr_key) — skip if already recorded
            using var chk = conn.CreateCommand();
            chk.CommandText = "SELECT id FROM component_differences WHERE observation_a_id=@a AND observation_b_id=@b AND attribute_key=@ak";
            chk.Parameters.AddWithValue("@a", diff.ObservationAId);
            chk.Parameters.AddWithValue("@b", diff.ObservationBId);
            chk.Parameters.AddWithValue("@ak", diff.AttributeKey);
            var existing = chk.ExecuteScalar() as string;
            if (existing is not null) return null; // already recorded

            Execute(conn, @"
                INSERT INTO component_differences
                    (id,asset_id,tenant_id,component_type,attribute_key,observation_a_id,observation_b_id,
                     value_a,value_b,source_a,source_b,confidence_a,confidence_b,detected_at_utc,approved_replacement)
                VALUES(@id,@aid,@tid,@ct,@ak,@oa,@ob,@va,@vb,@sa,@sb,@ca,@cb,@ts,0)",
                P("@id", diff.Id), P("@aid", diff.AssetId), P("@tid", diff.TenantId),
                P("@ct", diff.ComponentType), P("@ak", diff.AttributeKey),
                P("@oa", diff.ObservationAId), P("@ob", diff.ObservationBId),
                P("@va", diff.ValueA), P("@vb", diff.ValueB),
                P("@sa", diff.SourceA), P("@sb", diff.SourceB),
                P("@ca", diff.ConfidenceA), P("@cb", diff.ConfidenceB),
                P("@ts", Iso(diff.DetectedAtUtc)));
            return diff;
        }
    }

    public List<ComponentDifference> GetDifferences(string tenantId, string assetId)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM component_differences WHERE asset_id=@aid AND tenant_id=@tid ORDER BY detected_at_utc ASC";
            cmd.Parameters.AddWithValue("@aid", assetId);
            cmd.Parameters.AddWithValue("@tid", tenantId);
            using var r = cmd.ExecuteReader();
            var list = new List<ComponentDifference>();
            while (r.Read()) list.Add(MapDifference(r));
            return list;
        }
    }

    public bool ApproveDifference(string tenantId, string diffId, string approvedBy)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE component_differences
                SET approved_replacement=1, approved_by=@by, approved_at_utc=@ts
                WHERE id=@id AND tenant_id=@tid AND approved_replacement=0";
            cmd.Parameters.AddWithValue("@by", approvedBy);
            cmd.Parameters.AddWithValue("@ts", Iso(DateTimeOffset.UtcNow));
            cmd.Parameters.AddWithValue("@id", diffId);
            cmd.Parameters.AddWithValue("@tid", tenantId);
            return cmd.ExecuteNonQuery() > 0;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    private static void Execute(SqliteConnection conn, string sql, params (string Name, object Value)[] parms)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parms)
            cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }

    private static (string Name, object Value) P(string name, object value) => (name, value);
    private static string Iso(DateTimeOffset dt) => dt.UtcDateTime.ToString("o");

    private static string HashKey(string plain)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plain));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // ── Mappers ──────────────────────────────────────────────────────────────

    private static Tenant MapTenant(SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("id")),
        Name = r.GetString(r.GetOrdinal("name")),
        SiteCode = r.GetString(r.GetOrdinal("site_code")),
        CreatedAtUtc = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("created_at_utc")))
    };

    private static ItamAsset MapAsset(SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("id")),
        TenantId = r.GetString(r.GetOrdinal("tenant_id")),
        SerialNumber = r.IsDBNull(r.GetOrdinal("serial_number")) ? "" : r.GetString(r.GetOrdinal("serial_number")),
        AssetTag = r.IsDBNull(r.GetOrdinal("asset_tag")) ? "" : r.GetString(r.GetOrdinal("asset_tag")),
        AssetUuid = r.IsDBNull(r.GetOrdinal("asset_uuid")) ? "" : r.GetString(r.GetOrdinal("asset_uuid")),
        Model = r.IsDBNull(r.GetOrdinal("model")) ? "" : r.GetString(r.GetOrdinal("model")),
        CurrentLocation = r.IsDBNull(r.GetOrdinal("current_location")) ? "" : r.GetString(r.GetOrdinal("current_location")),
        LifecycleQueue = r.GetString(r.GetOrdinal("lifecycle_queue")),
        CreatedAtUtc = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("created_at_utc"))),
        UpdatedAtUtc = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("updated_at_utc")))
    };

    private static AssetEvent MapEvent(SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("id")),
        AssetId = r.GetString(r.GetOrdinal("asset_id")),
        TenantId = r.GetString(r.GetOrdinal("tenant_id")),
        EventType = r.GetString(r.GetOrdinal("event_type")),
        Location = r.IsDBNull(r.GetOrdinal("location")) ? "" : r.GetString(r.GetOrdinal("location")),
        Actor = r.IsDBNull(r.GetOrdinal("actor")) ? "" : r.GetString(r.GetOrdinal("actor")),
        NotesJson = r.GetString(r.GetOrdinal("notes_json")),
        SourceBenchId = r.IsDBNull(r.GetOrdinal("source_bench_id")) ? "" : r.GetString(r.GetOrdinal("source_bench_id")),
        RecordedAtUtc = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("recorded_at_utc")))
    };

    private static ComponentObservation MapObservation(SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("id")),
        AssetId = r.GetString(r.GetOrdinal("asset_id")),
        TenantId = r.GetString(r.GetOrdinal("tenant_id")),
        ComponentType = r.GetString(r.GetOrdinal("component_type")),
        AttributeKey = r.GetString(r.GetOrdinal("attribute_key")),
        AttributeValue = r.GetString(r.GetOrdinal("attribute_value")),
        Source = r.GetString(r.GetOrdinal("source")),
        Confidence = r.GetString(r.GetOrdinal("confidence")),
        BenchId = r.IsDBNull(r.GetOrdinal("bench_id")) ? "" : r.GetString(r.GetOrdinal("bench_id")),
        ObservedAtUtc = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("observed_at_utc")))
    };

    private static ComponentDifference MapDifference(SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("id")),
        AssetId = r.GetString(r.GetOrdinal("asset_id")),
        TenantId = r.GetString(r.GetOrdinal("tenant_id")),
        ComponentType = r.GetString(r.GetOrdinal("component_type")),
        AttributeKey = r.GetString(r.GetOrdinal("attribute_key")),
        ObservationAId = r.GetString(r.GetOrdinal("observation_a_id")),
        ObservationBId = r.GetString(r.GetOrdinal("observation_b_id")),
        ValueA = r.GetString(r.GetOrdinal("value_a")),
        ValueB = r.GetString(r.GetOrdinal("value_b")),
        SourceA = r.GetString(r.GetOrdinal("source_a")),
        SourceB = r.GetString(r.GetOrdinal("source_b")),
        ConfidenceA = r.GetString(r.GetOrdinal("confidence_a")),
        ConfidenceB = r.GetString(r.GetOrdinal("confidence_b")),
        DetectedAtUtc = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("detected_at_utc"))),
        ApprovedReplacement = r.GetInt32(r.GetOrdinal("approved_replacement")) == 1,
        ApprovedBy = r.IsDBNull(r.GetOrdinal("approved_by")) ? null : r.GetString(r.GetOrdinal("approved_by")),
        ApprovedAtUtc = r.IsDBNull(r.GetOrdinal("approved_at_utc")) ? null
                        : DateTimeOffset.Parse(r.GetString(r.GetOrdinal("approved_at_utc")))
    };
}
