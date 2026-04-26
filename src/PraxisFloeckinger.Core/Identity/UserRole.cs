namespace PraxisFloeckinger.Core.Identity;

/// <summary>
/// Rollen im System. Explizite numerische Werte sichern DB-Stabilität —
/// eine Umordnung der Members darf die gespeicherten Werte nie verschieben.
/// </summary>
public enum UserRole
{
    /// <summary>Patient mit Zugriff auf eigene Termine, Honorarnoten und Fragebögen.</summary>
    Patient = 1,

    /// <summary>Sekretärin: Termin- und Stammdatenverwaltung, kein Zugriff auf Diagnosen.</summary>
    Sekretaerin = 2,

    /// <summary>Therapeut: voller Zugriff auf eigene Patienten; keine anderen Patienten.</summary>
    Therapeut = 3,

    /// <summary>Therapeut mit Supervisor-Rechten: kann Therapeuten-in-Ausbildung beaufsichtigen.</summary>
    TherapeutSupervisor = 4,

    /// <summary>Admin einer einzelnen Praxis (Tenant); kein Zugriff auf andere Tenants.</summary>
    PraxisAdmin = 5,

    /// <summary>Plattform-Betreiber: verwaltet alle Tenants, sieht aber keine Patientendaten.</summary>
    SystemAdmin = 6,
}
