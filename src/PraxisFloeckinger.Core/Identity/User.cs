using PraxisFloeckinger.Core.Common;

namespace PraxisFloeckinger.Core.Identity;

/// <summary>
/// Benutzerkonto innerhalb einer Praxis (Tenant-DB).
/// Jeder Benutzer hat genau eine Rolle — Patienten und Therapeuten besitzen
/// zusätzlich ein <see cref="PatientProfile"/> bzw. <see cref="TherapistProfile"/>.
/// </summary>
public sealed class User : SoftDeletableEntityBase, IAuditable, ITenantScoped
{
    /// <summary>E-Mail-Adresse; unique pro Tenant-DB (max 320 Zeichen).</summary>
    public required string Email { get; set; }

    /// <summary>Argon2id-Hash; niemals im Klartext speichern (max 500 Zeichen).</summary>
    public required string PasswordHash { get; set; }

    /// <summary>Rolle im System — bestimmt Zugriffsrechte.</summary>
    public required UserRole Role { get; set; }

    /// <summary>Vorname (max 100 Zeichen).</summary>
    public required string FirstName { get; set; }

    /// <summary>Nachname (max 100 Zeichen).</summary>
    public required string LastName { get; set; }

    /// <summary>Telefonnummer (optional, max 50 Zeichen).</summary>
    public string? Phone { get; set; }

    /// <summary>True wenn TOTP-2FA aktiviert ist.</summary>
    public bool TotpEnabled { get; set; } = false;

    /// <summary>TOTP-Seed (Base32, max 200 Zeichen); null wenn 2FA deaktiviert.</summary>
    public string? TotpSecret { get; set; }
}
