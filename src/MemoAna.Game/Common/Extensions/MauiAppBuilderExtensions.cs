using MemoAna.Game.Core;
using MemoAna.Common.Abstract.ExceptionHandler;
using MemoAna.Common.Abstract.Localization;
using MemoAna.Common.Abstract.Repositories;
using MemoAna.Common.Abstract.Services;
using MemoAna.Common.Localization;
using MemoAna.Common.Repositories;
using MemoAna.Common.Services;
using MemoAna.Game.Abstract.Services;
using MemoAna.Game.Services;
using MemoAna.Game.Models;
using MemoAna.Common.Constants;
using MemoAna.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using Plugin.Maui.Audio;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Globalization;

namespace MemoAna.Common.Extensions;

public static class MauiAppBuilderExtensions
{
    extension(MauiAppBuilder builder)
    {
        public MauiApp RunGame<TApp>(Action<MauiAppBuilder> configurePresentation)
            where TApp : Application
        {
            CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("pt-BR");
            CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("pt-BR");
            CultureInfo.CurrentCulture = CultureInfo.DefaultThreadCurrentCulture;
            CultureInfo.CurrentUICulture = CultureInfo.DefaultThreadCurrentUICulture;
            return builder.UseMauiApp<TApp>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("Bangpop.ttf", "Bangpop");
                    fonts.AddFont("Gwenchana.ttf", "Gwenchana");
                })
                .UseInfrastructure()
                .UseApplication()
                .UsePresentation(configurePresentation)
                .Build()
                .ConfigureExceptionHandler()
                .TryMigrateDb();
        }
        
        private MauiAppBuilder UseAppLogging()
        {
            if (!Directory.Exists(Constants.Constants.Directories.Logs))
            {
                Directory.CreateDirectory(Constants.Constants.Directories.Logs);
            }

            string path = System.IO.Path.Combine(Constants.Constants.Directories.Logs, "log-.txt");
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel
                .Is(Serilog.Events.LogEventLevel.Verbose) // Define o nível mínimo de log
                .WriteTo
                .Console(Serilog.Events.LogEventLevel.Verbose, outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo
                .File(
                    path: path,
                    rollingInterval: RollingInterval.Day, // Cria um arquivo por dia
                    retainedFileCountLimit: 7,            // Mantém apenas os últimos 7 dias
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
                )
                .CreateLogger();

            builder.Logging.AddSerilog(Log.Logger, dispose: true);
            builder.Logging.AddConsole();
#if DEBUG
            builder.Logging.AddDebug();
#endif
            return builder;
        }

        private MauiAppBuilder UseApplication()
        {
            builder.Services.AddSingleton<IPermissionPlatformAdapter, DefaultPermissionPlatformAdapter>();
            builder.Services.AddSingleton<IPermissionService, PermissionService>();
            builder.Services.AddSingleton<IGameService, GameService>();
            builder.Services.AddSingleton<IAIService, AIService>();
            builder.Services.AddSingleton<IPVPService, PVPService>();
            builder.Services.AddSingleton<IRandomSource, RandomSource>();
            builder.Services.AddScoped<IImageConverterService, ImageConverterService>();
            builder.Services.AddScoped<MemoryCard>();
            return builder;
        }

        private MauiAppBuilder UseLocalization()
        {
            builder.Services.AddLocalization(options => options.ResourcesPath = "Resources/Localization");
            builder.Services.AddSingleton(typeof(ILocalizer<>), typeof(Localizer<>));
            return builder;
        }

        private  MauiAppBuilder UseInfrastructure()
        {
            builder.UseAppLogging();
            builder.AddSqlite();
            builder.UseLocalization();
            builder.Services.AddTransient<IPlatformService, PlatformService>();
            builder.Services.AddTransient<IPermissionService, PermissionService>();
            builder.Services.AddTransient<IExceptionHandler, ExceptionHandler>();
            builder.Services.AddSingleton<IAudioService, AudioService>();
            builder.Services.AddScoped<ISettingsService, SettingsService>();
            builder.Services.AddScoped<IAssetService, AssetService>();
            builder.Services.AddScoped<IThemeService, ThemeService>();
            builder.Services.AddSingleton<HttpClient>();
            builder.Services.AddSingleton(AudioManager.Current);
            return builder;
        }
        private MauiAppBuilder UsePresentation(Action<MauiAppBuilder> configure)
        {
            builder.Services.AddSingleton(sp
                => Microsoft.Maui.Controls.Application.Current?.Dispatcher
                ?? Microsoft.Maui.Dispatching.Dispatcher.GetForCurrentThread()!);
            configure?.Invoke(builder);
            return builder;
        }
        private MauiAppBuilder AddSqlite()
        {
            if (!Directory.Exists(Constants.Constants.Directories.Database))
            {
                Directory.CreateDirectory(Constants.Constants.Directories.Database);
            }

            builder.Services.AddDbContext<GameDbContext>(options =>
            {
                options.UseSqlite($"Data Source={Path.Combine(Constants.Constants.Directories.Database, "memoana.db3")}", dbOptions =>
                {
                    dbOptions.CommandTimeout(TimeSpan.FromSeconds(60).Seconds);
                });
            });
            builder.Services.AddTransient<IUnitOfWork, UnitOfWork>();
            builder.Services.AddTransient(typeof(IRepository<>), typeof(Repository<>));
            return builder;
        }
    }
}