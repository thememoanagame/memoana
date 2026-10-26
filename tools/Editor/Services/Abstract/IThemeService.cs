using Editor.Models;

namespace Editor.Services.Abstract;

public interface IThemeService
{
    Task<List<string>> LoadRepositoryAsync(string rootPath);
    Task<List<CardItem>> LoadThemeCardsAsync(string themeName);
    Task SaveLocalizationAsync(string themeName, List<CardItem> cards);
    Task BuildStaticApiAsync();
}

