using ItamService.Data;
using ItamService.Models;

namespace ItamService.Endpoints;

public static class AssetEndpoints
{
    public static void MapAssetEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/v1/assets");

        // POST /api/v1/assets/intake
        grp.MapPost("/intake", (IntakeRequest req, ItamDb db, TenantContext ctx) =>
        {
            if (string.IsNullOrWhiteSpace(req.SerialNumber) && string.IsNullOrWhiteSpace(req.AssetTag))
                return Results.BadRequest(new { error = "serial_number or asset_tag is required." });

            var asset = db.UpsertAsset(
                ctx.TenantId,
                req.SerialNumber ?? "",
                req.AssetTag ?? "",
                req.AssetUuid,
                req.Model,
                req.Location ?? "INTAKE-STAGING");

            // Record intake event
            db.AppendEvent(ctx.TenantId, asset.Id, "Intake",
                req.Location ?? "INTAKE-STAGING", req.Actor, req.Notes, null);

            return Results.Ok(asset);
        });

        // POST /api/v1/assets/{id}/events
        grp.MapPost("/{id}/events", (string id, AppendEventRequest req, ItamDb db, TenantContext ctx) =>
        {
            var asset = db.GetAssetById(ctx.TenantId, id);
            if (asset is null) return Results.NotFound(new { error = "Asset not found." });

            var ev = db.AppendEvent(ctx.TenantId, id,
                req.EventType, req.Location, req.Actor, req.Notes, req.SourceBenchId);
            return Results.Ok(ev);
        });

        // GET /api/v1/assets/{id}/timeline
        grp.MapGet("/{id}/timeline", (string id, ItamDb db, TenantContext ctx) =>
        {
            var asset = db.GetAssetById(ctx.TenantId, id);
            if (asset is null) return Results.NotFound(new { error = "Asset not found." });

            var timeline = db.GetTimeline(ctx.TenantId, id);
            return Results.Ok(new { assetId = id, count = timeline.Count, events = timeline });
        });

        // GET /api/v1/assets?limit=50&offset=0
        grp.MapGet("/", (ItamDb db, TenantContext ctx, int limit = 50, int offset = 0) =>
        {
            limit  = Math.Clamp(limit, 1, 200);
            offset = Math.Max(0, offset);
            var assets = db.ListAssets(ctx.TenantId, limit, offset);
            return Results.Ok(new { tenantId = ctx.TenantId, count = assets.Count, assets });
        });

        // GET /api/v1/assets/{id}
        grp.MapGet("/{id}", (string id, ItamDb db, TenantContext ctx) =>
        {
            var asset = db.GetAssetById(ctx.TenantId, id);
            return asset is null
                ? Results.NotFound(new { error = "Asset not found." })
                : Results.Ok(asset);
        });
    }
}
