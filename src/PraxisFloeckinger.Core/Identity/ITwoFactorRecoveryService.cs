namespace PraxisFloeckinger.Core.Identity;

/// <summary>
/// Generiert und konsumiert einmalige 2FA-Recovery-Codes.
/// Format: "ABCDE-FGHJK" (10 Zeichen aus lesbarem Alphabet, keine 0/O/1/I/L).
/// Gespeichert als Argon2id-Hash; Klartext wird einmalig beim Setup zurückgegeben.
/// </summary>
public interface ITwoFactorRecoveryService
{
    /// <summary>
    /// Löscht alle alten Recovery-Codes des Users und generiert <paramref name="count"/> neue.
    /// Gibt die Klartext-Codes zurück — einmalig anzeigen, dann verwerfen.
    /// </summary>
    Task<IReadOnlyList<string>> GenerateAsync(
        Guid userId,
        int count = 10,
        CancellationToken ct = default);

    /// <summary>
    /// Sucht einen passenden, noch nicht verwendeten Code für den User.
    /// Setzt <c>UsedAt</c> wenn gefunden → danach nicht mehr einlösbar.
    /// Gibt true zurück wenn ein gültiger Code eingelöst wurde.
    /// </summary>
    Task<bool> ConsumeAsync(Guid userId, string code, CancellationToken ct = default);

    /// <summary>Gibt die Anzahl der verbleibenden (noch nicht verwendeten) Codes zurück.</summary>
    Task<int> CountRemainingAsync(Guid userId, CancellationToken ct = default);
}
