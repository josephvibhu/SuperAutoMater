using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Velox.Shared.Dtos;
using Velox.Shared.Models;

namespace VeloxAPI.Data;

/// <summary>
/// SQLite data-access layer for the Velox platform.
/// All writes are append-only or additive — raw observations are never mutated.
/// Thread-safe via internal _gate lock.
/// </summary>
public sealed class VeloxDb : IDisposable
{
    private readonly string _connectionString;
    private readonly object _gate = new();
    private readonly SqliteConnection? _keepAliveConn;

    public string DbPath { get; }

    public VeloxDb(string? dbPath = null)
    {
        if (dbPath != null && dbPath.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
        {
            // Raw connection string passed directly (for in-memory test DBs)
            _connectionString = dbPath;
            DbPath = dbPath;
        }
        else
        {
            var root = dbPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Velox", "VeloxAPI");
            Directory.CreateDirectory(root);
            DbPath = Path.Combine(root, "velox.db");
            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = DbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            }.ToString();
        }

        if (_connectionString.Contains("Mode=Memory", StringComparison.OrdinalIgnoreCase))
        {
            _keepAliveConn = new SqliteConnection(_connectionString);
            _keepAliveConn.Open();
        }

        Initialize();
    }

    public static VeloxDb CreateInMemory(string? name = null)
    {
        var tag = name ?? ("test_" + Guid.NewGuid().ToString("N"));
        return new VeloxDb($"Data Source={tag};Mode=Memory;Cache=Shared");
    }

    // ── Schema Bootstrap ────────────────────────────────────────────────────

