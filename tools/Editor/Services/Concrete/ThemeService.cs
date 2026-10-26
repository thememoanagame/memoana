namespace Editor.Services.Concrete;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Editor.Models;

internal class ThemeService : Abstract.IThemeService
{
    private string _rootPath = string.Empty;
    private string ThemesDir => Path.Combine(_rootPath, "themes");
    private string PublicDir => Path.Combine(_rootPath, "public");
    private string RegistryPath => Path.Combine(_rootPath, "registry.json");

    private RootRegistry _registry = new();

    public async Task<List<string>> LoadRepositoryAsync(string rootPath)
    {
        _rootPath = rootPath;
        if (!Directory.Exists(ThemesDir))
            Directory.CreateDirectory(ThemesDir);

        if (File.Exists(RegistryPath))
        {
            var json = await File.ReadAllTextAsync(RegistryPath);
            _registry = JsonSerializer.Deserialize<RootRegistry>(json) ?? new RootRegistry();
        }

        var directories = Directory.GetDirectories(ThemesDir);
        return directories.Select(Path.GetFileName).ToList()!;
    }

    public async Task<List<CardItem>> LoadThemeCardsAsync(string themeName)
    {
        var themeDir = Path.Combine(ThemesDir, themeName);
        var files = Directory.GetFiles(themeDir, "*.webp")
                             .Select(Path.GetFileName)
                             .Where(f => f != "thumbnail.webp")
                             .ToList();

        var localizationPath = Path.Combine(themeDir, "localization.json");
        var existingCards = new List<CardItem>();

        if (File.Exists(localizationPath))
        {
            var json = await File.ReadAllTextAsync(localizationPath);
            existingCards = JsonSerializer.Deserialize<List<CardItem>>(json) ?? new();
        }

        // Garante que o registry tem o tema
        if (!_registry.Themes.ContainsKey(themeName))
            _registry.Themes[themeName] = new ThemeRegistry { Id = Guid.CreateVersion7().ToString() };

        var themeRegistry = _registry.Themes[themeName];
        var mergedCards = new List<CardItem>();

        foreach (var file in files)
        {
            // Atribui UUIDv7 imutável se for arquivo novo
            if (!themeRegistry.Images.ContainsKey(file!))
                themeRegistry.Images[file!] = Guid.CreateVersion7().ToString();

            var existing = existingCards.FirstOrDefault(c => c.FileName == file);
            mergedCards.Add(new CardItem
            {
                CardId = themeRegistry.Images[file!],
                FileName = file!,
                LocalFullPath = Path.Combine(themeDir, file!),
                Name = existing?.Name ?? new LocalizedName()
            });
        }

        return mergedCards;
    }

    public async Task SaveLocalizationAsync(string themeName, List<CardItem> cards)
    {
        var localizationPath = Path.Combine(ThemesDir, themeName, "localization.json");
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(cards, options);
        await File.WriteAllTextAsync(localizationPath, json);

        // Salva o registry atualizado também
        var registryJson = JsonSerializer.Serialize(_registry, options);
        await File.WriteAllTextAsync(RegistryPath, registryJson);
    }

    public async Task BuildStaticApiAsync()
    {
        if (Directory.Exists(PublicDir))
            Directory.Delete(PublicDir, true);

        Directory.CreateDirectory(Path.Combine(PublicDir, "themes"));

        var indexData = new List<object>();
        var themeDirs = Directory.GetDirectories(ThemesDir);
        var options = new JsonSerializerOptions { WriteIndented = true };

        foreach (var dir in themeDirs)
        {
            var themeName = Path.GetFileName(dir);
            var files = Directory.GetFiles(dir, "*.webp").Select(Path.GetFileName).ToList();

            // Validação estrita
            if (files.Count != 16 || !files.Contains("thumbnail.webp"))
                continue;

            var publicThemeDir = Path.Combine(PublicDir, "themes", themeName);
            Directory.CreateDirectory(publicThemeDir);

            // Copia as imagens
            foreach (var file in files)
                File.Copy(Path.Combine(dir, file!), Path.Combine(publicThemeDir, file!), true);

            // Lê as localizações salvas pelo Editor
            var localizationPath = Path.Combine(dir, "localization.json");
            var localizations = File.Exists(localizationPath)
                ? JsonSerializer.Deserialize<List<CardItem>>(await File.ReadAllTextAsync(localizationPath))
                : new List<CardItem>();

            // Monta o JSON de saída da coleção (formato final consumido pelo app do jogo)
            var publicCards = localizations!.Select(c => new
            {
                id = c.CardId,
                cardPath = $"/themes/{themeName}/{c.FileName}",
                name = c.Name
            }).ToList();

            var cardsJson = JsonSerializer.Serialize(publicCards, options);
            var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(cardsJson))).ToLower();

            await File.WriteAllTextAsync(Path.Combine(PublicDir, "themes", $"{themeName}.json"), cardsJson);

            indexData.Add(new
            {
                id = _registry.Themes[themeName].Id,
                name = char.ToUpper(themeName[0]) + themeName.Substring(1).Replace("-", " "),
                collectionPath = $"/themes/{themeName}.json",
                thumbnail = $"/themes/{themeName}/thumbnail.webp",
                count = publicCards.Count,
                hash
            });
        }

        await File.WriteAllTextAsync(Path.Combine(PublicDir, "index.json"), JsonSerializer.Serialize(indexData, options));
    }
}
