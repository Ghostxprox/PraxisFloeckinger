namespace PraxisFloeckinger.Core.Common;

/// <summary>
/// Marker-Interface: implementierende Entitäten liegen in der Tenant-DB (tenant_&lt;guid&gt;),
/// nicht in der Master-DB. Infrastructure kann darüber den korrekten DbContext wählen.
/// </summary>
public interface ITenantScoped { }
