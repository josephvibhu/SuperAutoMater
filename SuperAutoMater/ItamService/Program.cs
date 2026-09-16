using ItamService.Auth;
using ItamService.Data;
using ItamService.Domain;
using ItamService.Endpoints;
using ItamService.Models;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──────────────────────────────────────────────────────────────────
// TenantContext is scoped — populated per-request by ApiKeyMiddleware
builder.Services.AddScoped<TenantContext>();

// ItamDb is a singleton (thread-safe via internal _gate lock)
builder.Services.AddSingleton<ItamDb>();

// Domain services are scoped (depend on DI-resolved ItamDb)
builder.Services.AddScoped<ReconciliationEngine>();
builder.Services.AddScoped<ReportGenerator>();

builder.Services.AddOpenApi();

var app = builder.Build();

// ── Ensure DB schema is initialized on startup ─────────────────────────────
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<ItamDb>().Initialize();

// ── Middleware ────────────────────────────────────────────────────────────────
// Swagger/OpenAPI in dev only
if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseMiddleware<ApiKeyMiddleware>();

// ── Health check (unauthenticated) ────────────────────────────────────────────
app.MapGet("/health", () => Results.Ok(new
{
    status  = "healthy",
    service = "ItamService",
    version = "1.6.7",
    utc     = DateTimeOffset.UtcNow
}));

// ── API Endpoints ─────────────────────────────────────────────────────────────
app.MapAssetEndpoints();
app.MapComponentEndpoints();
app.MapReportEndpoints();
app.MapAdminEndpoints();

app.Run();

// Allow WebApplicationFactory to reference this type in tests
public partial class Program { }
