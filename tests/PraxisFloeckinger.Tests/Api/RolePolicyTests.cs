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
using PraxisFloeckinger.Api.Authentication;
using PraxisFloeckinger.Core.Tenancy;

namespace PraxisFloeckinger.Tests.Api;

/// <summary>
/// Prüft ob alle 8 Rollen-Policies korrekt greifen.
/// Kein Datenbankcontainer erforderlich: Fake-Resolver + Fake-Auth-Handler.
/// Die #if DEBUG Demo-Endpoints dienen als geschützte Testrouten.
/// </summary>
public sealed class RolePolicyTests : IClassFixture<RolePolicyFactory>
{
    private readonly RolePolicyFactory _factory;

    public RolePolicyTests(RolePolicyFactory factory) => _factory = factory;

    private HttpClient Client => _factory.CreateClient();

    private static (string, string)[] TenantHeader => [("X-Tenant-Subdomain", "test")];

    // ─── OnlyPatient ─────────────────────────────────────────────────────────

    [Fact]
    public async Task OnlyPatient_PatientAccessToken_Returns200()
    {
        var resp = await Client.GetAsync("/api/v1/demo/patient-only", TenantHeader,
            role: "Patient", purpose: "access");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OnlyPatient_TherapistAccessToken_Returns403()
    {
        var resp = await Client.GetAsync("/api/v1/demo/patient-only", TenantHeader,
            role: "Therapeut", purpose: "access");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ─── OnlyTherapist ───────────────────────────────────────────────────────

    [Fact]
    public async Task OnlyTherapist_TherapistAccessToken_Returns200()
    {
        var resp = await Client.GetAsync("/api/v1/demo/therapist-only", TenantHeader,
            role: "Therapeut", purpose: "access");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OnlyTherapist_PatientAccessToken_Returns403()
    {
        var resp = await Client.GetAsync("/api/v1/demo/therapist-only", TenantHeader,
            role: "Patient", purpose: "access");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ─── TherapyClinicalAccess ───────────────────────────────────────────────

    [Fact]
    public async Task TherapyClinicalAccess_SupervisorAccessToken_Returns200()
    {
        var resp = await Client.GetAsync("/api/v1/demo/clinical", TenantHeader,
            role: "TherapeutSupervisor", purpose: "access");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TherapyClinicalAccess_SekretariatAccessToken_Returns403()
    {
        var resp = await Client.GetAsync("/api/v1/demo/clinical", TenantHeader,
            role: "Sekretaerin", purpose: "access");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ─── AdminOrSupervisor ───────────────────────────────────────────────────

    [Fact]
    public async Task AdminOrSupervisor_PraxisAdminAccessToken_Returns200()
    {
        var resp = await Client.GetAsync("/api/v1/demo/admin", TenantHeader,
            role: "PraxisAdmin", purpose: "access");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AdminOrSupervisor_TherapistAccessToken_Returns403()
    {
        var resp = await Client.GetAsync("/api/v1/demo/admin", TenantHeader,
            role: "Therapeut", purpose: "access");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ─── MFA-Session-Token wird von Role-Policies abgelehnt ──────────────────

    [Fact]
    public async Task RolePolicy_MfaSessionToken_Returns403()
    {
        // purpose=mfa statt access → alle Rollen-Policies müssen 403 zurückgeben
        var resp = await Client.GetAsync("/api/v1/demo/patient-only", TenantHeader,
            role: "Patient", purpose: "mfa");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}

// ─── Factory ─────────────────────────────────────────────────────────────────

public sealed class RolePolicyFactory : WebApplicationFactory<Program>
{
    private static readonly string SharedKeyPath = CreateSharedJwtKeyFile();

    private static string CreateSharedJwtKeyFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "praxis-role-policy-test-jwt.key");
        if (!File.Exists(path))
        {
            using var rsa = RSA.Create(2048);
            File.WriteAllText(path, rsa.ExportRSAPrivateKeyPem());
        }
        return path;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration(cfg =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RunMigrationsOnStartup"] = "false",
                ["ConnectionStrings:Master"] =
                    "Host=localhost;Database=test_master;Username=test;Password=test",
                ["ConnectionStrings:TenantTemplate"] =
                    "Host=localhost;Database={DB};Username=test;Password=test",
                ["Tenancy:RootDomains:0"] = "localhost",
                ["Jwt:PrivateKeyPath"] = SharedKeyPath,
                ["Jwt:Issuer"] = "test",
                ["Jwt:Audience"] = "test",
                ["Jwt:AccessTokenMinutes"] = "15",
                ["RateLimit:LoginPermitLimit"] = "10000",
                ["RateLimit:RefreshPermitLimit"] = "10000",
                ["Encryption:DataKey"] = "UCtsfxg9zFWOxLxPNzOSMPva3ErIlXMbroDCCc1nBYY=",
            }));

        builder.ConfigureServices(services =>
        {
            // Fake-Resolver: immer denselben Test-Tenant zurückgeben
            var resolverDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ITenantResolver));
            if (resolverDescriptor is not null)
                services.Remove(resolverDescriptor);
            services.AddSingleton<ITenantResolver>(new FixedTenantResolver(
                new TenantInfo(Guid.NewGuid(), "test", "Host=localhost;Database=test;Username=test;Password=test")));

            // Fake-Auth: liest X-Test-Role und X-Test-Purpose aus dem Request
            services.AddAuthentication(RolePolicyTestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, RolePolicyTestAuthHandler>(
                    RolePolicyTestAuthHandler.SchemeName, _ => { });
        });
    }

    private sealed class FixedTenantResolver : ITenantResolver
    {
        private readonly TenantInfo _tenant;
        public FixedTenantResolver(TenantInfo tenant) => _tenant = tenant;
        public Task<TenantInfo?> ResolveAsync(string subdomain, CancellationToken ct = default)
            => Task.FromResult<TenantInfo?>(_tenant);
    }
}

// ─── Fake-Auth-Handler ────────────────────────────────────────────────────────

/// <summary>
/// Liest X-Test-Role und X-Test-Purpose aus dem Request-Header und erstellt
/// daraus die Claims-Identität — kein echtes JWT nötig.
/// </summary>
internal sealed class RolePolicyTestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "RolePolicyTest";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var role    = Request.Headers["X-Test-Role"].FirstOrDefault()    ?? "Patient";
        var purpose = Request.Headers["X-Test-Purpose"].FirstOrDefault() ?? "access";

        var claims = new List<Claim>
        {
            new("sub",     Guid.NewGuid().ToString()),
            new("email",   "test@test.dev"),
            new("role",    role),
            new("purpose", purpose),
        };

        if (Context.Items["TenantContext"] is ITenantContext tenantCtx)
        {
            claims.Add(new("tenant_id",  tenantCtx.TenantId.ToString()));
            claims.Add(new("tenant_sub", tenantCtx.Subdomain));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}

// ─── Extension ────────────────────────────────────────────────────────────────

internal static class HttpClientRolePolicyExtensions
{
    public static Task<HttpResponseMessage> GetAsync(
        this HttpClient client,
        string url,
        (string, string)[]? headers,
        string role,
        string purpose = "access")
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (headers is not null)
            foreach (var (k, v) in headers)
                request.Headers.Add(k, v);
        request.Headers.Add("X-Test-Role",    role);
        request.Headers.Add("X-Test-Purpose", purpose);
        return client.SendAsync(request);
    }
}
