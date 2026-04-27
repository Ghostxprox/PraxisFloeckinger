using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PraxisFloeckinger.Core.Tenancy;
using PraxisFloeckinger.Infrastructure.Persistence.Master;
using PraxisFloeckinger.Infrastructure.Tenancy;
using Testcontainers.PostgreSql;

namespace PraxisFloeckinger.Tests.Infrastructure;

public sealed class TenantResolverTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private MasterDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MasterDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new MasterDbContext(options);
    }

    private TenantResolver CreateResolver(IMemoryCache? cache = null)
    {
        cache ??= new MemoryCache(new MemoryCacheOptions());
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TenantTemplate"] =
                    "Host=localhost;Port=5432;Database={DB};Username=praxisdev;Password=test",
            })
            .Build();
        return new TenantResolver(
            CreateContext(),
            cache,
            new TenantConnectionStringBuilder(config),
            NullLogger<TenantResolver>.Instance);
    }

    // ─── Tests ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_KnownActiveSubdomain_ReturnsTenantInfo()
    {
        await using var ctx = CreateContext();
        ctx.Tenants.Add(new Tenant
        {
            Subdomain = "known-active",
            DisplayName = "Praxis Known",
            DbConnectionRef = "tenant_known",
            IsActive = true,
        });
        await ctx.SaveChangesAsync();

        var result = await CreateResolver().ResolveAsync("known-active");

        result.Should().NotBeNull();
        result!.Subdomain.Should().Be("known-active");
        result.ConnectionString.Should().Contain("tenant_known");
    }

    [Fact]
    public async Task ResolveAsync_UnknownSubdomain_ReturnsNullAndCachesResult()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var resolver = CreateResolver(cache);

        var first = await resolver.ResolveAsync("does-not-exist");
        first.Should().BeNull();

        // Tenant mit derselben Subdomain jetzt in DB anlegen
        await using var ctx = CreateContext();
        ctx.Tenants.Add(new Tenant
        {
            Subdomain = "does-not-exist",
            DisplayName = "Late Tenant",
            DbConnectionRef = "tenant_late",
            IsActive = true,
        });
        await ctx.SaveChangesAsync();

        // Zweiter Aufruf innerhalb TTL → muss null aus Cache zurückgeben
        var second = await resolver.ResolveAsync("does-not-exist");
        second.Should().BeNull("das null-Ergebnis muss für 60 Sekunden gecached sein");
    }

    [Fact]
    public async Task ResolveAsync_InactiveTenant_ReturnsNull()
    {
        await using var ctx = CreateContext();
        ctx.Tenants.Add(new Tenant
        {
            Subdomain = "inactive-tenant",
            DisplayName = "Inaktive Praxis",
            DbConnectionRef = "tenant_inactive",
            IsActive = false,
        });
        await ctx.SaveChangesAsync();

        var result = await CreateResolver().ResolveAsync("inactive-tenant");
        result.Should().BeNull("inaktive Tenants dürfen nicht aufgelöst werden");
    }

    [Fact]
    public async Task ResolveAsync_CalledTwiceWithinTtl_HitsCacheNotDb()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());

        await using var setupCtx = CreateContext();
        var tenant = new Tenant
        {
            Subdomain = "cache-test",
            DisplayName = "Cache Praxis",
            DbConnectionRef = "tenant_cache",
            IsActive = true,
        };
        setupCtx.Tenants.Add(tenant);
        await setupCtx.SaveChangesAsync();

        var resolver = CreateResolver(cache);

        // Erster Aufruf → DB-Abfrage, Cache befüllen
        var first = await resolver.ResolveAsync("cache-test");
        first.Should().NotBeNull();

        // Tenant aus DB entfernen — würde ein zweiter DB-Hit null liefern
        await using var deleteCtx = CreateContext();
        deleteCtx.Tenants.Remove((await deleteCtx.Tenants.FindAsync(tenant.Id))!);
        await deleteCtx.SaveChangesAsync();

        // Zweiter Aufruf innerhalb TTL → muss gecachten Wert liefern
        var second = await resolver.ResolveAsync("cache-test");
        second.Should().NotBeNull("zweiter Aufruf muss aus Cache kommen");
        second!.TenantId.Should().Be(first!.TenantId);
    }
}
