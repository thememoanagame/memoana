using MemoAna.Game.Dtos;

namespace MemoAna.Game.Abstract.Services;

public interface IThemeService
{
    Task<IReadOnlyCollection<CardThemeManifestDto>> GetThemesAsync(bool include = false);
    Task<CardThemeDto> GetThemeAsync(string themeName);
}
