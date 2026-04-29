using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PraxisFloeckinger.Api.Authentication;
using PraxisFloeckinger.Core.Identity;
using PraxisFloeckinger.Core.Tenancy;
using PraxisFloeckinger.Infrastructure.Identity;
using PraxisFloeckinger.Infrastructure.Persistence.Master;
using PraxisFloeckinger.Infrastructure.Persistence.Tenant;
using PraxisFloeckinger.Infrastructure.Tenancy;
using Testcontainers.PostgreSql;

namespace PraxisFloeckinger.Tests.Api;

/// <summary>
/// Integrationstests für den Account-Lockout-Mechanismus im Login-Flow.
/// </summary>
public sealed class LoginLockoutTests : IClassFixture<LoginLockoutFixture>
{
    private readonly LoginLockoutFixture _fixture;

    public LoginLockoutTests(LoginLockoutFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Login_WrongPasswordBelowMaxAttempts_StillReturns401()
    {
        // MaxAttempts=3 → 2 falsche Versuche → noch gesperrt
        await _fixture.Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(LoginLockoutFixture.User1Email, "WrongPW!"),
            headers: _fixture.TenantHeader);

        var resp = await _fixture.Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(LoginLockoutFixture.User1Email, "WrongPW!"),
            headers: _fixture.TenantHeader);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_AfterMaxAttempts_Returns429()
    {
        // 3 falsche Versuche → gesperrt → 4. Versuch gibt 429 zurück
        for (var i = 0; i < 3; i++)
            await _fixture.Client.PostAsJsonAsync("/api/v1/auth/login",
                new LoginRequest(LoginLockoutFixture.User2Email, "WrongPW!"),
                headers: _fixture.TenantHeader);

        var resp = await _fixture.Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(LoginLockoutFixture.User2Email, "AnyPassword!"),
            headers: _fixture.TenantHeader);

        resp.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Login_SuccessfulLogin_ResetsFailureCounter()
    {
        // 2 Fehlversuche, dann erfolgreich einloggen → Zähler zurückgesetzt
        await _fixture.Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(LoginLockoutFixture.User3Email, "WrongPW!"),
            headers: _fixture.TenantHeader);
        await _fixture.Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(LoginLockoutFixture.User3Email, "WrongPW!"),
            headers: _fixture.TenantHeader);

        var successResp = await _fixture.Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(LoginLockoutFixture.User3Email, LoginLockoutFixture.TestPassword),
            headers: _fixture.TenantHeader);
        successResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Nach Reset: 2 weitere Fehlversuche → noch nicht gesperrt (unter MaxAttempts=3)
        await _fixture.Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(LoginLockoutFixture.User3Email, "WrongPW!"),
            headers: _fixture.TenantHeader);
        var stillUnlockedResp = await _fixture.Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(LoginLockoutFixture.User3Email, "WrongPW!"),
            headers: _fixture.TenantHeader);

        stillUnlockedResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "Zähler wurde zurückgesetzt, Konto ist noch nicht gesperrt");
    }

    [Fact]
    public async Task Login_LockedAccount_CorrectPasswordStillReturns429()
    {
        // Konto sperren
        for (var i = 0; i < 3; i++)
            await _fixture.Client.PostAsJsonAsync("/api/v1/auth/login",
                new LoginRequest(LoginLockoutFixture.User4Email, "WrongPW!"),
                headers: _fixture.TenantHeader);

        // Korrektes Passwort → trotzdem 429 (Sperre wird VOR Passwort-Check geprüft)
        var resp = await _fixture.Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(LoginLockoutFixture.User4Email, LoginLockoutFixture.TestPassword),
            headers: _fixture.TenantHeader);

        resp.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}

// ─── Fixture ─────────────────────────────────────────────────────────────────

