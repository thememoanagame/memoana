using MemoAna.Common.Entities;

namespace MemoAna.Game.Entities;

public sealed class CardThemeManifestEntity(string id) : EntityBase(id)
{
    public string ThemeName { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public string ThumbnailSrc { get; set; } = string.Empty;
    public string CardThemeId { get; set; } = string.Empty;
    public CardThemeEntity? CardTheme { get; set; }
}