using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PraxisFloeckinger.Core.Tenancy;
using PraxisFloeckinger.Infrastructure.Persistence.Master;
using PraxisFloeckinger.Infrastructure.Persistence.Tenant;
using PraxisFloeckinger.Infrastructure.Tenancy;
using Testcontainers.PostgreSql;

namespace PraxisFloeckinger.Tests.Infrastructure;

public sealed class TenantProvisioningServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var masterCtx = CreateMasterContext();
        await masterCtx.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private MasterDbContext CreateMasterContext()
    {
        var options = new DbContextOptionsBuilder<MasterDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new MasterDbContext(options);
    }

    private IConfiguration CreateConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Server-level connection: points to 'postgres' DB — fine for CREATE DATABASE
                ["Tenancy:ServerConnection"] = _postgres.GetConnectionString(),
                ["ConnectionStrings:TenantTemplate"] =
                    ReplaceDatabase(_postgres.GetConnectionString(), "{DB}"),
            })
            .Build();

    private TenantProvisioningService CreateService()
    {
        var config = CreateConfiguration();
        return new TenantProvisioningService(
            CreateMasterContext(),
            new TenantDbContextFactory(),
            new TenantConnectionStringBuilder(config),
            config,
            NullLogger<TenantProvisioningService>.Instance);
    }

    // ─── Tests ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProvisionAsync_CreatesDbAndTenantRow()
    {
        await CreateService().ProvisionAsync("praxis-test", "Praxis Test", LicenseTier.Trial);

        await using var masterCtx = CreateMasterContext();
        var tenant = await masterCtx.Tenants
            .FirstOrDefaultAsync(t => t.Subdomain == "praxis-test");

        tenant.Should().NotBeNull();
        tenant!.IsActive.Should().BeTrue();
        tenant.LicenseTier.Should().Be(LicenseTier.Trial);
        tenant.DbConnectionRef.Should().StartWith("tenant_");
    }

    [Fact]
    public async Task ProvisionAsync_TenantTablesExistAfterProvisioning()
    {
        await CreateService().ProvisionAsync("tables-praxis", "Tables Test", LicenseTier.Trial);

        await using var masterCtx = CreateMasterContext();
        var tenant = await masterCtx.Tenants.FirstAsync(t => t.Subdomain == "tables-praxis");

        var config = CreateConfiguration();
        var connString = new TenantConnectionStringBuilder(config).Build(tenant.DbConnectionRef);
        var stub = new TenantInfo(tenant.Id, tenant.Subdomain, connString);

        await using var tenantCtx = new TenantDbContextFactory().Create(stub);
        var userCount = await tenantCtx.Users.CountAsync();
        userCount.Should().Be(0, "freshly provisioned tenant DB must have no users");
    }

    [Fact]
    public async Task ProvisionAsync_DuplicateSubdomain_ThrowsInvalidOperationException()
    {
        await CreateService().ProvisionAsync("duplicate", "Erste Praxis", LicenseTier.Trial);

        var act = () => CreateService().ProvisionAsync("duplicate", "Zweite Praxis", LicenseTier.Trial);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*duplicate*");
    }

    [Fact]
    public async Task ProvisionAsync_TenantId_IsPersistedCorrectly()
    {
        await CreateService().ProvisionAsync("id-test", "Id Test Praxis", LicenseTier.Professional);

        await using var masterCtx = CreateMasterContext();
        var tenant = await masterCtx.Tenants.FirstAsync(t => t.Subdomain == "id-test");

        tenant.Id.Should().NotBe(Guid.Empty);
        tenant.LicenseTier.Should().Be(LicenseTier.Professional);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static string ReplaceDatabase(string connectionString, string dbName)
    {
        var csb = new Npgsql.NpgsqlConnectionStringBuilder(connectionString) { Database = dbName };
        return csb.ToString();
    }
}
