using PraxisFloeckinger.Core.Common;

namespace PraxisFloeckinger.Core.Identity;

/// <summary>
/// Therapeuten-Profil — Erweiterung zu <see cref="User"/> mit Rolle
/// Therapeut oder TherapeutSupervisor.
/// 1:1-Beziehung über <see cref="UserId"/>.
/// </summary>
public sealed class TherapistProfile : SoftDeletableEntityBase, ITenantScoped
{
    /// <summary>FK auf den zugehörigen <see cref="User"/> (unveränderlich nach Anlage).</summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Fachgebiete (PostgreSQL text[], max 20 Einträge, je max 100 Zeichen).
    /// Validation auf max 100 Zeichen pro Eintrag liegt bei der Anwendung.
    /// </summary>
    public string[] Specializations { get; set; } = [];

    /// <summary>Kurzbeschreibung / Bio (max 5000 Zeichen, optional).</summary>
    public string? Bio { get; set; }

    /// <summary>True wenn dieser Therapeut als Supervisor agieren darf.</summary>
    public bool IsSupervisor { get; set; } = false;
}
