using MemoAna.Backend.Domain.Common;

namespace MemoAna.Backend.Domain.Game;

/// <summary>
/// NoSQL document containing all image assets for a card theme.
/// </summary>
public sealed class CardThemeAssetEntity(string id = "") : NoSqlEntityBase(id)
{
    /// <summary>
    /// Gets the card theme aggregate root identifier.
    /// </summary>
    public string CardThemeId { get; set; } = string.Empty;

    public List<string> Base64Images { get; set; } = [];
}
