using MemoAna.Common.Abstract.ExceptionHandler;
using MemoAna.Game.Entities;
using MemoAna.Common.Persistence;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
#if WINDOWS
using Microsoft.UI.Xaml;
#endif

namespace MemoAna.Common.Extensions;

internal static class MauiAppExtensions
{
    extension(MauiApp app)
    {
        //        internal MauiApp ConfigureExceptionHandler()
        //        {
        //            var handler = app.Services.GetRequiredService<IExceptionHandler>();
        //            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        //            {
        //                if (e.ExceptionObject is Exception ex)
        //                    _ = Task.Run(async () => await handler.HandleAsync(ex)); // fire-and-forget, cuidado com deadlocks
        //            };

        //            TaskScheduler.UnobservedTaskException += (s, e) =>
        //            {
        //                _ = handler.HandleAsync(e.Exception);
        //                e.SetObserved();
        //            };

        //#if ANDROID
        //            Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (s, e) =>
        //            {
        //                _ = handler.HandleAsync(e.Exception);
        //                e.Handled = true;
        //            };
        //#elif IOS || MACCATALYST
        //        ObjCRuntime.Runtime.MarshalManagedException += (s, e) =>
        //        {
        //            _ = handler.HandleAsync(e.Exception);
        //        };
        //#elif WINDOWS

        //        // dentro do RegisterGlobalHandlers
        //        if (Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?
        //                .Services.GetService<Microsoft.UI.Xaml.Application>() is { } winUiApp)
        //        {
        //            winUiApp.UnhandledException += (s, e) =>
        //            {
        //                e.Handled = true; // <- crucial: impede o crash do processo
        //                _ = handler.HandleAsync(e.Exception);
        //            };
        //        }
        //#endif
        //            return app;
        //        }
        internal MauiApp TryMigrate()
        {
            using IServiceScope scope = app.Services.CreateScope();
            GameDbContext context = scope.ServiceProvider.GetRequiredService<GameDbContext>();
            ILogger<MauiApp> logger = scope.ServiceProvider.GetRequiredService<ILogger<MauiApp>>();
            try
            {
                logger.LogInformation("Checking for pending migrations...");
                if (context.Database.GetPendingMigrations().Any())
                {
                    logger.LogInformation("Applying pending migrations...");
                    context.Database.Migrate();
                }
                return app;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to migrate database: {Message}", e.Message);
                context.Database.EnsureDeleted();
                logger.LogWarning("Database deleted due to migration failure. Recreating database...");
                context.Database.Migrate();
                logger.LogInformation("Database recreated successfully.");
                return app;
            }
        }
        internal MauiApp TrySeed()
        {
            using IServiceScope scope = app.Services.CreateScope();
            GameDbContext context = scope.ServiceProvider.GetRequiredService<GameDbContext>();
            ILogger<MauiApp> logger = scope.ServiceProvider.GetRequiredService<ILogger<MauiApp>>();

            try
            {
                var defaultThemes = new[] { "cars", "disney", "lion king", "marvel", "pokemon", "teen titans" };
                logger.LogInformation("Checking for existing default themes...");
                var existingThemesCount = context.CardThemeManifests
                    .Where(ct => defaultThemes.Contains(ct.ThemeName.ToLower()))
                    .Select(ct => ct.ThemeName.ToLower())
                    .Distinct()
                    .Count();

                if (existingThemesCount == defaultThemes.Length)
                {
                    logger.LogInformation("Themes already exist. Skipping seeding.");
                    return app;
                }

                var assembly = Assembly.GetExecutingAssembly();
                var resourceNames = assembly.GetManifestResourceNames();

                var existingThemes = context.CardThemeManifests
                    .Select(ct => ct.ThemeName.ToLower())
                    .ToList();
                logger.LogInformation("Checking for existing resources...");

                var pixarResources = resourceNames.Where(name => name.Contains("carros") && name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).ToList();
                var disneyResources = resourceNames.Where(name => name.Contains("disney") && name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)).ToList();
                var lionKingResources = resourceNames.Where(name => name.Contains("lion_king") && name.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)).ToList();
                var marvelResources = resourceNames.Where(name => name.Contains("marvel") && name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)).ToList();
                var pokemonResources = resourceNames.Where(name => name.Contains("pokemon") && name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).ToList();
                var teensTitansResources = resourceNames.Where(name => name.Contains("teen-titans") && name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).ToList();

