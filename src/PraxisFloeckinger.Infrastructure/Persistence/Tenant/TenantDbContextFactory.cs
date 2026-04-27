using Microsoft.EntityFrameworkCore;
using PraxisFloeckinger.Core.Tenancy;
using PraxisFloeckinger.Infrastructure.Tenancy;

namespace PraxisFloeckinger.Infrastructure.Persistence.Tenant;

/// <summary>
/// Erzeugt <see cref="TenantDbContext"/>-Instanzen auf Basis des Request-scoped
/// <see cref="ITenantContext"/>. Als Singleton registriert — der DbContext selbst
/// hat keine eigene Lifetime; der Aufrufer ist für Dispose verantwortlich.
/// </summary>
public sealed class TenantDbContextFactory : ITenantDbContextFactory
{
    public TenantDbContext Create(ITenantContext tenantContext)
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(tenantContext.ConnectionString)
            .Options;

        return new TenantDbContext(options, tenantContext);
    }
}
