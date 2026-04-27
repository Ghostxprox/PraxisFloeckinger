using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using PraxisFloeckinger.Core.Tenancy;
using PraxisFloeckinger.Infrastructure.Persistence.Master;

namespace PraxisFloeckinger.Infrastructure.Tenancy;

/// <summary>
/// Löst Subdomains zur zugehörigen <see cref="TenantInfo"/> auf.
/// Unbekannte und inaktive Subdomains werden als <c>null</c> gecached, um
/// DB-Hammering via ungültige Host-Header zu verhindern.
/// </summary>
public sealed class TenantResolver : ITenantResolver
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private readonly MasterDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly TenantConnectionStringBuilder _connectionStringBuilder;
    private readonly ILogger<TenantResolver> _logger;

    public TenantResolver(
        MasterDbContext db,
        IMemoryCache cache,
        TenantConnectionStringBuilder connectionStringBuilder,
        ILogger<TenantResolver> logger)
    {
        _db = db;
        _cache = cache;
        _connectionStringBuilder = connectionStringBuilder;
        _logger = logger;
    }

    public async Task<TenantInfo?> ResolveAsync(string subdomain, CancellationToken ct = default)
    {
        var cacheKey = $"tenant:subdomain:{subdomain.ToLowerInvariant()}";

        if (_cache.TryGetValue(cacheKey, out TenantInfo? cached))
        {
            _logger.LogDebug("Tenant-Cache-Hit für Subdomain '{Subdomain}'", subdomain);
            return cached;
        }

        var normalizedSubdomain = subdomain.ToLowerInvariant();
        var tenant = await _db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Subdomain == normalizedSubdomain && t.IsActive, ct);

        TenantInfo? tenantInfo = null;
        if (tenant is not null)
        {
            var connectionString = _connectionStringBuilder.Build(tenant.DbConnectionRef);
            tenantInfo = new TenantInfo(tenant.Id, tenant.Subdomain, connectionString);
            _logger.LogInformation(
                "Tenant für Subdomain '{Subdomain}' aufgelöst (TenantId: {TenantId})",
                subdomain, tenant.Id);
        }
        else
        {
            _logger.LogInformation(
                "Kein aktiver Tenant für Subdomain '{Subdomain}' gefunden", subdomain);
        }

        _cache.Set(cacheKey, tenantInfo, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheTtl,
        });

        return tenantInfo;
    }
}
