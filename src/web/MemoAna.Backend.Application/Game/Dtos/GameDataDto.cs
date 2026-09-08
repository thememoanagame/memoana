namespace MemoAna.Backend.Application.Game.Dtos;

/// <summary>
/// Represents the application view of a complete card theme aggregate.
/// </summary>
public sealed record GameDataDto(
    string Id,
    string ManifestId,
    string ThemeName,
    bool IsDefault,
    string PreviewAssetId,
    string Base64Image);
