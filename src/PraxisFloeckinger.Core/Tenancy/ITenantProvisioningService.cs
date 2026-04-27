namespace PraxisFloeckinger.Core.Tenancy;

/// <summary>
/// Provisioniert einen neuen Tenant: legt die Master-DB-Row an UND erstellt
/// die zugehörige Tenant-PostgreSQL-Datenbank mit allen Migrations.
/// Nicht idempotent — bei Fehlern nach CREATE DATABASE bleibt eine verwaiste DB übrig.
/// Recovery-Logik folgt in einem späteren Schritt.
/// </summary>
public interface ITenantProvisioningService
{
    Task ProvisionAsync(
        string subdomain,
        string displayName,
        LicenseTier tier,
        CancellationToken ct = default);
}
