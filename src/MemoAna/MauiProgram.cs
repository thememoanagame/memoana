using MemoAna.Common.Extensions;
using MudBlazor.Services;
namespace MemoAna;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp() => 
        MauiApp
        .CreateBuilder()
        .RunMauiApp<App>(
            builder =>
            {
#if DEBUG
                builder.Services.AddBlazorWebViewDeveloperTools();
#endif
                builder.Services.AddMauiBlazorWebView();
                builder.Services.AddMudServices();
            });
}
