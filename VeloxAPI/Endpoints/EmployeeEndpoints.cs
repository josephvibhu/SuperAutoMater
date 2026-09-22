using VeloxAPI.Auth;
using VeloxAPI.Data;
using VeloxAPI.Realtime;
using Velox.Shared.Dtos;
using Velox.Shared.Models;

namespace VeloxAPI.Endpoints;

public static class EmployeeEndpoints
{
    public static void MapEmployeeEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/v1/employees");

        // GET /api/v1/employees
        grp.MapGet("/", (HttpContext ctx, VeloxDb db) =>
        {
            var tenantId = ctx.TenantId();
            if (tenantId == null) return Results.Unauthorized();

            var list = db.ListEmployees(tenantId);
            return Results.Ok(new { count = list.Count, employees = list });
        });

        // POST /api/v1/employees
        grp.MapPost("/", async (CreateEmployeeRequest req, HttpContext ctx, VeloxDb db, SseHub sse) =>
        {
            var tenantId = ctx.TenantId();
            if (tenantId == null) return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest(new { error = "name is required." });
            if (string.IsNullOrWhiteSpace(req.Pin) || req.Pin.Length < 4)
                return Results.BadRequest(new { error = "pin must be at least 4 characters." });

            var emp = db.CreateEmployee(tenantId, req.Name, req.Pin, req.Role, req.StationId ?? "BENCH-01");
            await sse.BroadcastAsync(SseEventType.EmployeeUpdated, emp);
            return Results.Ok(emp);
        });

        // GET /api/v1/employees/{id}
        grp.MapGet("/{id}", (string id, HttpContext ctx, VeloxDb db) =>
        {
            var tenantId = ctx.TenantId();
            if (tenantId == null) return Results.Unauthorized();

            var emp = db.GetEmployee(tenantId, id);
            return emp is null
                ? Results.NotFound(new { error = "Employee not found." })
                : Results.Ok(emp);
        });

        // PATCH /api/v1/employees/{id}
        grp.MapMethods("/{id}", ["PATCH"], async (string id, PatchEmployeeRequest req, HttpContext ctx, VeloxDb db, SseHub sse) =>
        {
            var tenantId = ctx.TenantId();
            if (tenantId == null) return Results.Unauthorized();

            var ok = db.PatchEmployee(tenantId, id, req);
            if (!ok) return Results.NotFound(new { error = "Employee not found." });

            var emp = db.GetEmployee(tenantId, id)!;
            await sse.BroadcastAsync(SseEventType.EmployeeUpdated, emp);
            return Results.Ok(emp);
        });
    }
}