                var hasChanges = false;

                logger.LogInformation("Seeding themes if they do not exist...");
                if (!existingThemes.Contains("cars") && pixarResources.Any())
                {
                    ProcessAndAddTheme(context, assembly, "Cars", pixarResources, "png");
                    hasChanges = true;
                    logger.LogInformation("Cars theme seeded successfully.");
                }

                if (!existingThemes.Contains("disney") && disneyResources.Any())
                {
                    ProcessAndAddTheme(context, assembly, "Disney", disneyResources, "jpeg");
                    hasChanges = true;
                    logger.LogInformation("Disney theme seeded successfully.");

                }

                if (!existingThemes.Contains("lion king") && lionKingResources.Any())
                {
                    ProcessAndAddTheme(context, assembly, "Lion King", lionKingResources, "webp");
                    hasChanges = true;
                    logger.LogInformation("Lion King theme seeded successfully.");  
                }   

                if (!existingThemes.Contains("marvel") && marvelResources.Any())
                {
                    ProcessAndAddTheme(context, assembly, "Marvel", marvelResources, "jpeg");
                    hasChanges = true;
                    logger.LogInformation("Marvel theme seeded successfully.");
                }

                if (!existingThemes.Contains("pokemon") && pokemonResources.Any())
                {
                    ProcessAndAddTheme(context, assembly, "Pokemon", pokemonResources, "png");
                    hasChanges = true;
                    logger.LogInformation("Pokemon theme seeded successfully.");
                }

                if (!existingThemes.Contains("teen titans") && teensTitansResources.Any())
                {
                    ProcessAndAddTheme(context, assembly, "Teen Titans", teensTitansResources, "png");
                    hasChanges = true;
                    logger.LogInformation("Teen Titans theme seeded successfully.");
                }

                if (hasChanges)
                {
                    context.SaveChanges();
                    logger.LogInformation("Database seeding completed successfully.");
                }

                return app;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to seed database: {Message}", e.Message);
                return app;
            }
        }
    }

    private static void ProcessAndAddTheme(GameDbContext context, Assembly assembly, string themeName, List<string> resources, string defaultExt)
    {
        if (resources.Count <= 0) return;

        var base64List = new List<string>();

        foreach (var resourceName in resources)
        {
            using Stream stream = assembly.GetManifestResourceStream(resourceName)!;
            if (stream == null) continue;

            using MemoryStream ms = new();
            stream.CopyTo(ms);
            byte[] imageBytes = ms.ToArray();

            string extension = resourceName.EndsWith(".png") ? "png" : defaultExt;
            string base64String = $"data:image/{extension};base64,{Convert.ToBase64String(imageBytes)}";

            base64List.Add(base64String);
        }

        if (base64List.Count <= 0) return;

        var manifestId = Guid.CreateVersion7().ToString();
        var themeId = Guid.CreateVersion7().ToString();

        var manifest = new CardThemeManifestEntity(manifestId)
        {
            ThemeName = themeName,
            IsDefault = true,
            PreviewBase64Image = base64List.First(),
            CardThemeId = themeId,
            Id = manifestId
        };

        var cardTheme = new CardThemeEntity(themeId)
        {
            Base64Images = base64List,
            ManifestId = manifestId,
            Manifest = manifest,
            Id = themeId
        };
    
        if (!context.CardThemeManifests.Any(x => x.ThemeName.ToLower().Equals(manifest.ThemeName.ToLower())))
        {
            context.CardThemeManifests.Add(manifest);
        }
    
        if (!context.CardThemeManifests.Any(x => x.Id.ToLower().Equals(cardTheme.Id.ToLower())))
        {
            context.CardThemes.Add(cardTheme);
        }  
            
    }
}