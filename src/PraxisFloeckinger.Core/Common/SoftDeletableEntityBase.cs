namespace PraxisFloeckinger.Core.Common;

/// <summary>
/// Erweitert <see cref="EntityBase"/> um Soft-Delete-Felder.
/// Einträge werden nie physisch gelöscht — stattdessen wird <see cref="IsDeleted"/> gesetzt.
/// EF Core Global Query Filter in Infrastructure filtert gelöschte Einträge automatisch heraus.
/// </summary>
public abstract class SoftDeletableEntityBase : EntityBase
{
    /// <summary>True, wenn der Eintrag als gelöscht markiert ist.</summary>
    public bool IsDeleted { get; set; } = false;

    /// <summary>Zeitpunkt des Soft-Delete (UTC); null wenn nicht gelöscht.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>UserId desjenigen, der den Soft-Delete ausgelöst hat.</summary>
    public Guid? DeletedBy { get; set; }

    /// <summary>Geschützter Konstruktor für Subklassen und EF Core.</summary>
    protected SoftDeletableEntityBase() : base() { }
}
