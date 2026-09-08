using MemoAna.Backend.Domain.Common;

namespace MemoAna.Backend.Domain.Game;

/// <summary>
/// SQL catalog metadata for a card theme.
/// </summary>
public sealed class CardThemeManifestEntity(string id = "") : EntityBase(id)
{
    public string ThemeName { get; set; } = string.Empty;

    public bool IsDefault { get; set; }

    /// <summary>
    /// Gets the card theme aggregate root identifier.
    /// </summary>
    public string CardThemeId { get; set; } = string.Empty;

    /// <summary>
    /// Gets the stable identifier of the preview asset stored in NoSQL.
    /// </summary>
    public string PreviewAssetId { get; set; } = string.Empty;
}
