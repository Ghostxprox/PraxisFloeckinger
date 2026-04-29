using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PraxisFloeckinger.Api.Authentication;
using PraxisFloeckinger.Api.Tenancy;
using PraxisFloeckinger.Core.Cryptography;
using PraxisFloeckinger.Core.Identity;
using PraxisFloeckinger.Core.Tenancy;
using PraxisFloeckinger.Infrastructure.Cryptography;
using PraxisFloeckinger.Infrastructure.Identity;
using PraxisFloeckinger.Infrastructure.Persistence.Master;
using PraxisFloeckinger.Infrastructure.Persistence.Tenant;
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
builder.Services.AddSingleton<ITenantDbContextFactory, TenantDbContextFactory>();
builder.Services.AddScoped<TenantDbContext>(sp =>
    sp.GetRequiredService<ITenantDbContextFactory>()
      .Create(sp.GetRequiredService<ITenantContext>()));
builder.Services.AddScoped<ITenantProvisioningService, TenantProvisioningService>();

// ITenantContext wird aus HttpContext.Items gelesen — nur in tenant-scoped Requests gültig.
builder.Services.AddScoped<ITenantContext>(sp =>
{
    var accessor = sp.GetRequiredService<IHttpContextAccessor>();
    return accessor.HttpContext?.Items["TenantContext"] as ITenantContext
        ?? throw new InvalidOperationException(
            "ITenantContext wurde außerhalb eines tenant-scoped Requests angefordert. " +
            "Endpoint mit [RequireTenant] markieren oder Middleware-Reihenfolge prüfen.");
});

// Auth
builder.Services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
builder.Services.AddSingleton<JwtSigningKeyProvider>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();

// 2FA
builder.Services.AddSingleton<IFieldEncryptor, AesGcmFieldEncryptor>();
builder.Services.AddSingleton<ITotpService, TotpService>();
builder.Services.AddScoped<ITwoFactorRecoveryService, TwoFactorRecoveryService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddPraxisJwtBearer();
builder.Services.AddAuthorization(options => options.AddMfaPolicies());

// Rate Limiting (Login: 5/min, Refresh: 10/min, beide per IP)
// RateLimit:LoginPermitLimit / RateLimit:RefreshPermitLimit können in Tests überschrieben werden.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;

    var loginLimit = int.TryParse(builder.Configuration["RateLimit:LoginPermitLimit"], out var ll) ? ll : 5;
    var refreshLimit = int.TryParse(builder.Configuration["RateLimit:RefreshPermitLimit"], out var rl) ? rl : 10;

    options.AddPolicy("login", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = loginLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
            }));

    options.AddPolicy("refresh", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = refreshLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
            }));
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
    var provisioner = scope.ServiceProvider.GetRequiredService<ITenantProvisioningService>();
    var tenantFactory = scope.ServiceProvider.GetRequiredService<ITenantDbContextFactory>();
    var tenantConnBuilder = scope.ServiceProvider.GetRequiredService<TenantConnectionStringBuilder>();
    var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    var totpService = scope.ServiceProvider.GetRequiredService<ITotpService>();
    var fieldEncryptor = scope.ServiceProvider.GetRequiredService<IFieldEncryptor>();
    var logger = scope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger(nameof(MasterDataSeeder));
    await MasterDataSeeder.SeedDevelopmentDataAsync(
        db, provisioner, tenantFactory, tenantConnBuilder,
        passwordHasher, totpService, fieldEncryptor, logger);
}

app.UseRateLimiter();
app.UseMiddleware<TenantResolverMiddleware>();
app.UseAuthentication();
app.UseMiddleware<TokenTenantValidatorMiddleware>();
app.UseAuthorization();

// ─── Endpoints ───────────────────────────────────────────────────────────────

app.MapGet("/healthz", () => Results.Ok(new { status = "healthy" }));

app.MapGet("/api/v1/whoami", (HttpContext ctx) =>
{
    var principal = ctx.User;
    return Results.Ok(new
    {
        userId = principal.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub),
        email = principal.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email),
        role = principal.FindFirstValue("role"),
        tenantId = principal.FindFirstValue("tenant_id"),
        tenantSubdomain = principal.FindFirstValue("tenant_sub"),
    });
})
.WithMetadata(new RequireTenantAttribute())
.AddEndpointFilter<RequireTenantFilter>()
.RequireAuthorization(MfaPolicies.RequireFullAuth);

app.MapAuthEndpoints();

app.Run();

// Zugänglich für WebApplicationFactory in Integrationstests
public partial class Program;
