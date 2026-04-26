namespace PraxisFloeckinger.Core.Therapy;

/// <summary>
/// Modus/Setting einer Therapiestunde. Mehrere Modi können pro Slot kombiniert werden.
/// Explizite Werte sichern DB-Stabilität.
/// </summary>
public enum TherapyMode
{
    /// <summary>Formelles Setting (Anzug, Praxisräume).</summary>
    Anzug = 1,

    /// <summary>Halbformelles Setting.</summary>
    BusinessCasual = 2,

    /// <summary>Informelles Setting.</summary>
    Casual = 3,

    /// <summary>Stunde mit Therapiehund.</summary>
    MitHund = 4,

    /// <summary>Stunde mit Pferd (zweite Location).</summary>
    MitPferd = 5,

    /// <summary>Online-Therapie (Jitsi).</summary>
    Online = 6,

    /// <summary>Therapie beim gemeinsamen Mittagessen.</summary>
    Mittagessen = 7,

    /// <summary>Sportbegleitete Therapie (Laufen, Calisthenics).</summary>
    Sportbegleitet = 8,
}
