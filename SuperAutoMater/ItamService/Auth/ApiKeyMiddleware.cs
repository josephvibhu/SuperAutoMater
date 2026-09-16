using ItamService.Data;
using ItamService.Models;

namespace ItamService.Auth;

/// <summary>
/// Reads the X-Api-Key header, validates it against the ITAM database,
/// and populates the scoped TenantContext. Returns 401 if missing/invalid.
/// All subsequent endpoints rely on TenantContext — no request can act
/// across tenant boundaries because TenantContext.TenantId is always set here.
/// </summary>
public sealed class ApiKeyMiddleware(RequestDelegate next)
{
    private const string HeaderName = "X-Api-Key";

    public async Task InvokeAsync(HttpContext ctx, ItamDb db, TenantContext tenantCtx)
    {
        // Admin endpoints under /api/v1/admin require the special admin master key
        // configured in ITAM_ADMIN_KEY environment variable.
        string path = ctx.Request.Path.Value ?? "";
        if (path.StartsWith("/api/v1/admin", StringComparison.OrdinalIgnoreCase))
        {
            string? adminKey = Environment.GetEnvironmentVariable("ITAM_ADMIN_KEY");
            string? provided = ctx.Request.Headers[HeaderName].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(adminKey) || provided != adminKey)
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsJsonAsync(new { error = "Admin API key required." });
                return;
            }
            // Admin requests don''t need tenant scoping — proceed
            await next(ctx);
            return;
        }

        // All other API routes require a tenant API key
        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            string? key = ctx.Request.Headers[HeaderName].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(key))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsJsonAsync(new { error = "X-Api-Key header is required." });
                return;
            }

            var tenant = db.ValidateApiKey(key);
            if (tenant is null)
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsJsonAsync(new { error = "Invalid or revoked API key." });
                return;
            }

            tenantCtx.TenantId = tenant.Id;
            tenantCtx.SiteCode = tenant.SiteCode;
        }

        await next(ctx);
    }
}
