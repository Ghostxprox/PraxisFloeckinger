namespace PraxisFloeckinger.Core.Tenancy;

/// <summary>
/// Lizenz-Stufe eines Tenants. Explizite Werte sichern DB-Stabilität.
/// </summary>
public enum LicenseTier
{
    /// <summary>Kostenlose Testphase (zeitlich begrenzt).</summary>
    Trial = 1,

    /// <summary>Standard-Lizenz für Einzelpraxen.</summary>
    Standard = 2,

    /// <summary>Erweiterte Lizenz (mehrere Therapeuten, White-Label).</summary>
    Professional = 3,

    /// <summary>Enterprise-Lizenz für Kliniken und Institute (On-Premise-Option).</summary>
    Enterprise = 4,
}
