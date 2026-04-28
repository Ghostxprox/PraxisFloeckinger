using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
/// Integrationstests für Auth-Endpoints via echtem Postgres-Container.
/// Alle Tests teilen einen Container (IClassFixture) für Performanz.
/// </summary>
public sealed class AuthEndpointsTests : IClassFixture<AuthTestFixture>
{
    private readonly AuthTestFixture _fixture;

    public AuthEndpointsTests(AuthTestFixture fixture) => _fixture = fixture;

    // ─── Login ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithTokens()
    {
        var response = await _fixture.Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(AuthTestFixture.TherapistEmail, AuthTestFixture.TestPassword),
            headers: _fixture.TenantHeader);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        body.Should().NotBeNull();
        body!.AccessToken.Should().NotBeNullOrEmpty();
        body.RefreshToken.Should().NotBeNullOrEmpty();
        body.AccessTokenExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var response = await _fixture.Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(AuthTestFixture.TherapistEmail, "WrongPassword!"),
            headers: _fixture.TenantHeader);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_UnknownEmail_Returns401_SameMessageAsWrongPassword()
    {
        var wrongPassResponse = await _fixture.Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(AuthTestFixture.TherapistEmail, "WrongPassword!"),
            headers: _fixture.TenantHeader);
        var unknownEmailResponse = await _fixture.Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("nobody@example.com", "AnyPassword!"),
            headers: _fixture.TenantHeader);

        unknownEmailResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var wrongBody = await wrongPassResponse.Content.ReadAsStringAsync();
        var unknownBody = await unknownEmailResponse.Content.ReadAsStringAsync();
        // Beide müssen denselben "title" haben (Enumeration-Schutz)
        wrongBody.Should().Contain("Invalid credentials");
        unknownBody.Should().Contain("Invalid credentials");
    }

    [Fact]
    public async Task Login_WithoutTenant_Returns400()
    {
        var response = await _fixture.Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(AuthTestFixture.TherapistEmail, AuthTestFixture.TestPassword));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ─── Refresh ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_ValidToken_Returns200_RotatesToken()
    {
        var (_, refreshToken) = await LoginAsync();

        var response = await _fixture.Client.PostAsJsonAsync("/api/v1/auth/refresh",
            new RefreshRequest(refreshToken),
            headers: _fixture.TenantHeader);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        body!.RefreshToken.Should().NotBe(refreshToken, "Token muss rotiert werden");
    }

    [Fact]
    public async Task Refresh_ReusedToken_Returns401()
    {
        var (_, refreshToken) = await LoginAsync();

        // Erste Rotation: erfolgreich
        await _fixture.Client.PostAsJsonAsync("/api/v1/auth/refresh",
            new RefreshRequest(refreshToken),
            headers: _fixture.TenantHeader);

        // Zweite Rotation mit altem Token: 401
        var response = await _fixture.Client.PostAsJsonAsync("/api/v1/auth/refresh",
            new RefreshRequest(refreshToken),
            headers: _fixture.TenantHeader);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_InvalidToken_Returns401()
    {
        var response = await _fixture.Client.PostAsJsonAsync("/api/v1/auth/refresh",
            new RefreshRequest("this-token-does-not-exist"),
            headers: _fixture.TenantHeader);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─── Logout ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Logout_RevokesRefreshToken()
    {
        var (accessToken, refreshToken) = await LoginAsync();

        var logoutResp = await _fixture.Client.PostAsJsonAsync("/api/v1/auth/logout",
            new LogoutRequest(refreshToken),
            headers: _fixture.TenantHeader,
            bearer: accessToken);

        logoutResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Refresh-Token sollte jetzt ungültig sein
        var refreshResp = await _fixture.Client.PostAsJsonAsync("/api/v1/auth/refresh",
            new RefreshRequest(refreshToken),
            headers: _fixture.TenantHeader);
        refreshResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─── Whoami (protected endpoint) ─────────────────────────────────────────

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_Returns401()
    {
        var response = await _fixture.Client.GetAsync("/api/v1/whoami",
            headers: _fixture.TenantHeader);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithValidToken_Returns200()
    {
        var (accessToken, _) = await LoginAsync();

        var response = await _fixture.Client.GetAsync("/api/v1/whoami",
            headers: _fixture.TenantHeader,
            bearer: accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(AuthTestFixture.TherapistEmail);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithTokenForOtherTenant_Returns403()
    {
        // Token für "auth-test" holen
        var (accessToken, _) = await LoginAsync();

        // Denselben Token gegen "auth-other" Subdomain verwenden
        var response = await _fixture.Client.GetAsync("/api/v1/whoami",
            headers: [("X-Tenant-Subdomain", AuthTestFixture.OtherTenantSubdomain)],
            bearer: accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private async Task<(string accessToken, string refreshToken)> LoginAsync()
    {
        var response = await _fixture.Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(AuthTestFixture.TherapistEmail, AuthTestFixture.TestPassword),
            headers: _fixture.TenantHeader);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        return (body!.AccessToken, body.RefreshToken);
    }
}

// ─── Fixture ─────────────────────────────────────────────────────────────────

public sealed class AuthTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private string _tempKeyDir = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public const string TenantSubdomain = "auth-test";
    public const string OtherTenantSubdomain = "auth-other";
    public const string TherapistEmail = "auth-therapeut@test.dev";
    public const string TestPassword = "AuthTest123!";

    public HttpClient Client { get; private set; } = null!;
    public (string, string)[] TenantHeader => [("X-Tenant-Subdomain", TenantSubdomain)];

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _tempKeyDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempKeyDir);

        var masterConnString = _postgres.GetConnectionString();
        var templateConnString = ReplaceDb(_postgres.GetConnectionString(), "{DB}");
        var serverConnString = _postgres.GetConnectionString(); // postgres DB als Server-Level

        // Master-DB aufbauen
        var masterOptions = new DbContextOptionsBuilder<MasterDbContext>()
            .UseNpgsql(masterConnString).Options;
        await using var masterCtx = new MasterDbContext(masterOptions);
        await masterCtx.Database.MigrateAsync();

        // Tenant "auth-test" provisionieren
        var config = BuildConfig(masterConnString, templateConnString, serverConnString);
        var connBuilder = new TenantConnectionStringBuilder(config);
        var factory = new TenantDbContextFactory();
        var provService = new TenantProvisioningService(
            masterCtx, factory, connBuilder, config,
            NullLogger<TenantProvisioningService>.Instance);

        await provService.ProvisionAsync(TenantSubdomain, "Auth Test Praxis", LicenseTier.Trial);
        await provService.ProvisionAsync(OtherTenantSubdomain, "Auth Other Praxis", LicenseTier.Trial);

        // Demo-User in Tenant-DB anlegen
        var tenant = await masterCtx.Tenants.FirstAsync(t => t.Subdomain == TenantSubdomain);
        var connString = connBuilder.Build(tenant.DbConnectionRef);
        var tenantInfo = new TenantInfo(tenant.Id, tenant.Subdomain, connString);
        var tenantOptions = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(connString).Options;
        await using var tenantCtx = new TenantDbContext(tenantOptions, tenantInfo);

        var hasher = new Argon2idPasswordHasher(NullLogger<Argon2idPasswordHasher>.Instance);
        var pwHash = hasher.Hash(TestPassword);

        tenantCtx.Users.Add(new User
        {
            Email = TherapistEmail,
            PasswordHash = pwHash,
            Role = UserRole.Therapeut,
            FirstName = "Auth",
            LastName = "Test",
        });
        await tenantCtx.SaveChangesAsync();

        // WebApplicationFactory konfigurieren
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, cfg) =>
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["RunMigrationsOnStartup"] = "false",
                        ["ConnectionStrings:Master"] = masterConnString,
                        ["ConnectionStrings:TenantTemplate"] = templateConnString,
                        ["Tenancy:ServerConnection"] = serverConnString,
                        ["Tenancy:RootDomains:0"] = "localhost",
                        ["Jwt:PrivateKeyPath"] = Path.Combine(_tempKeyDir, "test.key"),
                        ["Jwt:Issuer"] = "praxis-floeckinger",
                        ["Jwt:Audience"] = "praxis-floeckinger-api",
                        ["Jwt:AccessTokenMinutes"] = "15",
                        ["RateLimit:LoginPermitLimit"] = "10000",
                        ["RateLimit:RefreshPermitLimit"] = "10000",
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

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static IConfiguration BuildConfig(string master, string template, string server) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TenantTemplate"] = template,
                ["Tenancy:ServerConnection"] = server,
            })
            .Build();

    private static string ReplaceDb(string cs, string db)
    {
        var b = new NpgsqlConnectionStringBuilder(cs) { Database = db };
        return b.ToString();
    }
}

// ─── Extension Helpers ────────────────────────────────────────────────────────

internal static class HttpClientAuthExtensions
{
    public static Task<HttpResponseMessage> PostAsJsonAsync<T>(
        this HttpClient client,
        string url,
        T value,
        (string, string)[]? headers = null,
        string? bearer = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(value),
        };
        if (headers is not null)
            foreach (var (k, v) in headers)
                request.Headers.Add(k, v);
        if (bearer is not null)
            request.Headers.Authorization = new("Bearer", bearer);
        return client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> GetAsync(
        this HttpClient client,
        string url,
        (string, string)[]? headers = null,
        string? bearer = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (headers is not null)
            foreach (var (k, v) in headers)
                request.Headers.Add(k, v);
        if (bearer is not null)
            request.Headers.Authorization = new("Bearer", bearer);
        return client.SendAsync(request);
    }
}
