using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ItamService.Data;
using ItamService.Models;

namespace ItamService.Domain;

/// <summary>
/// Assembles the customer-viewable return/redeployment report.
/// The report is signed with HMAC-SHA256 using the ITAM_REPORT_SIGNING_KEY
/// environment variable (falls back to a per-process random key if not set).
/// JSON-first; HTML rendering is a thin wrapper over the same DTO.
/// </summary>
public sealed class ReportGenerator(ItamDb db)
{
    // Per-process signing key fallback (deterministic within one server session)
    private static readonly byte[] _fallbackKey =
        RandomNumberGenerator.GetBytes(32);

    public AssetReturnReport GenerateReport(string tenantId, string assetId, string siteCode)
    {
        var asset    = db.GetAssetById(tenantId, assetId);
        var timeline = db.GetTimeline(tenantId, assetId);
        var allObs   = db.GetObservations(tenantId, assetId);
        var diffs    = db.GetDifferences(tenantId, assetId);

        // Latest observation per (component_type, attribute_key)
        var latest = allObs
            .GroupBy(o => (o.ComponentType, o.AttributeKey))
            .Select(g => g.OrderByDescending(o => o.ObservedAtUtc).First())
            .ToList();

        var report = new AssetReturnReport
        {
            AssetId            = assetId,
            TenantId           = tenantId,
            SiteCode           = siteCode,
            Asset              = asset,
            Timeline           = timeline,
            LatestObservations = latest,
            Differences        = diffs,
            GeneratedAtUtc     = DateTimeOffset.UtcNow
        };

        report.HmacSignature = ComputeSignature(report);
        return report;
    }

    /// <summary>Renders a minimal, customer-safe HTML page for the report.</summary>
    public string RenderHtml(AssetReturnReport report)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html><head><meta charset='utf-8'/>");
        sb.Append("<meta name='viewport' content='width=device-width,initial-scale=1'/>");
        sb.Append("<title>Asset Return Report</title>");
        sb.Append("<style>");
        sb.Append("body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;background:#0d1117;color:#c9d1d9;padding:24px 16px;display:flex;justify-content:center;}");
        sb.Append(".wrap{max-width:720px;width:100%}.card{background:#161b22;border:1px solid #30363d;border-radius:12px;padding:24px;margin-bottom:16px;}");
        sb.Append("h1{font-size:20px;color:#f0f6fc;margin:0 0 4px}.badge{display:inline-block;padding:3px 12px;border-radius:20px;font-size:12px;font-weight:700;}");
        sb.Append(".green{background:#238636;color:#fff}.red{background:#b91c1c;color:#fff}.yellow{background:#9a6700;color:#fff}");
        sb.Append("table{width:100%;border-collapse:collapse;font-size:13px;}td,th{padding:8px 10px;border-bottom:1px solid #21262d;text-align:left;}");
        sb.Append("th{color:#8b949e;font-weight:600;font-size:11px;text-transform:uppercase;}");
        sb.Append(".mono{font-family:'Cascadia Mono','Consolas',monospace;font-size:11px;color:#58a6ff;word-break:break-all;}");
        sb.Append("</style></head><body><div class='wrap'>");

        // Header
        sb.Append("<div class='card'>");
        sb.Append($"<h1>Asset Return / Redeployment Report</h1>");
        sb.Append($"<p style='color:#8b949e;font-size:12px;margin:4px 0 16px'>Site: {Esc(report.SiteCode)} · Generated: {report.GeneratedAtUtc:yyyy-MM-dd HH:mm} UTC</p>");
        if (report.Asset is not null)
        {
            sb.Append($"<table><tr><th>Field</th><th>Value</th></tr>");
            sb.Append($"<tr><td>Serial Number</td><td>{Esc(report.Asset.SerialNumber)}</td></tr>");
            sb.Append($"<tr><td>Asset Tag</td><td>{Esc(report.Asset.AssetTag)}</td></tr>");
            sb.Append($"<tr><td>Model</td><td>{Esc(report.Asset.Model)}</td></tr>");
            sb.Append($"<tr><td>Lifecycle Queue</td><td>{Esc(report.Asset.LifecycleQueue)}</td></tr>");
            sb.Append($"<tr><td>Last Location</td><td>{Esc(report.Asset.CurrentLocation)}</td></tr>");
            sb.Append("</table>");
        }
        sb.Append("</div>");

