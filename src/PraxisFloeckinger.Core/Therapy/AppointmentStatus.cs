namespace PraxisFloeckinger.Core.Therapy;

/// <summary>
/// Status eines gebuchten Termins. Explizite Werte sichern DB-Stabilität.
/// </summary>
public enum AppointmentStatus
{
    /// <summary>Termin ist gebucht und bestätigt.</summary>
    Booked = 1,

    /// <summary>Termin wurde storniert (durch Patient oder Therapeut).</summary>
    Cancelled = 2,

    /// <summary>Termin wurde abgehalten.</summary>
    Completed = 3,

    /// <summary>Patient ist ohne Absage nicht erschienen.</summary>
    NoShow = 4,
}
