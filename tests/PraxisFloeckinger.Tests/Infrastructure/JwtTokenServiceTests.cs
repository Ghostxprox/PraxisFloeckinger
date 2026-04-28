using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using PraxisFloeckinger.Core.Identity;
using PraxisFloeckinger.Core.Tenancy;
using PraxisFloeckinger.Infrastructure.Identity;

namespace PraxisFloeckinger.Tests.Infrastructure;

/// <summary>
/// Unit-Tests für JwtTokenService: kein Datenbankcontainer nötig.
/// JwtSigningKeyProvider erzeugt Key in einem temporären Verzeichnis.
/// </summary>
public sealed class JwtTokenServiceTests : IDisposable
{
    private readonly string _tempKeyDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly JwtSigningKeyProvider _keyProvider;
    private readonly JwtTokenService _service;

    private static readonly User TestUser = new()
    {
        Email = "test@jwt.test",
        PasswordHash = "hash",
        Role = UserRole.Therapeut,
        FirstName = "JWT",
        LastName = "Test",
    };

    private static readonly TenantInfo TestTenant =
        new(Guid.NewGuid(), "test-tenant", "connstring");

    public JwtTokenServiceTests()
    {
        Directory.CreateDirectory(_tempKeyDir);
        var keyPath = Path.Combine(_tempKeyDir, "test.key");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:PrivateKeyPath"] = keyPath,
                ["Jwt:Issuer"] = "test-issuer",
                ["Jwt:Audience"] = "test-audience",
                ["Jwt:AccessTokenMinutes"] = "15",
            })
            .Build();

        var env = new FakeDevEnvironment();
        _keyProvider = new JwtSigningKeyProvider(
            config, env, NullLogger<JwtSigningKeyProvider>.Instance);
        _service = new JwtTokenService(_keyProvider, config);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempKeyDir))
            Directory.Delete(_tempKeyDir, recursive: true);
    }

    // ─── Tests ───────────────────────────────────────────────────────────────

    [Fact]
    public void IssueAccessToken_ContainsAllRequiredClaims()
    {
        var token = _service.IssueAccessToken(TestUser, TestTenant);

        var handler = new JwtSecurityTokenHandler();
        var parsed = handler.ReadJwtToken(token);

        parsed.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Sub
            && c.Value == TestUser.Id.ToString());
        parsed.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Email
            && c.Value == TestUser.Email);
        parsed.Claims.Should().Contain(c => c.Type == "role"
            && c.Value == "Therapeut");
        parsed.Claims.Should().Contain(c => c.Type == "tenant_id"
            && c.Value == TestTenant.TenantId.ToString());
        parsed.Claims.Should().Contain(c => c.Type == "tenant_sub"
            && c.Value == TestTenant.Subdomain);
        parsed.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Jti);
    }

    [Fact]
    public void IssueAccessToken_TokenIsValidlySignedByPublicKey()
    {
        var token = _service.IssueAccessToken(TestUser, TestTenant);

        var handler = new JwtSecurityTokenHandler();
        var validationParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "test-issuer",
            ValidateAudience = true,
            ValidAudience = "test-audience",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            IssuerSigningKey = _keyProvider.ValidationKey,
            ValidateIssuerSigningKey = true,
        };

        var act = () => handler.ValidateToken(token, validationParams, out _);
        act.Should().NotThrow("ein gültig signierter Token muss Validierung bestehen");
    }

    [Fact]
    public void IssueAccessToken_TokenHasCorrectLifetime()
    {
        var before = DateTimeOffset.UtcNow;
        var token = _service.IssueAccessToken(TestUser, TestTenant);
        var after = DateTimeOffset.UtcNow;

        var parsed = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var exp = new DateTimeOffset(parsed.ValidTo, TimeSpan.Zero);

        exp.Should().BeOnOrAfter(before.AddMinutes(14));
        exp.Should().BeOnOrBefore(after.AddMinutes(16));
    }

    [Fact]
    public void IssueAccessToken_RejectedWithWrongIssuer()
    {
        var token = _service.IssueAccessToken(TestUser, TestTenant);

        var handler = new JwtSecurityTokenHandler();
        var validationParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "wrong-issuer",
            ValidateAudience = false,
            ValidateLifetime = false,
            IssuerSigningKey = _keyProvider.ValidationKey,
        };

        var act = () => handler.ValidateToken(token, validationParams, out _);
        act.Should().Throw<SecurityTokenInvalidIssuerException>();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private sealed class FakeDevEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = "/";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
