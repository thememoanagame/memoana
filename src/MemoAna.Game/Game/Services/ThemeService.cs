using MemoAna.Common.Abstract.Repositories;
using MemoAna.Game.Abstract.Services;
using MemoAna.Game.Dtos;
using MemoAna.Game.Entities;

namespace MemoAna.Game.Services;

public sealed class ThemeService(IRepository<CardThemeManifestEntity> repository) : IThemeService
{
    public async Task<IReadOnlyCollection<CardThemeManifestDto>> GetThemesAsync(bool include = false)
        => [.. (await repository.ListAsync(x => true, include ? [x => x.CardTheme]: null!, CancellationToken.None))
            .Select(theme => new CardThemeManifestDto(theme) )];

    public async Task<CardThemeDto> GetThemeAsync(string themeName)
    {
        var manifest = await repository.FirstOrDefaultAsync(x => x.ThemeName == themeName, [x => x.CardTheme], CancellationToken.None);
        return manifest is null ? throw new InvalidOperationException($"Theme '{themeName}' not found.") : new CardThemeDto(manifest.CardTheme!.ImagesSrc, manifest.Id, new(manifest));
    }
}
