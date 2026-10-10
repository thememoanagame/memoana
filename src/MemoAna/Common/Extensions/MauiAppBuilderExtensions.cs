using MemoAna.Common.Abstract.Localization;
using MemoAna.Common.Concrete.Localization;
using MemoAna.Game.Services.Abstract;
using MemoAna.Game.Services.Concrete;
using MemoAna.Infrastructure.Api;
using MemoAna.Infrastructure.Assets;
using MemoAna.Infrastructure.Session;
using MemoAna.Infrastructure.SignalR;
using System.Text.Json;
using System.Text.Json.Serialization;


namespace MemoAna.Common.Extensions;

public static class MauiAppBuilderExtensions
{
    extension(MauiAppBuilder builder)
    {
        public MauiAppBuilder CreateGame<TApp>(Action<MauiAppBuilder> configurePresentation)
            where TApp : Application
        {
            builder.UseMauiApp<TApp>()
                .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("Gwenchana.ttf", "Gwenchana");
            });
            
#if DEBUG
            builder.Logging.AddDebug();
#endif
            builder.Services.AddLocalization(options => options.ResourcesPath = "Resources/Localization");
            builder.Services.AddSingleton<ILocalizer, Localizer>();
            builder.AddInfrastructure()
                .AddApplication()
                .AddPresentation(configurePresentation);
            return builder;
        }
        
        private MauiAppBuilder AddApplication()
        {
            return builder;
        }

        private  MauiAppBuilder AddInfrastructure()
        {
            builder.Services.AddScoped<IAudioService, AudioService>();
            builder.Services.AddSingleton(sp =>
            {
                var baseUrl = builder.Configuration["ApiOptions:BaseUrl"] ?? "https://memoana.gdhub.xyz";
                return new HttpClient { BaseAddress = new Uri(baseUrl, UriKind.Absolute), Timeout = TimeSpan.FromSeconds(45) };
            });
            builder.Services.AddSingleton(sp =>
            {
                var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
                options.Converters.Add(new JsonStringEnumConverter());
                 return options;
            });
            builder.Services.AddSingleton<IGameApiClient, GameApiClient>();
            builder.Services.AddSingleton<IGameSessionStore, SecureGameSessionStore>();
            builder.Services.AddSingleton<IGameAssetStore, LocalGameAssetStore>();
            builder.Services.AddSingleton<IGameHubClient>(sp =>
            {
                var baseUrl = builder.Configuration["ApiOptions:BaseUrl"] ?? "https://memoana.gdhub.xyz";
                return new GameHubClient(new Uri(baseUrl, UriKind.Absolute), sp.GetRequiredService<JsonSerializerOptions>(), sp.GetRequiredService<ILogger<GameHubClient>>());
            });
            builder.Services.AddSingleton<GameSessionCoordinator>(sp =>
            {
                var coordinator = new GameSessionCoordinator(sp.GetRequiredService<IGameApiClient>(), sp.GetRequiredService<IGameSessionStore>(), sp.GetRequiredService<IGameAssetStore>(), sp.GetRequiredService<IGameHubClient>(), sp.GetRequiredService<ILogger<GameSessionCoordinator>>());
                coordinator.Start();
                return coordinator;
            });
            builder.Services.AddSingleton(AudioManager.Current);
            return builder;
        }

        private MauiAppBuilder AddPresentation(Action<MauiAppBuilder> configure)
        {
            builder.Services.AddSingleton(sp
                => Microsoft.Maui.Controls.Application.Current?.Dispatcher
                ?? Microsoft.Maui.Dispatching.Dispatcher.GetForCurrentThread()!);
            configure?.Invoke(builder);
            return builder;
        }
    }
}
