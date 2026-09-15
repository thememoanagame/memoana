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
        IReadOnlyList<string> base64Images,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeName);
        ValidateImages(base64Images);

        CardThemeEntity theme = new();
        CardThemeManifestEntity manifest = new()
        {
            CardThemeId = theme.Id,
            ThemeName = themeName,
            IsDefault = isDefault
        };
        CardThemeAssetEntity asset = new(theme.Id)
        {
            CardThemeId = theme.Id,
            Base64Images = [.. base64Images]
        };
        manifest.PreviewAssetId = asset.Id;
        theme.ManifestId = manifest.Id;

        await cardThemeRepository.AddAsync(theme, cancellationToken);
        await manifestRepository.AddAsync(manifest, cancellationToken);
        await assetRepository.UpsertAsync(
            asset,
            candidate => candidate.CardThemeId == theme.Id,
            cancellationToken);

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

        CardThemeAssetEntity? asset = await assetRepository.FirstOrDefaultAsync(
            candidate => candidate.CardThemeId == theme.Id,
            cancellationToken);
        return asset is null ? null : Map(theme, manifest, asset);
    }

    public async Task<GameDataDto?> GetByThemeNameAsync(
        string themeName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeName);
        CardThemeManifestEntity? manifest = (await manifestRepository.ListAsync(
            candidate => candidate.ThemeName == themeName,
            cancellationToken)).FirstOrDefault();
        if (manifest is null)
        {
            return null;
        }

        CardThemeAssetEntity? asset = await assetRepository.FirstOrDefaultAsync(
            candidate => candidate.CardThemeId == manifest.CardThemeId,
            cancellationToken);
        return asset is null
            ? null
            : new GameDataDto(
                manifest.CardThemeId,
                manifest.Id,
                manifest.ThemeName,
                manifest.IsDefault,
                asset.Id,
                asset.Base64Images);
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
        Dictionary<string, CardThemeAssetEntity> assetsByThemeId =
            assets.ToDictionary(asset => asset.CardThemeId);

        List<GameDataDto> result = [];
        foreach (CardThemeEntity theme in themes)
        {
            if (!manifestsById.TryGetValue(theme.ManifestId, out CardThemeManifestEntity? manifest)
                || !assetsByThemeId.TryGetValue(theme.Id, out CardThemeAssetEntity? asset))
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
        IReadOnlyList<string> base64Images,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(themeName);
        ValidateImages(base64Images);

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

        CardThemeAssetEntity? asset = await assetRepository.FirstOrDefaultAsync(
            candidate => candidate.CardThemeId == theme.Id,
            cancellationToken);
        if (asset is null)
        {
            return null;
        }

        manifest.ThemeName = themeName;
        manifest.IsDefault = isDefault;
        asset.Base64Images = [.. base64Images];
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
            CardThemeAssetEntity? asset = await assetRepository.FirstOrDefaultAsync(
                candidate => candidate.CardThemeId == theme.Id,
                cancellationToken);
            if (asset is not null)
            {
                _ = await assetRepository.RemoveAsync(asset.Id, cancellationToken);
            }
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
            asset.Base64Images);

    private static void ValidateImages(IReadOnlyList<string> images)
    {
        ArgumentNullException.ThrowIfNull(images);
        if (images.Count == 0 || images.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "At least one non-empty Base64 image is required.",
                nameof(images));
        }
    }
}
