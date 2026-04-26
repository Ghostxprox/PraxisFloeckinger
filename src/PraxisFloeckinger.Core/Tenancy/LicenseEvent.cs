using PraxisFloeckinger.Core.Common;

namespace PraxisFloeckinger.Core.Tenancy;

/// <summary>
/// Unveränderlicher Audit-Eintrag für jeden Lizenz-Statuswechsel eines Tenants.
/// Lebt in <c>tenants_master</c> — implementiert kein <see cref="ITenantScoped"/>.
/// Wird nie soft-deleted (Lizenz-Historie muss vollständig erhalten bleiben).
/// </summary>
public sealed class LicenseEvent : EntityBase, IAuditable
{
    /// <summary>Referenz auf den betroffenen Tenant.</summary>
    public required Guid TenantId { get; init; }

    /// <summary>Art des Lizenz-Ereignisses.</summary>
    public required LicenseEventType EventType { get; init; }

    /// <summary>Zeitpunkt ab dem das Ereignis wirksam ist (UTC).</summary>
    public required DateTimeOffset EffectiveAt { get; init; }

    /// <summary>Optionale Anmerkung (z.B. "manuell aktiviert durch Support").</summary>
    public string? Note { get; set; }
}
