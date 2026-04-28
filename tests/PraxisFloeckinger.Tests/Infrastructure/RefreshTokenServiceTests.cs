using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PraxisFloeckinger.Core.Identity;
using PraxisFloeckinger.Core.Tenancy;
using PraxisFloeckinger.Infrastructure.Identity;
using PraxisFloeckinger.Infrastructure.Persistence.Tenant;
using Testcontainers.PostgreSql;

namespace PraxisFloeckinger.Tests.Infrastructure;

public sealed class RefreshTokenServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private TenantInfo _tenantInfo = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _tenantInfo = new TenantInfo(Guid.NewGuid(), "rt-test", _postgres.GetConnectionString());
        await using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private TenantDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(_tenantInfo.ConnectionString)
            .Options;
        return new TenantDbContext(options, _tenantInfo);
    }

    private async Task<User> CreateUserAsync(UserRole role = UserRole.Therapeut)
    {
        await using var ctx = CreateContext();
        var user = new User
        {
            Email = $"{Guid.NewGuid():N}@test.dev",
            PasswordHash = "hash",
            Role = role,
            FirstName = "Test",
            LastName = "User",
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user;
    }

    // ─── Tests ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task IssueAsync_StoresOnlyHashNotRawToken()
    {
        await using var ctx = CreateContext();
        var user = await CreateUserAsync();
        var service = new RefreshTokenService(ctx, NullLogger<RefreshTokenService>.Instance);

        var (rawToken, entity) = await service.IssueAsync(user, false, "127.0.0.1", "TestAgent");

        var expectedHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(rawToken)));
        entity.TokenHash.Should().Be(expectedHash,
            "nur SHA-256-Hash darf gespeichert werden");
        entity.TokenHash.Should().NotBe(rawToken, "Rohwert darf nicht gespeichert sein");
        entity.TokenHash.Length.Should().Be(64, "SHA-256-Hex hat 64 Zeichen");
    }

    [Fact]
    public async Task ValidateAsync_ValidToken_ReturnsEntity()
    {
        await using var ctx = CreateContext();
        var user = await CreateUserAsync();
        var service = new RefreshTokenService(ctx, NullLogger<RefreshTokenService>.Instance);

        var (rawToken, issued) = await service.IssueAsync(user, false, "127.0.0.1", "Agent");

        var result = await service.ValidateAsync(rawToken);

        result.Should().NotBeNull();
        result!.Id.Should().Be(issued.Id);
    }

    [Fact]
    public async Task ValidateAsync_RevokedToken_ReturnsNull()
    {
        await using var ctx = CreateContext();
        var user = await CreateUserAsync();
        var service = new RefreshTokenService(ctx, NullLogger<RefreshTokenService>.Instance);

        var (rawToken, _) = await service.IssueAsync(user, false, "127.0.0.1", "Agent");
        await service.RevokeAsync(rawToken, "logout");

        var result = await service.ValidateAsync(rawToken);
        result.Should().BeNull("widerrufener Token ist ungültig");
    }

    [Fact]
    public async Task RevokeAsync_SetsRevokedAtAndReason()
    {
        await using var ctx = CreateContext();
        var user = await CreateUserAsync();
        var service = new RefreshTokenService(ctx, NullLogger<RefreshTokenService>.Instance);

        var (rawToken, entity) = await service.IssueAsync(user, false, "127.0.0.1", "Agent");
        var before = DateTimeOffset.UtcNow;
        await service.RevokeAsync(rawToken, "test-reason");

        await using var readCtx = CreateContext();
        var saved = await readCtx.RefreshTokens.IgnoreQueryFilters()
            .FirstAsync(r => r.Id == entity.Id);

        saved.RevokedAt.Should().NotBeNull();
        saved.RevokedAt!.Value.Should().BeOnOrAfter(before.AddSeconds(-1));
        saved.RevokedReason.Should().Be("test-reason");
    }

    [Fact]
    public async Task IssueAsync_PatientRole_24hLifetime()
    {
        await using var ctx = CreateContext();
        var user = await CreateUserAsync(UserRole.Patient);
        var service = new RefreshTokenService(ctx, NullLogger<RefreshTokenService>.Instance);

        var before = DateTimeOffset.UtcNow;
        var (_, entity) = await service.IssueAsync(user, false, "127.0.0.1", "Agent");

        entity.ExpiresAt.Should().BeCloseTo(before.AddHours(24), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task IssueAsync_TherapeutRole_8hLifetime()
    {
        await using var ctx = CreateContext();
        var user = await CreateUserAsync(UserRole.Therapeut);
        var service = new RefreshTokenService(ctx, NullLogger<RefreshTokenService>.Instance);

        var before = DateTimeOffset.UtcNow;
        var (_, entity) = await service.IssueAsync(user, false, "127.0.0.1", "Agent");

        entity.ExpiresAt.Should().BeCloseTo(before.AddHours(8), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task IssueAsync_RememberMe_30dLifetime()
    {
        await using var ctx = CreateContext();
        var user = await CreateUserAsync(UserRole.Patient);
        var service = new RefreshTokenService(ctx, NullLogger<RefreshTokenService>.Instance);

        var before = DateTimeOffset.UtcNow;
        var (_, entity) = await service.IssueAsync(user, rememberMe: true, "127.0.0.1", "Agent");

        entity.ExpiresAt.Should().BeCloseTo(before.AddDays(30), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RevokeAllForUserAsync_RevokesAllActiveTokens()
    {
        await using var ctx = CreateContext();
        var user = await CreateUserAsync();
        var service = new RefreshTokenService(ctx, NullLogger<RefreshTokenService>.Instance);

        var (t1, _) = await service.IssueAsync(user, false, "127.0.0.1", "A");
        var (t2, _) = await service.IssueAsync(user, false, "127.0.0.1", "B");

        await service.RevokeAllForUserAsync(user.Id, "admin-revoke");

        (await service.ValidateAsync(t1)).Should().BeNull();
        (await service.ValidateAsync(t2)).Should().BeNull();
    }
}
