using MemoAna.Backend.Domain.Common;

namespace MemoAna.Backend.Domain.Game;

/// <summary>
/// Aggregate root for a card theme catalog entry.
/// </summary>
public sealed class CardThemeEntity(string id = "") : EntityBase(id),
    IRelationalEntityBase
{
    /// <summary>
    /// Gets the SQL identifier of the manifest belonging to this theme.
    /// </summary>
    public string ManifestId { get; set; } = string.Empty;
}
