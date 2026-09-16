using ItamService.Data;
using ItamService.Models;

namespace ItamService.Endpoints;

/// <summary>
/// Admin-only endpoints for tenant and API key management.
/// Protected by ITAM_ADMIN_KEY env var — checked in ApiKeyMiddleware before these are reached.
/// </summary>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/v1/admin");

        // POST /api/v1/admin/tenants  — create tenant + issue first API key
        grp.MapPost("/tenants", (CreateTenantRequest req, ItamDb db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.SiteCode))
                return Results.BadRequest(new { error = "name and site_code are required." });
            if (string.IsNullOrWhiteSpace(req.FirstKeyLabel))
                return Results.BadRequest(new { error = "first_key_label is required." });

            var tenant = db.CreateTenant(req.Name, req.SiteCode);
            var (keyRecord, plainKey) = db.IssueApiKey(tenant.Id, req.FirstKeyLabel);

            return Results.Ok(new
            {
                tenant,
                apiKey = new ApiKeyIssuedResponse(
                    keyRecord.Id, tenant.Id, plainKey, keyRecord.Label, keyRecord.CreatedAtUtc),
                notice = "Store the plaintext key securely — it will not be shown again."
            });
        });

        // POST /api/v1/admin/tenants/{tenantId}/keys  — issue additional key
        grp.MapPost("/tenants/{tenantId}/keys", (string tenantId, IssueKeyRequest req, ItamDb db) =>
        {
            var tenant = db.GetTenantById(tenantId);
            if (tenant is null) return Results.NotFound(new { error = "Tenant not found." });
            if (string.IsNullOrWhiteSpace(req.Label))
                return Results.BadRequest(new { error = "label is required." });

            var (keyRecord, plainKey) = db.IssueApiKey(tenantId, req.Label);
            return Results.Ok(new ApiKeyIssuedResponse(
                keyRecord.Id, tenantId, plainKey, keyRecord.Label, keyRecord.CreatedAtUtc));
        });

        // DELETE /api/v1/admin/tenants/{tenantId}/keys/{keyId}  — revoke a key
        grp.MapDelete("/tenants/{tenantId}/keys/{keyId}", (string tenantId, string keyId, ItamDb db) =>
        {
            var tenant = db.GetTenantById(tenantId);
            if (tenant is null) return Results.NotFound(new { error = "Tenant not found." });

            bool revoked = db.RevokeApiKey(tenantId, keyId);
            return revoked
                ? Results.Ok(new { message = "API key revoked. Existing sessions using this key will be rejected immediately." })
                : Results.NotFound(new { error = "Key not found, already revoked, or does not belong to this tenant." });
        });
    }
}
