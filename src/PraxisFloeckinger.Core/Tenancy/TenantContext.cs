namespace PraxisFloeckinger.Core.Tenancy;

/// <summary>
/// Request-scoped DI-Instanz des aufgelösten Tenants.
/// Wird von <c>TenantResolverMiddleware</c> in <c>HttpContext.Items</c> abgelegt
/// und über die Scoped-DI-Registrierung als <see cref="ITenantContext"/> bereitgestellt.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    public Guid TenantId { get; }
    public string Subdomain { get; }
    public string ConnectionString { get; }

    public TenantContext(Guid tenantId, string subdomain, string connectionString)
    {
        TenantId = tenantId;
        Subdomain = subdomain;
        ConnectionString = connectionString;
    }
}