        // Timeline
        sb.Append("<div class='card'><h2 style='font-size:15px;color:#f0f6fc;margin:0 0 12px'>Lifecycle Timeline</h2>");
        sb.Append("<table><tr><th>Time (UTC)</th><th>Event</th><th>Location</th><th>Actor</th></tr>");
        foreach (var ev in report.Timeline)
            sb.Append($"<tr><td>{ev.RecordedAtUtc:yyyy-MM-dd HH:mm}</td><td>{Esc(ev.EventType)}</td><td>{Esc(ev.Location)}</td><td>{Esc(ev.Actor)}</td></tr>");
        sb.Append("</table></div>");

        // Component snapshot
        if (report.LatestObservations.Count > 0)
        {
            sb.Append("<div class='card'><h2 style='font-size:15px;color:#f0f6fc;margin:0 0 12px'>Component Snapshot</h2>");
            sb.Append("<table><tr><th>Component</th><th>Attribute</th><th>Value</th><th>Source</th><th>Confidence</th><th>Observed</th></tr>");
            foreach (var o in report.LatestObservations.OrderBy(o => o.ComponentType).ThenBy(o => o.AttributeKey))
                sb.Append($"<tr><td>{Esc(o.ComponentType)}</td><td>{Esc(o.AttributeKey)}</td><td>{Esc(o.AttributeValue)}</td><td>{Esc(o.Source)}</td><td>{Esc(o.Confidence)}</td><td>{o.ObservedAtUtc:yyyy-MM-dd}</td></tr>");
            sb.Append("</table></div>");
        }

        // Differences
        if (report.Differences.Count > 0)
        {
            string badge = report.UnresolvedDifferences > 0
                ? $"<span class='badge red'>{report.UnresolvedDifferences} unresolved</span>"
                : "<span class='badge green'>All resolved</span>";
            sb.Append($"<div class='card'><h2 style='font-size:15px;color:#f0f6fc;margin:0 0 12px'>Detected Differences {badge}</h2>");
            sb.Append("<table><tr><th>Component</th><th>Attribute</th><th>Previous</th><th>Current</th><th>Status</th></tr>");
            foreach (var d in report.Differences)
            {
                string status = d.ApprovedReplacement
                    ? $"<span class='badge green'>Approved replacement</span>"
                    : "<span class='badge yellow'>Difference detected</span>";
                sb.Append($"<tr><td>{Esc(d.ComponentType)}</td><td>{Esc(d.AttributeKey)}</td><td>{Esc(d.ValueA)}</td><td>{Esc(d.ValueB)}</td><td>{status}</td></tr>");
            }
            sb.Append("</table></div>");
        }

        // Signature
        sb.Append("<div class='card'>");
        sb.Append("<p style='font-size:11px;color:#8b949e;margin:0 0 4px'>HMAC-SHA256 Report Signature</p>");
        sb.Append($"<p class='mono'>{Esc(report.HmacSignature)}</p>");
        sb.Append("</div>");

        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string ComputeSignature(AssetReturnReport report)
    {
        // Serialize the report without the signature field for signing
        var forSigning = new
        {
            report.ReportId, report.AssetId, report.TenantId, report.SiteCode,
            report.GeneratedAtUtc, report.UnresolvedDifferences,
            TimelineCount = report.Timeline.Count,
            DiffsCount    = report.Differences.Count,
            AssetSerial   = report.Asset?.SerialNumber ?? ""
        };
        string canonical = JsonSerializer.Serialize(forSigning);

        string? envKey = Environment.GetEnvironmentVariable("ITAM_REPORT_SIGNING_KEY");
        byte[] key = string.IsNullOrWhiteSpace(envKey)
            ? _fallbackKey
            : SHA256.HashData(Encoding.UTF8.GetBytes(envKey));

        byte[] sig = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(sig).ToLowerInvariant();
    }

    private static string Esc(string? s) =>
        System.Web.HttpUtility.HtmlEncode(s ?? "");
}
