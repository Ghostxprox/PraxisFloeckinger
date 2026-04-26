namespace PraxisFloeckinger.Core.Tenancy;

/// <summary>
/// Repräsentiert den aufgelösten Tenant für den aktuellen HTTP-Request.
/// Wird von <c>TenantResolverMiddleware</c> per DI bereitgestellt (Schritt 4).
/// </summary>
public interface ITenantContext
{
    /// <summary>Eindeutige Tenant-ID (entspricht dem GUID-Suffix der Tenant-DB).</summary>
    Guid TenantId { get; }

    /// <summary>Subdomain, über die dieser Tenant erreichbar ist (z.B. "floeckinger").</summary>
    string Subdomain { get; }

    /// <summary>
    /// Connection-String zur Tenant-spezifischen PostgreSQL-DB.
    /// Darf nicht in Logs erscheinen (enthält Credentials).
    /// </summary>
    string ConnectionString { get; }
}
