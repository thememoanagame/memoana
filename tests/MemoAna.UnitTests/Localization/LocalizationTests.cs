using System.Globalization;
using System.Resources;
using MemoAna.Common.Abstract.Localization;
using MemoAna.Common.Localization;
using GameStrings = MemoAna.Game.Resources.Localization.Strings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Xunit;

namespace MemoAna.UnitTests.Localization;

public sealed class LocalizationTests
{
    [Fact]
    public void GameLocalizer_UsesPortugueseResources()
    {
        using CultureScope _ = new("pt-BR");
        ILocalizer<GameStrings> localizer = CreateLocalizer();

        Assert.Equal("Tema não disponível.", localizer["ThemeUnavailable"]);
    }

    [Fact]
    public void GameLocalizer_UsesEnglishResources()
    {
        using CultureScope _ = new("en-US");
        ILocalizer<GameStrings> localizer = CreateLocalizer();

        Assert.Equal("Theme unavailable.", localizer["ThemeUnavailable"]);
    }

    [Fact]
    public void GameLocalizer_ResolvesResourcesFromGameAssembly()
    {
        ILocalizer<GameStrings> localizer = CreateLocalizer();

        Assert.Equal("PVP network error.", WithCulture("en-US", () => localizer["PvpNetworkError"]));
    }

    [Fact]
    public void GameResources_HaveTheSameKeysInAllCultures()
    {
        ResourceManager manager = new(typeof(GameStrings).FullName!, typeof(GameStrings).Assembly);
        HashSet<string> neutral = Keys(manager.GetResourceSet(CultureInfo.InvariantCulture, true, false));
        HashSet<string> english = Keys(manager.GetResourceSet(CultureInfo.GetCultureInfo("en-US"), true, false));
        HashSet<string> portuguese = Keys(manager.GetResourceSet(CultureInfo.GetCultureInfo("pt-BR"), true, false));

        Assert.Equal(neutral, english);
        Assert.Equal(neutral, portuguese);
    }

    private static HashSet<string> Keys(ResourceSet? resourceSet) =>
        resourceSet?.Cast<System.Collections.DictionaryEntry>().Select(entry => (string)entry.Key).ToHashSet()
        ?? throw new InvalidOperationException("Localization resource set was not found.");

    private static ILocalizer<GameStrings> CreateLocalizer()
    {
        ServiceCollection services = new();
        services.AddLocalization(options => options.ResourcesPath = "Resources/Localization");
        services.AddLogging();
        services.AddSingleton(typeof(ILocalizer<>), typeof(Localizer<>));
        return services.BuildServiceProvider().GetRequiredService<ILocalizer<GameStrings>>();
    }

    private static string WithCulture(string culture, Func<string> action)
    {
        using CultureScope _ = new(culture);
        return action();
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo previousUiCulture = CultureInfo.CurrentUICulture;
        private readonly CultureInfo previousCulture = CultureInfo.CurrentCulture;

        public CultureScope(string culture)
        {
            CultureInfo selected = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = selected;
            CultureInfo.CurrentCulture = selected;
        }

        public void Dispose()
        {
            CultureInfo.CurrentUICulture = previousUiCulture;
            CultureInfo.CurrentCulture = previousCulture;
        }
    }
}
