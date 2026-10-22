using System.Text.Json.Serialization;
using VeloxAPI.Auth;
using VeloxAPI.Data;
using VeloxAPI.Endpoints;
using VeloxAPI.Realtime;

var builder = WebApplication.CreateBuilder(args);

// ── Kestrel Limits ────────────────────────────────────────────────────────────
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 250_000_000; // 250 MB for binary payload uploads
});

// ── JSON Options ──────────────────────────────────────────────────────────────
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// ── Configuration ─────────────────────────────────────────────────────────────
// Env vars take priority; appsettings.json is fallback
builder.Configuration
    .AddEnvironmentVariables()
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true);

// ── Services ──────────────────────────────────────────────────────────────────
var dbPath = builder.Configuration["VELOX_DB_PATH"]
          ?? Environment.GetEnvironmentVariable("VELOX_DB_PATH");

builder.Services.AddSingleton<VeloxDb>(_ => new VeloxDb(dbPath));
builder.Services.AddSingleton<SseHub>();
builder.Services.AddSingleton<JwtService>();

// CORS — allow VeloxLink (PWA) from any origin in dev; restrict in prod via env var
var allowedOrigins = (builder.Configuration["VELOX_CORS_ORIGINS"]
                   ?? Environment.GetEnvironmentVariable("VELOX_CORS_ORIGINS")
                   ?? "*").Split(',', StringSplitOptions.RemoveEmptyEntries);

builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(p =>
    {
        if (allowedOrigins is ["*"])
            p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        else
            p.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader().AllowCredentials();
    }));

var app = builder.Build();

// ── Ensure DB is bootstrapped ─────────────────────────────────────────────────
app.Services.GetRequiredService<VeloxDb>().Initialize();

// ── Middleware ────────────────────────────────────────────────────────────────
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors();
app.UseMiddleware<ApiKeyMiddleware>();
app.UseMiddleware<JwtMiddleware>();

// ── Health check (unauthenticated) ───────────────────────────────────────────
app.MapGet("/health", (SseHub sse) => Results.Ok(new
{
    status      = "healthy",
    service     = "Velox Platform Cloud",
    version     = "1.6.10",
    cluster     = "ap-south-1 Mumbai",
    liveClients = sse.ConnectedCount,
    utc         = DateTimeOffset.UtcNow
}));

// ── Velox Link Mobile Companion Direct Route ──────────────────────────────────
app.MapGet("/link", (IWebHostEnvironment env) =>
{
    var path = Path.Combine(env.WebRootPath, "link", "index.html");
    return Results.File(path, "text/html");
});

// ── API Endpoints ─────────────────────────────────────────────────────────────
app.MapAuthEndpoints();
app.MapAssetEndpoints();
app.MapEmployeeEndpoints();
app.MapSseEndpoints();
app.MapJobEndpoints();
app.MapAdminEndpoints();
app.MapShowroomEndpoints();
app.MapAlphaEndpoints();

app.Run();

// Allow WebApplicationFactory to reference this type in integration tests
public partial class Program { }
