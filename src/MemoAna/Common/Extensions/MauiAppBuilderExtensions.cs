using MemoAna.Common.Abstract.Localization;
using MemoAna.Common.Concrete.Localization;
using MemoAna.Game.Services.Abstract;
using MemoAna.Game.Services.Concrete;


namespace MemoAna.Common.Extensions;

public static class MauiAppBuilderExtensions
{
    extension(MauiAppBuilder builder)
    {
        public MauiAppBuilder RunMauiApp<TApp>(Action<MauiAppBuilder> configurePresentation)
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
            builder.Services.AddSingleton<HttpClient>();
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
