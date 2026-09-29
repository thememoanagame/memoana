using MemoAna.Game.Entities;

namespace MemoAna.Game.Dtos;

public class CardThemeManifestDto(CardThemeManifestEntity theme = null!)
{
    public string ThemeName { get; set; } = theme is null ? string.Empty : theme.ThemeName;
    public bool IsDefault { get; set; } = theme is not null && theme.IsDefault;
    public string PreviewBase64Image { get; set; } = theme is null ? string.Empty : theme.PreviewBase64Image;
    public string CardThemeId { get; set; } = theme is null ? string.Empty : theme.CardThemeId;
    public CardThemeDto? CardTheme { get; set; } = theme is null ? null : theme.CardTheme != null ? new CardThemeDto(theme.CardTheme.Base64Images, theme.CardTheme.ManifestId, null) : null;
}

public sealed record CardThemeDto(ICollection<string> Base64Images,string ManifestId, CardThemeManifestDto? Manifest);