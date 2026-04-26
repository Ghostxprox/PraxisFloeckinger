namespace PraxisFloeckinger.Core.Tenancy;

/// <summary>
/// Unveränderliche Tenant-Informationen für einen Request.
/// Implementiert <see cref="ITenantContext"/> als immutables Record.
/// </summary>
/// <param name="TenantId">Eindeutige Tenant-ID.</param>
/// <param name="Subdomain">Subdomain des Tenants.</param>
/// <param name="ConnectionString">Connection-String zur Tenant-DB (nicht loggen!).</param>
public sealed record TenantInfo(
    Guid TenantId,
    string Subdomain,
    string ConnectionString
) : ITenantContext;
