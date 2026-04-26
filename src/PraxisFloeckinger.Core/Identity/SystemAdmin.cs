using PraxisFloeckinger.Core.Common;

namespace PraxisFloeckinger.Core.Identity;

/// <summary>
/// Plattform-Administrator (verwaltet Tenants, sieht keine Patientendaten).
/// Lebt in <c>tenants_master</c> — implementiert kein <see cref="ITenantScoped"/>.
/// Unterstützt Soft-Delete für revisionssichere Deaktivierung.
/// </summary>
public sealed class SystemAdmin : SoftDeletableEntityBase, IAuditable
{
    /// <summary>E-Mail-Adresse (Login-Name, unique, max 320 Zeichen).</summary>
    public required string Email { get; set; }

    /// <summary>Vollständiger Name für Anzeige und Audit-Log (max 200 Zeichen).</summary>
    public required string FullName { get; set; }

    /// <summary>
    /// Argon2id-Hash des Passworts (id, m=64MB, t=3, p=4).
    /// Max 500 Zeichen (Argon2id-Ausgabe inkl. Parameter-Prefix ist ~96 Zeichen,
    /// Puffer für zukünftige Algorithmuswechsel).
    /// </summary>
    public required string PasswordHash { get; set; }

    /// <summary>True wenn TOTP-2FA aktiviert ist (Pflicht für produktive Accounts).</summary>
    public bool TotpEnabled { get; set; } = false;

    /// <summary>TOTP-Secret (Base32, verschlüsselt at rest). Null wenn 2FA nicht konfiguriert.</summary>
    public string? TotpSecret { get; set; }
}
