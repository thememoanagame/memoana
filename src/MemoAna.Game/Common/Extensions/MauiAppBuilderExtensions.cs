using MemoAna.ame.Services;
using MemoAna.Game.Core;
using MemoAna.Common.Abstract.Localization;
using MemoAna.Common.Abstract.Repositories;
using MemoAna.Common.Abstract.Services;
using MemoAna.Common.Localization;
using MemoAna.Common.Repositories;
using MemoAna.Common.Services;
using MemoAna.Game.Abstract.Services;
using MemoAna.Game.Services;
using MemoAna.Game.Models;
using MemoAna.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using MemoAna.Common.Enums;
using Plugin.Maui.Audio;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Sinks.ILogger;
using Microsoft.Extensions.Hosting;
using System.Globalization;

namespace MemoAna.Common.Extensions;

public static class MauiAppBuilderExtensions
{
    extension(MauiAppBuilder builder)
    {
        public MauiApp RunMauiApp<TApp>(Action<MauiAppBuilder> configurePresentation)
            where TApp : Application
        {
            CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("pt-BR");
            CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("pt-BR");
            CultureInfo.CurrentCulture = CultureInfo.DefaultThreadCurrentCulture;
            CultureInfo.CurrentUICulture = CultureInfo.DefaultThreadCurrentUICulture;
            builder.UseMauiApp<TApp>().ConfigureFonts(fonts =>
            {
                fonts.AddFont("Bangpop.ttf", "Bangpop");
                fonts.AddFont("Gwenchana.ttf", "Gwenchana");
            });
            string path = System.IO.Path.Combine(FileSystem.AppDataDirectory, "logs", "log-.txt");
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
#if ANDROID
            builder.Services.AddTransient<IPlatformService, PlatformService>((sp) => new PlatformService(MauiPlatform.Android));
#endif
#if MACCATALYST

            builder.Services.AddTransient<IPlatformService, PlatformService>((sp) => new PlatformService(MauiPlatform.MacOS));
#endif
#if IOS
            builder.Services.AddTransient<IPlatformService, PlatformService>((sp) => new PlatformService(MauiPlatform.IOS));
#endif
#if WINDOWS
            builder.Services.AddTransient<IPlatformService, PlatformService>((sp) => new PlatformService(MauiPlatform.Windows));
#endif
            builder.Services.AddLocalization(options => options.ResourcesPath = "Resources/Localization");
            builder.Services.AddSingleton(typeof(ILocalizer<>), typeof(Localizer<>));
            
            return builder.AddInfrastructure()
                .AddApplication()
                .AddPresentation(configurePresentation)
                .TryMigrateDb()
                .TrySeed();
        }
        
        private MauiAppBuilder AddApplication()
        {
            builder.Services.AddSingleton<IGameService, GameService>();
            builder.Services.AddSingleton<IAIService, AIService>();
            builder.Services.AddSingleton<IPVPService, PVPService>();
            builder.Services.AddSingleton<IRandomSource, RandomSource>();
            builder.Services.AddScoped<IImageConverterService, ImageConverterService>();
            builder.Services.AddScoped<MemoryCard>();
            return builder;
        }

        private  MauiAppBuilder AddInfrastructure()
        {
            builder.AddSqlite();
            builder.Services.AddScoped<IAudioService, AudioService>();
            builder.Services.AddScoped<ISettingsService, SettingsService>();
            builder.Services.AddScoped<IThemeService, ThemeService>();
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
        private MauiAppBuilder AddSqlite()
        {
            builder.Services.AddDbContext<GameDbContext>(options =>
            {
                string dir = Path.Combine(FileSystem.AppDataDirectory, "resources");
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                options.UseSqlite($"Data Source={Path.Combine(dir, "memoana.db3")}", dbOptions =>
                {
                    dbOptions.CommandTimeout(TimeSpan.FromSeconds(60).Seconds);
                });
            });
            builder.Services.AddTransient<IUnitOfWork, UnitOfWork>();
            builder.Services.AddTransient(typeof(IRepository<>), typeof(Repository<>));
            return builder;
        }

        private MauiApp TryMigrateDb()
        {
            MauiApp app = builder.Build();
            using var scope = app.Services.CreateScope();
            GameDbContext context = scope.ServiceProvider.GetRequiredService<GameDbContext>();
            try
            {
                if (context.Database.GetPendingMigrations().Any())
                {
                    context.Database.Migrate();
                }
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 1 || ex.SqliteErrorCode == 19)
            {
                try
                {
                    Console.WriteLine($"Database migration/merge failed: {ex.Message}");
                    Console.WriteLine("Deleting and Migrating...");
                    context.Database.EnsureDeleted();
                    context.Database.Migrate();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Database delete/migrate failed: {e.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Database migration/merge failed: {ex.Message}");
            } 
            finally 
            { 
                Console.WriteLine("Starting application..."); 
            }
            return app;
        } 
    }
}

