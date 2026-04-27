using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using PraxisFloeckinger.Core.Identity;
using PraxisFloeckinger.Core.Tenancy;
using PraxisFloeckinger.Infrastructure.Persistence.Tenant;
using Testcontainers.PostgreSql;

namespace PraxisFloeckinger.Tests.Infrastructure;

public sealed class TenantDbContextTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private TenantInfo _tenantInfo = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _tenantInfo = new TenantInfo(Guid.NewGuid(), "test-tenant", _postgres.GetConnectionString());
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

    // ─── Migrations ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Migration_AllTablesExist()
    {
        await using var ctx = CreateContext();
        // CountAsync throws if the table does not exist
        await ctx.Users.CountAsync();
        await ctx.PatientProfiles.CountAsync();
        await ctx.TherapistProfiles.CountAsync();
    }

    // ─── Soft-Delete ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SoftDelete_User_HiddenFromNormalQuery_VisibleWithIgnoreQueryFilters()
    {
        await using var ctx = CreateContext();
        var user = CreateUser("soft@delete.test");
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        user.IsDeleted = true;
        await ctx.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var normal = await readCtx.Users
            .FirstOrDefaultAsync(u => u.Email == "soft@delete.test");
        normal.Should().BeNull("soft-deleted user must be filtered out");

        var withDeleted = await readCtx.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == "soft@delete.test");
        withDeleted.Should().NotBeNull("soft-deleted user must be visible with IgnoreQueryFilters");
        withDeleted!.IsDeleted.Should().BeTrue();
    }

    // ─── SaveChanges audit hook ───────────────────────────────────────────────

    [Fact]
    public async Task SaveChangesAsync_SoftDelete_SetsDeletedAt()
    {
        await using var ctx = CreateContext();
        var user = CreateUser("deletedat@audit.test");
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var before = DateTimeOffset.UtcNow;
        user.IsDeleted = true;
        await ctx.SaveChangesAsync();
        var after = DateTimeOffset.UtcNow;

        await using var readCtx = CreateContext();
        var saved = await readCtx.Users.IgnoreQueryFilters()
            .FirstAsync(u => u.Email == "deletedat@audit.test");

        saved.DeletedAt.Should().NotBeNull();
        saved.DeletedAt!.Value.Should().BeOnOrAfter(before.AddSeconds(-1));
        saved.DeletedAt!.Value.Should().BeOnOrBefore(after.AddSeconds(1));
    }

    [Fact]
    public async Task SaveChangesAsync_Update_SetsModifiedAt()
    {
        await using var ctx = CreateContext();
        var user = CreateUser("modifiedat@audit.test");
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        user.ModifiedAt.Should().BeNull("new entity must not have ModifiedAt set");

        user.Phone = "+43 123 456";
        await ctx.SaveChangesAsync();

        user.ModifiedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task SaveChangesAsync_SecondSoftDelete_DoesNotOverwriteDeletedAt()
    {
        await using var ctx = CreateContext();
        var user = CreateUser("nodoubledel@audit.test");
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        user.IsDeleted = true;
        await ctx.SaveChangesAsync();
        var firstDeletedAt = user.DeletedAt;

        // Saving again without changing IsDeleted must not update DeletedAt
        user.Phone = "+43 999";
        await ctx.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var saved = await readCtx.Users.IgnoreQueryFilters()
            .FirstAsync(u => u.Email == "nodoubledel@audit.test");
        saved.DeletedAt.Should().Be(firstDeletedAt);
    }

    // ─── 1:1 relationships ────────────────────────────────────────────────────

    [Fact]
    public async Task PatientProfile_CascadeDelete_WhenUserHardDeleted()
    {
        await using var ctx = CreateContext();
        var user = CreateUser("cascade@patient.test", UserRole.Patient);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        ctx.PatientProfiles.Add(new PatientProfile
        {
            UserId = user.Id,
            BirthDate = new DateOnly(1990, 1, 1),
            Address = "Teststraße 1, 1010 Wien",
        });
        await ctx.SaveChangesAsync();

        await ctx.Users.IgnoreQueryFilters()
            .Where(u => u.Id == user.Id)
            .ExecuteDeleteAsync();

        await using var readCtx = CreateContext();
        var profile = await readCtx.PatientProfiles.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.UserId == user.Id);
        profile.Should().BeNull("PatientProfile must be cascade-deleted with User");
    }

    [Fact]
    public async Task TherapistProfile_Specializations_PersistedAsTextArray()
    {
        await using var ctx = CreateContext();
        var user = CreateUser("array@therapist.test", UserRole.Therapeut);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        ctx.TherapistProfiles.Add(new TherapistProfile
        {
            UserId = user.Id,
            Specializations = ["Verhaltenstherapie", "Stressbewältigung"],
        });
        await ctx.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var profile = await readCtx.TherapistProfiles
            .FirstAsync(p => p.UserId == user.Id);
        profile.Specializations.Should().BeEquivalentTo(["Verhaltenstherapie", "Stressbewältigung"]);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static User CreateUser(string email, UserRole role = UserRole.Therapeut) => new()
    {
        Email = email,
        PasswordHash = "argon2id-placeholder",
        Role = role,
        FirstName = "Test",
        LastName = "User",
    };
}
