using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PraxisFloeckinger.Core.Tenancy;

namespace PraxisFloeckinger.Tests.Api;

/// <summary>
/// Integrationstests für <see cref="PraxisFloeckinger.Api.Tenancy.TenantResolverMiddleware"/>
/// via <see cref="WebApplicationFactory{TEntryPoint}"/>.
/// Der echte <see cref="ITenantResolver"/> wird durch einfache Test-Doubles ersetzt,
/// sodass kein Datenbankcontainer benötigt wird.
/// Die JWT-Authentifizierung wird durch einen Fake-Handler ersetzt, der Tenant-Claims
/// direkt aus <c>HttpContext.Items["TenantContext"]</c> liest.
/// </summary>
public sealed class TenantResolverMiddlewareTests
{
    // ─── Hilfsmethode ────────────────────────────────────────────────────────

    private static TestFactory CreateFactory(
        ITenantResolver resolver,
        string environment = "Testing")
        => new(resolver, environment);

    // ─── Tests ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Request_OhneSubdomain_PassesThrough_NoTenantRequired()
    {
        using var factory = CreateFactory(new NeverCalledResolver());
        var client = factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Request_MitDevHeader_ResolvesTenant_Returns200()
    {
        var tenantInfo = new TenantInfo(Guid.NewGuid(), "floeckinger", "connstring");
        using var factory = CreateFactory(new FixedResolver(tenantInfo), "Development");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Subdomain", "floeckinger");

        var response = await client.GetAsync("/api/v1/whoami");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("floeckinger");
        body.Should().Contain(tenantInfo.TenantId.ToString());
    }

    [Fact]
    public async Task Request_MitDevHeader_ImNichtDevelopmentMode_IgnoriertHeader_Returns400()
    {
        // Im Testing-Modus wird der Dev-Header ignoriert → kein Tenant → 400
        var tenantInfo = new TenantInfo(Guid.NewGuid(), "floeckinger", "connstring");
        using var factory = CreateFactory(new FixedResolver(tenantInfo), "Testing");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Subdomain", "floeckinger");

        // Host ist localhost → kein Tenant → RequireTenantFilter greift
        var response = await client.GetAsync("/api/v1/whoami");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Request_MitUnbekannterSubdomain_Returns404WithProblemDetails()
    {
        using var factory = CreateFactory(new NullResolver(), "Development");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Subdomain", "existiert-nicht");

        var response = await client.GetAsync("/api/v1/whoami");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Tenant not found");
    }

    [Fact]
    public async Task Request_MitInaktivemTenant_Returns404()
    {
        // Aus Sicht der Middleware: resolver gibt null zurück (egal ob unbekannt oder inaktiv)
        using var factory = CreateFactory(new NullResolver(), "Development");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Subdomain", "inactive");

        var response = await client.GetAsync("/api/v1/whoami");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Request_AnRequireTenantEndpoint_OhneTenant_Returns400WithProblemDetails()
    {
        using var factory = CreateFactory(new NeverCalledResolver());
        var client = factory.CreateClient();

        // Kein Header, localhost → kein Tenant → RequireTenantFilter
        var response = await client.GetAsync("/api/v1/whoami");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Tenant required");
    }

    // ─── WebApplicationFactory ────────────────────────────────────────────────

    private sealed class TestFactory : WebApplicationFactory<Program>
    {
        private readonly ITenantResolver _resolver;
        private readonly string _environment;

        // Einmaliges RSA-Schlüsselpaar für alle Middleware-Tests (JwtSigningKeyProvider).
        private static readonly string SharedKeyPath = CreateSharedJwtKeyFile();

        private static string CreateSharedJwtKeyFile()
        {
            var path = Path.Combine(Path.GetTempPath(), "praxis-tenant-mw-test-jwt.key");
            if (!File.Exists(path))
            {
                using var rsa = RSA.Create(2048);
                File.WriteAllText(path, rsa.ExportRSAPrivateKeyPem());
            }
            return path;
        }

        public TestFactory(ITenantResolver resolver, string environment)
        {
            _resolver = resolver;
            _environment = environment;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(_environment);

            builder.ConfigureAppConfiguration(config =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Master"] =
                        "Host=localhost;Database=test_master;Username=test;Password=test",
                    ["ConnectionStrings:TenantTemplate"] =
                        "Host=localhost;Database={DB};Username=test;Password=test",
                    ["Tenancy:RootDomains:0"] = "praxis-floeckinger.at",
                    // Migrations in Testumgebung nie ausführen (kein echter DB-Container)
                    ["RunMigrationsOnStartup"] = "false",
                    // JWT-Key für JwtSigningKeyProvider (Singleton muss initialisiert werden können)
                    ["Jwt:PrivateKeyPath"] = SharedKeyPath,
                    ["Jwt:Issuer"] = "test",
                    ["Jwt:Audience"] = "test",
                    ["Jwt:AccessTokenMinutes"] = "15",
                    // Rate-Limiting in Tests hochschrauben
                    ["RateLimit:LoginPermitLimit"] = "10000",
                    ["RateLimit:RefreshPermitLimit"] = "10000",
                    ["Encryption:DataKey"] = "UCtsfxg9zFWOxLxPNzOSMPva3ErIlXMbroDCCc1nBYY=",
                }));

            builder.ConfigureServices(services =>
            {
                // Echten TenantResolver durch Test-Double ersetzen
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(ITenantResolver));
                if (descriptor is not null)
                    services.Remove(descriptor);

                services.AddSingleton(_resolver);

                // JWT-Auth durch Fake-Handler ersetzen, der Tenant-Claims aus HttpContext.Items liest.
                // Dadurch testen wir die TenantResolverMiddleware ohne echtes JWT.
                services.AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                        TestAuthHandler.SchemeName, _ => { });
            });
        }
    }

    // ─── Fake-Auth-Handler ────────────────────────────────────────────────────

    /// <summary>
    /// Authentifiziert jeden Request als Test-User. Liest Tenant-Daten aus
    /// <c>HttpContext.Items["TenantContext"]</c> und fügt sie als Claims ein,
    /// damit <c>/api/v1/whoami</c> tenant_id / tenant_sub zurückgeben kann.
    /// </summary>
    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim>
            {
                new("sub", Guid.NewGuid().ToString()),
                new("email", "test@test.dev"),
                new("role", "Therapeut"),
            };

            if (Context.Items["TenantContext"] is ITenantContext tenantCtx)
            {
                claims.Add(new("tenant_id", tenantCtx.TenantId.ToString()));
                claims.Add(new("tenant_sub", tenantCtx.Subdomain));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            var ticket = new AuthenticationTicket(principal, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    // ─── Test-Doubles ─────────────────────────────────────────────────────────

    private sealed class FixedResolver : ITenantResolver
    {
        private readonly TenantInfo? _result;
        public FixedResolver(TenantInfo? result) => _result = result;
        public Task<TenantInfo?> ResolveAsync(string subdomain, CancellationToken ct = default)
            => Task.FromResult(_result);
    }

    private sealed class NullResolver : ITenantResolver
    {
        public Task<TenantInfo?> ResolveAsync(string subdomain, CancellationToken ct = default)
            => Task.FromResult<TenantInfo?>(null);
    }

    private sealed class NeverCalledResolver : ITenantResolver
    {
        public Task<TenantInfo?> ResolveAsync(string subdomain, CancellationToken ct = default)
            => throw new InvalidOperationException(
                "TenantResolver wurde unerwartet aufgerufen — kein Subdomain erwartet.");
    }
}
