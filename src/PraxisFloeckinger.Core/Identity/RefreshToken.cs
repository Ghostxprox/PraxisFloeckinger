using PraxisFloeckinger.Core.Common;

namespace PraxisFloeckinger.Core.Identity;

/// <summary>
/// Opaker Refresh-Token pro User in der Tenant-DB.
/// Nur der SHA-256-Hash des Tokens wird gespeichert — nie der Rohwert.
/// </summary>
public sealed class RefreshToken : SoftDeletableEntityBase, ITenantScoped
{
    /// <summary>FK zum User dem dieser Token gehört.</summary>
    public required Guid UserId { get; init; }

    /// <summary>SHA-256-Hash des Raw-Tokens als Hex (64 Zeichen). Index für Lookup.</summary>
    public required string TokenHash { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>"logout" | "rotation" | "admin-revoke"</summary>
    public string? RevokedReason { get; set; }

    /// <summary>IPv4 oder IPv6, max 64 Zeichen.</summary>
    public required string CreatedFromIp { get; init; }

    /// <summary>User-Agent-String, max 512 Zeichen.</summary>
    public required string CreatedFromUserAgent { get; init; }
}
