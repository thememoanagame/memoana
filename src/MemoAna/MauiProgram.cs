using System.Reflection;
using MemoAna.Common.Extensions;
using Microsoft.Extensions.Configuration;
using MudBlazor.Services;
namespace MemoAna;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        var assembly = Assembly.GetExecutingAssembly();

        // 2. Definir o namespace raiz onde o arquivo está (substitua pelo seu namespace)
        string baseNamespace = "MemoAna";

        // 3. Carregar o appsettings.json base
        using var streamBase = assembly.GetManifestResourceStream($"{baseNamespace}.appsettings.json");
        if (streamBase != null)
        {
            builder.Configuration.AddJsonStream(streamBase);
        }

        // 4. Determinar o ambiente e carregar o arquivo específico para sobrescrever o base
        string envFileName = GetEnvironmentSettingsFileName();
        using var streamEnv = assembly.GetManifestResourceStream($"{baseNamespace}.{envFileName}");
        if (streamEnv != null)
        {
            builder.Configuration.AddJsonStream(streamEnv);
        }
        builder.CreateGame<App>(b =>
            {
                b.Services.AddMauiBlazorWebView();
                b.Services.AddMudServices();
            });
        return builder.Build();
    }

    private static string GetEnvironmentSettingsFileName()
    {
#if DEV_TUNNEL
        return "appsettings.Tunnel.json";
#elif DEBUG
        return "appsettings.Development.json";
#else
        return "appsettings.Production.json";
#endif
    }
}
