using PraxisFloeckinger.Core.Tenancy;
using PraxisFloeckinger.Infrastructure.Persistence.Tenant;

namespace PraxisFloeckinger.Infrastructure.Tenancy;

/// <summary>
/// Erzeugt einen <see cref="TenantDbContext"/> für einen gegebenen Tenant.
/// Bewusst in Infrastructure (nicht in Core), weil der Rückgabetyp <see cref="TenantDbContext"/>
/// EF Core kennt — Core darf keine EF-Core-Abhängigkeit haben.
/// Der Aufrufer ist für das Dispose des zurückgegebenen Context verantwortlich.
/// </summary>
public interface ITenantDbContextFactory
{
    TenantDbContext Create(ITenantContext tenantContext);
}