public sealed class LoginLockoutFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private string _tempKeyDir = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public const string TenantSubdomain = "lockout-flow-test";
    public const string TestPassword = "LockoutTest123!";

    // Separate User pro Test, damit Tests sich nicht gegenseitig beeinflussen
    public const string User1Email = "lockout-user1@test.dev";
    public const string User2Email = "lockout-user2@test.dev";
    public const string User3Email = "lockout-user3@test.dev";
    public const string User4Email = "lockout-user4@test.dev";

    public HttpClient Client { get; private set; } = null!;
    public (string, string)[] TenantHeader => [("X-Tenant-Subdomain", TenantSubdomain)];

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _tempKeyDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempKeyDir);

        var masterConnString  = _postgres.GetConnectionString();
        var templateConnString = ReplaceDb(_postgres.GetConnectionString(), "{DB}");

        var masterOptions = new DbContextOptionsBuilder<MasterDbContext>()
            .UseNpgsql(masterConnString).Options;
        await using var masterCtx = new MasterDbContext(masterOptions);
        await masterCtx.Database.MigrateAsync();

        var config       = BuildConfig(masterConnString, templateConnString);
        var connBuilder  = new TenantConnectionStringBuilder(config);
        var tenantFactory = new TenantDbContextFactory();
        var provService  = new TenantProvisioningService(
            masterCtx, tenantFactory, connBuilder, config,
            NullLogger<TenantProvisioningService>.Instance);

        await provService.ProvisionAsync(TenantSubdomain, "Lockout Test Praxis", LicenseTier.Trial);

        var tenant     = await masterCtx.Tenants.FirstAsync(t => t.Subdomain == TenantSubdomain);
        var connString = connBuilder.Build(tenant.DbConnectionRef);
        var tenantInfo = new TenantInfo(tenant.Id, tenant.Subdomain, connString);
        var tenantOptions = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(connString).Options;
        await using var tenantCtx = new TenantDbContext(tenantOptions, tenantInfo);

        var hasher = new Argon2idPasswordHasher(NullLogger<Argon2idPasswordHasher>.Instance);
        var pwHash = hasher.Hash(TestPassword);

        foreach (var email in new[] { User1Email, User2Email, User3Email, User4Email })
        {
            tenantCtx.Users.Add(new User
            {
                Email        = email,
                PasswordHash = pwHash,
                Role         = UserRole.Patient,
                FirstName    = "Lockout",
                LastName     = "Test",
            });
        }
        await tenantCtx.SaveChangesAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, cfg) =>
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["RunMigrationsOnStartup"]              = "false",
                        ["ConnectionStrings:Master"]            = masterConnString,
                        ["ConnectionStrings:TenantTemplate"]    = templateConnString,
                        ["Tenancy:ServerConnection"]            = masterConnString,
                        ["Tenancy:RootDomains:0"]              = "localhost",
                        ["Jwt:PrivateKeyPath"]                  = Path.Combine(_tempKeyDir, "test.key"),
                        ["Jwt:Issuer"]                          = "praxis-floeckinger",
                        ["Jwt:Audience"]                        = "praxis-floeckinger-api",
                        ["Jwt:AccessTokenMinutes"]             = "15",
                        ["RateLimit:LoginPermitLimit"]          = "10000",
                        ["RateLimit:RefreshPermitLimit"]        = "10000",
                        ["Encryption:DataKey"]                  = "UCtsfxg9zFWOxLxPNzOSMPva3ErIlXMbroDCCc1nBYY=",
                        // MaxAttempts=3 damit Tests schnell laufen
                        ["Auth:Lockout:MaxAttempts"]            = "3",
                        ["Auth:Lockout:LockoutMinutes"]         = "15",
                    }));
            });

        Client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        _factory.Dispose();
        await _postgres.DisposeAsync();
        if (Directory.Exists(_tempKeyDir))
            Directory.Delete(_tempKeyDir, recursive: true);
    }

    private static IConfiguration BuildConfig(string master, string template) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TenantTemplate"] = template,
                ["Tenancy:ServerConnection"]         = master,
            })
            .Build();

    private static string ReplaceDb(string cs, string db)
    {
        var b = new NpgsqlConnectionStringBuilder(cs) { Database = db };
        return b.ToString();
    }
}
