using MemoAna.Backend.Application.Common.Abstractions;
using MemoAna.Backend.Application.Game.Abstractions;
using MemoAna.Backend.Application.Game.Dtos;
using MemoAna.Backend.Domain.Game;

namespace MemoAna.Backend.Infrastructure.Game;

/// <summary>
/// Coordinates relational card theme metadata and MongoDB asset documents.
/// </summary>
public sealed class GameDataService(
    IRepository<CardThemeEntity> cardThemeRepository,
    IRepository<CardThemeManifestEntity> manifestRepository,
    INoRepository<CardThemeAssetEntity> assetRepository)
    : IGameDataService
{
    public async Task<GameDataDto> CreateAsync(
        string themeName,
        bool isDefault,
        string base64Image,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(base64Image);

        CardThemeEntity theme = new();
        CardThemeManifestEntity manifest = new()
        {
            CardThemeId = theme.Id,
            ThemeName = themeName,
            IsDefault = isDefault
        };
        CardThemeAssetEntity asset = new()
        {
            CardThemeId = theme.Id,
            Base64Image = base64Image
        };
        manifest.PreviewAssetId = asset.Id;
        theme.ManifestId = manifest.Id;

        await cardThemeRepository.AddAsync(theme, cancellationToken);
        await manifestRepository.AddAsync(manifest, cancellationToken);
        await assetRepository.AddAsync(asset, cancellationToken);

        return Map(theme, manifest, asset);
    }

    public async Task<GameDataDto?> GetByIdAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        CardThemeEntity? theme = await cardThemeRepository.GetByIdAsync(
            id,
            cancellationToken);
        if (theme is null)
        {
            return null;
        }

        CardThemeManifestEntity? manifest =
            await manifestRepository.GetByIdAsync(
                theme.ManifestId,
                cancellationToken);
        if (manifest is null)
        {
            return null;
        }

        CardThemeAssetEntity? asset = await assetRepository.GetByIdAsync(
            manifest.PreviewAssetId,
            cancellationToken);
        return asset is null ? null : Map(theme, manifest, asset);
    }

    public async Task<IReadOnlyList<GameDataDto>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CardThemeEntity> themes =
            await cardThemeRepository.ListAsync(
                cancellationToken: cancellationToken);
        IReadOnlyList<CardThemeManifestEntity> manifests =
            await manifestRepository.ListAsync(
                cancellationToken: cancellationToken);
        IReadOnlyList<CardThemeAssetEntity> assets =
            await assetRepository.ListAsync(
                cancellationToken: cancellationToken);

        Dictionary<string, CardThemeManifestEntity> manifestsById =
            manifests.ToDictionary(manifest => manifest.Id);
        Dictionary<string, CardThemeAssetEntity> assetsById =
            assets.ToDictionary(asset => asset.Id);

        List<GameDataDto> result = [];
        foreach (CardThemeEntity theme in themes)
        {
            if (!manifestsById.TryGetValue(theme.ManifestId, out CardThemeManifestEntity? manifest)
                || !assetsById.TryGetValue(manifest.PreviewAssetId, out CardThemeAssetEntity? asset))
            {
                continue;
            }

            result.Add(Map(theme, manifest, asset));
        }

        return result;
    }

    public async Task<GameDataDto?> UpdateAsync(
        string id,
        string themeName,
        bool isDefault,
        string base64Image,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(themeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(base64Image);

        CardThemeEntity? theme = await cardThemeRepository.GetByIdAsync(
            id,
            cancellationToken);
        if (theme is null)
        {
            return null;
        }

        CardThemeManifestEntity? manifest =
            await manifestRepository.GetByIdAsync(
                theme.ManifestId,
                cancellationToken);
        if (manifest is null)
        {
            return null;
        }

        CardThemeAssetEntity? asset = await assetRepository.GetByIdAsync(
            manifest.PreviewAssetId,
            cancellationToken);
        if (asset is null)
        {
            return null;
        }

        manifest.ThemeName = themeName;
        manifest.IsDefault = isDefault;
        asset.Base64Image = base64Image;
        _ = await manifestRepository.UpdateAsync(manifest, cancellationToken);
        _ = await assetRepository.UpdateAsync(asset, cancellationToken);
        return Map(theme, manifest, asset);
    }

    public async Task<bool> DeleteAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        CardThemeEntity? theme = await cardThemeRepository.GetByIdAsync(
            id,
            cancellationToken);
        if (theme is null)
        {
            return false;
        }

        CardThemeManifestEntity? manifest =
            await manifestRepository.GetByIdAsync(
                theme.ManifestId,
                cancellationToken);
        if (manifest is not null)
        {
            _ = await assetRepository.RemoveAsync(
                manifest.PreviewAssetId,
                cancellationToken);
            _ = await manifestRepository.RemoveAsync(
                manifest.Id,
                cancellationToken);
        }

        return await cardThemeRepository.RemoveAsync(
            theme.Id,
            cancellationToken);
    }

    private static GameDataDto Map(
        CardThemeEntity theme,
        CardThemeManifestEntity manifest,
        CardThemeAssetEntity asset) =>
        new(
            theme.Id,
            manifest.Id,
            manifest.ThemeName,
            manifest.IsDefault,
            asset.Id,
            asset.Base64Image);
}
