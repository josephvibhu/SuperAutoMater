using ItamService.Data;
using ItamService.Domain;
using ItamService.Models;

namespace ItamService.Endpoints;

public static class ReportEndpoints
{
    public static void MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /api/v1/assets/{id}/report  — JSON signed report
        app.MapGet("/api/v1/assets/{id}/report", (
            string id, ItamDb db, ReportGenerator gen, TenantContext ctx) =>
        {
            var asset = db.GetAssetById(ctx.TenantId, id);
            if (asset is null) return Results.NotFound(new { error = "Asset not found." });

            var report = gen.GenerateReport(ctx.TenantId, id, ctx.SiteCode);
            return Results.Ok(report);
        });

        // GET /api/v1/assets/{id}/report/html  — Customer-viewable HTML
        app.MapGet("/api/v1/assets/{id}/report/html", (
            string id, ItamDb db, ReportGenerator gen, TenantContext ctx) =>
        {
            var asset = db.GetAssetById(ctx.TenantId, id);
            if (asset is null) return Results.NotFound(new { error = "Asset not found." });

            var report = gen.GenerateReport(ctx.TenantId, id, ctx.SiteCode);
            string html = gen.RenderHtml(report);
            return Results.Content(html, "text/html; charset=utf-8");
        });
    }
}
