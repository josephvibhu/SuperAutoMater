using ItamService.Data;
using ItamService.Domain;
using ItamService.Models;

namespace ItamService.Endpoints;

public static class ComponentEndpoints
{
    public static void MapComponentEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/v1/assets");

        // POST /api/v1/assets/{id}/observations
        // Appends one or more component observations and runs reconciliation.
        grp.MapPost("/{id}/observations", (
            string id, ObservationBatchRequest req,
            ItamDb db, ReconciliationEngine engine, TenantContext ctx) =>
        {
            var asset = db.GetAssetById(ctx.TenantId, id);
            if (asset is null) return Results.NotFound(new { error = "Asset not found." });

            if (req.Observations is null || req.Observations.Count == 0)
                return Results.BadRequest(new { error = "At least one observation is required." });

            var written = new List<ComponentObservation>();
            foreach (var item in req.Observations)
            {
                if (string.IsNullOrWhiteSpace(item.ComponentType) || string.IsNullOrWhiteSpace(item.AttributeKey))
                    continue;

                var obs = db.AppendObservation(
                    ctx.TenantId, id,
                    item.ComponentType.Trim(),
                    item.AttributeKey.Trim(),
                    item.AttributeValue ?? "",
                    item.Source ?? "Unknown",
                    item.Confidence ?? "High",
                    req.BenchId);
                written.Add(obs);
            }

            // Run reconciliation — detects differences vs previous observations
            var newDiffs = engine.ReconcileObservations(ctx.TenantId, id, written);

            return Results.Ok(new
            {
                assetId      = id,
                written      = written.Count,
                observations = written,
                differencesDetected = newDiffs.Count,
                differences  = newDiffs
            });
        });

        // GET /api/v1/assets/{id}/observations
        grp.MapGet("/{id}/observations", (string id, ItamDb db, TenantContext ctx) =>
        {
            var asset = db.GetAssetById(ctx.TenantId, id);
            if (asset is null) return Results.NotFound(new { error = "Asset not found." });

            var observations = db.GetObservations(ctx.TenantId, id);
            // Group by component type for convenience
            var grouped = observations
                .GroupBy(o => o.ComponentType)
                .ToDictionary(g => g.Key, g => g.OrderBy(o => o.AttributeKey).ThenBy(o => o.ObservedAtUtc).ToList());

            return Results.Ok(new { assetId = id, totalObservations = observations.Count, byComponent = grouped });
        });

        // GET /api/v1/assets/{id}/differences
        grp.MapGet("/{id}/differences", (string id, ItamDb db, TenantContext ctx) =>
        {
            var asset = db.GetAssetById(ctx.TenantId, id);
            if (asset is null) return Results.NotFound(new { error = "Asset not found." });

            var diffs = db.GetDifferences(ctx.TenantId, id);
            return Results.Ok(new
            {
                assetId       = id,
                total         = diffs.Count,
                unresolved    = diffs.Count(d => !d.ApprovedReplacement),
                resolved      = diffs.Count(d => d.ApprovedReplacement),
                differences   = diffs
            });
        });

        // POST /api/v1/assets/{assetId}/differences/{diffId}/approve
        grp.MapPost("/{assetId}/differences/{diffId}/approve", (
            string assetId, string diffId,
            ApproveReplacementRequest req,
            ItamDb db, TenantContext ctx) =>
        {
            var asset = db.GetAssetById(ctx.TenantId, assetId);
            if (asset is null) return Results.NotFound(new { error = "Asset not found." });

            if (string.IsNullOrWhiteSpace(req.ApprovedBy))
                return Results.BadRequest(new { error = "approved_by is required." });

            bool updated = db.ApproveDifference(ctx.TenantId, diffId, req.ApprovedBy);
            if (!updated) return Results.NotFound(new { error = "Difference not found, already approved, or does not belong to this asset." });

            return Results.Ok(new
            {
                diffId,
                approvedBy    = req.ApprovedBy,
                approvedAtUtc = DateTimeOffset.UtcNow,
                message       = "Difference marked as approved replacement. History is preserved."
            });
        });
    }
}
