using VeloxAPI.Auth;
using VeloxAPI.Data;
using Velox.Shared.Dtos;

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
        // GET /api/v1/events/stream — SSE long-poll, accepts API key OR JWT
        app.MapGet("/api/v1/events/stream", async (HttpContext ctx, VeloxAPI.Realtime.SseHub sse) =>
        {
            var tenantId = ctx.TenantId() ?? ctx.Items["TenantId"] as string;
            if (tenantId == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsJsonAsync(new { error = "Authentication required." });
                return;
            }
            await sse.ServeAsync(ctx, ctx.RequestAborted);
        });
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
