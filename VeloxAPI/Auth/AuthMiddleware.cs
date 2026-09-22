using VeloxAPI.Data;

namespace VeloxAPI.Auth;

/// <summary>
/// Resolves the tenant ID from the X-API-Key header for machine-to-machine calls.
/// Used by VeloxBench and VeloxManager (not end-users).
/// </summary>
public sealed class ApiKeyMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var path = ctx.Request.Path.Value ?? "";

        // Whenever X-API-Key is present, resolve and attach TenantId
        var apiKey = ctx.Request.Headers["X-API-Key"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var db = ctx.RequestServices.GetRequiredService<VeloxDb>();
            var resolvedTenant = db.ResolveTenantFromApiKey(apiKey);
            if (resolvedTenant != null)
            {
                ctx.Items["TenantId"] = resolvedTenant;
            }
        }
        else
        {
            var headerTenant = ctx.Request.Headers["X-Tenant-Id"].FirstOrDefault()
                            ?? ctx.Request.Query["tenantId"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(headerTenant))
            {
                ctx.Items["TenantId"] = headerTenant;
            }
        }

        // Static files and UI routes bypass API key requirement
        if (!path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            await next(ctx);
            return;
        }

        // Unauthenticated routes (admin routes enforce X-Admin-Secret internally)
        if (path == "/health" ||
            path.StartsWith("/api/v1/auth", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/v1/admin", StringComparison.OrdinalIgnoreCase))
        {
            await next(ctx);
            return;
        }

        // SSE stream and employee operations accept JWT
        var authHeader = ctx.Request.Headers["Authorization"].FirstOrDefault();
        if ((path.StartsWith("/api/v1/events", StringComparison.OrdinalIgnoreCase) ||
             path.StartsWith("/api/v1/jobs", StringComparison.OrdinalIgnoreCase) ||
             path.StartsWith("/api/v1/assets", StringComparison.OrdinalIgnoreCase)) && authHeader != null)
        {
            await next(ctx);
            return;
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            ctx.Response.StatusCode = 401;
            await ctx.Response.WriteAsJsonAsync(new { error = "X-API-Key header is required." });
            return;
        }

        if (!ctx.Items.ContainsKey("TenantId"))
        {
            ctx.Response.StatusCode = 401;
            await ctx.Response.WriteAsJsonAsync(new { error = "Invalid or revoked API key." });
            return;
        }

        await next(ctx);
    }
}

/// <summary>
/// Validates Bearer JWT tokens for employee-facing VeloxLink requests.
/// Populates HttpContext.Items with EmployeeId, TenantId, Role.
/// </summary>
public sealed class JwtMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var authHeader = ctx.Request.Headers["Authorization"].FirstOrDefault();
        if (authHeader != null && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token     = authHeader["Bearer ".Length..].Trim();
            var jwt       = ctx.RequestServices.GetRequiredService<JwtService>();
            var principal = jwt.Validate(token);
            if (principal != null)
            {
                var empId = principal.FindFirst("sub")?.Value
                         ?? principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (!string.IsNullOrWhiteSpace(empId))
                {
                    ctx.Items["EmployeeId"] = empId;
                }

                var tid = principal.FindFirst("tid")?.Value
                       ?? (ctx.Items.TryGetValue("TenantId", out var t) ? t as string : null);
                if (!string.IsNullOrWhiteSpace(tid))
                {
                    ctx.Items["TenantId"] = tid;
                }

                var role = principal.FindFirst("role")?.Value;
                if (!string.IsNullOrWhiteSpace(role)) ctx.Items["Role"] = role;

                var name = principal.FindFirst("name")?.Value
                        ?? principal.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
                if (!string.IsNullOrWhiteSpace(name)) ctx.Items["Name"] = name;
            }
        }
        await next(ctx);
    }
}
