using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PraxisFloeckinger.Core.Identity;
using PraxisFloeckinger.Core.Tenancy;
using PraxisFloeckinger.Infrastructure.Identity;
using PraxisFloeckinger.Infrastructure.Persistence.Tenant;
using Testcontainers.PostgreSql;

namespace PraxisFloeckinger.Tests.Infrastructure;

public sealed class AccountLockoutServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private TenantInfo _tenantInfo = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _tenantInfo = new TenantInfo(Guid.NewGuid(), "lockout-test", _postgres.GetConnectionString());
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

    private AccountLockoutService CreateService(TenantDbContext ctx, int maxAttempts = 5, int lockoutMinutes = 15)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Lockout:MaxAttempts"]    = maxAttempts.ToString(),
                ["Auth:Lockout:LockoutMinutes"] = lockoutMinutes.ToString(),
            })
            .Build();
        return new AccountLockoutService(ctx, config);
    }

    private async Task<User> CreateUserAsync()
    {
        await using var ctx = CreateContext();
        var user = new User
        {
            Email        = $"{Guid.NewGuid():N}@lockout.dev",
            PasswordHash = "hash",
            Role         = UserRole.Patient,
            FirstName    = "Lock",
            LastName     = "Test",
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user;
    }

    // ─── Tests ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task IsLockedOut_NewUser_ReturnsFalse()
    {
        var user = await CreateUserAsync();
        await using var ctx = CreateContext();
        var svc = CreateService(ctx);

        var locked = svc.IsLockedOut(user);

        locked.Should().BeFalse();
    }

    [Fact]
    public async Task RegisterFailure_BelowMaxAttempts_DoesNotLock()
    {
        var user = await CreateUserAsync();
        await using var ctx = CreateContext();
        var svc = CreateService(ctx, maxAttempts: 3);

        // 2 Fehlversuche — noch keine Sperre
        await svc.RegisterFailureAsync(user);
        await svc.RegisterFailureAsync(user);

        svc.IsLockedOut(user).Should().BeFalse();
        user.FailedLoginAttempts.Should().Be(2);
    }

    [Fact]
    public async Task RegisterFailure_AtMaxAttempts_LocksAccount()
    {
        var user = await CreateUserAsync();
        await using var ctx = CreateContext();
        var svc = CreateService(ctx, maxAttempts: 3, lockoutMinutes: 15);

        // 3. Fehlversuch → Sperre
        await svc.RegisterFailureAsync(user);
        await svc.RegisterFailureAsync(user);
        await svc.RegisterFailureAsync(user);

        svc.IsLockedOut(user).Should().BeTrue();
        user.LockedUntil.Should().NotBeNull();
        user.LockedUntil!.Value.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(14));
    }

    [Fact]
    public async Task ResetAsync_ClearsCounterAndUnlocks()
    {
        var user = await CreateUserAsync();
        await using var ctx = CreateContext();
        var svc = CreateService(ctx, maxAttempts: 1);

        // Sperren
        await svc.RegisterFailureAsync(user);
        svc.IsLockedOut(user).Should().BeTrue();

        // Entsperren
        await svc.ResetAsync(user);

        svc.IsLockedOut(user).Should().BeFalse();
        user.FailedLoginAttempts.Should().Be(0);
        user.LockedUntil.Should().BeNull();
    }
}
