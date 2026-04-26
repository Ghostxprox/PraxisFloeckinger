namespace PraxisFloeckinger.Core.Compliance;

/// <summary>
/// Art der auditpflichtigen Aktion. Wird im Audit-Log gespeichert.
/// Explizite Werte sichern DB-Stabilität (DSGVO Art. 5 Abs. 2, §15 PthG).
/// </summary>
public enum AuditAction
{
    /// <summary>Neuer Datensatz angelegt.</summary>
    Create = 1,

    /// <summary>Datensatz abgerufen (relevant für Gesundheitsdaten).</summary>
    Read = 2,

    /// <summary>Datensatz geändert.</summary>
    Update = 3,

    /// <summary>Datensatz gelöscht (Soft-Delete oder Hard-Delete nach Aufbewahrungsfrist).</summary>
    Delete = 4,

    /// <summary>Benutzer hat sich eingeloggt.</summary>
    Login = 5,

    /// <summary>Benutzer hat sich ausgeloggt.</summary>
    Logout = 6,

    /// <summary>Daten wurden exportiert (DSGVO Art. 20).</summary>
    Export = 7,

    /// <summary>Datensatz wurde gedruckt oder als PDF erzeugt.</summary>
    Print = 8,
}
