using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using PraxisFloeckinger.Core.Identity;
using PraxisFloeckinger.Core.Tenancy;
using PraxisFloeckinger.Infrastructure.Persistence.Master;
using Testcontainers.PostgreSql;

namespace PraxisFloeckinger.Tests.Infrastructure;

/// <summary>
/// Integrationstests für <see cref="MasterDbContext"/> gegen echte PostgreSQL-Instanz
/// via Testcontainers. Kein InMemory — wir testen gegen denselben Datenbank-Typ
/// wie in Produktion (Unique Constraints, Query Filters, Migrations).
/// </summary>
public sealed class MasterDbContextTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Migrations einmalig auf den Container anwenden
        await using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    private MasterDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MasterDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new MasterDbContext(options);
    }

    // ─── Migrations ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AllMigrations_ApplySuccessfully_OnFreshDatabase()
    {
        await using var ctx = CreateContext();
        var pending = await ctx.Database.GetPendingMigrationsAsync();
        pending.Should().BeEmpty();
    }

    // ─── Tenant ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Tenant_CanBeCreatedAndRead()
    {
        await using var ctx = CreateContext();

        var tenant = new Tenant
        {
            Subdomain = "floeckinger",
            DisplayName = "Praxis Flöckinger",
            DbConnectionRef = "tenant_floeckinger",
            LicenseTier = LicenseTier.Trial,
        };

        ctx.Tenants.Add(tenant);
        await ctx.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Tenants.FindAsync(tenant.Id);

        loaded.Should().NotBeNull();
        loaded!.Subdomain.Should().Be("floeckinger");
        loaded.DisplayName.Should().Be("Praxis Flöckinger");
        loaded.LicenseTier.Should().Be(LicenseTier.Trial);
        loaded.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Tenant_Subdomain_UniqueConstraint_IsEnforced()
    {
        await using var ctx = CreateContext();

        ctx.Tenants.Add(new Tenant
        {
            Subdomain = "duplicate-subdomain",
            DisplayName = "Praxis A",
            DbConnectionRef = "tenant_a",
        });
        await ctx.SaveChangesAsync();

        await using var ctx2 = CreateContext();
        ctx2.Tenants.Add(new Tenant
        {
            Subdomain = "duplicate-subdomain",
            DisplayName = "Praxis B",
            DbConnectionRef = "tenant_b",
        });

        var act = async () => await ctx2.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    // ─── LicenseEvent ────────────────────────────────────────────────────────

    [Fact]
    public async Task LicenseEvent_CanBeCreatedForTenant()
    {
        await using var ctx = CreateContext();

        var tenant = new Tenant
        {
            Subdomain = "license-event-test",
            DisplayName = "Praxis LizenzTest",
            DbConnectionRef = "tenant_lizenztest",
        };
        ctx.Tenants.Add(tenant);
        await ctx.SaveChangesAsync();

        var licenseEvent = new LicenseEvent
        {
            TenantId = tenant.Id,
            EventType = LicenseEventType.TrialStarted,
            EffectiveAt = DateTimeOffset.UtcNow,
        };
        ctx.LicenseEvents.Add(licenseEvent);
        await ctx.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.LicenseEvents.FindAsync(licenseEvent.Id);

        loaded.Should().NotBeNull();
        loaded!.TenantId.Should().Be(tenant.Id);
        loaded.EventType.Should().Be(LicenseEventType.TrialStarted);
    }

    // ─── SystemAdmin Soft-Delete ──────────────────────────────────────────────

    [Fact]
    public async Task SystemAdmin_SoftDelete_IsFilteredFromQueries()
    {
        await using var ctx = CreateContext();

        var admin = new SystemAdmin
        {
            Email = "admin-softdelete@test.local",
            FullName = "Test Admin",
            PasswordHash = "argon2id-placeholder",
        };
        ctx.SystemAdmins.Add(admin);
        await ctx.SaveChangesAsync();

        // Soft-Delete setzen
        admin.IsDeleted = true;
        admin.DeletedAt = DateTimeOffset.UtcNow;
        await ctx.SaveChangesAsync();

        // Normaler Query — muss durch Global Query Filter gefiltert werden
        await using var readCtx = CreateContext();
        var found = await readCtx.SystemAdmins
            .FirstOrDefaultAsync(a => a.Email == "admin-softdelete@test.local");
        found.Should().BeNull("gelöschte SystemAdmins müssen vom Query Filter ausgeblendet werden");
    }

    [Fact]
    public async Task SystemAdmin_SoftDeleted_IsStillVisibleWithIgnoreQueryFilters()
    {
        await using var ctx = CreateContext();

        var admin = new SystemAdmin
        {
            Email = "admin-ignore-filter@test.local",
            FullName = "Test Admin 2",
            PasswordHash = "argon2id-placeholder",
        };
        ctx.SystemAdmins.Add(admin);
        await ctx.SaveChangesAsync();

        admin.IsDeleted = true;
        admin.DeletedAt = DateTimeOffset.UtcNow;
        await ctx.SaveChangesAsync();

        // Mit IgnoreQueryFilters muss der Eintrag noch auffindbar sein
        await using var readCtx = CreateContext();
        var found = await readCtx.SystemAdmins
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Email == "admin-ignore-filter@test.local");
        found.Should().NotBeNull("der Eintrag darf nicht physisch gelöscht worden sein");
        found!.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task SystemAdmin_Email_UniqueConstraint_IsEnforced()
    {
        await using var ctx = CreateContext();
        ctx.SystemAdmins.Add(new SystemAdmin
        {
            Email = "unique-admin@test.local",
            FullName = "Admin 1",
            PasswordHash = "argon2id-placeholder",
        });
        await ctx.SaveChangesAsync();

        await using var ctx2 = CreateContext();
        ctx2.SystemAdmins.Add(new SystemAdmin
        {
            Email = "unique-admin@test.local",
            FullName = "Admin 2",
            PasswordHash = "argon2id-placeholder",
        });

        var act = async () => await ctx2.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
