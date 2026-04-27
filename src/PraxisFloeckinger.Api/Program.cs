using Microsoft.EntityFrameworkCore;
using PraxisFloeckinger.Api.Tenancy;
using PraxisFloeckinger.Core.Tenancy;
using PraxisFloeckinger.Infrastructure.Persistence.Master;
using PraxisFloeckinger.Infrastructure.Tenancy;

var builder = WebApplication.CreateBuilder(args);

// ─── Services ────────────────────────────────────────────────────────────────

builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();

builder.Services.AddDbContext<MasterDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("Master")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:Master ist nicht konfiguriert.")));

builder.Services.AddSingleton<TenantConnectionStringBuilder>();
builder.Services.AddScoped<ITenantResolver, TenantResolver>();

// ITenantContext wird aus HttpContext.Items gelesen — nur in tenant-scoped Requests gültig.
// Endpoints ohne Tenant dürfen ITenantContext nicht direkt injizieren;
// stattdessen RequireTenantFilter nutzen.
builder.Services.AddScoped<ITenantContext>(sp =>
{
    var accessor = sp.GetRequiredService<IHttpContextAccessor>();
    return accessor.HttpContext?.Items["TenantContext"] as ITenantContext
        ?? throw new InvalidOperationException(
            "ITenantContext wurde außerhalb eines tenant-scoped Requests angefordert. " +
            "Endpoint mit [RequireTenant] markieren oder Middleware-Reihenfolge prüfen.");
});

// ─── App ─────────────────────────────────────────────────────────────────────

var app = builder.Build();

// RunMigrationsOnStartup=false in Tests gesetzt um DB-Verbindung zu unterdrücken
if (app.Environment.IsDevelopment()
    && app.Configuration.GetValue("RunMigrationsOnStartup", defaultValue: true))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<MasterDbContext>();
    await db.Database.MigrateAsync();
    var logger = scope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger(nameof(MasterDataSeeder));
    await MasterDataSeeder.SeedDevelopmentDataAsync(db, logger);
}

app.UseMiddleware<TenantResolverMiddleware>();

// ─── Endpoints ───────────────────────────────────────────────────────────────

app.MapGet("/healthz", () => Results.Ok(new { status = "healthy" }));

app.MapGet("/api/v1/whoami", (HttpContext ctx) =>
{
    var tenant = (ITenantContext)ctx.Items["TenantContext"]!;
    return Results.Ok(new { tenantId = tenant.TenantId, subdomain = tenant.Subdomain });
})
.WithMetadata(new RequireTenantAttribute())
.AddEndpointFilter<RequireTenantFilter>();

app.Run();

// Zugänglich für WebApplicationFactory in Integrationstests
public partial class Program;
