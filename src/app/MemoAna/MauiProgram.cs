using MemoAna.Common.Extensions;
using MudBlazor.Services;
#if MAUI_DEVFLOW
using Microsoft.Maui.DevFlow.Agent;
using Microsoft.Maui.DevFlow.Blazor;
#endif
namespace MemoAna;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp() => 
        MauiApp
        .CreateBuilder()
        .RunMauiApp<App>(
            builder =>
            {
                builder.Services.AddMauiBlazorWebView();
                builder.Services.AddMudServices();
#if MAUI_DEVFLOW
                builder.AddMauiDevFlowAgent();
                builder.AddMauiBlazorDevFlowTools();
#endif
            });
}
