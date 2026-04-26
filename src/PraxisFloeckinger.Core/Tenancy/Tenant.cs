using PraxisFloeckinger.Core.Common;

namespace PraxisFloeckinger.Core.Tenancy;

/// <summary>
/// Repräsentiert eine registrierte Praxis (Tenant) in der Master-DB.
/// Lebt in <c>tenants_master</c>, NICHT in einer Tenant-DB — implementiert daher
/// kein <see cref="ITenantScoped"/>.
/// </summary>
public sealed class Tenant : EntityBase, IAuditable
{
    /// <summary>
    /// Eindeutige Subdomain (Pflichtfeld, max 63 Zeichen, lowercase, kebab-case).
    /// Unveränderlich nach dem Anlegen — Änderungen würden DNS-Einträge brechen.
    /// </summary>
    public required string Subdomain { get; init; }

    /// <summary>Anzeigename der Praxis (max 200 Zeichen).</summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// Optionale eigene Domain via CNAME (max 253 Zeichen, z.B. "app.praxis-fischer.at").
    /// </summary>
    public string? CustomDomain { get; set; }

    /// <summary>
    /// Verweis auf den Tenant-spezifischen Connection-String (z.B. "tenant_abc123").
    /// Enthält NIE den echten Connection-String — dieser liegt in einem Secret-Store.
    /// Unveränderlich nach dem Anlegen.
    /// </summary>
    public required string DbConnectionRef { get; init; }

    /// <summary>Aktuelle Lizenz-Stufe des Tenants.</summary>
    public LicenseTier LicenseTier { get; set; } = LicenseTier.Trial;

    /// <summary>False wenn der Tenant gesperrt ist (z.B. Kündigung, Zahlungsverzug).</summary>
    public bool IsActive { get; set; } = true;
}
