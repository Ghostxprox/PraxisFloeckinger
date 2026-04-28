namespace PraxisFloeckinger.Core.Identity;

/// <summary>
/// Verwaltet den Lifecycle von Refresh-Tokens (Ausgabe, Validierung, Widerruf).
/// Persistiert nur den SHA-256-Hash — niemals den Rohwert.
/// </summary>
public interface IRefreshTokenService
{
    /// <summary>
    /// Generiert einen neuen Refresh-Token, speichert den Hash in der Tenant-DB
    /// und gibt Rohwert + Entity zurück.
    /// Lifetime: Patient=24h, Mitarbeiter=8h; rememberMe: alle=30d.
    /// </summary>
    Task<(string token, RefreshToken entity)> IssueAsync(
        User user,
        bool rememberMe,
        string ip,
        string userAgent,
        CancellationToken ct = default);

    /// <summary>
    /// Validiert einen Raw-Token: hasht ihn, sucht in DB, prüft Ablauf + Widerruf.
    /// Gibt die Entity zurück oder null (nicht gefunden / abgelaufen / widerrufen).
    /// Prüft NICHT ob der zugehörige User noch existiert — das ist Aufgabe des Aufrufers.
    /// </summary>
    Task<RefreshToken?> ValidateAsync(string token, CancellationToken ct = default);

    /// <summary>Setzt RevokedAt + RevokedReason auf dem angegebenen Raw-Token.</summary>
    Task RevokeAsync(string token, string reason, CancellationToken ct = default);

    /// <summary>Widerruft alle aktiven Tokens eines Users (z.B. bei Logout-Everywhere).</summary>
    Task RevokeAllForUserAsync(Guid userId, string reason, CancellationToken ct = default);
}
