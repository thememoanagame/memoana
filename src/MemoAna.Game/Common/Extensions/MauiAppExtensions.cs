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
        internal MauiApp ConfigureExceptionHandler()
               {
                   var handler = app.Services.GetRequiredService<IExceptionHandler>();
                   AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                   {
                       if (e.ExceptionObject is Exception ex)
                           _ = Task.Run(async () => await handler.HandleAsync(ex)); // fire-and-forget, cuidado com deadlocks
                   };

                   TaskScheduler.UnobservedTaskException += (s, e) =>
                   {
                       _ = handler.HandleAsync(e.Exception);
                       e.SetObserved();
                   };

        #if ANDROID
                   Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (s, e) =>
                   {
                       _ = handler.HandleAsync(e.Exception);
                       e.Handled = true;
                   };
        #elif IOS || MACCATALYST
               ObjCRuntime.Runtime.MarshalManagedException += (s, e) =>
               {
                   _ = handler.HandleAsync(e.Exception);
               };
        #elif WINDOWS

               // dentro do RegisterGlobalHandlers
               if (Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?
                       .Services.GetService<Microsoft.UI.Xaml.Application>() is { } winUiApp)
               {
                   winUiApp.UnhandledException += (s, e) =>
                   {
                       e.Handled = true; // <- crucial: impede o crash do processo
                       _ = handler.HandleAsync(e.Exception);
                   };
               }
        #endif
                   return app;
               }

        internal MauiApp TryMigrateDb()
        {
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