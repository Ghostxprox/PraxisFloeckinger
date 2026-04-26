namespace PraxisFloeckinger.Core.Common;

/// <summary>
/// Abstrakte Basisklasse für alle persistierten Entitäten.
/// Id wird automatisch beim Erstellen vergeben; CreatedAt wird auf UTC-Now gesetzt.
/// </summary>
public abstract class EntityBase
{
    /// <summary>Primärschlüssel — immer eine neue GUID, nie 0 oder leer.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Zeitpunkt der Erstellung (UTC).</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>UserId des Erstellers; null bei system-generierten Einträgen.</summary>
    public Guid? CreatedBy { get; init; }

    /// <summary>Zeitpunkt der letzten Änderung (UTC); null solange nie geändert.</summary>
    public DateTimeOffset? ModifiedAt { get; set; }

    /// <summary>UserId des letzten Bearbeiters; null solange nie geändert.</summary>
    public Guid? ModifiedBy { get; set; }

    /// <summary>
    /// Geschützter Konstruktor für Subklassen und EF Core.
    /// Setzt CreatedAt auf UtcNow — überschreibe ihn nur in Tests.
    /// </summary>
    protected EntityBase() { }
}
