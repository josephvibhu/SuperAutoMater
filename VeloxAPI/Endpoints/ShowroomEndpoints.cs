using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VeloxAPI.Auth;
using VeloxAPI.Data;
using Velox.Shared.Dtos;
using Velox.Shared.Models;

namespace VeloxAPI.Endpoints
{
    public static class ShowroomEndpoints
    {
        public static void MapShowroomEndpoints(this IEndpointRouteBuilder app)
        {
            // Browser shortcut redirect: /showroom -> /showroom/index.html
            app.MapGet("/showroom", () => Results.Redirect("/showroom/index.html"));

            var grp = app.MapGroup("/api/v1/showroom");

            // GET /api/v1/showroom/assets — Read-only public inventory for sales & media
            grp.MapGet("/assets", (
                string? status,
                string? category,
                string? grade,
                string? location,
                string? q,
                HttpContext ctx,
                VeloxDb db) =>
            {
                var tenantId = ctx.TenantId() ?? "tenant-default";

                LifecycleQueue? queueFilter = null;
                if (string.IsNullOrWhiteSpace(status) || string.Equals(status, "ready", StringComparison.OrdinalIgnoreCase) || string.Equals(status, "rts", StringComparison.OrdinalIgnoreCase))
                {
                    queueFilter = LifecycleQueue.ReadyForSale;
                }
                else if (string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
                {
                    queueFilter = null;
                }
                else if (Enum.TryParse<LifecycleQueue>(status, true, out var parsedQueue))
                {
                    queueFilter = parsedQueue;
                }

                AssetCategory? catFilter = null;
                if (!string.IsNullOrWhiteSpace(category) && Enum.TryParse<AssetCategory>(category, true, out var parsedCat))
                {
                    catFilter = parsedCat;
                }

                var (assets, total) = db.SearchAssets(tenantId, q, queueFilter?.ToString(), grade, limit: 1000, offset: 0);

                // In-memory refining for category & location
                var query = assets.AsEnumerable();

                if (catFilter.HasValue)
                {
                    query = query.Where(a => a.Category == catFilter.Value);
                }

                if (!string.IsNullOrWhiteSpace(location))
                {
                    query = query.Where(a => a.CurrentLocation.Contains(location, StringComparison.OrdinalIgnoreCase));
                }

                var list = query.ToList();
                return Results.Ok(new
                {
                    count = list.Count,
                    filter = new { status = status ?? "ReadyForSale", category, grade, location, q },
                    assets = list
                });
            });

            // GET /api/v1/showroom/summary — Quick metrics for sales dashboard
            grp.MapGet("/summary", (HttpContext ctx, VeloxDb db) =>
            {
                var tenantId = ctx.TenantId() ?? "tenant-default";
                var (allReady, _) = db.SearchAssets(tenantId, null, "ReadyForSale", null, limit: 1000, offset: 0);

                var byBrand = allReady
                    .GroupBy(a => ExtractBrand(a.Model))
                    .ToDictionary(g => g.Key, g => g.Count());

                var byGrade = allReady
                    .GroupBy(a => a.Grade.ToString())
                    .ToDictionary(g => g.Key, g => g.Count());

                var byLocation = allReady
                    .GroupBy(a => string.IsNullOrWhiteSpace(a.CurrentLocation) ? "Unassigned" : a.CurrentLocation)
                    .OrderByDescending(g => g.Count())
                    .Take(10)
                    .ToDictionary(g => g.Key, g => g.Count());

                return Results.Ok(new
                {
                    totalReadyForSale = allReady.Count,
                    pristineGradeCount = allReady.Count(a => a.Grade == AssetGrade.A || (a.CosmeticGrade != null && a.CosmeticGrade.Contains("A"))),
                    byBrand,
                    byGrade,
                    topLocations = byLocation,
                    updatedAtUtc = DateTimeOffset.UtcNow
                });
            });
        }

        private static string ExtractBrand(string model)
        {
            if (string.IsNullOrWhiteSpace(model)) return "Other";
            var upper = model.ToUpperInvariant();
            if (upper.Contains("HP") || upper.Contains("ELITEBOOK") || upper.Contains("PROBOOK")) return "HP";
            if (upper.Contains("DELL") || upper.Contains("LATITUDE") || upper.Contains("PRECISION")) return "Dell";
            if (upper.Contains("LENOVO") || upper.Contains("THINKPAD") || upper.Contains("YOGA")) return "Lenovo";
            if (upper.Contains("MICROSOFT") || upper.Contains("SURFACE")) return "Microsoft";
            if (upper.Contains("APPLE") || upper.Contains("MACBOOK")) return "Apple";
            return "Other";
        }
    }
}
