namespace PraxisFloeckinger.Core.Identity;

public interface IAccountLockoutService
{
    /// <summary>True wenn das Konto aktuell gesperrt ist.</summary>
    bool IsLockedOut(User user);

    /// <summary>Registriert einen fehlgeschlagenen Versuch; sperrt das Konto bei Überschreitung.</summary>
    Task RegisterFailureAsync(User user, CancellationToken ct = default);

    /// <summary>Setzt den Zähler auf 0 und hebt die Sperre auf.</summary>
    Task ResetAsync(User user, CancellationToken ct = default);
}
