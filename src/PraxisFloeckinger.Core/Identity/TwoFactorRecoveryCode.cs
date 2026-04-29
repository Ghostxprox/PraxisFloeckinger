using PraxisFloeckinger.Core.Common;

namespace PraxisFloeckinger.Core.Identity;

/// <summary>
/// Einmal-verwendbarer Recovery-Code für 2FA-Fallback.
/// Gespeichert als Argon2id-Hash — Klartext wird einmalig beim Setup angezeigt.
/// </summary>
public sealed class TwoFactorRecoveryCode : SoftDeletableEntityBase, ITenantScoped
{
    public required Guid UserId { get; set; }

    /// <summary>Argon2id-Hash des Klartext-Codes.</summary>
    public required string CodeHash { get; set; }

    /// <summary>Zeitpunkt der Einlösung (UTC); null wenn noch nicht verwendet.</summary>
    public DateTimeOffset? UsedAt { get; set; }
}
