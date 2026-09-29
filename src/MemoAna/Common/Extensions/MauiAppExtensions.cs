using MemoAna.Common.Abstract.ExceptionHandler;
using MemoAna.Game.Entities;
using MemoAna.Common.Persistence;
using System.Reflection;
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

        internal MauiApp TrySeed()
        {
            try
            {
                using IServiceScope scope = app.Services.CreateScope();
                GameDbContext context = scope.ServiceProvider.GetRequiredService<GameDbContext>();

                if (!context.GameSettings.Any())
                {
                    context.GameSettings.Add(new GameSettingsEntity()
                    {
                        Id = Guid.CreateVersion7().ToString(),
                        Options = {
                            CardFlipDelayMs = 900,
                            CloudSaveEnabled = true,
                            ConfirmOnExit = true,
                            IsHapticFeedbackEnabled = true,
                            IsMusicEnabled = true,
                            IsSfxEnabled = true,
                            Language = Common.Enums.Language.system
                        }
                    });
                    context.SaveChanges();
                }
                var defaultThemes = new[] { "disney", "marvel", "pokemon", "cars" };

                var existingThemesCount = context.CardThemeManifests
                    .Where(ct => defaultThemes.Contains(ct.ThemeName.ToLower()))
                    .Select(ct => ct.ThemeName.ToLower())
                    .Distinct()
                    .Count();

                if (existingThemesCount == defaultThemes.Length)
                {
                    return app;
                }

                var assembly = Assembly.GetExecutingAssembly();
                var resourceNames = assembly.GetManifestResourceNames();

                var existingThemes = context.CardThemeManifests
                    .Select(ct => ct.ThemeName.ToLower())
                    .ToList();

                var pixarResources = resourceNames.Where(name => name.Contains("carros") && name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).ToList();
                var disneyResources = resourceNames.Where(name => name.Contains("disney") && name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)).ToList();
                var marvelResources = resourceNames.Where(name => name.Contains("marvel") && name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)).ToList();
                var pokemonResources = resourceNames.Where(name => name.Contains("pokemon") && name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).ToList();

                var hasChanges = false;

                if (!existingThemes.Contains("cars") && pixarResources.Any())
                {
                    ProcessAndAddTheme(context, assembly, "Cars", pixarResources, "png");
                    hasChanges = true;
                }

                if (!existingThemes.Contains("disney") && disneyResources.Any())
                {
                    ProcessAndAddTheme(context, assembly, "Disney", disneyResources, "jpeg");
                    hasChanges = true;
                }

                if (!existingThemes.Contains("marvel") && marvelResources.Any())
                {
                    ProcessAndAddTheme(context, assembly, "Marvel", marvelResources, "jpeg");
                    hasChanges = true;
                }

                if (!existingThemes.Contains("pokemon") && pokemonResources.Any())
                {
                    ProcessAndAddTheme(context, assembly, "Pokemon", pokemonResources, "png");
                    hasChanges = true;
                }

                if (hasChanges)
                {
                    context.SaveChanges();
                }

                return app;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to seed database: {e.Message}");
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