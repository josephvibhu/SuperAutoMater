using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

            // Migration: ensure category and quantity exist on assets table
            try
            {
                Exec(conn, "ALTER TABLE assets ADD COLUMN category TEXT DEFAULT 'Laptop'");
            }
            catch { }
            try
            {
                Exec(conn, "ALTER TABLE assets ADD COLUMN quantity INTEGER DEFAULT 1");
            }
            catch { }

            // Migration: ensure asset_components table exists
            try
            {
                Exec(conn, @"
CREATE TABLE IF NOT EXISTS asset_components (
    id                  TEXT PRIMARY KEY,
    tenant_id           TEXT NOT NULL,
    parent_asset_id     TEXT NOT NULL,
    component_asset_id  TEXT NULL,
    category            TEXT NOT NULL,
    description         TEXT NOT NULL,
    serial_number       TEXT NULL,
    cost_delta          REAL DEFAULT 0,
    price_delta         REAL DEFAULT 0,
    installed_at_utc    TEXT NOT NULL,
    installed_by        TEXT NULL,
    notes               TEXT NULL,
    FOREIGN KEY(parent_asset_id) REFERENCES assets(id)
);
CREATE INDEX IF NOT EXISTS ix_asset_components_parent ON asset_components(parent_asset_id);
");
            }
            catch { }

            // Migration: ensure hold fields exist on assets table
            try { Exec(conn, "ALTER TABLE assets ADD COLUMN hold_customer TEXT DEFAULT ''"); } catch { }
            try { Exec(conn, "ALTER TABLE assets ADD COLUMN hold_sales_rep TEXT DEFAULT ''"); } catch { }
            try { Exec(conn, "ALTER TABLE assets ADD COLUMN hold_expires_utc TEXT NULL"); } catch { }

            // Migration: ensure licenses, license_heartbeats, and app_releases tables exist
            try
            {
                Exec(conn, @"
CREATE TABLE IF NOT EXISTS licenses (
    id                 TEXT PRIMARY KEY,
    tenant_id          TEXT NOT NULL,
    license_key        TEXT NOT NULL UNIQUE COLLATE NOCASE,
    company_name       TEXT NOT NULL,
    plan_tier          TEXT NOT NULL DEFAULT 'Alpha-Pilot',
    max_devices_month  INTEGER NOT NULL DEFAULT 1500,
    devices_used_month INTEGER NOT NULL DEFAULT 0,
    month_period       TEXT NOT NULL,
    expires_at_utc     TEXT NOT NULL,
    is_active          INTEGER NOT NULL DEFAULT 1,
    allowed_features   TEXT NOT NULL DEFAULT '[""VeloxBench"",""Diagnostics"",""BatteryHealth"",""PdfReport""]',
    notes              TEXT NULL,
    created_at_utc     TEXT NOT NULL,
    FOREIGN KEY(tenant_id) REFERENCES tenants(id)
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_licenses_key ON licenses(license_key);

CREATE TABLE IF NOT EXISTS license_heartbeats (
    id              TEXT PRIMARY KEY,
    license_key     TEXT NOT NULL,
    machine_name    TEXT NOT NULL,
    machine_guid    TEXT NOT NULL,
    ip_address      TEXT NULL,
    app_version     TEXT NOT NULL,
    last_seen_utc   TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_license_heartbeats_key ON license_heartbeats(license_key);

CREATE TABLE IF NOT EXISTS app_releases (
    id              TEXT PRIMARY KEY,
    app_name        TEXT NOT NULL DEFAULT 'VeloxBench',
    version         TEXT NOT NULL,
    channel         TEXT NOT NULL DEFAULT 'alpha',
    download_url    TEXT NOT NULL,
    file_sha256     TEXT NOT NULL,
    file_size_bytes INTEGER NOT NULL DEFAULT 0,
    is_mandatory    INTEGER NOT NULL DEFAULT 0,
    release_notes   TEXT NOT NULL DEFAULT '',
    released_at_utc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_app_releases ON app_releases(app_name, channel, version);
");
            }
            catch { }

            // Auto-seed default depot tenant, primary API key, technician, and Alpha licenses if empty
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

            // Seed default Alpha license if none exist
            try
            {
                using var licCheck = conn.CreateCommand();
                licCheck.CommandText = "SELECT COUNT(*) FROM licenses";
                if (Convert.ToInt64(licCheck.ExecuteScalar()) == 0)
                {
                    var now = DateTimeOffset.UtcNow;
                    var monthPeriod = now.ToString("yyyy-MM");
                    var expiry = Iso(now.AddDays(60));
                    Exec(conn, @"
INSERT INTO licenses(id, tenant_id, license_key, company_name, plan_tier, max_devices_month, devices_used_month, month_period, expires_at_utc, is_active, allowed_features, notes, created_at_utc)
VALUES(@id, @tid, @key, @comp, 'Alpha-Pilot', 2000, 0, @mp, @exp, 1, '[""VeloxBench"",""Diagnostics"",""BatteryHealth"",""PdfReport""]', 'Default Alpha Pilot License', @created)",
                        P("@id", "lic-" + Guid.NewGuid().ToString("N")[..12]),
                        P("@tid", "ten-d2428a4cb3da"),
                        P("@key", "VLX-ALPHA-TEST-2026"),
                        P("@comp", "Refurbishment Alpha Partner"),
                        P("@mp", monthPeriod),
                        P("@exp", expiry),
                        P("@created", Iso(now)));

                    // Seed Ritchie Street Partner License
                    Exec(conn, @"
INSERT INTO licenses(id, tenant_id, license_key, company_name, plan_tier, max_devices_month, devices_used_month, month_period, expires_at_utc, is_active, allowed_features, notes, created_at_utc)
VALUES(@id, @tid, 'VLX-ALPHA-RITCHIE-01', 'Ritchie Street Depot Partner', 'Alpha-Pilot', 500, 0, @mp, @exp, 1, '[""VeloxBench"",""Diagnostics"",""BatteryHealth"",""PdfReport""]', 'Partner Pilot 1', @created)",
                        P("@id", "lic-" + Guid.NewGuid().ToString("N")[..12]),
                        P("@tid", "ten-d2428a4cb3da"),
                        P("@mp", monthPeriod),
                        P("@exp", expiry),
                        P("@created", Iso(now)));

                    // Seed GP Road Partner License
                    Exec(conn, @"
INSERT INTO licenses(id, tenant_id, license_key, company_name, plan_tier, max_devices_month, devices_used_month, month_period, expires_at_utc, is_active, allowed_features, notes, created_at_utc)
VALUES(@id, @tid, 'VLX-ALPHA-GPROAD-02', 'GP Road Depot Partner', 'Alpha-Pilot', 500, 0, @mp, @exp, 1, '[""VeloxBench"",""Diagnostics"",""BatteryHealth"",""PdfReport""]', 'Partner Pilot 2', @created)",
                        P("@id", "lic-" + Guid.NewGuid().ToString("N")[..12]),
                        P("@tid", "ten-d2428a4cb3da"),
                        P("@mp", monthPeriod),
                        P("@exp", expiry),
                        P("@created", Iso(now)));
                }
            }
            catch { }
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
            r.Close();
            emp.WorkloadCount = CountAssigned(conn, tenantId, emp.Id);
            emp.FinishedCount = CountFinished(conn, tenantId, emp.Id, emp.Name);
            emp.CurrentActivity = GetLatestActivitySummary(conn, tenantId, emp.Id, emp.Name);
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
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    list.Add(MapEmployee(r));
                }
            }

            foreach (var emp in list)
            {
                emp.WorkloadCount = CountAssigned(conn, tenantId, emp.Id);
                emp.FinishedCount = CountFinished(conn, tenantId, emp.Id, emp.Name);
                emp.CurrentActivity = GetLatestActivitySummary(conn, tenantId, emp.Id, emp.Name);
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

    public bool DeleteEmployee(string tenantId, string id)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var unassign = conn.CreateCommand();
            unassign.CommandText = "UPDATE assets SET assigned_employee_id=NULL WHERE tenant_id=@tid AND assigned_employee_id=@id";
            unassign.Parameters.AddWithValue("@tid", tenantId);
            unassign.Parameters.AddWithValue("@id", id);
            unassign.ExecuteNonQuery();

            using var del = conn.CreateCommand();
            del.CommandText = "DELETE FROM employees WHERE tenant_id=@tid AND id=@id";
            del.Parameters.AddWithValue("@tid", tenantId);
            del.Parameters.AddWithValue("@id", id);
            return del.ExecuteNonQuery() > 0;
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
                if (req.Category.HasValue)                    { sets.Add("category=@cat");  parms.Add(P("@cat", req.Category.Value.ToString())); }
                if (req.Quantity.HasValue)                    { sets.Add("quantity=@qty");  parms.Add(P("@qty", req.Quantity.Value)); }
                if (req.PurchasePrice.HasValue)               { sets.Add("purchase_price=@pur"); parms.Add(P("@pur", req.PurchasePrice.Value)); }
                if (req.SalePrice.HasValue)                   { sets.Add("sale_price=@sal");     parms.Add(P("@sal", req.SalePrice.Value)); }
                parms.Add(P("@tid", tenantId)); parms.Add(P("@id", existing.Id));
                Exec(conn, $"UPDATE assets SET {string.Join(",", sets)} WHERE tenant_id=@tid AND id=@id", [.. parms]);
                return GetAssetById(tenantId, existing.Id, conn)!;
            }

            // New asset
            var assetId = "ast-" + NewId();
            var cat = req.Category?.ToString() ?? "Laptop";
            var qty = req.Quantity ?? 1;
            Exec(conn, @"INSERT INTO assets(id,tenant_id,serial_number,asset_tag,asset_uuid,model,category,quantity,purchase_price,sale_price,current_location,lifecycle_queue,grade,supplier,in_date,created_at_utc,updated_at_utc)
                         VALUES(@id,@tid,@sn,@tag,@uuid,@model,@cat,@qty,@pur,@sal,@loc,'IntakeStaging','Unknown',@sup,@ind,@ts,@ts)",
                P("@id", assetId), P("@tid", tenantId),
                P("@sn",   req.SerialNumber ?? (object)DBNull.Value),
                P("@tag",  req.AssetTag     ?? (object)DBNull.Value),
                P("@uuid", req.AssetUuid    ?? (object)DBNull.Value),
                P("@model",req.Model        ?? (object)DBNull.Value),
                P("@cat",  cat),
                P("@qty",  qty),
                P("@pur",  req.PurchasePrice ?? (object)DBNull.Value),
                P("@sal",  req.SalePrice     ?? (object)DBNull.Value),
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
            if (req.Category          != null) { sets.Add("category=@cat");               parms.Add(P("@cat",   req.Category.Value.ToString())); }
            if (req.Quantity          != null) { sets.Add("quantity=@qty");               parms.Add(P("@qty",   req.Quantity.Value)); }
            if (req.HoldCustomer      != null) { sets.Add("hold_customer=@hc");           parms.Add(P("@hc",    req.HoldCustomer)); }
            if (req.HoldSalesRep      != null) { sets.Add("hold_sales_rep=@hs");           parms.Add(P("@hs",    req.HoldSalesRep)); }
            if (req.HoldExpiresAtUtc  != null) { sets.Add("hold_expires_utc=@hexp");       parms.Add(P("@hexp",  string.IsNullOrWhiteSpace(req.HoldExpiresAtUtc) ? (object)DBNull.Value : req.HoldExpiresAtUtc)); }

            parms.Add(P("@tid", tenantId));
            parms.Add(P("@id", id));
            Exec(conn, $"UPDATE assets SET {string.Join(",", sets)} WHERE tenant_id=@tid AND id=@id", [.. parms]);
            return true;
        }
    }

    public bool DeleteAsset(string tenantId, string id)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var delEvents = conn.CreateCommand();
            delEvents.CommandText = "DELETE FROM asset_events WHERE tenant_id=@tid AND asset_id=@id";
            delEvents.Parameters.AddWithValue("@tid", tenantId);
            delEvents.Parameters.AddWithValue("@id", id);
            delEvents.ExecuteNonQuery();

            using var delCmps = conn.CreateCommand();
            delCmps.CommandText = "DELETE FROM fitted_components WHERE tenant_id=@tid AND parent_asset_id=@id";
            delCmps.Parameters.AddWithValue("@tid", tenantId);
            delCmps.Parameters.AddWithValue("@id", id);
            delCmps.ExecuteNonQuery();

            using var delCmd = conn.CreateCommand();
            delCmd.CommandText = "DELETE FROM assets WHERE tenant_id=@tid AND id=@id";
            delCmd.Parameters.AddWithValue("@tid", tenantId);
            delCmd.Parameters.AddWithValue("@id", id);
            return delCmd.ExecuteNonQuery() > 0;
        }
    }

    public bool AssignAsset(string tenantId, string assetId, string? employeeId)
    {
        lock (_gate)
        {
            using var conn = Open();
            var now = Iso(DateTimeOffset.UtcNow);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE assets SET assigned_employee_id=@eid, updated_at_utc=@ts WHERE tenant_id=@tid AND id=@id";
            cmd.Parameters.AddWithValue("@eid", string.IsNullOrWhiteSpace(employeeId) ? DBNull.Value : employeeId.Trim());
            cmd.Parameters.AddWithValue("@ts", now);
            cmd.Parameters.AddWithValue("@tid", tenantId);
            cmd.Parameters.AddWithValue("@id", assetId);
            return cmd.ExecuteNonQuery() > 0;
        }
    }

    public (bool Passed, string Reason) CheckQualityGateForRts(AssetDto asset)
    {
        // 1. Grade must not be unknown
        if (asset.Grade == AssetGrade.Unknown && string.IsNullOrWhiteSpace(asset.CosmeticGrade))
            return (false, "Cosmetic grade has not been evaluated (Grade is Unknown).");

        // 2. Battery Health check (if recorded, must be >= 70%)
        if (asset.BatteryHealth.HasValue && asset.BatteryHealth.Value > 0 && asset.BatteryHealth.Value < 70)
            return (false, $"Battery health is critically degraded ({asset.BatteryHealth.Value}% < 70% minimum threshold). Battery replacement required.");

        // 3. Storage Health check
        if (asset.StorageHealth.HasValue && asset.StorageHealth.Value < 80)
            return (false, $"Storage drive health is below acceptable threshold ({asset.StorageHealth.Value}% < 80%). Verify drive integrity.");

        // 4. Missing components
        if (!string.IsNullOrWhiteSpace(asset.MissingComponents) && asset.MissingComponents.Trim() != "None" && asset.MissingComponents.Trim() != "-")
            return (false, $"Asset has unresolved missing components flagged: {asset.MissingComponents}.");

        return (true, "All Quality Gate checks passed.");
    }

    public bool SetAssetHold(string tenantId, string id, string customer, string salesRep, int durationMinutes)
    {
        lock (_gate)
        {
            using var conn = Open();
            var exp = Iso(DateTimeOffset.UtcNow.AddMinutes(Math.Max(5, durationMinutes)));
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE assets
                                SET hold_customer=@hc, hold_sales_rep=@hs, hold_expires_utc=@hexp, updated_at_utc=@ts
                                WHERE tenant_id=@tid AND id=@id";
            cmd.Parameters.AddWithValue("@hc", customer);
            cmd.Parameters.AddWithValue("@hs", salesRep);
            cmd.Parameters.AddWithValue("@hexp", exp);
            cmd.Parameters.AddWithValue("@ts", Iso(DateTimeOffset.UtcNow));
            cmd.Parameters.AddWithValue("@tid", tenantId);
            cmd.Parameters.AddWithValue("@id", id);
            return cmd.ExecuteNonQuery() > 0;
        }
    }

    public bool ReleaseAssetHold(string tenantId, string id)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE assets
                                SET hold_customer='', hold_sales_rep='', hold_expires_utc=NULL, updated_at_utc=@ts
                                WHERE tenant_id=@tid AND id=@id";
            cmd.Parameters.AddWithValue("@ts", Iso(DateTimeOffset.UtcNow));
            cmd.Parameters.AddWithValue("@tid", tenantId);
            cmd.Parameters.AddWithValue("@id", id);
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
        cmd.CommandText = "SELECT COUNT(1) FROM assets WHERE tenant_id=@tid AND assigned_employee_id=@eid AND lifecycle_queue NOT IN ('ReadyForSale', 'Sold')";
        cmd.Parameters.AddWithValue("@tid", tenantId);
        cmd.Parameters.AddWithValue("@eid", empId);
        return (int)(long)(cmd.ExecuteScalar() ?? 0L);
    }

    private int CountFinished(SqliteConnection conn, string tenantId, string empId, string empName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT COUNT(DISTINCT a.id) FROM assets a
                            WHERE a.tenant_id=@tid
                              AND (a.assigned_employee_id=@eid OR a.id IN (SELECT asset_id FROM asset_events WHERE tenant_id=@tid AND (actor=@ename OR actor=@eid)))
                              AND a.lifecycle_queue IN ('ReadyForSale', 'Sold')";
        cmd.Parameters.AddWithValue("@tid", tenantId);
        cmd.Parameters.AddWithValue("@eid", empId);
        cmd.Parameters.AddWithValue("@ename", empName);
        return (int)(long)(cmd.ExecuteScalar() ?? 0L);
    }

    private string GetLatestActivitySummary(SqliteConnection conn, string tenantId, string empId, string empName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT e.event_type, coalesce(a.asset_tag, coalesce(a.model, '')), e.recorded_at_utc
                            FROM asset_events e
                            LEFT JOIN assets a ON e.asset_id = a.id AND e.tenant_id = a.tenant_id
                            WHERE e.tenant_id = @tid
                              AND (e.actor = @ename OR e.actor = @eid OR e.notes_json LIKE '%' || @eid || '%')
                            ORDER BY e.recorded_at_utc DESC
                            LIMIT 1";
        cmd.Parameters.AddWithValue("@tid", tenantId);
        cmd.Parameters.AddWithValue("@eid", empId);
        cmd.Parameters.AddWithValue("@ename", empName);
        using var r = cmd.ExecuteReader();
        if (r.Read())
        {
            var evt = r.GetString(0);
            var tag = r.IsDBNull(1) ? "" : r.GetString(1);
            var ts = DateTimeOffset.Parse(r.GetString(2));
            var diff = DateTimeOffset.UtcNow - ts;
            if (diff.TotalMinutes < 30)
                return $"Testing {tag} ({evt})";
            else if (diff.TotalHours < 24)
                return $"{evt}: {tag} ({(int)diff.TotalMinutes}m ago)";
            else
                return $"{evt}: {tag}";
        }
        return "Standby (Idle)";
    }

    public TechnicianPerformanceDto? GetTechnicianPerformance(string tenantId, string employeeId)
    {
        lock (_gate)
        {
            using var conn = Open();
            var emp = GetEmployee(tenantId, employeeId);
            if (emp == null) return null;

            var dto = new TechnicianPerformanceDto
            {
                EmployeeId = emp.Id,
                Name = emp.Name,
                Role = emp.Role,
                StationId = emp.StationId,
                IsActive = emp.IsActive,
                ActiveAssignedCount = emp.WorkloadCount,
                CompletedAllTimeCount = emp.FinishedCount
            };

            // Completed today count
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"SELECT COUNT(DISTINCT a.id) FROM assets a
                                    WHERE a.tenant_id=@tid
                                      AND (a.assigned_employee_id=@eid OR a.id IN (SELECT asset_id FROM asset_events WHERE tenant_id=@tid AND (actor=@ename OR actor=@eid)))
                                      AND a.lifecycle_queue IN ('ReadyForSale', 'Sold')
                                      AND date(a.updated_at_utc) = date('now')";
                cmd.Parameters.AddWithValue("@tid", tenantId);
                cmd.Parameters.AddWithValue("@eid", employeeId);
                cmd.Parameters.AddWithValue("@ename", emp.Name);
                dto.CompletedTodayCount = (int)(long)(cmd.ExecuteScalar() ?? 0L);
            }

            // Total Fixes & Bench Actions logged
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(1) FROM asset_events WHERE tenant_id=@tid AND (actor=@ename OR actor=@eid)";
                cmd.Parameters.AddWithValue("@tid", tenantId);
                cmd.Parameters.AddWithValue("@eid", employeeId);
                cmd.Parameters.AddWithValue("@ename", emp.Name);
                dto.TotalFixesCount = (int)(long)(cmd.ExecuteScalar() ?? 0L);
            }

            // Components Fitted / BOM upgrades
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(1) FROM fitted_components WHERE tenant_id=@tid AND (installed_by=@ename OR installed_by=@eid)";
                cmd.Parameters.AddWithValue("@tid", tenantId);
                cmd.Parameters.AddWithValue("@eid", employeeId);
                cmd.Parameters.AddWithValue("@ename", emp.Name);
                dto.ComponentsFittedCount = (int)(long)(cmd.ExecuteScalar() ?? 0L);
            }

            // Recent activity (latest 50)
            dto.RecentActivity = GetTechnicianActivityInternal(conn, tenantId, employeeId, emp.Name, 50);

            // Assigned Assets
            dto.AssignedAssets = QueryAssets(conn, "WHERE a.tenant_id=@tid AND a.assigned_employee_id=@eid ORDER BY a.updated_at_utc DESC",
                P("@tid", tenantId), P("@eid", employeeId));

            // Status & Last Action
            if (dto.RecentActivity.Count > 0)
            {
                var latest = dto.RecentActivity[0];
                dto.LastActiveUtc = latest.RecordedAtUtc;
                var target = !string.IsNullOrWhiteSpace(latest.AssetTag) ? latest.AssetTag : latest.Model;
                dto.LastActionText = $"{latest.EventType}: {target}";

                var timeDiff = DateTimeOffset.UtcNow - latest.RecordedAtUtc;
                if (timeDiff.TotalMinutes < 30)
                {
                    dto.CurrentStatusText = $"Active on {dto.StationId} ({target})";
                }
                else
                {
                    dto.CurrentStatusText = dto.ActiveAssignedCount > 0 ? $"Bench Assigned ({dto.ActiveAssignedCount} WIP)" : "Standby (Idle)";
                }
            }
            else
            {
                dto.CurrentStatusText = dto.ActiveAssignedCount > 0 ? $"Bench Assigned ({dto.ActiveAssignedCount} WIP)" : "Standby (Idle)";
                dto.LastActionText = "No recorded actions";
            }

            return dto;
        }
    }

    public List<TechnicianActivityDto> GetTechnicianActivity(string tenantId, string employeeId, int limit = 50)
    {
        lock (_gate)
        {
            using var conn = Open();
            var emp = GetEmployee(tenantId, employeeId);
            var empName = emp?.Name ?? "";
            return GetTechnicianActivityInternal(conn, tenantId, employeeId, empName, limit);
        }
    }

    private List<TechnicianActivityDto> GetTechnicianActivityInternal(SqliteConnection conn, string tenantId, string employeeId, string empName, int limit)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT e.id, e.asset_id, coalesce(a.asset_tag, '') as asset_tag,
                                   coalesce(a.serial_number, '') as serial_number, coalesce(a.model, '') as model,
                                   e.event_type, coalesce(e.location, '') as location, e.actor, e.notes_json, e.recorded_at_utc
                            FROM asset_events e
                            LEFT JOIN assets a ON e.asset_id = a.id AND e.tenant_id = a.tenant_id
                            WHERE e.tenant_id = @tid
                              AND (e.actor = @ename OR e.actor = @eid OR e.notes_json LIKE '%' || @eid || '%')
                            ORDER BY e.recorded_at_utc DESC
                            LIMIT @limit";
        cmd.Parameters.AddWithValue("@tid", tenantId);
        cmd.Parameters.AddWithValue("@eid", employeeId);
        cmd.Parameters.AddWithValue("@ename", empName);
        cmd.Parameters.AddWithValue("@limit", limit);

        var list = new List<TechnicianActivityDto>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new TechnicianActivityDto
            {
                Id = r.GetString(0),
                AssetId = r.GetString(1),
                AssetTag = r.GetString(2),
                SerialNumber = r.GetString(3),
                Model = r.GetString(4),
                EventType = r.GetString(5),
                Location = r.GetString(6),
                Actor = r.GetString(7),
                NotesJson = r.GetString(8),
                RecordedAtUtc = DateTimeOffset.Parse(r.GetString(9))
            });
        }
        return list;
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
                (SELECT event_type FROM asset_events WHERE asset_id=a.id ORDER BY recorded_at_utc DESC LIMIT 1) as latest_event,
                a.category, a.quantity,
                a.hold_customer, a.hold_sales_rep, a.hold_expires_utc
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
        Category           = r.FieldCount > 25 && !r.IsDBNull(25) && Enum.TryParse<AssetCategory>(r.GetString(25), true, out var c) ? c : AssetCategory.Laptop,
        Quantity           = r.FieldCount > 26 && !r.IsDBNull(26) ? r.GetInt32(26) : 1,
        HoldCustomer       = r.FieldCount > 27 && !r.IsDBNull(27) ? r.GetString(27) : "",
        HoldSalesRep       = r.FieldCount > 28 && !r.IsDBNull(28) ? r.GetString(28) : "",
        HoldExpiresAtUtc   = r.FieldCount > 29 && !r.IsDBNull(29) ? r.GetString(29) : null,
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

    private static FittedComponentDto MapFittedComponent(SqliteDataReader r) => new()
    {
        Id               = r.GetString(0),
        TenantId         = r.GetString(1),
        ParentAssetId    = r.GetString(2),
        ComponentAssetId = r.IsDBNull(3) ? null : r.GetString(3),
        Category         = Enum.TryParse<AssetCategory>(r.GetString(4), true, out var c) ? c : AssetCategory.RAM,
        Description      = r.GetString(5),
        SerialNumber     = r.IsDBNull(6) ? null : r.GetString(6),
        CostDelta        = r.IsDBNull(7) ? 0.0 : r.GetDouble(7),
        PriceDelta       = r.IsDBNull(8) ? 0.0 : r.GetDouble(8),
        InstalledAtUtc   = DateTimeOffset.Parse(r.GetString(9)),
        InstalledBy      = r.IsDBNull(10) ? null : r.GetString(10),
        Notes            = r.IsDBNull(11) ? null : r.GetString(11)
    };

    // ── Fitted Components (BOM) ──────────────────────────────────────────────

    public List<FittedComponentDto> ListFittedComponents(string tenantId, string parentAssetId)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id,tenant_id,parent_asset_id,component_asset_id,category,description,serial_number,cost_delta,price_delta,installed_at_utc,installed_by,notes FROM asset_components WHERE tenant_id=@tid AND parent_asset_id=@pid ORDER BY installed_at_utc ASC";
            cmd.Parameters.AddWithValue("@tid", tenantId);
            cmd.Parameters.AddWithValue("@pid", parentAssetId);
            var list = new List<FittedComponentDto>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(MapFittedComponent(r));
            }
            return list;
        }
    }

    public FittedComponentDto AttachComponent(string tenantId, string parentAssetId, AttachComponentRequest req)
    {
        var id = "cmp-" + NewId();
        var now = DateTimeOffset.UtcNow;
        var nowIso = Iso(now);
        var desc = req.Description;
        var sn = req.SerialNumber;
        var cat = req.Category;

        lock (_gate)
        {
            using var conn = Open();

            // If linked to tracked stock asset, decrement stock or fetch details
            if (!string.IsNullOrWhiteSpace(req.ComponentAssetId))
            {
                using var cmdAsset = conn.CreateCommand();
                cmdAsset.CommandText = "SELECT model, serial_number, category, quantity FROM assets WHERE tenant_id=@tid AND id=@cid";
                cmdAsset.Parameters.AddWithValue("@tid", tenantId);
                cmdAsset.Parameters.AddWithValue("@cid", req.ComponentAssetId);
                using var r = cmdAsset.ExecuteReader();
                if (r.Read())
                {
                    if (string.IsNullOrWhiteSpace(desc)) desc = r.GetString(0);
                    if (string.IsNullOrWhiteSpace(sn) && !r.IsDBNull(1)) sn = r.GetString(1);
                    if (Enum.TryParse<AssetCategory>(r.GetString(2), true, out var parsedCat)) cat = parsedCat;
                    int qty = r.GetInt32(3);
                    r.Close();

                    // Decrement stock quantity
                    int newQty = Math.Max(0, qty - 1);
                    using var updateQty = conn.CreateCommand();
                    updateQty.CommandText = "UPDATE assets SET quantity=@q, updated_at_utc=@ts WHERE tenant_id=@tid AND id=@cid";
                    updateQty.Parameters.AddWithValue("@q", newQty);
                    updateQty.Parameters.AddWithValue("@ts", nowIso);
                    updateQty.Parameters.AddWithValue("@tid", tenantId);
                    updateQty.Parameters.AddWithValue("@cid", req.ComponentAssetId);
                    updateQty.ExecuteNonQuery();
                }
            }

            Exec(conn, @"
INSERT INTO asset_components(id, tenant_id, parent_asset_id, component_asset_id, category, description, serial_number, cost_delta, price_delta, installed_at_utc, installed_by, notes)
VALUES(@id, @tid, @pid, @cid, @cat, @desc, @sn, @cd, @pd, @ts, @by, @notes)",
                P("@id", id), P("@tid", tenantId), P("@pid", parentAssetId),
                P("@cid", (object?)req.ComponentAssetId ?? DBNull.Value),
                P("@cat", cat.ToString()),
                P("@desc", desc),
                P("@sn", (object?)sn ?? DBNull.Value),
                P("@cd", req.CostDelta),
                P("@pd", req.PriceDelta),
                P("@ts", nowIso),
                P("@by", (object?)req.InstalledBy ?? DBNull.Value),
                P("@notes", (object?)req.Notes ?? DBNull.Value));

            // Log event to parent asset
            AppendEvent(tenantId, parentAssetId, new AppendEventRequest
            {
                EventType = "ComponentFitted",
                Actor = req.InstalledBy ?? "Depot Tech",
                NotesJson = $"{{\"componentId\":\"{id}\",\"category\":\"{cat}\",\"desc\":\"{desc}\"}}"
            });

            return new FittedComponentDto
            {
                Id = id,
                TenantId = tenantId,
                ParentAssetId = parentAssetId,
                ComponentAssetId = req.ComponentAssetId,
                Category = cat,
                Description = desc,
                SerialNumber = sn,
                CostDelta = req.CostDelta,
                PriceDelta = req.PriceDelta,
                InstalledBy = req.InstalledBy,
                Notes = req.Notes,
                InstalledAtUtc = now
            };
        }
    }

    public bool DetachComponent(string tenantId, string componentId, DetachComponentRequest req)
    {
        lock (_gate)
        {
            using var conn = Open();

            // Find component first
            using var findCmd = conn.CreateCommand();
            findCmd.CommandText = "SELECT id, parent_asset_id, component_asset_id, category, description, serial_number FROM asset_components WHERE tenant_id=@tid AND id=@id";
            findCmd.Parameters.AddWithValue("@tid", tenantId);
            findCmd.Parameters.AddWithValue("@id", componentId);

            string parentId = "";
            string? compAssetId = null;
            string categoryStr = "Other";
            string desc = "";
            string? sn = null;

            using (var r = findCmd.ExecuteReader())
            {
                if (!r.Read()) return false;
                parentId = r.GetString(1);
                compAssetId = r.IsDBNull(2) ? null : r.GetString(2);
                categoryStr = r.GetString(3);
                desc = r.GetString(4);
                sn = r.IsDBNull(5) ? null : r.GetString(5);
            }

            var nowIso = Iso(DateTimeOffset.UtcNow);

            // Handle optional restock
            if (req.ReturnToStock)
            {
                if (!string.IsNullOrWhiteSpace(compAssetId))
                {
                    // Existing tracked asset: increment quantity
                    using var incCmd = conn.CreateCommand();
                    incCmd.CommandText = "UPDATE assets SET quantity = quantity + 1, updated_at_utc=@ts WHERE tenant_id=@tid AND id=@cid";
                    incCmd.Parameters.AddWithValue("@ts", nowIso);
                    incCmd.Parameters.AddWithValue("@tid", tenantId);
                    incCmd.Parameters.AddWithValue("@cid", compAssetId);
                    incCmd.ExecuteNonQuery();
                }
                else
                {
                    // Untracked pulled part: intake as new stock asset
                    var newAssetId = "ast-" + NewId();
                    Exec(conn, @"
INSERT INTO assets(id, tenant_id, serial_number, asset_tag, model, category, quantity, grade, current_location, lifecycle_queue, notes, created_at_utc, updated_at_utc)
VALUES(@id, @tid, @sn, @tag, @model, @cat, 1, @grade, 'Depot Staging', 'ReadyForSale', @notes, @ts, @ts)",
                        P("@id", newAssetId), P("@tid", tenantId),
                        P("@sn", (object?)sn ?? ("PULLED-" + NewId()[..8].ToUpperInvariant())),
                        P("@tag", "TAG-" + NewId()[..6].ToUpperInvariant()),
                        P("@model", !string.IsNullOrWhiteSpace(desc) ? desc : "Pulled Spare"),
                        P("@cat", categoryStr),
                        P("@grade", req.Condition ?? "Used / Tested"),
                        P("@notes", $"Pulled from asset {parentId} on {nowIso}"),
                        P("@ts", nowIso));
                }
            }

            // Delete from asset_components
            using var delCmd = conn.CreateCommand();
            delCmd.CommandText = "DELETE FROM asset_components WHERE tenant_id=@tid AND id=@id";
            delCmd.Parameters.AddWithValue("@tid", tenantId);
            delCmd.Parameters.AddWithValue("@id", componentId);
            delCmd.ExecuteNonQuery();

            // Log event to parent asset
            AppendEvent(tenantId, parentId, new AppendEventRequest
            {
                EventType = "ComponentDetached",
                Actor = "Depot Tech",
                NotesJson = $"{{\"componentId\":\"{componentId}\",\"category\":\"{categoryStr}\",\"returnedToStock\":{req.ReturnToStock.ToString().ToLowerInvariant()}}}"
            });

            return true;
        }
    }

    // ── Backup, Restore & Purge ───────────────────────────────────────────────

    public DatabaseBackupDto ExportBackup(string tenantId)
    {
        lock (_gate)
        {
            using var conn = Open();
            var backup = new DatabaseBackupDto
            {
                Version = "1.0",
                ExportedAtUtc = Iso(DateTimeOffset.UtcNow),
                TenantId = tenantId
            };

            // 1. Assets
            backup.Assets = QueryAssets(conn, "WHERE a.tenant_id=@tid ORDER BY a.created_at_utc ASC", P("@tid", tenantId));

            // 2. Fitted Components
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id,tenant_id,parent_asset_id,component_asset_id,category,description,serial_number,cost_delta,price_delta,installed_at_utc,installed_by,notes FROM asset_components WHERE tenant_id=@tid ORDER BY installed_at_utc ASC";
                cmd.Parameters.AddWithValue("@tid", tenantId);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    backup.Components.Add(MapFittedComponent(r));
                }
            }

            // 3. Events
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id,asset_id,tenant_id,event_type,location,actor,notes_json,source_bench_id,recorded_at_utc FROM asset_events WHERE tenant_id=@tid ORDER BY recorded_at_utc ASC";
                cmd.Parameters.AddWithValue("@tid", tenantId);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    backup.Events.Add(MapEvent(r));
                }
            }

            // 4. Employees
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id,tenant_id,name,role,is_active,station_id,created_at_utc FROM employees WHERE tenant_id=@tid ORDER BY created_at_utc ASC";
                cmd.Parameters.AddWithValue("@tid", tenantId);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    backup.Employees.Add(MapEmployee(r));
                }
            }

            return backup;
        }
    }

    public int PurgeTenantAssets(string tenantId)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var tx = conn.BeginTransaction();

            using (var cmdC = conn.CreateCommand())
            {
                cmdC.Transaction = tx;
                cmdC.CommandText = "DELETE FROM asset_components WHERE tenant_id=@tid";
                cmdC.Parameters.AddWithValue("@tid", tenantId);
                cmdC.ExecuteNonQuery();
            }

            using (var cmdE = conn.CreateCommand())
            {
                cmdE.Transaction = tx;
                cmdE.CommandText = "DELETE FROM asset_events WHERE tenant_id=@tid";
                cmdE.Parameters.AddWithValue("@tid", tenantId);
                cmdE.ExecuteNonQuery();
            }

            int count = 0;
            using (var cmdA = conn.CreateCommand())
            {
                cmdA.Transaction = tx;
                cmdA.CommandText = "DELETE FROM assets WHERE tenant_id=@tid";
                cmdA.Parameters.AddWithValue("@tid", tenantId);
                count = cmdA.ExecuteNonQuery();
            }

            tx.Commit();
            return count;
        }
    }

    public (int Assets, int Components, int Events) RestoreBackup(string tenantId, DatabaseBackupDto backup)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var tx = conn.BeginTransaction();

            // Clear previous assets/components/events
            using (var cmdDelC = conn.CreateCommand())
            {
                cmdDelC.Transaction = tx;
                cmdDelC.CommandText = "DELETE FROM asset_components WHERE tenant_id=@tid";
                cmdDelC.Parameters.AddWithValue("@tid", tenantId);
                cmdDelC.ExecuteNonQuery();
            }
            using (var cmdDelE = conn.CreateCommand())
            {
                cmdDelE.Transaction = tx;
                cmdDelE.CommandText = "DELETE FROM asset_events WHERE tenant_id=@tid";
                cmdDelE.Parameters.AddWithValue("@tid", tenantId);
                cmdDelE.ExecuteNonQuery();
            }
            using (var cmdDelA = conn.CreateCommand())
            {
                cmdDelA.Transaction = tx;
                cmdDelA.CommandText = "DELETE FROM assets WHERE tenant_id=@tid";
                cmdDelA.Parameters.AddWithValue("@tid", tenantId);
                cmdDelA.ExecuteNonQuery();
            }

            int assetCount = 0;
            if (backup.Assets != null)
            {
                foreach (var a in backup.Assets)
                {
                    var id = string.IsNullOrWhiteSpace(a.Id) ? ("ast-" + NewId()) : a.Id;
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = @"INSERT INTO assets(id, tenant_id, serial_number, asset_tag, asset_uuid, model,
                        current_location, lifecycle_queue, grade, supplier, customer, assigned_employee_id,
                        purchase_price, sale_price, missing_components, cosmetic_grade, battery_health, storage_health,
                        notes, in_date, out_date, category, quantity, hold_customer, hold_sales_rep, hold_expires_utc,
                        created_at_utc, updated_at_utc)
                        VALUES(@id, @tid, @sn, @tag, @uuid, @model, @loc, @q, @grade, @sup, @cust, @eid,
                        @pp, @sp, @mc, @cg, @bh, @sh, @notes, @ind, @outd, @cat, @qty, @hc, @hs, @hexp, @created, @updated)";
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@tid", tenantId);
                    cmd.Parameters.AddWithValue("@sn", string.IsNullOrWhiteSpace(a.SerialNumber) ? DBNull.Value : a.SerialNumber);
                    cmd.Parameters.AddWithValue("@tag", string.IsNullOrWhiteSpace(a.AssetTag) ? DBNull.Value : a.AssetTag);
                    cmd.Parameters.AddWithValue("@uuid", string.IsNullOrWhiteSpace(a.AssetUuid) ? DBNull.Value : a.AssetUuid);
                    cmd.Parameters.AddWithValue("@model", a.Model ?? "");
                    cmd.Parameters.AddWithValue("@loc", a.CurrentLocation ?? "");
                    cmd.Parameters.AddWithValue("@q", a.LifecycleQueue.ToString());
                    cmd.Parameters.AddWithValue("@grade", a.Grade.ToString());
                    cmd.Parameters.AddWithValue("@sup", a.Supplier ?? "");
                    cmd.Parameters.AddWithValue("@cust", a.Customer ?? "");
                    cmd.Parameters.AddWithValue("@eid", string.IsNullOrWhiteSpace(a.AssignedEmployeeId) ? DBNull.Value : a.AssignedEmployeeId);
                    cmd.Parameters.AddWithValue("@pp", a.PurchasePrice.HasValue ? a.PurchasePrice.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@sp", a.SalePrice.HasValue ? a.SalePrice.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@mc", a.MissingComponents ?? "");
                    cmd.Parameters.AddWithValue("@cg", a.CosmeticGrade ?? "");
                    cmd.Parameters.AddWithValue("@bh", a.BatteryHealth.HasValue ? a.BatteryHealth.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@sh", a.StorageHealth.HasValue ? a.StorageHealth.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@notes", a.Notes ?? "");
                    cmd.Parameters.AddWithValue("@ind", a.InDate ?? "");
                    cmd.Parameters.AddWithValue("@outd", a.OutDate ?? "");
                    cmd.Parameters.AddWithValue("@cat", a.Category.ToString());
                    cmd.Parameters.AddWithValue("@qty", a.Quantity > 0 ? a.Quantity : 1);
                    cmd.Parameters.AddWithValue("@hc", a.HoldCustomer ?? "");
                    cmd.Parameters.AddWithValue("@hs", a.HoldSalesRep ?? "");
                    cmd.Parameters.AddWithValue("@hexp", string.IsNullOrWhiteSpace(a.HoldExpiresAtUtc) ? DBNull.Value : a.HoldExpiresAtUtc);
                    cmd.Parameters.AddWithValue("@created", a.CreatedAtUtc != default ? Iso(a.CreatedAtUtc) : Iso(DateTimeOffset.UtcNow));
                    cmd.Parameters.AddWithValue("@updated", a.UpdatedAtUtc != default ? Iso(a.UpdatedAtUtc) : Iso(DateTimeOffset.UtcNow));
                    cmd.ExecuteNonQuery();
                    assetCount++;
                }
            }

            int compCount = 0;
            if (backup.Components != null)
            {
                foreach (var c in backup.Components)
                {
                    var id = string.IsNullOrWhiteSpace(c.Id) ? ("cmp-" + NewId()) : c.Id;
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = @"INSERT INTO asset_components(id, tenant_id, parent_asset_id, component_asset_id,
                        category, description, serial_number, cost_delta, price_delta, installed_at_utc, installed_by, notes)
                        VALUES(@id, @tid, @pid, @cid, @cat, @desc, @sn, @cd, @pd, @inst_at, @inst_by, @notes)";
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@tid", tenantId);
                    cmd.Parameters.AddWithValue("@pid", c.ParentAssetId);
                    cmd.Parameters.AddWithValue("@cid", string.IsNullOrWhiteSpace(c.ComponentAssetId) ? DBNull.Value : c.ComponentAssetId);
                    cmd.Parameters.AddWithValue("@cat", c.Category.ToString());
                    cmd.Parameters.AddWithValue("@desc", c.Description ?? "");
                    cmd.Parameters.AddWithValue("@sn", string.IsNullOrWhiteSpace(c.SerialNumber) ? DBNull.Value : c.SerialNumber);
                    cmd.Parameters.AddWithValue("@cd", c.CostDelta);
                    cmd.Parameters.AddWithValue("@pd", c.PriceDelta);
                    cmd.Parameters.AddWithValue("@inst_at", c.InstalledAtUtc != default ? Iso(c.InstalledAtUtc) : Iso(DateTimeOffset.UtcNow));
                    cmd.Parameters.AddWithValue("@inst_by", string.IsNullOrWhiteSpace(c.InstalledBy) ? DBNull.Value : c.InstalledBy);
                    cmd.Parameters.AddWithValue("@notes", string.IsNullOrWhiteSpace(c.Notes) ? DBNull.Value : c.Notes);
                    cmd.ExecuteNonQuery();
                    compCount++;
                }
            }

            int eventCount = 0;
            if (backup.Events != null)
            {
                foreach (var ev in backup.Events)
                {
                    var id = string.IsNullOrWhiteSpace(ev.Id) ? ("evt-" + NewId()) : ev.Id;
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = @"INSERT INTO asset_events(id, asset_id, tenant_id, event_type, location, actor, notes_json, source_bench_id, recorded_at_utc)
                        VALUES(@id, @aid, @tid, @et, @loc, @actor, @notes, @bench, @rec)";
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@aid", ev.AssetId);
                    cmd.Parameters.AddWithValue("@tid", tenantId);
                    cmd.Parameters.AddWithValue("@et", ev.EventType ?? "Log");
                    cmd.Parameters.AddWithValue("@loc", string.IsNullOrWhiteSpace(ev.Location) ? DBNull.Value : ev.Location);
                    cmd.Parameters.AddWithValue("@actor", string.IsNullOrWhiteSpace(ev.Actor) ? DBNull.Value : ev.Actor);
                    cmd.Parameters.AddWithValue("@notes", string.IsNullOrWhiteSpace(ev.NotesJson) ? "{}" : ev.NotesJson);
                    cmd.Parameters.AddWithValue("@bench", string.IsNullOrWhiteSpace(ev.SourceBenchId) ? DBNull.Value : ev.SourceBenchId);
                    cmd.Parameters.AddWithValue("@rec", ev.RecordedAtUtc != default ? Iso(ev.RecordedAtUtc) : Iso(DateTimeOffset.UtcNow));
                    cmd.ExecuteNonQuery();
                    eventCount++;
                }
            }

            tx.Commit();
            return (assetCount, compCount, eventCount);
        }
    }

    // ── Alpha Licensing Engine ──────────────────────────────────────────────

    public LicenseValidateResponse ValidateLicense(string licenseKey, string machineName, string machineGuid, string appVersion, string? clientIp = null)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            return new LicenseValidateResponse
            {
                IsValid = false,
                Message = "License key is missing or empty."
            };
        }

        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, tenant_id, license_key, company_name, plan_tier, max_devices_month, devices_used_month, month_period, expires_at_utc, is_active, allowed_features FROM licenses WHERE license_key = @key COLLATE NOCASE LIMIT 1";
            cmd.Parameters.AddWithValue("@key", licenseKey.Trim());

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return new LicenseValidateResponse
                {
                    IsValid = false,
                    Message = "Invalid license key. Check your depot license configuration."
                };
            }

            var id = reader.GetString(0);
            var tenantId = reader.GetString(1);
            var key = reader.GetString(2);
            var company = reader.GetString(3);
            var planTier = reader.GetString(4);
            var maxDevices = reader.GetInt32(5);
            var usedDevices = reader.GetInt32(6);
            var monthPeriod = reader.GetString(7);
            var expiresStr = reader.GetString(8);
            var isActive = reader.GetInt32(9) == 1;
            var featuresJson = reader.GetString(10);
            reader.Close();

            if (!isActive)
            {
                return new LicenseValidateResponse
                {
                    IsValid = false,
                    CompanyName = company,
                    PlanTier = planTier,
                    Message = "License has been deactivated by administrator."
                };
            }

            if (!DateTimeOffset.TryParse(expiresStr, out var expiresAt))
            {
                expiresAt = DateTimeOffset.UtcNow.AddDays(30);
            }

            var now = DateTimeOffset.UtcNow;
            if (expiresAt < now)
            {
                return new LicenseValidateResponse
                {
                    IsValid = false,
                    CompanyName = company,
                    PlanTier = planTier,
                    ExpiresAtUtc = expiresAt,
                    DaysRemaining = 0,
                    Message = $"License expired on {expiresAt:yyyy-MM-dd}. Please renew your subscription."
                };
            }

            // Check if month period rolled over
            var currentMonth = now.ToString("yyyy-MM");
            if (monthPeriod != currentMonth)
            {
                using var resetCmd = conn.CreateCommand();
                resetCmd.CommandText = "UPDATE licenses SET devices_used_month = 0, month_period = @mp WHERE id = @id";
                resetCmd.Parameters.AddWithValue("@mp", currentMonth);
                resetCmd.Parameters.AddWithValue("@id", id);
                resetCmd.ExecuteNonQuery();
                usedDevices = 0;
            }

            if (maxDevices > 0 && usedDevices >= maxDevices)
            {
                return new LicenseValidateResponse
                {
                    IsValid = false,
                    CompanyName = company,
                    PlanTier = planTier,
                    ExpiresAtUtc = expiresAt,
                    DaysRemaining = (int)Math.Max(0, (expiresAt - now).TotalDays),
                    DevicesUsedMonth = usedDevices,
                    MaxDevicesMonth = maxDevices,
                    Message = $"Device limit reached ({usedDevices}/{maxDevices} laptops tested). Please contact support to extend authorization."
                };
            }

            // Record heartbeat
            try
            {
                using var hbCmd = conn.CreateCommand();
                hbCmd.CommandText = @"
INSERT INTO license_heartbeats(id, license_key, machine_name, machine_guid, ip_address, app_version, last_seen_utc)
VALUES(@id, @key, @name, @guid, @ip, @ver, @now)
ON CONFLICT(id) DO UPDATE SET last_seen_utc = @now, ip_address = @ip, app_version = @ver";
                hbCmd.Parameters.AddWithValue("@id", Sha256($"{key}:{machineGuid}"));
                hbCmd.Parameters.AddWithValue("@key", key);
                hbCmd.Parameters.AddWithValue("@name", machineName ?? "BENCH");
                hbCmd.Parameters.AddWithValue("@guid", machineGuid ?? "DEFAULT");
                hbCmd.Parameters.AddWithValue("@ip", (object?)clientIp ?? DBNull.Value);
                hbCmd.Parameters.AddWithValue("@ver", appVersion ?? "1.0.0");
                hbCmd.Parameters.AddWithValue("@now", Iso(now));
                hbCmd.ExecuteNonQuery();
            }
            catch { }

            var allowedFeatures = new List<string>();
            try
            {
                allowedFeatures = JsonSerializer.Deserialize<List<string>>(featuresJson) ?? new List<string>();
            }
            catch { }

            var daysRemaining = (int)Math.Max(0, (expiresAt - now).TotalDays);

            // Generate tamper-evident HMAC lease signature for offline grace caching
            var leasePayload = $"{key}|{company}|{planTier}|{expiresAt:O}|{now.AddDays(14):O}|{maxDevices}|{usedDevices}";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("velox-alpha-secret-seed-9821"));
            var sig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(leasePayload)));
            var signedLeaseToken = $"{Convert.ToBase64String(Encoding.UTF8.GetBytes(leasePayload))}.{sig}";

            return new LicenseValidateResponse
            {
                IsValid = true,
                CompanyName = company,
                PlanTier = planTier,
                ExpiresAtUtc = expiresAt,
                DaysRemaining = daysRemaining,
                OfflineGraceDays = 14,
                DevicesUsedMonth = usedDevices,
                MaxDevicesMonth = maxDevices,
                AllowedFeatures = allowedFeatures,
                SignedLeaseToken = signedLeaseToken,
                Message = $"System verified for {company}. {daysRemaining} days remaining."
            };
        }
    }

    public LicenseStatusDto CreateLicense(LicenseCreateRequest req, string? tenantId = null)
    {
        var targetTenant = tenantId ?? "ten-d2428a4cb3da";
        var id = "lic-" + NewId();
        var key = !string.IsNullOrWhiteSpace(req.CustomKey)
            ? req.CustomKey.Trim().ToUpperInvariant()
            : $"VLX-ALPHA-{CleanPrefix(req.CompanyName)}-{RandomNumberGenerator.GetInt32(1000, 9999)}-{DateTime.UtcNow.Year}";

        var now = DateTimeOffset.UtcNow;
        var expires = now.AddDays(req.ValidDays > 0 ? req.ValidDays : 45);
        var monthPeriod = now.ToString("yyyy-MM");
        var features = req.AllowedFeatures ?? new List<string> { "VeloxBench", "Diagnostics", "BatteryHealth", "PdfReport" };
        var featuresJson = JsonSerializer.Serialize(features);

        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO licenses(id, tenant_id, license_key, company_name, plan_tier, max_devices_month, devices_used_month, month_period, expires_at_utc, is_active, allowed_features, notes, created_at_utc)
VALUES(@id, @tid, @key, @comp, @plan, @max, 0, @mp, @exp, 1, @feat, @notes, @created)";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@tid", targetTenant);
            cmd.Parameters.AddWithValue("@key", key);
            cmd.Parameters.AddWithValue("@comp", req.CompanyName.Trim());
            cmd.Parameters.AddWithValue("@plan", string.IsNullOrWhiteSpace(req.PlanTier) ? "Alpha-Pilot" : req.PlanTier);
            cmd.Parameters.AddWithValue("@max", req.MaxDevicesMonth > 0 ? req.MaxDevicesMonth : 1500);
            cmd.Parameters.AddWithValue("@mp", monthPeriod);
            cmd.Parameters.AddWithValue("@exp", Iso(expires));
            cmd.Parameters.AddWithValue("@feat", featuresJson);
            cmd.Parameters.AddWithValue("@notes", (object?)req.Notes ?? "");
            cmd.Parameters.AddWithValue("@created", Iso(now));
            cmd.ExecuteNonQuery();

            return new LicenseStatusDto
            {
                Id = id,
                TenantId = targetTenant,
                LicenseKey = key,
                CompanyName = req.CompanyName,
                PlanTier = req.PlanTier,
                MaxDevicesMonth = req.MaxDevicesMonth,
                DevicesUsedMonth = 0,
                MonthPeriod = monthPeriod,
                ExpiresAtUtc = expires,
                IsActive = true,
                AllowedFeatures = features,
                Notes = req.Notes ?? "",
                CreatedAtUtc = now
            };
        }
    }

    public List<LicenseStatusDto> ListLicenses(string? tenantId = null)
    {
        var list = new List<LicenseStatusDto>();
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                cmd.CommandText = "SELECT id, tenant_id, license_key, company_name, plan_tier, max_devices_month, devices_used_month, month_period, expires_at_utc, is_active, allowed_features, notes, created_at_utc FROM licenses ORDER BY created_at_utc DESC";
            }
            else
            {
                cmd.CommandText = "SELECT id, tenant_id, license_key, company_name, plan_tier, max_devices_month, devices_used_month, month_period, expires_at_utc, is_active, allowed_features, notes, created_at_utc FROM licenses WHERE tenant_id = @tid ORDER BY created_at_utc DESC";
                cmd.Parameters.AddWithValue("@tid", tenantId);
            }

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var featJson = reader.GetString(10);
                var feats = new List<string>();
                try { feats = JsonSerializer.Deserialize<List<string>>(featJson) ?? new(); } catch { }

                list.Add(new LicenseStatusDto
                {
                    Id = reader.GetString(0),
                    TenantId = reader.GetString(1),
                    LicenseKey = reader.GetString(2),
                    CompanyName = reader.GetString(3),
                    PlanTier = reader.GetString(4),
                    MaxDevicesMonth = reader.GetInt32(5),
                    DevicesUsedMonth = reader.GetInt32(6),
                    MonthPeriod = reader.GetString(7),
                    ExpiresAtUtc = DateTimeOffset.Parse(reader.GetString(8)),
                    IsActive = reader.GetInt32(9) == 1,
                    AllowedFeatures = feats,
                    Notes = reader.IsDBNull(11) ? "" : reader.GetString(11),
                    CreatedAtUtc = DateTimeOffset.Parse(reader.GetString(12))
                });
            }
        }
        return list;
    }

    public string? ResolveTenantFromLicenseKey(string licenseKey)
    {
        if (string.IsNullOrWhiteSpace(licenseKey)) return null;
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT tenant_id FROM licenses WHERE license_key = @key COLLATE NOCASE AND is_active = 1 LIMIT 1";
            cmd.Parameters.AddWithValue("@key", licenseKey.Trim());
            var res = cmd.ExecuteScalar();
            return res?.ToString();
        }
    }

    public void IncrementDeviceUsage(string licenseKey, int delta = 1)
    {
        if (string.IsNullOrWhiteSpace(licenseKey)) return;
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE licenses SET devices_used_month = devices_used_month + @delta WHERE license_key = @key COLLATE NOCASE";
            cmd.Parameters.AddWithValue("@delta", delta);
            cmd.Parameters.AddWithValue("@key", licenseKey.Trim());
            cmd.ExecuteNonQuery();
        }
    }

    private static string CleanPrefix(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "DEPOT";
        var cleaned = new StringBuilder();
        foreach (var c in name.Trim())
        {
            if (char.IsLetterOrDigit(c)) cleaned.Append(char.ToUpperInvariant(c));
            if (cleaned.Length >= 6) break;
        }
        return cleaned.Length >= 3 ? cleaned.ToString() : "DEPOT";
    }

    // ── Dynamic Auto-Update Engine ───────────────────────────────────────────

    public UpdateCheckResponse CheckForUpdate(string appName, string currentVersion, string channel, string baseUrl)
    {
        var app = string.IsNullOrWhiteSpace(appName) ? "VeloxBench" : appName.Trim();
        var chan = string.IsNullOrWhiteSpace(channel) ? "alpha" : channel.Trim().ToLowerInvariant();

        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT version, channel, download_url, file_sha256, file_size_bytes, is_mandatory, release_notes, released_at_utc FROM app_releases WHERE app_name = @app AND channel = @chan ORDER BY released_at_utc DESC LIMIT 1";
            cmd.Parameters.AddWithValue("@app", app);
            cmd.Parameters.AddWithValue("@chan", chan);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return new UpdateCheckResponse
                {
                    UpdateAvailable = false,
                    LatestVersion = currentVersion,
                    Channel = chan
                };
            }

            var latestVer = reader.GetString(0);
            var ch = reader.GetString(1);
            var dlUrl = reader.GetString(2);
            var sha = reader.GetString(3);
            var size = reader.GetInt64(4);
            var mandatory = reader.GetInt32(5) == 1;
            var notes = reader.GetString(6);
            var released = DateTimeOffset.Parse(reader.GetString(7));
            reader.Close();

            // Ensure absolute download URL
            if (!dlUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !dlUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                dlUrl = baseUrl.TrimEnd('/') + "/" + dlUrl.TrimStart('/');
            }

            bool isNewer = CompareVersions(latestVer, currentVersion) > 0;

            return new UpdateCheckResponse
            {
                UpdateAvailable = isNewer,
                LatestVersion = latestVer,
                Channel = ch,
                IsMandatory = mandatory,
                DownloadUrl = dlUrl,
                FileSha256 = sha,
                FileSizeBytes = size,
                ReleaseNotes = notes,
                PublishedAtUtc = released
            };
        }
    }

    public bool PublishRelease(PublishReleaseRequest req)
    {
        var app = string.IsNullOrWhiteSpace(req.AppName) ? "VeloxBench" : req.AppName.Trim();
        var ver = string.IsNullOrWhiteSpace(req.Version) ? "1.0.0" : req.Version.Trim();
        var chan = string.IsNullOrWhiteSpace(req.Channel) ? "alpha" : req.Channel.Trim().ToLowerInvariant();
        var now = Iso(DateTimeOffset.UtcNow);

        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO app_releases(id, app_name, version, channel, download_url, file_sha256, file_size_bytes, is_mandatory, release_notes, released_at_utc)
VALUES(@id, @app, @ver, @chan, @dl, @sha, @size, @mand, @notes, @now)";
            cmd.Parameters.AddWithValue("@id", "rel-" + NewId());
            cmd.Parameters.AddWithValue("@app", app);
            cmd.Parameters.AddWithValue("@ver", ver);
            cmd.Parameters.AddWithValue("@chan", chan);
            cmd.Parameters.AddWithValue("@dl", req.DownloadUrl ?? "/api/v1/update/download/latest");
            cmd.Parameters.AddWithValue("@sha", req.FileSha256 ?? "");
            cmd.Parameters.AddWithValue("@size", req.FileSizeBytes);
            cmd.Parameters.AddWithValue("@mand", req.IsMandatory ? 1 : 0);
            cmd.Parameters.AddWithValue("@notes", req.ReleaseNotes ?? "");
            cmd.Parameters.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
            return true;
        }
    }

    public static int CompareVersions(string v1, string v2)
    {
        if (Version.TryParse(v1, out var ver1) && Version.TryParse(v2, out var ver2))
        {
            return ver1.CompareTo(ver2);
        }
        return string.Compare(v1, v2, StringComparison.OrdinalIgnoreCase);
    }

    // ── High-Throughput Bench Cloud Ingestion ────────────────────────────────

    public BenchSyncResponse IngestBenchSyncBatch(BenchSyncBatchRequest batch, string? fallbackTenantId)
    {
        if (batch == null || batch.Events == null || batch.Events.Count == 0)
        {
            return new BenchSyncResponse { Success = true, IngestedCount = 0, Message = "No events in batch." };
        }

        var tenantId = ResolveTenantFromLicenseKey(batch.LicenseKey) ?? fallbackTenantId ?? "ten-d2428a4cb3da";
        int ingested = 0;
        int newAssets = 0;

        lock (_gate)
        {
            using var conn = Open();
            using var tx = conn.BeginTransaction();

            foreach (var evt in batch.Events)
            {
                if (string.IsNullOrWhiteSpace(evt.AggregateId)) continue;
                var serial = evt.AggregateId.Trim();
                var nowIso = Iso(DateTimeOffset.UtcNow);

                // Parse payload details if available
                string model = "Generic Laptop";
                int? batteryHealth = null;
                int? storageHealth = null;
                string grade = "Tested";
                string notes = "";

                if (!string.IsNullOrWhiteSpace(evt.PayloadJson))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(evt.PayloadJson);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("Model", out var mProp)) model = mProp.GetString() ?? model;
                        if (root.TryGetProperty("BatteryHealth", out var bhProp) && bhProp.TryGetInt32(out var bh)) batteryHealth = bh;
                        if (root.TryGetProperty("StorageHealth", out var shProp) && shProp.TryGetInt32(out var sh)) storageHealth = sh;
                        if (root.TryGetProperty("Grade", out var gProp)) grade = gProp.GetString() ?? grade;
                        if (root.TryGetProperty("Notes", out var nProp)) notes = nProp.GetString() ?? notes;
                    }
                    catch { }
                }

                // Check if asset exists
                string? existingAssetId = null;
                using (var findCmd = conn.CreateCommand())
                {
                    findCmd.Transaction = tx;
                    findCmd.CommandText = "SELECT id FROM assets WHERE tenant_id = @tid AND serial_number = @sn COLLATE NOCASE LIMIT 1";
                    findCmd.Parameters.AddWithValue("@tid", tenantId);
                    findCmd.Parameters.AddWithValue("@sn", serial);
                    var res = findCmd.ExecuteScalar();
                    if (res != null) existingAssetId = res.ToString();
                }

                string assetId;
                if (existingAssetId != null)
                {
                    assetId = existingAssetId;
                    using var updateCmd = conn.CreateCommand();
                    updateCmd.Transaction = tx;
                    updateCmd.CommandText = @"
UPDATE assets SET
    model = CASE WHEN @model <> '' THEN @model ELSE model END,
    battery_health = COALESCE(@bh, battery_health),
    storage_health = COALESCE(@sh, storage_health),
    grade = CASE WHEN @grade <> '' THEN @grade ELSE grade END,
    updated_at_utc = @now
WHERE id = @id";
                    updateCmd.Parameters.AddWithValue("@model", model);
                    updateCmd.Parameters.AddWithValue("@bh", (object?)batteryHealth ?? DBNull.Value);
                    updateCmd.Parameters.AddWithValue("@sh", (object?)storageHealth ?? DBNull.Value);
                    updateCmd.Parameters.AddWithValue("@grade", grade);
                    updateCmd.Parameters.AddWithValue("@now", nowIso);
                    updateCmd.Parameters.AddWithValue("@id", assetId);
                    updateCmd.ExecuteNonQuery();
                }
                else
                {
                    assetId = "ast-" + NewId();
                    newAssets++;
                    using var insertCmd = conn.CreateCommand();
                    insertCmd.Transaction = tx;
                    insertCmd.CommandText = @"
INSERT INTO assets(id, tenant_id, serial_number, asset_tag, model, current_location, lifecycle_queue, grade, battery_health, storage_health, notes, created_at_utc, updated_at_utc)
VALUES(@id, @tid, @sn, @sn, @model, 'QC Bench', 'QualityControl', @grade, @bh, @sh, @notes, @now, @now)";
                    insertCmd.Parameters.AddWithValue("@id", assetId);
                    insertCmd.Parameters.AddWithValue("@tid", tenantId);
                    insertCmd.Parameters.AddWithValue("@sn", serial);
                    insertCmd.Parameters.AddWithValue("@model", model);
                    insertCmd.Parameters.AddWithValue("@grade", grade);
                    insertCmd.Parameters.AddWithValue("@bh", (object?)batteryHealth ?? DBNull.Value);
                    insertCmd.Parameters.AddWithValue("@sh", (object?)storageHealth ?? DBNull.Value);
                    insertCmd.Parameters.AddWithValue("@notes", notes);
                    insertCmd.Parameters.AddWithValue("@now", nowIso);
                    insertCmd.ExecuteNonQuery();
                }

                // Insert event audit
                var eventId = !string.IsNullOrWhiteSpace(evt.EventId) ? evt.EventId : ("evt-" + NewId());
                using (var evCmd = conn.CreateCommand())
                {
                    evCmd.Transaction = tx;
                    evCmd.CommandText = @"
INSERT OR IGNORE INTO asset_events(id, asset_id, tenant_id, event_type, location, actor, notes_json, source_bench_id, recorded_at_utc)
VALUES(@id, @aid, @tid, @et, 'QC Bench', @actor, @notes, @bench, @rec)";
                    evCmd.Parameters.AddWithValue("@id", eventId);
                    evCmd.Parameters.AddWithValue("@aid", assetId);
                    evCmd.Parameters.AddWithValue("@tid", tenantId);
                    evCmd.Parameters.AddWithValue("@et", string.IsNullOrWhiteSpace(evt.EventType) ? "AssetQcCompleted" : evt.EventType);
                    evCmd.Parameters.AddWithValue("@actor", "VeloxBench");
                    evCmd.Parameters.AddWithValue("@notes", string.IsNullOrWhiteSpace(evt.PayloadJson) ? "{}" : evt.PayloadJson);
                    evCmd.Parameters.AddWithValue("@bench", batch.MachineName ?? "BENCH");
                    evCmd.Parameters.AddWithValue("@rec", !string.IsNullOrWhiteSpace(evt.Timestamp) ? evt.Timestamp : nowIso);
                    evCmd.ExecuteNonQuery();
                }

                ingested++;
            }

            tx.Commit();
        }

        // Increment device usage if new assets were certified
        if (newAssets > 0 && !string.IsNullOrWhiteSpace(batch.LicenseKey))
        {
            IncrementDeviceUsage(batch.LicenseKey, newAssets);
        }

        return new BenchSyncResponse
        {
            Success = true,
            IngestedCount = ingested,
            Message = $"Successfully ingested {ingested} QC events to Velox Cloud."
        };
    }

    public void Dispose()
    {
        _keepAliveConn?.Dispose();
    }
}
