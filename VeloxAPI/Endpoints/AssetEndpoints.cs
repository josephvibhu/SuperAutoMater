using VeloxAPI.Auth;
using VeloxAPI.Data;
using VeloxAPI.Realtime;
using Velox.Shared.Dtos;
using Velox.Shared.Models;

namespace VeloxAPI.Endpoints;

public static class AssetEndpoints
{
    public static void MapAssetEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/v1/assets");

        // POST /api/v1/assets/intake
        grp.MapPost("/intake", async (IntakeRequest req, HttpContext ctx, VeloxDb db, SseHub sse) =>
        {
            var tenantId = ctx.TenantId();
            if (tenantId == null) return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(req.SerialNumber) && string.IsNullOrWhiteSpace(req.AssetTag))
                return Results.BadRequest(new { error = "serial_number or asset_tag is required." });

            var asset = db.UpsertAsset(tenantId, req);

            if (!string.IsNullOrWhiteSpace(req.Actor))
                db.AppendEvent(tenantId, asset.Id, new AppendEventRequest
                {
                    EventType = "Intake",
                    Location  = req.Location ?? "IntakeStaging",
                    Actor     = req.Actor,
                    NotesJson = req.Notes != null ? $"{{\"notes\":\"{req.Notes}\"}}" : "{}"
                });

            await sse.BroadcastAsync(SseEventType.AssetUpdated, asset);
            return Results.Ok(asset);
        });

        // GET /api/v1/assets/search
        grp.MapGet("/search", (HttpContext ctx, VeloxDb db,
            string? q = null, string? queue = null, string? grade = null,
            int limit = 50, int offset = 0) =>
        {
            var tenantId = ctx.TenantId();
            if (tenantId == null) return Results.Unauthorized();

            var (items, total) = db.SearchAssets(tenantId, q, queue, grade, limit, offset);
            return Results.Ok(new { total, limit, offset, items });
        });

        // GET /api/v1/assets
        grp.MapGet("/", (HttpContext ctx, VeloxDb db, int limit = 50, int offset = 0) =>
        {
            var tenantId = ctx.TenantId();
            if (tenantId == null) return Results.Unauthorized();

            var items = db.ListAssets(tenantId, limit, offset);
            return Results.Ok(new { count = items.Count, items });
        });

        // GET /api/v1/assets/{id}
        grp.MapGet("/{id}", (string id, HttpContext ctx, VeloxDb db) =>
        {
            var tenantId = ctx.TenantId();
            if (tenantId == null) return Results.Unauthorized();

            var asset = db.GetAssetById(tenantId, id);
            return asset is null
                ? Results.NotFound(new { error = "Asset not found." })
                : Results.Ok(asset);
        });

        // PATCH /api/v1/assets/{id}
        grp.MapMethods("/{id}", ["PATCH"], async (string id, PatchAssetRequest req, HttpContext ctx, VeloxDb db, SseHub sse) =>
        {
            var tenantId = ctx.TenantId();
            if (tenantId == null) return Results.Unauthorized();

            var ok = db.PatchAsset(tenantId, id, req);
            if (!ok) return Results.NotFound(new { error = "Asset not found." });

            var asset = db.GetAssetById(tenantId, id)!;

            if (!string.IsNullOrWhiteSpace(req.Actor))
                db.AppendEvent(tenantId, id, new AppendEventRequest
                {
                    EventType = "Updated",
                    Actor     = req.Actor,
                    NotesJson = "{}"
                });

            await sse.BroadcastAsync(SseEventType.AssetUpdated, asset);
            return Results.Ok(asset);
        });

        // POST /api/v1/assets/{id}/events
        grp.MapPost("/{id}/events", async (string id, AppendEventRequest req, HttpContext ctx, VeloxDb db, SseHub sse) =>
        {
            var tenantId = ctx.TenantId();
            if (tenantId == null) return Results.Unauthorized();

            var asset = db.GetAssetById(tenantId, id);
            if (asset is null) return Results.NotFound(new { error = "Asset not found." });

            var ev = db.AppendEvent(tenantId, id, req);
            await sse.BroadcastAsync(SseEventType.AssetEventAdded, new { assetId = id, ev });
            return Results.Ok(ev);
        });

        // POST /api/v1/assets/{id}/assign
        grp.MapPost("/{id}/assign", async (string id, AssignRequest req, HttpContext ctx, VeloxDb db, SseHub sse) =>
        {
            var tenantId = ctx.TenantId();
            if (tenantId == null) return Results.Unauthorized();

            var ok = db.AssignAsset(tenantId, id, req.EmployeeId);
            if (!ok) return Results.NotFound(new { error = "Asset not found." });

            db.AppendEvent(tenantId, id, new AppendEventRequest
            {
                EventType = "Assigned",
                Actor     = req.Actor ?? "Manager",
                NotesJson = $"{{\"employeeId\":\"{req.EmployeeId}\"}}"
            });

            var asset = db.GetAssetById(tenantId, id)!;
            await sse.BroadcastAsync(SseEventType.AssetAssigned, new { assetId = id, employeeId = req.EmployeeId, asset });
            return Results.Ok(asset);
        });

        // GET /api/v1/assets/{id}/timeline
        grp.MapGet("/{id}/timeline", (string id, HttpContext ctx, VeloxDb db) =>
        {
            var tenantId = ctx.TenantId();
            if (tenantId == null) return Results.Unauthorized();

            var asset = db.GetAssetById(tenantId, id);
            if (asset is null) return Results.NotFound(new { error = "Asset not found." });

            var timeline = db.GetTimeline(tenantId, id);
            return Results.Ok(new { assetId = id, count = timeline.Count, events = timeline });
        });

        // GET /api/v1/assets/lookup?q={serial/tag/uuid}  — unauthenticated fast lookup for LAN hybrid
        grp.MapGet("/lookup", (string? q, string? serial, string? tag, HttpContext ctx, VeloxDb db) =>
        {
            // Allow API key OR no auth (used by LAN hybrid model — caller is on same LAN)
            var tenantId = ctx.TenantId();
            if (tenantId == null) return Results.Unauthorized();

            var search = q ?? serial ?? tag;
            if (string.IsNullOrWhiteSpace(search))
                return Results.BadRequest(new { error = "q, serial, or tag query parameter required." });

            var asset = db.FindAssetByAny(tenantId, search);
            return asset is null
                ? Results.Ok(new { found = false })
                : Results.Ok(new { found = true, asset });
        });
    }
}
