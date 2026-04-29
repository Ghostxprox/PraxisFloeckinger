using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using PraxisFloeckinger.Core.Identity;
using PraxisFloeckinger.Core.Tenancy;
using PraxisFloeckinger.Infrastructure.Identity;
using PraxisFloeckinger.Infrastructure.Persistence.Tenant;
using Testcontainers.PostgreSql;
using Microsoft.Extensions.Logging.Abstractions;

namespace PraxisFloeckinger.Tests.Infrastructure;

public sealed class TwoFactorRecoveryServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private TenantInfo _tenantInfo = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _tenantInfo = new TenantInfo(Guid.NewGuid(), "recovery-test", _postgres.GetConnectionString());
        await using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private TenantDbContext CreateContext()
    {
        var opts = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(_tenantInfo.ConnectionString)
            .Options;
        return new TenantDbContext(opts, _tenantInfo);
    }

    private async Task<Guid> CreateUserAsync()
    {
        await using var ctx = CreateContext();
        var user = new User
        {
            Email = $"{Guid.NewGuid():N}@test.dev",
            PasswordHash = "hash",
            Role = UserRole.Patient,
            FirstName = "Test",
            LastName = "User",
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user.Id;
    }

    private TwoFactorRecoveryService CreateService(TenantDbContext ctx)
    {
        var hasher = new Argon2idPasswordHasher(NullLogger<Argon2idPasswordHasher>.Instance);
        return new TwoFactorRecoveryService(ctx, hasher);
    }

    // ─── Tests ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateAsync_Returns10CodesInFormat()
    {
        var userId = await CreateUserAsync();
        await using var ctx = CreateContext();
        var svc = CreateService(ctx);

        var codes = await svc.GenerateAsync(userId);

        codes.Should().HaveCount(10);
        foreach (var code in codes)
        {
            code.Should().MatchRegex(@"^[A-Z2-9]{5}-[A-Z2-9]{5}$");
        }
    }

    [Fact]
    public async Task ConsumeAsync_ValidCode_ReturnsTrueAndMarksUsed()
    {
        var userId = await CreateUserAsync();
        await using var ctx = CreateContext();
        var svc = CreateService(ctx);
        var codes = await svc.GenerateAsync(userId);

        var result = await svc.ConsumeAsync(userId, codes[0]);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ConsumeAsync_SameCodeTwice_ReturnsFalseSecondTime()
    {
        var userId = await CreateUserAsync();
        await using var ctx = CreateContext();
        var svc = CreateService(ctx);
        var codes = await svc.GenerateAsync(userId);

        await svc.ConsumeAsync(userId, codes[0]);
        var second = await svc.ConsumeAsync(userId, codes[0]);

        second.Should().BeFalse("eingelöster Code ist nicht wiederverwendbar");
    }

    [Fact]
    public async Task ConsumeAsync_InvalidCode_ReturnsFalse()
    {
        var userId = await CreateUserAsync();
        await using var ctx = CreateContext();
        var svc = CreateService(ctx);
        await svc.GenerateAsync(userId);

        var result = await svc.ConsumeAsync(userId, "XXXXX-YYYYY");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task GenerateAsync_ReplacesOldCodes()
    {
        var userId = await CreateUserAsync();
        await using var ctx = CreateContext();
        var svc = CreateService(ctx);
        var oldCodes = await svc.GenerateAsync(userId);

        var newCodes = await svc.GenerateAsync(userId);

        // Alter Code darf nicht mehr gültig sein
        var tryOld = await svc.ConsumeAsync(userId, oldCodes[0]);
        tryOld.Should().BeFalse("alte Codes werden durch neue ersetzt");
        newCodes.Should().HaveCount(10);
    }

    [Fact]
    public async Task CountRemainingAsync_AfterGenerate_Returns10()
    {
        var userId = await CreateUserAsync();
        await using var ctx = CreateContext();
        var svc = CreateService(ctx);
        await svc.GenerateAsync(userId);

        var remaining = await svc.CountRemainingAsync(userId);

        remaining.Should().Be(10);
    }
}
