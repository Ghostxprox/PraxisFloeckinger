namespace PraxisFloeckinger.Core.Tenancy;

/// <summary>
/// Löst einen Subdomain-String zur zugehörigen <see cref="TenantInfo"/> auf.
/// Null-Ergebnis bedeutet: kein aktiver Tenant mit dieser Subdomain bekannt.
/// </summary>
public interface ITenantResolver
{
    Task<TenantInfo?> ResolveAsync(string subdomain, CancellationToken ct = default);
}
