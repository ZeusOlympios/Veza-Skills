using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using VezaSkills.Api.Auth;
using VezaSkills.Api.Data;
using VezaSkills.Api.Services;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

LoadDotEnv();

var builder = WebApplication.CreateBuilder(args);

// Render gives the port in PORT. Locally, launchSettings decides.
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
if (!builder.Environment.IsDevelopment())
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Allowed front-end addresses, comma separated
var origins = (Environment.GetEnvironmentVariable("FRONTEND_URL") ?? "http://localhost:5173")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(BuildConnectionString(Environment.GetEnvironmentVariable("DATABASE_URL") ?? "")));

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<ClaudeService>();
builder.Services.AddScoped<ProfileService>();

builder.Services.AddAuthentication(SupabaseAuthHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SupabaseAuthHandler>(SupabaseAuthHandler.SchemeName, null);
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.ContentType = "application/json";
        await ctx.HttpContext.Response.WriteAsync(
            "{\"error\":{\"code\":\"rate_limited\",\"message\":\"Too many requests. Please wait a bit and try again.\"}}", ct);
    };

    string Key(HttpContext c) =>
        c.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? c.Connection.RemoteIpAddress?.ToString() ?? "anon";

    o.AddPolicy("chat", c => RateLimitPartition.GetFixedWindowLimiter(Key(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromHours(1) }));
    o.AddPolicy("ai", c => RateLimitPartition.GetFixedWindowLimiter(Key(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromHours(1) }));
    o.AddPolicy("public", c => RateLimitPartition.GetFixedWindowLimiter(
        c.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Behind Render's proxy, trust the forwarded address so rate limits use the real client IP
var forwarded = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwarded.KnownNetworks.Clear();
forwarded.KnownProxies.Clear();
app.UseForwardedHeaders(forwarded);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();

// Create tables, lock them down with row level security, and load seed data.
// If this fails the API still starts, so /api/health keeps working.
const string RlsSql = """
DO $$
DECLARE r record;
BEGIN
  FOR r IN SELECT tablename FROM pg_tables WHERE schemaname = 'public' LOOP
    EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY', r.tablename);
  END LOOP;
END $$;
""";

using (var scope = app.Services.CreateScope())
{
    var log = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var creator = db.GetService<IRelationalDatabaseCreator>();
try
{
    await creator.CreateTablesAsync();
}
catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P07")
{
    // tables already exist, nothing to do
}
        try { await db.Database.ExecuteSqlRawAsync(RlsSql); }
        catch (Exception ex) { log.LogWarning(ex, "Row level security step skipped"); }
        await SeedData.RunAsync(db, log);
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Database initialisation failed");
    }
}

app.Run();

// ---------- helpers ----------

// Loads KEY=VALUE lines from a .env file (current folder or up to 3 parents) for local development
static void LoadDotEnv()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    for (var i = 0; i < 4 && dir != null; i++, dir = dir.Parent)
    {
        var file = Path.Combine(dir.FullName, ".env");
        if (!File.Exists(file)) continue;
        foreach (var line in File.ReadAllLines(file))
        {
            var t = line.Trim();
            if (t.Length == 0 || t.StartsWith('#')) continue;
            var idx = t.IndexOf('=');
            if (idx <= 0) continue;
            var key = t[..idx].Trim();
            var val = t[(idx + 1)..].Trim().Trim('"');
            if (Environment.GetEnvironmentVariable(key) == null)
                Environment.SetEnvironmentVariable(key, val);
        }
        break;
    }
}

// Accepts either an Npgsql connection string or a postgres:// URL (what Supabase shows)
static string BuildConnectionString(string raw)
{
    if (!raw.StartsWith("postgres", StringComparison.OrdinalIgnoreCase)) return raw;
    var uri = new Uri(raw);
    var parts = uri.UserInfo.Split(':', 2);
    var user = Uri.UnescapeDataString(parts[0]);
    var pass = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "";
    var port = uri.Port > 0 ? uri.Port : 5432;
    var database = uri.AbsolutePath.TrimStart('/');
    return $"Host={uri.Host};Port={port};Database={database};Username={user};Password={pass};SSL Mode=Require;Trust Server Certificate=true";
}
