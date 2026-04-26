namespace PraxisFloeckinger.Core.Therapy;

/// <summary>
/// Verfügbarkeitsstatus eines Zeitslots. Explizite Werte sichern DB-Stabilität.
/// </summary>
public enum SlotStatus
{
    /// <summary>Slot ist frei und buchbar.</summary>
    Free = 1,

    /// <summary>Slot ist durch einen Termin belegt.</summary>
    Booked = 2,

    /// <summary>Slot ist manuell gesperrt (Urlaub, interne Reservierung, etc.).</summary>
    Blocked = 3,
}
