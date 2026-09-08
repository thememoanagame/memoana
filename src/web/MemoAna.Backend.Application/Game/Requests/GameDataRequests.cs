namespace MemoAna.Backend.Application.Game.Requests;

/// <summary>Represents the payload used to create or update game data.</summary>
/// <param name="ThemeName">The display name of the card theme.</param>
/// <param name="IsDefault">Whether the theme is the default theme.</param>
/// <param name="Base64Image">The preview image encoded as Base64.</param>
public sealed record GameDataRequest(
    string ThemeName,
    bool IsDefault,
    string Base64Image);
