namespace PraxisFloeckinger.Core.Tenancy;

/// <summary>
/// Art eines Lizenz-Ereignisses im Audit-Trail. Explizite Werte sichern DB-Stabilität.
/// </summary>
public enum LicenseEventType
{
    /// <summary>Testphase wurde gestartet.</summary>
    TrialStarted = 1,

    /// <summary>Lizenz wurde aktiviert (nach Zahlung oder manuell durch SystemAdmin).</summary>
    Activated = 2,

    /// <summary>Lizenz wurde verlängert.</summary>
    Renewed = 3,

    /// <summary>Lizenz wurde gesperrt (z.B. bei Zahlungsverzug).</summary>
    Suspended = 4,

    /// <summary>Lizenz wurde gekündigt.</summary>
    Cancelled = 5,
}
