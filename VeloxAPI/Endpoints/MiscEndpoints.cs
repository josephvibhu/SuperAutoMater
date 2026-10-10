using VeloxAPI.Auth;
using VeloxAPI.Data;
using VeloxAPI.Realtime;
using Velox.Shared.Dtos;
using Velox.Shared.Models;

namespace VeloxAPI.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // POST /api/v1/auth/login — employee PIN login, returns JWT
        app.MapPost("/api/v1/auth/login", (LoginRequest req, HttpContext ctx, VeloxDb db, JwtService jwt) =>
        {
            // For login, tenantId comes from X-API-Key OR from a query param (VeloxLink doesn't store a key, it uses the depot's shared API key)
            var tenantId = ctx.TenantId();
            if (tenantId == null)
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Pin))
                return Results.BadRequest(new { error = "name and pin are required." });

            var emp = db.ValidatePin(tenantId, req.Name, req.Pin);
            if (emp == null)
                return Results.Json(new { error = "Invalid name or PIN." }, statusCode: 401);

            var (token, expiresAt) = jwt.IssueToken(emp.Id, emp.Name, tenantId, emp.Role);

            return Results.Ok(new LoginResponse
            {
                Token        = token,
                EmployeeId   = emp.Id,
                Name         = emp.Name,
                Role         = emp.Role,
                StationId    = emp.StationId,
                ExpiresAtUtc = expiresAt
            });
        });
    }
}

public static class SseEndpoints
{
    public static void MapSseEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /api/v1/events and /api/v1/events/stream — SSE long-poll, accepts API key OR JWT
        app.MapGet("/api/v1/events/stream", HandleSse);
        app.MapGet("/api/v1/events", HandleSse);

        static async Task HandleSse(HttpContext ctx, VeloxAPI.Realtime.SseHub sse)
        {
            var tenantId = ctx.TenantId() ?? ctx.Items["TenantId"] as string;
            if (tenantId == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsJsonAsync(new { error = "Authentication required." });
                return;
            }
            await sse.ServeAsync(ctx, ctx.RequestAborted);
        }
    }
}

public static class JobEndpoints
{
    public static void MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /api/v1/jobs/mine — returns assets assigned to the calling employee (JWT sub)
        app.MapGet("/api/v1/jobs/mine", (HttpContext ctx, VeloxDb db) =>
        {
            var employeeId = ctx.Items["EmployeeId"] as string;
            var tenantId   = ctx.Items["TenantId"] as string;

            if (string.IsNullOrWhiteSpace(employeeId) || string.IsNullOrWhiteSpace(tenantId))
                return Results.Unauthorized();

            var jobs = db.GetJobsForEmployee(tenantId, employeeId);
            return Results.Ok(new { employeeId, count = jobs.Count, jobs });
        });
    }
}

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        string ResolveAdminTenant(HttpContext ctx, VeloxDb db)
        {
            var apiKey = ctx.Request.Headers["X-API-Key"].FirstOrDefault()
                      ?? ctx.Request.Headers["X-Velox-Api-Key"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                var resolved = db.ResolveTenantFromApiKey(apiKey);
                if (resolved != null) return resolved;
            }
            return ctx.TenantId() ?? ctx.Items["TenantId"] as string ?? "ten-d2428a4cb3da";
        }

        // POST /api/v1/admin/setup — one-time depot setup: creates tenant + API key
        // Protected by VELOX_ADMIN_SECRET header
        app.MapPost("/api/v1/admin/setup", (AdminSetupRequest req, HttpContext ctx, VeloxDb db, IConfiguration config) =>
        {
            var adminSecret = config["VELOX_ADMIN_SECRET"] ?? Environment.GetEnvironmentVariable("VELOX_ADMIN_SECRET");
            var provided    = ctx.Request.Headers["X-Admin-Secret"].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(adminSecret) || provided != adminSecret)
            {
                ctx.Response.StatusCode = 403;
                return Results.Json(new { error = "Invalid admin secret." }, statusCode: 403);
            }

            var (tenantId, plainKey) = db.CreateTenantWithKey(req.DepotName, req.SiteCode, req.KeyLabel);
            return Results.Ok(new
            {
                message  = "Depot created. Store your API key — it will not be shown again.",
                tenantId,
                apiKey   = plainKey,
                note     = "Set X-API-Key: {apiKey} on all machine-to-machine requests."
            });
        });

        // GET /api/v1/admin/backup — full database JSON dump
        app.MapGet("/api/v1/admin/backup", (HttpContext ctx, VeloxDb db) =>
        {
            var tenantId = ResolveAdminTenant(ctx, db);
            var backup = db.ExportBackup(tenantId);
            return Results.Ok(backup);
        });

        // POST /api/v1/admin/restore — restore database from JSON backup
        app.MapPost("/api/v1/admin/restore", async (DatabaseBackupDto backup, HttpContext ctx, VeloxDb db, SseHub sse) =>
        {
            var tenantId = ResolveAdminTenant(ctx, db);
            var (assets, comps, events) = db.RestoreBackup(tenantId, backup);
            await sse.BroadcastAsync(SseEventType.AssetUpdated, new { restored = assets });
            return Results.Ok(new RestoreBackupResponse
            {
                Success = true,
                Message = $"Cloud database successfully restored with {assets} assets, {comps} components, and {events} event logs.",
                RestoredAssets = assets,
                RestoredComponents = comps,
                RestoredEvents = events
            });
        });

        // POST /api/v1/admin/purge-assets — purge all previous asset entries & history to start clean
        app.MapPost("/api/v1/admin/purge-assets", async (HttpContext ctx, VeloxDb db, SseHub sse) =>
        {
            var tenantId = ResolveAdminTenant(ctx, db);
            var deleted = db.PurgeTenantAssets(tenantId);
            await sse.BroadcastAsync(SseEventType.AssetUpdated, new { purged = deleted });
            return Results.Ok(new PurgeAssetsResponse
            {
                Success = true,
                Message = $"Purged {deleted} previous assets and associated components/history from cloud database.",
                PurgedAssets = deleted
            });
        });
    }
}

public sealed class AdminSetupRequest
{
    public string DepotName { get; init; } = "";
    public string SiteCode  { get; init; } = "";
    public string KeyLabel  { get; init; } = "primary";
}

// ── HttpContext extension helpers ─────────────────────────────────────────────
public static class HttpContextExtensions
{
    public static string? TenantId(this HttpContext ctx)
        => ctx.Items["TenantId"] as string;

    public static string? EmployeeId(this HttpContext ctx)
        => ctx.Items["EmployeeId"] as string;
}
