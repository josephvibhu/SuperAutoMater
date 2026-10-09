using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VeloxAPI.Auth;
using VeloxAPI.Data;
using VeloxAPI.Realtime;
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

                var onHoldCount = allReady.Count(a => a.IsOnHold);

                return Results.Ok(new
                {
                    totalReadyForSale = allReady.Count,
                    pristineGradeCount = allReady.Count(a => a.Grade == AssetGrade.A || (a.CosmeticGrade != null && a.CosmeticGrade.Contains("A"))),
                    onHoldCount,
                    byBrand,
                    byGrade,
                    topLocations = byLocation,
                    updatedAtUtc = DateTimeOffset.UtcNow
                });
            });

            // POST /api/v1/showroom/assets/{id}/hold — Sales reservation
            grp.MapPost("/assets/{id}/hold", async (string id, HoldAssetRequest req, HttpContext ctx, VeloxDb db, SseHub sse) =>
            {
                var tenantId = ctx.TenantId() ?? "tenant-default";
                var asset = db.GetAssetById(tenantId, id);
                if (asset is null) return Results.NotFound(new { error = "Asset not found." });

                if (asset.IsOnHold)
                {
                    return Results.Conflict(new { error = $"Asset is already reserved by {asset.HoldSalesRep} for {asset.HoldCustomer} until {asset.HoldExpiresAtUtc}." });
                }

                if (string.IsNullOrWhiteSpace(req.CustomerName))
                    return Results.BadRequest(new { error = "Customer name is required to place a reservation." });

                var duration = req.DurationMinutes > 0 ? req.DurationMinutes : 120;
                var salesRep = string.IsNullOrWhiteSpace(req.SalesRep) ? "Sales Team" : req.SalesRep.Trim();

                var ok = db.SetAssetHold(tenantId, id, req.CustomerName.Trim(), salesRep, duration);
                if (!ok) return Results.StatusCode(500);

                var updated = db.GetAssetById(tenantId, id)!;
                db.AppendEvent(tenantId, id, new AppendEventRequest
                {
                    EventType = "Reserved/Hold",
                    Actor = salesRep,
                    NotesJson = $"{{\"customer\":\"{req.CustomerName.Trim()}\",\"durationMinutes\":{duration}}}"
                });

                await sse.BroadcastAsync(SseEventType.AssetUpdated, updated);
                return Results.Ok(updated);
            });

            // POST /api/v1/showroom/assets/{id}/release-hold — Release sales reservation
            grp.MapPost("/assets/{id}/release-hold", async (string id, HttpContext ctx, VeloxDb db, SseHub sse) =>
            {
                var tenantId = ctx.TenantId() ?? "tenant-default";
                var asset = db.GetAssetById(tenantId, id);
                if (asset is null) return Results.NotFound(new { error = "Asset not found." });

                var ok = db.ReleaseAssetHold(tenantId, id);
                if (!ok) return Results.StatusCode(500);

                var updated = db.GetAssetById(tenantId, id)!;
                db.AppendEvent(tenantId, id, new AppendEventRequest
                {
                    EventType = "HoldReleased",
                    Actor = "ShowroomSales",
                    NotesJson = "{}"
                });

                await sse.BroadcastAsync(SseEventType.AssetUpdated, updated);
                return Results.Ok(updated);
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