    public void Initialize()
    {
        lock (_gate)
        {
            using var conn = Open();
            Exec(conn, "PRAGMA journal_mode=WAL;");
            Exec(conn, "PRAGMA foreign_keys=ON;");

            Exec(conn, @"
CREATE TABLE IF NOT EXISTS schema_migrations (
    version INTEGER PRIMARY KEY,
    applied_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS tenants (
    id             TEXT PRIMARY KEY,
    name           TEXT NOT NULL,
    site_code      TEXT NOT NULL UNIQUE COLLATE NOCASE,
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
CREATE UNIQUE INDEX IF NOT EXISTS ix_api_keys_hash ON api_keys(key_hash);

CREATE TABLE IF NOT EXISTS employees (
    id             TEXT PRIMARY KEY,
    tenant_id      TEXT NOT NULL,
    name           TEXT NOT NULL,
    pin_hash       TEXT NOT NULL,
    role           TEXT NOT NULL DEFAULT 'Technician',
    is_active      INTEGER NOT NULL DEFAULT 1,
    station_id     TEXT NULL DEFAULT 'BENCH-01',
    created_at_utc TEXT NOT NULL,
    FOREIGN KEY(tenant_id) REFERENCES tenants(id)
);
CREATE INDEX IF NOT EXISTS ix_employees_tenant ON employees(tenant_id);

CREATE TABLE IF NOT EXISTS assets (
    id                   TEXT PRIMARY KEY,
    tenant_id            TEXT NOT NULL,
    serial_number        TEXT NULL COLLATE NOCASE,
    asset_tag            TEXT NULL COLLATE NOCASE,
    asset_uuid           TEXT NULL,
    model                TEXT NULL,
    current_location     TEXT NULL,
    lifecycle_queue      TEXT NOT NULL DEFAULT 'IntakeStaging',
    grade                TEXT NOT NULL DEFAULT 'Unknown',
    supplier             TEXT NULL,
    customer             TEXT NULL,
    assigned_employee_id TEXT NULL,
    purchase_price       REAL NULL,
    sale_price           REAL NULL,
    missing_components   TEXT NULL,
    cosmetic_grade       TEXT NULL,
    battery_health       INTEGER NULL,
    storage_health       INTEGER NULL,
    notes                TEXT NULL,
    in_date              TEXT NULL,
    out_date             TEXT NULL,
    created_at_utc       TEXT NOT NULL,
    updated_at_utc       TEXT NOT NULL,
    FOREIGN KEY(tenant_id) REFERENCES tenants(id)
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_assets_tenant_serial
    ON assets(tenant_id, serial_number)
    WHERE serial_number IS NOT NULL AND serial_number <> '';
CREATE UNIQUE INDEX IF NOT EXISTS ix_assets_tenant_tag
    ON assets(tenant_id, asset_tag)
    WHERE asset_tag IS NOT NULL AND asset_tag <> '';
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

INSERT OR IGNORE INTO schema_migrations(version, applied_at_utc) VALUES(1, CURRENT_TIMESTAMP);");

            // Migration: ensure station_id exists on employees table
            try
            {
                Exec(conn, "ALTER TABLE employees ADD COLUMN station_id TEXT DEFAULT 'BENCH-01'");
            }
            catch
            {
                // Column already exists
            }

            // Auto-seed default depot tenant, primary API key, and technician if empty
            using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = "SELECT COUNT(*) FROM tenants";
            var tenantCount = Convert.ToInt64(checkCmd.ExecuteScalar());
            if (tenantCount == 0)
            {
                var defaultTenantId = "ten-d2428a4cb3da";
                var defaultKey = "vx-2fe1479ad70dffd3017a0aa8c67b1c270fc9dd00bb9d4bdf";
                var keyHash = Sha256(defaultKey);
                var now = Iso(DateTimeOffset.UtcNow);

                Exec(conn,
                    "INSERT INTO tenants(id,name,site_code,created_at_utc) VALUES(@id,@name,@site,@ts)",
                    P("@id", defaultTenantId), P("@name", "Velox Main Depot"),
                    P("@site", "MAIN"), P("@ts", now));

                Exec(conn,
                    "INSERT INTO api_keys(id,tenant_id,key_hash,label,created_at_utc) VALUES(@id,@tid,@kh,@lbl,@ts)",
                    P("@id", "key-default-001"), P("@tid", defaultTenantId), P("@kh", keyHash),
                    P("@lbl", "primary"), P("@ts", now));

                var pinHash = PinHash(defaultTenantId, "1234");
                Exec(conn,
                    "INSERT INTO employees(id,tenant_id,name,pin_hash,role,is_active,station_id,created_at_utc) VALUES(@id,@tid,@name,@ph,@role,1,'BENCH-01',@ts)",
                    P("@id", "emp-john-tech-01"), P("@tid", defaultTenantId), P("@name", "John Tech"),
                    P("@ph", pinHash), P("@role", "Technician"), P("@ts", now));
            }
        }
    }

    // ── Tenant / API Key ────────────────────────────────────────────────────

    public (string TenantId, string PlainKey) CreateTenantWithKey(string name, string siteCode, string keyLabel)
    {
        var tenantId = "ten-" + NewId();
        var plainKey = "vx-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var keyHash  = Sha256(plainKey);
        var keyId    = "key-" + NewId();
        var now      = Iso(DateTimeOffset.UtcNow);

        lock (_gate)
        {
            using var conn = Open();
            Exec(conn,
                "INSERT INTO tenants(id,name,site_code,created_at_utc) VALUES(@id,@name,@site,@ts)",
                P("@id", tenantId), P("@name", name),
                P("@site", siteCode.ToUpperInvariant()), P("@ts", now));

            Exec(conn,
                "INSERT INTO api_keys(id,tenant_id,key_hash,label,created_at_utc) VALUES(@id,@tid,@kh,@lbl,@ts)",
                P("@id", keyId), P("@tid", tenantId), P("@kh", keyHash),
                P("@lbl", keyLabel), P("@ts", now));
        }
        return (tenantId, plainKey);
    }

    public string? ResolveTenantFromApiKey(string plainKey)
    {
        var hash = Sha256(plainKey);
        lock (_gate)
        {
            using var conn = Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "SELECT tenant_id FROM api_keys WHERE key_hash=@h AND revoked_at_utc IS NULL";
            cmd.Parameters.AddWithValue("@h", hash);
            var result = cmd.ExecuteScalar();
            return result as string;
        }
    }

    // ── Employees ────────────────────────────────────────────────────────────

    public EmployeeDto CreateEmployee(string tenantId, string name, string plainPin, EmployeeRole role, string stationId = "BENCH-01")
    {
        var emp = new EmployeeDto
        {
            Id            = "emp-" + NewId(),
            TenantId      = tenantId,
            Name          = name,
            Role          = role,
            IsActive      = true,
            StationId     = string.IsNullOrWhiteSpace(stationId) ? "BENCH-01" : stationId,
            CreatedAtUtc  = DateTimeOffset.UtcNow
        };
        lock (_gate)
        {
            using var conn = Open();
            Exec(conn,
                "INSERT INTO employees(id,tenant_id,name,pin_hash,role,is_active,station_id,created_at_utc) VALUES(@id,@tid,@name,@ph,@role,1,@station,@ts)",
                P("@id", emp.Id), P("@tid", tenantId), P("@name", name),
                P("@ph", PinHash(tenantId, plainPin)), P("@role", role.ToString()),
                P("@station", emp.StationId),
                P("@ts", Iso(emp.CreatedAtUtc)));
        }
        return emp;
    }

    public EmployeeDto? ValidatePin(string tenantId, string name, string plainPin)
    {
        var hash = PinHash(tenantId, plainPin);
        lock (_gate)
        {
            using var conn = Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "SELECT id,tenant_id,name,role,is_active,station_id,created_at_utc FROM employees WHERE tenant_id=@tid AND name=@name COLLATE NOCASE AND pin_hash=@ph AND is_active=1";
            cmd.Parameters.AddWithValue("@tid", tenantId);
            cmd.Parameters.AddWithValue("@name", name);
            cmd.Parameters.AddWithValue("@ph", hash);
            using var r = cmd.ExecuteReader();
            return r.Read() ? MapEmployee(r) : null;
        }
    }

    public EmployeeDto? GetEmployee(string tenantId, string id)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "SELECT id,tenant_id,name,role,is_active,station_id,created_at_utc FROM employees WHERE tenant_id=@tid AND id=@id";
            cmd.Parameters.AddWithValue("@tid", tenantId);
            cmd.Parameters.AddWithValue("@id", id);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            var emp = MapEmployee(r);
            emp.WorkloadCount = CountAssigned(conn, tenantId, emp.Id);
            return emp;
        }
    }

    public List<EmployeeDto> ListEmployees(string tenantId)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "SELECT id,tenant_id,name,role,is_active,station_id,created_at_utc FROM employees WHERE tenant_id=@tid ORDER BY name";
            cmd.Parameters.AddWithValue("@tid", tenantId);
            var list = new List<EmployeeDto>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var emp = MapEmployee(r);
                emp.WorkloadCount = CountAssigned(conn, tenantId, emp.Id);
                list.Add(emp);
            }
            return list;
        }
    }

    public bool PatchEmployee(string tenantId, string id, PatchEmployeeRequest req)
    {
        lock (_gate)
        {
            using var conn = Open();
            // Verify exists
            using var check = conn.CreateCommand();
            check.CommandText = "SELECT COUNT(1) FROM employees WHERE tenant_id=@tid AND id=@id";
            check.Parameters.AddWithValue("@tid", tenantId);
            check.Parameters.AddWithValue("@id", id);
            if ((long)(check.ExecuteScalar() ?? 0L) == 0) return false;

            var sets = new List<string>();
            var parms = new List<SqliteParameter>();

            if (req.Name != null)      { sets.Add("name=@name");       parms.Add(P("@name", req.Name)); }
            if (req.Pin != null)       { sets.Add("pin_hash=@ph");     parms.Add(P("@ph", PinHash(tenantId, req.Pin))); }
            if (req.Role != null)      { sets.Add("role=@role");       parms.Add(P("@role", req.Role.ToString())); }
            if (req.IsActive != null)  { sets.Add("is_active=@act");   parms.Add(P("@act", req.IsActive.Value ? 1 : 0)); }
            if (req.StationId != null) { sets.Add("station_id=@st");   parms.Add(P("@st", req.StationId)); }

            if (sets.Count == 0) return true;

            parms.Add(P("@tid", tenantId));
            parms.Add(P("@id", id));
            Exec(conn, $"UPDATE employees SET {string.Join(",", sets)} WHERE tenant_id=@tid AND id=@id", [.. parms]);
            return true;
        }
    }

    // ── Assets ───────────────────────────────────────────────────────────────

    public AssetDto UpsertAsset(string tenantId, IntakeRequest req)
    {
        var now = Iso(DateTimeOffset.UtcNow);
        lock (_gate)
        {
            using var conn = Open();
            // Try to find existing by serial or tag
            AssetDto? existing = null;
            if (!string.IsNullOrWhiteSpace(req.SerialNumber))
                existing = FindAsset(conn, tenantId, "serial_number", req.SerialNumber);
            if (existing == null && !string.IsNullOrWhiteSpace(req.AssetTag))
                existing = FindAsset(conn, tenantId, "asset_tag", req.AssetTag);

            if (existing != null)
            {
                // Update known fields if supplied
                var sets = new List<string> { "updated_at_utc=@ts" };
                var parms = new List<SqliteParameter> { P("@ts", now) };
                if (!string.IsNullOrWhiteSpace(req.Model))    { sets.Add("model=@m");       parms.Add(P("@m", req.Model)); }
                if (!string.IsNullOrWhiteSpace(req.Location)) { sets.Add("current_location=@loc"); parms.Add(P("@loc", req.Location)); }
                if (!string.IsNullOrWhiteSpace(req.Supplier)) { sets.Add("supplier=@sup");  parms.Add(P("@sup", req.Supplier)); }
                if (!string.IsNullOrWhiteSpace(req.InDate))   { sets.Add("in_date=@ind");   parms.Add(P("@ind", req.InDate)); }
                parms.Add(P("@tid", tenantId)); parms.Add(P("@id", existing.Id));
                Exec(conn, $"UPDATE assets SET {string.Join(",", sets)} WHERE tenant_id=@tid AND id=@id", [.. parms]);
                return GetAssetById(tenantId, existing.Id, conn)!;
            }

            // New asset
            var assetId = "ast-" + NewId();
            Exec(conn, @"INSERT INTO assets(id,tenant_id,serial_number,asset_tag,asset_uuid,model,current_location,lifecycle_queue,grade,supplier,in_date,created_at_utc,updated_at_utc)
                         VALUES(@id,@tid,@sn,@tag,@uuid,@model,@loc,'IntakeStaging','Unknown',@sup,@ind,@ts,@ts)",
                P("@id", assetId), P("@tid", tenantId),
                P("@sn",   req.SerialNumber ?? (object)DBNull.Value),
                P("@tag",  req.AssetTag     ?? (object)DBNull.Value),
                P("@uuid", req.AssetUuid    ?? (object)DBNull.Value),
                P("@model",req.Model        ?? (object)DBNull.Value),
                P("@loc",  req.Location     ?? (object)DBNull.Value),
                P("@sup",  req.Supplier     ?? (object)DBNull.Value),
                P("@ind",  req.InDate       ?? (object)DBNull.Value),
                P("@ts",   now));

            return GetAssetById(tenantId, assetId, conn)!;
        }
    }

    public AssetDto? GetAssetById(string tenantId, string id, SqliteConnection? existingConn = null)
    {
        if (existingConn != null) return QuerySingleAsset(existingConn, "WHERE a.tenant_id=@tid AND a.id=@id", P("@tid", tenantId), P("@id", id));
        lock (_gate)
        {
            using var conn = Open();
            return QuerySingleAsset(conn, "WHERE a.tenant_id=@tid AND a.id=@id", P("@tid", tenantId), P("@id", id));
        }
    }

    public AssetDto? FindAssetBySerial(string tenantId, string serial)
    {
        lock (_gate)
        {
            using var conn = Open();
            return QuerySingleAsset(conn, "WHERE a.tenant_id=@tid AND a.serial_number=@v COLLATE NOCASE", P("@tid", tenantId), P("@v", serial));
        }
    }

    public AssetDto? FindAssetByTag(string tenantId, string tag)
    {
        lock (_gate)
        {
            using var conn = Open();
            return QuerySingleAsset(conn, "WHERE a.tenant_id=@tid AND a.asset_tag=@v COLLATE NOCASE", P("@tid", tenantId), P("@v", tag));
        }
    }

    public AssetDto? FindAssetByAny(string tenantId, string q)
    {
        lock (_gate)
        {
            using var conn = Open();
            return QuerySingleAsset(conn,
                "WHERE a.tenant_id=@tid AND (a.serial_number=@q COLLATE NOCASE OR a.asset_tag=@q COLLATE NOCASE OR a.asset_uuid=@q)",
                P("@tid", tenantId), P("@q", q));
        }
    }

    public (List<AssetDto> Items, int Total) SearchAssets(string tenantId, string? q, string? queue, string? grade, int limit, int offset)
    {
        limit  = Math.Clamp(limit,  1, 200);
        offset = Math.Max(0, offset);

        var conditions = new List<string> { "a.tenant_id=@tid" };
        var parms = new List<SqliteParameter> { P("@tid", tenantId) };

        if (!string.IsNullOrWhiteSpace(q))
        {
            conditions.Add("(a.serial_number LIKE @q COLLATE NOCASE OR a.asset_tag LIKE @q COLLATE NOCASE OR a.model LIKE @q COLLATE NOCASE OR a.supplier LIKE @q COLLATE NOCASE OR a.customer LIKE @q COLLATE NOCASE)");
            parms.Add(P("@q", $"%{q}%"));
        }
        if (!string.IsNullOrWhiteSpace(queue)) { conditions.Add("a.lifecycle_queue=@queue"); parms.Add(P("@queue", queue)); }
        if (!string.IsNullOrWhiteSpace(grade)) { conditions.Add("a.grade=@grade");           parms.Add(P("@grade", grade)); }

        var where = "WHERE " + string.Join(" AND ", conditions);

        lock (_gate)
        {
            using var conn = Open();

            // Count
            using var countCmd = conn.CreateCommand();
            countCmd.CommandText = $"SELECT COUNT(1) FROM assets a {where}";
            foreach (var p in parms) countCmd.Parameters.Add(p);
            var total = (int)(long)(countCmd.ExecuteScalar() ?? 0L);

            // Page
            var items = QueryAssets(conn, $"{where} ORDER BY a.updated_at_utc DESC LIMIT @lim OFFSET @off",
                [.. parms, P("@lim", limit), P("@off", offset)]);

            return (items, total);
        }
    }

    public List<AssetDto> ListAssets(string tenantId, int limit = 50, int offset = 0)
    {
        limit  = Math.Clamp(limit, 1, 200);
        offset = Math.Max(0, offset);
        lock (_gate)
        {
            using var conn = Open();
            return QueryAssets(conn, "WHERE a.tenant_id=@tid ORDER BY a.updated_at_utc DESC LIMIT @lim OFFSET @off",
                P("@tid", tenantId), P("@lim", limit), P("@off", offset));
        }
    }

    public bool PatchAsset(string tenantId, string id, PatchAssetRequest req)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var check = conn.CreateCommand();
            check.CommandText = "SELECT COUNT(1) FROM assets WHERE tenant_id=@tid AND id=@id";
            check.Parameters.AddWithValue("@tid", tenantId);
            check.Parameters.AddWithValue("@id", id);
            if ((long)(check.ExecuteScalar() ?? 0L) == 0) return false;

            var sets  = new List<string> { "updated_at_utc=@ts" };
            var parms = new List<SqliteParameter> { P("@ts", Iso(DateTimeOffset.UtcNow)) };

            if (req.CurrentLocation   != null) { sets.Add("current_location=@loc");     parms.Add(P("@loc",   req.CurrentLocation)); }
            if (req.LifecycleQueue    != null) { sets.Add("lifecycle_queue=@q");         parms.Add(P("@q",     req.LifecycleQueue.ToString())); }
            if (req.Grade             != null) { sets.Add("grade=@grade");               parms.Add(P("@grade", req.Grade.ToString())); }
            if (req.Supplier          != null) { sets.Add("supplier=@sup");              parms.Add(P("@sup",   req.Supplier)); }
            if (req.Customer          != null) { sets.Add("customer=@cust");             parms.Add(P("@cust",  req.Customer)); }
            if (req.MissingComponents != null) { sets.Add("missing_components=@mc");     parms.Add(P("@mc",    req.MissingComponents)); }
            if (req.CosmeticGrade     != null) { sets.Add("cosmetic_grade=@cg");         parms.Add(P("@cg",    req.CosmeticGrade)); }
            if (req.BatteryHealth     != null) { sets.Add("battery_health=@bh");         parms.Add(P("@bh",    req.BatteryHealth.Value)); }
            if (req.StorageHealth     != null) { sets.Add("storage_health=@sh");         parms.Add(P("@sh",    req.StorageHealth.Value)); }
            if (req.Notes             != null) { sets.Add("notes=@notes");               parms.Add(P("@notes", req.Notes)); }
            if (req.InDate            != null) { sets.Add("in_date=@ind");               parms.Add(P("@ind",   req.InDate)); }
            if (req.OutDate           != null) { sets.Add("out_date=@outd");             parms.Add(P("@outd",  req.OutDate)); }
            if (req.PurchasePrice     != null) { sets.Add("purchase_price=@pp");         parms.Add(P("@pp",    req.PurchasePrice.Value)); }
            if (req.SalePrice         != null) { sets.Add("sale_price=@sp");             parms.Add(P("@sp",    req.SalePrice.Value)); }

            parms.Add(P("@tid", tenantId));
            parms.Add(P("@id", id));
            Exec(conn, $"UPDATE assets SET {string.Join(",", sets)} WHERE tenant_id=@tid AND id=@id", [.. parms]);
            return true;
        }
    }

    public bool AssignAsset(string tenantId, string assetId, string employeeId)
    {
        lock (_gate)
        {
            using var conn = Open();
            var now = Iso(DateTimeOffset.UtcNow);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE assets SET assigned_employee_id=@eid, updated_at_utc=@ts WHERE tenant_id=@tid AND id=@id";
            cmd.Parameters.AddWithValue("@eid", employeeId);
            cmd.Parameters.AddWithValue("@ts", now);
            cmd.Parameters.AddWithValue("@tid", tenantId);
            cmd.Parameters.AddWithValue("@id", assetId);
            return cmd.ExecuteNonQuery() > 0;
        }
    }

    // ── Asset Events ─────────────────────────────────────────────────────────

    public AssetEventDto AppendEvent(string tenantId, string assetId, AppendEventRequest req)
    {
        var ev = new AssetEventDto
        {
            Id            = "evt-" + NewId(),
            AssetId       = assetId,
            TenantId      = tenantId,
            EventType     = req.EventType,
            Location      = req.Location ?? "",
            Actor         = req.Actor    ?? "",
            NotesJson     = req.NotesJson ?? "{}",
            SourceBenchId = req.SourceBenchId ?? "",
            RecordedAtUtc = DateTimeOffset.UtcNow
        };
        lock (_gate)
        {
            using var conn = Open();
            Exec(conn,
                "INSERT INTO asset_events(id,asset_id,tenant_id,event_type,location,actor,notes_json,source_bench_id,recorded_at_utc) VALUES(@id,@aid,@tid,@et,@loc,@act,@nj,@src,@ts)",
                P("@id",  ev.Id), P("@aid", ev.AssetId), P("@tid", ev.TenantId),
                P("@et",  ev.EventType), P("@loc", ev.Location), P("@act", ev.Actor),
                P("@nj",  ev.NotesJson), P("@src", ev.SourceBenchId), P("@ts", Iso(ev.RecordedAtUtc)));
        }
        return ev;
    }

    public List<AssetEventDto> GetTimeline(string tenantId, string assetId)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "SELECT id,asset_id,tenant_id,event_type,location,actor,notes_json,source_bench_id,recorded_at_utc FROM asset_events WHERE tenant_id=@tid AND asset_id=@aid ORDER BY recorded_at_utc ASC";
            cmd.Parameters.AddWithValue("@tid", tenantId);
            cmd.Parameters.AddWithValue("@aid", assetId);
            var list = new List<AssetEventDto>();
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(MapEvent(r));
            return list;
        }
    }

    // ── Jobs ─────────────────────────────────────────────────────────────────

    public List<JobDto> GetJobsForEmployee(string tenantId, string employeeId)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"SELECT a.id, a.asset_tag, a.serial_number, a.model, a.lifecycle_queue, a.grade, a.notes, a.updated_at_utc
                                FROM assets a WHERE a.tenant_id=@tid AND a.assigned_employee_id=@eid ORDER BY a.updated_at_utc DESC";
            cmd.Parameters.AddWithValue("@tid", tenantId);
            cmd.Parameters.AddWithValue("@eid", employeeId);
            var list = new List<JobDto>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new JobDto
                {
                    AssetId       = r.GetString(0),
                    AssetTag      = r.IsDBNull(1) ? "" : r.GetString(1),
                    SerialNumber  = r.IsDBNull(2) ? "" : r.GetString(2),
                    Model         = r.IsDBNull(3) ? "" : r.GetString(3),
                    Queue         = Enum.TryParse<LifecycleQueue>(r.GetString(4), out var q) ? q : LifecycleQueue.IntakeStaging,
                    Grade         = Enum.TryParse<AssetGrade>(r.GetString(5), out var g)     ? g : AssetGrade.Unknown,
                    Notes         = r.IsDBNull(6) ? "" : r.GetString(6),
                    AssignedAtUtc = DateTimeOffset.Parse(r.GetString(7))
                });
            }
            return list;
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    private static void Exec(SqliteConnection conn, string sql, params SqliteParameter[] parms)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in parms) cmd.Parameters.Add(p);
        cmd.ExecuteNonQuery();
    }

    private static SqliteParameter P(string name, object? value) =>
        new SqliteParameter(name, value ?? DBNull.Value);

    private static string NewId() => Guid.NewGuid().ToString("N")[..12];
    private static string Iso(DateTimeOffset dt) => dt.UtcDateTime.ToString("o");

    private static string Sha256(string input)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string PinHash(string tenantId, string pin) =>
        Sha256($"velox:{tenantId}:{pin}");

    private AssetDto? FindAsset(SqliteConnection conn, string tenantId, string col, string val)
        => QuerySingleAsset(conn, $"WHERE a.tenant_id=@tid AND a.{col}=@v COLLATE NOCASE", P("@tid", tenantId), P("@v", val));

    private int CountAssigned(SqliteConnection conn, string tenantId, string empId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM assets WHERE tenant_id=@tid AND assigned_employee_id=@eid";
        cmd.Parameters.AddWithValue("@tid", tenantId);
        cmd.Parameters.AddWithValue("@eid", empId);
        return (int)(long)(cmd.ExecuteScalar() ?? 0L);
    }

    private AssetDto? QuerySingleAsset(SqliteConnection conn, string where, params SqliteParameter[] parms)
    {
        var list = QueryAssets(conn, where, parms);
        return list.Count > 0 ? list[0] : null;
    }

    private List<AssetDto> QueryAssets(SqliteConnection conn, string where, params SqliteParameter[] parms)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"SELECT a.id, a.tenant_id, a.serial_number, a.asset_tag, a.asset_uuid,
                a.model, a.current_location, a.lifecycle_queue, a.grade, a.supplier, a.customer,
                a.assigned_employee_id, a.purchase_price, a.sale_price, a.missing_components,
                a.cosmetic_grade, a.battery_health, a.storage_health, a.notes, a.in_date,
                a.out_date, a.created_at_utc, a.updated_at_utc,
                e.name as emp_name,
                (SELECT event_type FROM asset_events WHERE asset_id=a.id ORDER BY recorded_at_utc DESC LIMIT 1) as latest_event
            FROM assets a
            LEFT JOIN employees e ON e.id = a.assigned_employee_id
            {where}";
        foreach (var p in parms) cmd.Parameters.Add(p);
        var list = new List<AssetDto>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(MapAsset(r));
        return list;
    }

    private static AssetDto MapAsset(SqliteDataReader r) => new()
    {
        Id                 = r.GetString(0),
        TenantId           = r.GetString(1),
        SerialNumber       = r.IsDBNull(2)  ? "" : r.GetString(2),
        AssetTag           = r.IsDBNull(3)  ? "" : r.GetString(3),
        AssetUuid          = r.IsDBNull(4)  ? "" : r.GetString(4),
        Model              = r.IsDBNull(5)  ? "" : r.GetString(5),
        CurrentLocation    = r.IsDBNull(6)  ? "" : r.GetString(6),
        LifecycleQueue     = Enum.TryParse<LifecycleQueue>(r.GetString(7), out var q) ? q : LifecycleQueue.IntakeStaging,
        Grade              = Enum.TryParse<AssetGrade>(r.GetString(8), out var g)     ? g : AssetGrade.Unknown,
        Supplier           = r.IsDBNull(9)  ? "" : r.GetString(9),
        Customer           = r.IsDBNull(10) ? "" : r.GetString(10),
        AssignedEmployeeId = r.IsDBNull(11) ? "" : r.GetString(11),
        PurchasePrice      = r.IsDBNull(12) ? null : r.GetDouble(12),
        SalePrice          = r.IsDBNull(13) ? null : r.GetDouble(13),
        MissingComponents  = r.IsDBNull(14) ? "" : r.GetString(14),
        CosmeticGrade      = r.IsDBNull(15) ? "" : r.GetString(15),
        BatteryHealth      = r.IsDBNull(16) ? null : r.GetInt32(16),
        StorageHealth      = r.IsDBNull(17) ? null : r.GetInt32(17),
        Notes              = r.IsDBNull(18) ? "" : r.GetString(18),
        InDate             = r.IsDBNull(19) ? "" : r.GetString(19),
        OutDate            = r.IsDBNull(20) ? "" : r.GetString(20),
        CreatedAtUtc       = DateTimeOffset.Parse(r.GetString(21)),
        UpdatedAtUtc       = DateTimeOffset.Parse(r.GetString(22)),
        AssignedEmployeeName = r.IsDBNull(23) ? "" : r.GetString(23),
        LatestEventType    = r.IsDBNull(24) ? "" : r.GetString(24),
    };

    private static EmployeeDto MapEmployee(SqliteDataReader r) => new()
    {
        Id           = r.GetString(0),
        TenantId     = r.GetString(1),
        Name         = r.GetString(2),
        Role         = Enum.TryParse<EmployeeRole>(r.GetString(3), out var role) ? role : EmployeeRole.Technician,
        IsActive     = r.GetInt32(4) == 1,
        StationId    = r.IsDBNull(5) ? "BENCH-01" : r.GetString(5),
        CreatedAtUtc = DateTimeOffset.Parse(r.GetString(6))
    };

    private static AssetEventDto MapEvent(SqliteDataReader r) => new()
    {
        Id            = r.GetString(0),
        AssetId       = r.GetString(1),
        TenantId      = r.GetString(2),
        EventType     = r.GetString(3),
        Location      = r.IsDBNull(4) ? "" : r.GetString(4),
        Actor         = r.IsDBNull(5) ? "" : r.GetString(5),
        NotesJson     = r.GetString(6),
        SourceBenchId = r.IsDBNull(7) ? "" : r.GetString(7),
        RecordedAtUtc = DateTimeOffset.Parse(r.GetString(8))
    };

    public void Dispose()
    {
        _keepAliveConn?.Dispose();
    }
}
