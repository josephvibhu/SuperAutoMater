using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Velox.Shared.Dtos;
using Velox.Shared.Models;
using VeloxAPI.Data;
using VeloxAPI.Realtime;

namespace VeloxAPI.Endpoints;

public static class AlphaEndpoints
{
    public static void MapAlphaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapLicenseEndpoints();
        app.MapUpdateEndpoints();
        app.MapBenchEndpoints();
    }

    // ── Alpha Licensing Endpoints ─────────────────────────────────────────────

    public static void MapLicenseEndpoints(this IEndpointRouteBuilder app)
    {
        // POST /api/v1/license/validate — validates client license & returns signed offline lease
        app.MapPost("/api/v1/license/validate", (LicenseValidateRequest req, HttpContext ctx, VeloxDb db) =>
        {
            var clientIp = ctx.Connection.RemoteIpAddress?.ToString();
            var res = db.ValidateLicense(req.LicenseKey, req.MachineName, req.MachineGuid, req.AppVersion, clientIp);
            if (!res.IsValid)
            {
                return Results.Json(res, statusCode: 403);
            }
            return Results.Ok(res);
        });

        // GET /api/v1/license/status — check remaining units and days
        app.MapGet("/api/v1/license/status", (string? key, HttpContext ctx, VeloxDb db) =>
        {
            var licKey = key ?? ctx.Request.Headers["X-License-Key"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(licKey))
            {
                return Results.BadRequest(new { error = "License key is required (query param ?key=... or X-License-Key header)." });
            }

            var res = db.ValidateLicense(licKey, "STATUS-CHECK", "STATUS-CHECK", "1.0.0");
            return Results.Ok(res);
        });

        // POST /api/v1/admin/license/create — admin creates new client license
        app.MapPost("/api/v1/admin/license/create", (LicenseCreateRequest req, HttpContext ctx, VeloxDb db, IConfiguration config) =>
        {
            var adminSecret = config["VELOX_ADMIN_SECRET"] ?? Environment.GetEnvironmentVariable("VELOX_ADMIN_SECRET");
            var provided = ctx.Request.Headers["X-Admin-Secret"].FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(adminSecret) && provided != adminSecret)
            {
                return Results.Json(new { error = "Invalid admin secret." }, statusCode: 403);
            }

            if (string.IsNullOrWhiteSpace(req.CompanyName))
            {
                return Results.BadRequest(new { error = "CompanyName is required." });
            }

            var lic = db.CreateLicense(req);
            return Results.Ok(new
            {
                message = $"License created for {req.CompanyName}.",
                license = lic
            });
        });

        // POST /api/v1/admin/tenants/provision — admin provisions full multi-tenant client with dedicated DB & plan key
        app.MapPost("/api/v1/admin/tenants/provision", (TenantProvisionRequest req, HttpContext ctx, VeloxDb db, IConfiguration config) =>
        {
            var adminSecret = config["VELOX_ADMIN_SECRET"] ?? Environment.GetEnvironmentVariable("VELOX_ADMIN_SECRET");
            var provided = ctx.Request.Headers["X-Admin-Secret"].FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(adminSecret) && provided != adminSecret)
            {
                return Results.Json(new { error = "Invalid admin secret." }, statusCode: 403);
            }

            if (string.IsNullOrWhiteSpace(req.CompanyName))
            {
                return Results.BadRequest(new { error = "CompanyName is required." });
            }

            try
            {
                var provision = db.ProvisionTenant(req);
                return Results.Ok(provision);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // GET /api/v1/admin/license/list — admin lists all issued licenses
        app.MapGet("/api/v1/admin/license/list", (HttpContext ctx, VeloxDb db, IConfiguration config) =>
        {
            var adminSecret = config["VELOX_ADMIN_SECRET"] ?? Environment.GetEnvironmentVariable("VELOX_ADMIN_SECRET");
            var provided = ctx.Request.Headers["X-Admin-Secret"].FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(adminSecret) && provided != adminSecret)
            {
                return Results.Json(new { error = "Invalid admin secret." }, statusCode: 403);
            }

            var licenses = db.ListLicenses();
            return Results.Ok(new { count = licenses.Count, licenses });
        });
    }

    // ── Dynamic Auto-Update Endpoints ─────────────────────────────────────────

    public static void MapUpdateEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /api/v1/update/check — check if newer binary version exists
        app.MapGet("/api/v1/update/check", (string? appName, string? currentVersion, string? channel, HttpContext ctx, VeloxDb db) =>
        {
            var app = appName ?? "VeloxBench";
            var ver = currentVersion ?? "1.0.0";
            var chan = channel ?? "alpha";
            var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";

            var res = db.CheckForUpdate(app, ver, chan, baseUrl);
            return Results.Ok(res);
        });

        // GET/HEAD /api/v1/update/download and /api/v1/update/download/latest — download latest portable VeloxHub or suite binary
        var downloadHandler = (string? appName, string? app, string? channel, HttpContext ctx, VeloxDb db) =>
        {
            var targetApp = !string.IsNullOrWhiteSpace(appName) ? appName : (!string.IsNullOrWhiteSpace(app) ? app : "VeloxHub");
            var chan = channel ?? "alpha";

            // Search possible binary release directories
            var searchPaths = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "releases", $"{targetApp}.exe"),
                Path.Combine(AppContext.BaseDirectory, "wwwroot", "downloads", $"{targetApp}.exe"),
                Path.Combine(AppContext.BaseDirectory, "wwwroot", $"{targetApp}.exe"),
                Path.Combine(AppContext.BaseDirectory, "..", "Releases", targetApp, $"{targetApp}.exe"),
                Path.Combine(AppContext.BaseDirectory, "..", "Releases", $"{targetApp}.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Velox", "Releases", $"{targetApp}.exe")
            };

            string? foundFile = searchPaths.FirstOrDefault(File.Exists);
            if (foundFile == null)
            {
                return Results.NotFound(new { error = $"No binary payload found on server for {targetApp}." });
            }

            return Results.File(foundFile, "application/vnd.microsoft.portable-executable", $"{targetApp}.exe");
        };

        app.MapMethods("/api/v1/update/download", new[] { "GET", "HEAD" }, downloadHandler);
        app.MapMethods("/api/v1/update/download/latest", new[] { "GET", "HEAD" }, downloadHandler);

        // POST /api/v1/admin/update/publish — publish or register new version metadata
        app.MapPost("/api/v1/admin/update/publish", (PublishReleaseRequest req, HttpContext ctx, VeloxDb db, IConfiguration config) =>
        {
            var adminSecret = config["VELOX_ADMIN_SECRET"] ?? Environment.GetEnvironmentVariable("VELOX_ADMIN_SECRET");
            var provided = ctx.Request.Headers["X-Admin-Secret"].FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(adminSecret) && provided != adminSecret)
            {
                return Results.Json(new { error = "Invalid admin secret." }, statusCode: 403);
            }

            db.PublishRelease(req);
            return Results.Ok(new
            {
                message = $"Successfully registered release {req.AppName} v{req.Version} on channel '{req.Channel}'.",
                release = req
            });
        });

        // POST /api/v1/admin/update/upload-payload — upload binary payload and register in catalog
        app.MapPost("/api/v1/admin/update/upload-payload", async (HttpRequest req, HttpContext ctx, VeloxDb db, IConfiguration config) =>
        {
            var adminSecret = config["VELOX_ADMIN_SECRET"] ?? Environment.GetEnvironmentVariable("VELOX_ADMIN_SECRET");
            var provided = ctx.Request.Headers["X-Admin-Secret"].FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(adminSecret) && provided != adminSecret)
            {
                return Results.Json(new { error = "Invalid admin secret." }, statusCode: 403);
            }

            var appName = req.Query["app"].FirstOrDefault() ?? "VeloxHub";
            var channel = req.Query["channel"].FirstOrDefault() ?? "alpha";
            var version = req.Query["version"].FirstOrDefault() ?? "1.6.10";

            var releasesDir = Path.Combine(AppContext.BaseDirectory, "releases");
            Directory.CreateDirectory(releasesDir);
            var destPath = Path.Combine(releasesDir, $"{appName}.exe");

            using (var fileStream = File.Create(destPath))
            {
                await req.Body.CopyToAsync(fileStream);
            }

            var fileInfo = new FileInfo(destPath);
            using var sha = System.Security.Cryptography.SHA256.Create();
            using var fs = File.OpenRead(destPath);
            var hashBytes = await sha.ComputeHashAsync(fs);
            var hashHex = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

            var pubReq = new PublishReleaseRequest
            {
                AppName = appName,
                Version = version,
                Channel = channel,
                DownloadUrl = $"/api/v1/update/download?app={appName}&channel={channel}",
                FileSha256 = hashHex,
                FileSizeBytes = fileInfo.Length,
                IsMandatory = false,
                ReleaseNotes = $"{appName} production payload v{version}"
            };

            db.PublishRelease(pubReq);

            return Results.Ok(new
            {
                message = $"Successfully uploaded and published {appName} v{version} ({fileInfo.Length:N0} bytes)",
                sha256 = hashHex,
                path = destPath
            });
        });
    }

    // ── High-Throughput Bench Cloud Ingestion ────────────────────────────────

    public static void MapBenchEndpoints(this IEndpointRouteBuilder app)
    {
        // POST /api/v1/bench/sync — direct, reliable replacement for Google Sheets webhook
        app.MapPost("/api/v1/bench/sync", async (BenchSyncBatchRequest req, HttpContext ctx, VeloxDb db, SseHub sse) =>
        {
            var fallbackTenant = ctx.Items["TenantId"] as string ?? "ten-d2428a4cb3da";
            var res = db.IngestBenchSyncBatch(req, fallbackTenant);

            if (res.Success && res.IngestedCount > 0)
            {
                await sse.BroadcastAsync(SseEventType.AssetUpdated, new
                {
                    source = "VeloxBench",
                    bench = req.MachineName,
                    count = res.IngestedCount
                });
            }

            return Results.Ok(res);
        });

        // POST /api/v1/bench/link/upload — mobile companion uploads defect photo / barcode
        app.MapPost("/api/v1/bench/link/upload", async (LinkUploadRequest req, SseHub sse) =>
        {
            if (string.IsNullOrWhiteSpace(req.SessionId) && string.IsNullOrWhiteSpace(req.BenchId))
            {
                return Results.BadRequest(new { error = "SessionId or BenchId is required." });
            }

            await sse.BroadcastAsync(SseEventType.AssetUpdated, new
            {
                source = "VeloxLink",
                sessionId = req.SessionId,
                benchId = req.BenchId,
                defectType = req.DefectType,
                notes = req.Notes,
                timestamp = DateTimeOffset.UtcNow
            });

            return Results.Ok(new
            {
                success = true,
                message = "Defect photo successfully linked to active inspection session.",
                sessionId = req.SessionId
            });
        });
    }
}

public sealed class LinkUploadRequest
{
    public string SessionId { get; set; } = "";
    public string BenchId { get; set; } = "";
    public string DefectType { get; set; } = "Scratch";
    public string Notes { get; set; } = "";
    public string ImageBase64 { get; set; } = "";
}
