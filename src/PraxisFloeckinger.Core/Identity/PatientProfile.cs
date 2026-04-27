using PraxisFloeckinger.Core.Common;

namespace PraxisFloeckinger.Core.Identity;

/// <summary>
/// Patientenstammdaten — Erweiterung zu <see cref="User"/> mit Rolle Patient.
/// 1:1-Beziehung über <see cref="UserId"/>.
/// DSGVO / §15 PthG: 10 Jahre Aufbewahrungspflicht, Soft-Delete only.
/// </summary>
public sealed class PatientProfile : SoftDeletableEntityBase, ITenantScoped
{
    /// <summary>FK auf den zugehörigen <see cref="User"/> (unveränderlich nach Anlage).</summary>
    public required Guid UserId { get; init; }

    /// <summary>Geburtsdatum.</summary>
    public required DateOnly BirthDate { get; set; }

    /// <summary>Wohnadresse (max 500 Zeichen).</summary>
    public required string Address { get; set; }

    /// <summary>
    /// Notfallkontakt (max 500 Zeichen, optional).
    /// Sichtbar für Sekretariat — kein Therapieinhalt!
    /// </summary>
    public string? EmergencyContact { get; set; }

    /// <summary>Versicherungsinfos (max 200 Zeichen, optional).</summary>
    public string? InsuranceInfo { get; set; }

    /// <summary>
    /// Interne Notizen des Sekretariats (max 2000 Zeichen, optional).
    /// "Öffentlich" = sichtbar im Praxis-Office für Sekretariat und Therapeuten,
    /// NICHT für den Patienten selbst und NICHT über die Web-API.
    /// Niemals Diagnose- oder Therapieinhalte hier!
    /// </summary>
    public string? PublicNotes { get; set; }
}
