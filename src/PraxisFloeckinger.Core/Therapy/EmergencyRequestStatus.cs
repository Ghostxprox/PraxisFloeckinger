namespace PraxisFloeckinger.Core.Therapy;

/// <summary>
/// Status einer Sonderwunsch-Anfrage (intern "EmergencyRequest").
/// Explizite Werte sichern DB-Stabilität.
/// </summary>
public enum EmergencyRequestStatus
{
    /// <summary>Anfrage wartet auf Reaktion des Therapeuten.</summary>
    Pending = 1,

    /// <summary>Therapeut hat die Anfrage genehmigt.</summary>
    Approved = 2,

    /// <summary>Therapeut hat die Anfrage abgelehnt.</summary>
    Rejected = 3,

    /// <summary>Therapeut hat einen Gegenvorschlag unterbreitet.</summary>
    CounterProposed = 4,
}
