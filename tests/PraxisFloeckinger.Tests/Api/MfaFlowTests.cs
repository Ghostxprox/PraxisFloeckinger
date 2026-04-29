using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using OtpNet;
using PraxisFloeckinger.Api.Authentication;
using PraxisFloeckinger.Core.Identity;
using PraxisFloeckinger.Core.Tenancy;
using PraxisFloeckinger.Infrastructure.Cryptography;
using PraxisFloeckinger.Infrastructure.Identity;
using PraxisFloeckinger.Infrastructure.Persistence.Master;
using PraxisFloeckinger.Infrastructure.Persistence.Tenant;
using PraxisFloeckinger.Infrastructure.Tenancy;
using Testcontainers.PostgreSql;

namespace PraxisFloeckinger.Tests.Api;

/// <summary>
/// End-to-End-Tests für den zweistufigen Login-Flow und alle MFA-Endpoints.
/// Alle Tests teilen einen Container (IClassFixture) für Performanz.
/// </summary>
public sealed class MfaFlowTests : IClassFixture<MfaTestFixture>
{
    private readonly MfaTestFixture _fixture;

    public MfaFlowTests(MfaTestFixture fixture) => _fixture = fixture;

    // ─── Login zweistufig ─────────────────────────────────────────────────────

    [Fact]
    public async Task Login_PatientWithoutMfa_Returns200WithFullToken()
    {
        var response = await _fixture.Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(MfaTestFixture.PatientEmail, MfaTestFixture.TestPassword),
            headers: _fixture.TenantHeader);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        body!.Status.Should().Be("ok");
        body.AccessToken.Should().NotBeNullOrEmpty();
        body.MfaSessionToken.Should().BeNull();
    }

    [Fact]
    public async Task Login_TherapistWithoutMfa_ReturnsMfaSetupRequired()
    {
        var response = await _fixture.Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(MfaTestFixture.TherapistNoMfaEmail, MfaTestFixture.TestPassword),
            headers: _fixture.TenantHeader);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        body!.Status.Should().Be("mfa_setup_required");
        body.MfaSessionToken.Should().NotBeNullOrEmpty();
        body.AccessToken.Should().BeNull();
    }

    // ─── MFA Setup ────────────────────────────────────────────────────────────

    [Fact]
    public async Task MfaSetup_GeneratesQrCodeAndSecret()
    {
        var mfaToken = await LoginGetMfaTokenAsync(MfaTestFixture.TherapistNoMfaEmail);

        var response = await _fixture.Client.PostAsync(
            "/api/v1/auth/mfa/setup", null,
            headers: _fixture.TenantHeader,
            bearer: mfaToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("qrCodeBase64").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("secret").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("otpauthUri").GetString().Should().StartWith("otpauth://totp/");
    }

    [Fact]
    public async Task MfaSetupConfirm_ValidCode_EnablesMfaAndReturnsRecoveryCodes()
    {
        var mfaToken = await LoginGetMfaTokenAsync(MfaTestFixture.TherapistSetupEmail);
        var secret = await SetupMfaGetSecretAsync(mfaToken);
        var code = ComputeTotpCode(secret);

        var response = await _fixture.Client.PostAsJsonAsync(
            "/api/v1/auth/mfa/setup/confirm",
            new MfaVerifyRequest(code),
            headers: _fixture.TenantHeader,
            bearer: mfaToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var codes = doc.RootElement.GetProperty("recoveryCodes").EnumerateArray().ToList();
        codes.Should().HaveCount(10);
        codes[0].GetString().Should().MatchRegex(@"^[A-Z2-9]{5}-[A-Z2-9]{5}$");
        doc.RootElement.GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task MfaSetupConfirm_InvalidCode_DoesNotEnable()
    {
        var mfaToken = await LoginGetMfaTokenAsync(MfaTestFixture.TherapistSetup2Email);
        await SetupMfaGetSecretAsync(mfaToken);

        var response = await _fixture.Client.PostAsJsonAsync(
            "/api/v1/auth/mfa/setup/confirm",
            new MfaVerifyRequest("000000"),
            headers: _fixture.TenantHeader,
            bearer: mfaToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─── MFA Verify (nach Login mit aktiviertem 2FA) ──────────────────────────

    [Fact]
    public async Task Login_TherapistWithMfa_ReturnsMfaRequired()
    {
        var response = await _fixture.Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(MfaTestFixture.TherapistWithMfaEmail, MfaTestFixture.TestPassword),
            headers: _fixture.TenantHeader);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        body!.Status.Should().Be("mfa_required");
        body.MfaSessionToken.Should().NotBeNullOrEmpty();
        body.AccessToken.Should().BeNull();
    }

    [Fact]
    public async Task MfaVerify_ValidCode_ReturnsFullToken()
    {
        var loginResponse = await _fixture.Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(MfaTestFixture.TherapistWithMfaEmail, MfaTestFixture.TestPassword),
            headers: _fixture.TenantHeader);
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        var mfaToken = loginBody!.MfaSessionToken!;

        var code = ComputeTotpCode(MfaTestFixture.TherapistWithMfaSecret);
        var response = await _fixture.Client.PostAsJsonAsync(
            "/api/v1/auth/mfa/verify",
            new MfaVerifyRequest(code),
            headers: _fixture.TenantHeader,
            bearer: mfaToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        body!.Status.Should().Be("ok");
        body.AccessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task MfaVerify_InvalidCode_Returns401()
    {
        var loginResponse = await _fixture.Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(MfaTestFixture.TherapistWithMfaEmail, MfaTestFixture.TestPassword),
            headers: _fixture.TenantHeader);
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        var mfaToken = loginBody!.MfaSessionToken!;

        var response = await _fixture.Client.PostAsJsonAsync(
            "/api/v1/auth/mfa/verify",
            new MfaVerifyRequest("000000"),
            headers: _fixture.TenantHeader,
            bearer: mfaToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─── Recovery-Codes ───────────────────────────────────────────────────────

    [Fact]
    public async Task MfaRecover_ValidCode_ReturnsFullTokenAndSetsMustRotate()
    {
        var loginResponse = await _fixture.Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(MfaTestFixture.TherapistWithMfaEmail, MfaTestFixture.TestPassword),
            headers: _fixture.TenantHeader);
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        var mfaToken = loginBody!.MfaSessionToken!;

        var recoveryCode = _fixture.RecoveryCodes[0];
        var response = await _fixture.Client.PostAsJsonAsync(
            "/api/v1/auth/mfa/recover",
            new MfaRecoverRequest(recoveryCode),
            headers: _fixture.TenantHeader,
            bearer: mfaToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        body!.Status.Should().Be("ok");
        body.AccessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task MfaRecover_AlreadyUsedCode_Returns401()
    {
        var loginResponse = await _fixture.Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(MfaTestFixture.TherapistWithMfaEmail, MfaTestFixture.TestPassword),
            headers: _fixture.TenantHeader);
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        var mfaToken = loginBody!.MfaSessionToken!;

        // Zweiten, noch nicht verwendeten Code einlösen (Code 0 wurde in vorherigem Test verwendet)
        var usedCode = _fixture.RecoveryCodes[1];
        await _fixture.Client.PostAsJsonAsync("/api/v1/auth/mfa/recover",
            new MfaRecoverRequest(usedCode),
            headers: _fixture.TenantHeader, bearer: mfaToken);

        // Wieder einloggen und alten Code nochmal versuchen
        var loginResponse2 = await _fixture.Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(MfaTestFixture.TherapistWithMfaEmail, MfaTestFixture.TestPassword),
            headers: _fixture.TenantHeader);
        var loginBody2 = await loginResponse2.Content.ReadFromJsonAsync<LoginResponse>();
        var mfaToken2 = loginBody2!.MfaSessionToken!;

        var response = await _fixture.Client.PostAsJsonAsync(
            "/api/v1/auth/mfa/recover",
            new MfaRecoverRequest(usedCode),
            headers: _fixture.TenantHeader,
            bearer: mfaToken2);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─── MFA Disable ─────────────────────────────────────────────────────────

    [Fact]
    public async Task MfaDisable_PatientWithPwdAndCode_Disables()
    {
        // Patient ohne 2FA kann auch 2FA deaktivieren (Endpoint erlaubt es, deaktiviert ist trivial)
        var (accessToken, _) = await LoginPatientAsync();

        var response = await _fixture.Client.PostAsJsonAsync(
            "/api/v1/auth/mfa/disable",
            new MfaDisableRequest(MfaTestFixture.TestPassword, "000000"),
            headers: _fixture.TenantHeader,
            bearer: accessToken);

        // Patient hat TotpEnabled=false, TotpSecret=null → Code-Prüfung wird übersprungen
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task MfaDisable_TherapistRole_Returns403()
    {
        var code = ComputeTotpCode(MfaTestFixture.TherapistWithMfaSecret);
        var loginResponse = await _fixture.Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(MfaTestFixture.TherapistWithMfaEmail, MfaTestFixture.TestPassword),
            headers: _fixture.TenantHeader);
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        var mfaToken = loginBody!.MfaSessionToken!;
        var verifyResp = await _fixture.Client.PostAsJsonAsync("/api/v1/auth/mfa/verify",
            new MfaVerifyRequest(ComputeTotpCode(MfaTestFixture.TherapistWithMfaSecret)),
            headers: _fixture.TenantHeader, bearer: mfaToken);
        var accessBody = await verifyResp.Content.ReadFromJsonAsync<LoginResponse>();
        var accessToken = accessBody!.AccessToken!;

        var response = await _fixture.Client.PostAsJsonAsync(
            "/api/v1/auth/mfa/disable",
            new MfaDisableRequest(MfaTestFixture.TestPassword, code),
            headers: _fixture.TenantHeader,
            bearer: accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private async Task<string> LoginGetMfaTokenAsync(string email)
    {
        var response = await _fixture.Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(email, MfaTestFixture.TestPassword),
            headers: _fixture.TenantHeader);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.MfaSessionToken!;
    }

    private async Task<string> SetupMfaGetSecretAsync(string mfaToken)
    {
        var response = await _fixture.Client.PostAsync(
            "/api/v1/auth/mfa/setup", null,
            headers: _fixture.TenantHeader, bearer: mfaToken);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("secret").GetString()!;
    }

    private async Task<(string accessToken, string refreshToken)> LoginPatientAsync()
    {
        var response = await _fixture.Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(MfaTestFixture.PatientEmail, MfaTestFixture.TestPassword),
            headers: _fixture.TenantHeader);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        return (body!.AccessToken!, body.RefreshToken!);
    }

    private static string ComputeTotpCode(string base32Secret)
    {
        var secretBytes = Base32Encoding.ToBytes(base32Secret);
        var totp = new Totp(secretBytes, step: 30, mode: OtpHashMode.Sha1, totpSize: 6);
        return totp.ComputeTotp();
    }
}

// ─── Fixture ─────────────────────────────────────────────────────────────────

public sealed class MfaTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private string _tempKeyDir = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public const string TenantSubdomain = "mfa-test";
    public const string TestPassword = "MfaTest123!";
    public const string PatientEmail = "patient-mfa@test.dev";
    public const string TherapistNoMfaEmail = "therapeut-nomfa@test.dev";
    public const string TherapistSetupEmail = "therapeut-setup@test.dev";
    public const string TherapistSetup2Email = "therapeut-setup2@test.dev";
    public const string TherapistWithMfaEmail = "therapeut-withmfa@test.dev";
    public const string TherapistWithMfaSecret = "JBSWY3DPEHPK3PXP";
    public const string EncryptionKey = "UCtsfxg9zFWOxLxPNzOSMPva3ErIlXMbroDCCc1nBYY=";

    public HttpClient Client { get; private set; } = null!;
    public (string, string)[] TenantHeader => [("X-Tenant-Subdomain", TenantSubdomain)];
    public IReadOnlyList<string> RecoveryCodes { get; private set; } = [];

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _tempKeyDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempKeyDir);

        var masterConnString = _postgres.GetConnectionString();
        var templateConnString = ReplaceDb(_postgres.GetConnectionString(), "{DB}");

        // Master-DB migrieren
        var masterOptions = new DbContextOptionsBuilder<MasterDbContext>()
            .UseNpgsql(masterConnString).Options;
        await using var masterCtx = new MasterDbContext(masterOptions);
        await masterCtx.Database.MigrateAsync();

        // Tenant provisionieren
        var config = BuildConfig(masterConnString, templateConnString);
        var connBuilder = new TenantConnectionStringBuilder(config);
        var factory = new TenantDbContextFactory();
        var provService = new TenantProvisioningService(
            masterCtx, factory, connBuilder, config,
            NullLogger<TenantProvisioningService>.Instance);
        await provService.ProvisionAsync(TenantSubdomain, "MFA Test Praxis", LicenseTier.Trial);

        // Tenant-DB befüllen
        var tenant = await masterCtx.Tenants.FirstAsync(t => t.Subdomain == TenantSubdomain);
        var connString = connBuilder.Build(tenant.DbConnectionRef);
        var tenantInfo = new TenantInfo(tenant.Id, tenant.Subdomain, connString);
        var tenantOptions = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(connString).Options;
        await using var tenantCtx = new TenantDbContext(tenantOptions, tenantInfo);

        var hasher = new Argon2idPasswordHasher(NullLogger<Argon2idPasswordHasher>.Instance);
        var pwHash = hasher.Hash(TestPassword);

        // Encryptor für TOTP-Secret
        var encConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Encryption:DataKey"] = EncryptionKey,
            })
            .Build();
        var encryptor = new AesGcmFieldEncryptor(encConfig);

        // Patient (kein 2FA)
        tenantCtx.Users.Add(new User
        {
            Email = PatientEmail,
            PasswordHash = pwHash,
            Role = UserRole.Patient,
            FirstName = "Pat",
            LastName = "Ient",
        });

        // Therapeut ohne 2FA (muss Setup durchlaufen)
        tenantCtx.Users.Add(new User
        {
            Email = TherapistNoMfaEmail,
            PasswordHash = pwHash,
            Role = UserRole.Therapeut,
            FirstName = "No",
            LastName = "Mfa",
        });

        // Therapeut für Setup-Test (eigener User, damit Setup idempotent ist)
        tenantCtx.Users.Add(new User
        {
            Email = TherapistSetupEmail,
            PasswordHash = pwHash,
            Role = UserRole.Therapeut,
            FirstName = "Setup",
            LastName = "Test",
        });

        // Therapeut für Setup-Confirm-Invalid-Test
        tenantCtx.Users.Add(new User
        {
            Email = TherapistSetup2Email,
            PasswordHash = pwHash,
            Role = UserRole.Therapeut,
            FirstName = "Setup2",
            LastName = "Test",
        });

        // Therapeut mit aktiviertem 2FA (fixes Secret für Smoke-Test)
        var withMfa = new User
        {
            Email = TherapistWithMfaEmail,
            PasswordHash = pwHash,
            Role = UserRole.Therapeut,
            FirstName = "With",
            LastName = "Mfa",
            TotpEnabled = true,
            TotpSecret = encryptor.Encrypt(TherapistWithMfaSecret),
        };
        tenantCtx.Users.Add(withMfa);
        await tenantCtx.SaveChangesAsync();

        // Recovery-Codes für TherapistWithMfa vorher generieren
        var recoveryHasher = new Argon2idPasswordHasher(NullLogger<Argon2idPasswordHasher>.Instance);
        var recSvc = new TwoFactorRecoveryService(tenantCtx, recoveryHasher);
        var codes = await recSvc.GenerateAsync(withMfa.Id);
        RecoveryCodes = codes;

        // WebApplicationFactory
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
                        ["Tenancy:ServerConnection"] = masterConnString,
                        ["Tenancy:RootDomains:0"] = "localhost",
                        ["Jwt:PrivateKeyPath"] = Path.Combine(_tempKeyDir, "test.key"),
                        ["Jwt:Issuer"] = "praxis-floeckinger",
                        ["Jwt:Audience"] = "praxis-floeckinger-api",
                        ["Jwt:AccessTokenMinutes"] = "15",
                        ["RateLimit:LoginPermitLimit"] = "10000",
                        ["RateLimit:RefreshPermitLimit"] = "10000",
                        ["Encryption:DataKey"] = EncryptionKey,
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
                ["Tenancy:ServerConnection"] = master,
            })
            .Build();

    private static string ReplaceDb(string cs, string db)
    {
        var b = new NpgsqlConnectionStringBuilder(cs) { Database = db };
        return b.ToString();
    }
}

// ─── Extension: PostAsync mit null-Body ──────────────────────────────────────

internal static class HttpClientMfaExtensions
{
    public static Task<HttpResponseMessage> PostAsync(
        this HttpClient client,
        string url,
        HttpContent? content,
        (string, string)[]? headers = null,
        string? bearer = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = content ?? new StringContent(""),
        };
        if (headers is not null)
            foreach (var (k, v) in headers)
                request.Headers.Add(k, v);
        if (bearer is not null)
            request.Headers.Authorization = new("Bearer", bearer);
        return client.SendAsync(request);
    }
}
