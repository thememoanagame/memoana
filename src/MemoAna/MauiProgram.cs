using MemoAna.Common.Extensions;
using MudBlazor.Services;
namespace MemoAna;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp() => 
        MauiApp
        .CreateBuilder()
        .RunGame<App>(
            builder =>
            {
                builder.Services.AddSingleton<MainPage>();
                builder.Services.AddSingleton<App>();
#if DEBUG
                builder.Services.AddBlazorWebViewDeveloperTools();
#endif
                builder.Services.AddMauiBlazorWebView();
                builder.Services.AddMudServices();
            });
}
