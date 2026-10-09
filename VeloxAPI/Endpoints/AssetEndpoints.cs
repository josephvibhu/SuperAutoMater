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

            var existing = db.GetAssetById(tenantId, id);
            if (existing == null) return Results.NotFound(new { error = "Asset not found." });

            // Mandatory Quality Gate enforcement (unless manager override bypass is supplied)
            if (req.LifecycleQueue == LifecycleQueue.ReadyForSale && existing.LifecycleQueue != LifecycleQueue.ReadyForSale && req.BypassQualityGate != true)
            {
                var simulated = new AssetDto
                {
                    Grade = req.Grade ?? existing.Grade,
                    CosmeticGrade = req.CosmeticGrade ?? existing.CosmeticGrade,
                    BatteryHealth = req.BatteryHealth ?? existing.BatteryHealth,
                    StorageHealth = req.StorageHealth ?? existing.StorageHealth,
                    MissingComponents = req.MissingComponents ?? existing.MissingComponents
                };
                var (passed, reason) = db.CheckQualityGateForRts(simulated);
                if (!passed)
                {
                    return Results.BadRequest(new { error = $"Quality Gate Violation: {reason}", qualityGateFailed = true, reason });
                }
            }

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

        // DELETE /api/v1/assets/{id}
        grp.MapDelete("/{id}", async (string id, HttpContext ctx, VeloxDb db, SseHub sse) =>
        {
            var tenantId = ctx.TenantId();
            if (tenantId == null) return Results.Unauthorized();

            var ok = db.DeleteAsset(tenantId, id);
            if (!ok) return Results.NotFound(new { error = "Asset not found." });

            await sse.BroadcastAsync(SseEventType.AssetUpdated, new { id, deleted = true });
            return Results.Ok(new { success = true, id });
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

            var isUnassign = string.IsNullOrWhiteSpace(req.EmployeeId) || req.EmployeeId.Equals("unassign", StringComparison.OrdinalIgnoreCase);
            var targetEmpId = isUnassign ? null : req.EmployeeId.Trim();

            var ok = db.AssignAsset(tenantId, id, targetEmpId);
            if (!ok) return Results.NotFound(new { error = "Asset not found." });

            db.AppendEvent(tenantId, id, new AppendEventRequest
            {
                EventType = isUnassign ? "Unassigned" : "Assigned",
                Actor     = req.Actor ?? "Manager",
                NotesJson = isUnassign ? "{}" : $"{{\"employeeId\":\"{targetEmpId}\"}}"
            });

            var asset = db.GetAssetById(tenantId, id)!;
            await sse.BroadcastAsync(SseEventType.AssetAssigned, new { assetId = id, employeeId = targetEmpId, asset });
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

        // ── Fitted Components (BOM) Endpoints ────────────────────────────────

        // GET /api/v1/assets/{id}/components
        grp.MapGet("/{id}/components", (string id, HttpContext ctx, VeloxDb db) =>
        {
            var tenantId = ctx.TenantId();
            if (tenantId == null) return Results.Unauthorized();

            var asset = db.GetAssetById(tenantId, id);
            if (asset is null) return Results.NotFound(new { error = "Asset not found." });

            var components = db.ListFittedComponents(tenantId, id);
            return Results.Ok(new { assetId = id, count = components.Count, items = components });
        });

        // POST /api/v1/assets/{id}/components
        grp.MapPost("/{id}/components", async (string id, AttachComponentRequest req, HttpContext ctx, VeloxDb db, SseHub sse) =>
        {
            var tenantId = ctx.TenantId();
            if (tenantId == null) return Results.Unauthorized();

            var asset = db.GetAssetById(tenantId, id);
            if (asset is null) return Results.NotFound(new { error = "Parent asset not found." });

            if (string.IsNullOrWhiteSpace(req.Description) && string.IsNullOrWhiteSpace(req.ComponentAssetId))
                return Results.BadRequest(new { error = "Either description or component_asset_id is required." });

            var cmp = db.AttachComponent(tenantId, id, req);
            var updatedAsset = db.GetAssetById(tenantId, id)!;
            await sse.BroadcastAsync(SseEventType.AssetUpdated, updatedAsset);
            return Results.Ok(cmp);
        });

        // DELETE /api/v1/assets/{id}/components/{componentId}
        grp.MapDelete("/{id}/components/{componentId}", async (string id, string componentId, bool returnToStock, string? condition, HttpContext ctx, VeloxDb db, SseHub sse) =>
        {
            var tenantId = ctx.TenantId();
            if (tenantId == null) return Results.Unauthorized();

            var asset = db.GetAssetById(tenantId, id);
            if (asset is null) return Results.NotFound(new { error = "Parent asset not found." });

            var req = new DetachComponentRequest
            {
                ReturnToStock = returnToStock,
                Condition = condition ?? "Used / Tested"
            };

            var ok = db.DetachComponent(tenantId, componentId, req);
            if (!ok) return Results.NotFound(new { error = "Fitted component not found." });

            var updatedAsset = db.GetAssetById(tenantId, id)!;
            await sse.BroadcastAsync(SseEventType.AssetUpdated, updatedAsset);
            return Results.Ok(new { success = true });
        });
    }
}
