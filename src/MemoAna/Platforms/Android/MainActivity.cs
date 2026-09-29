using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.Core.View;
namespace MemoAna.Platforms.Android
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            EnterFullscreen();
        }

        private void EnterFullscreen()
        {
            if (Window is null)
                return;

            var controller = WindowCompat.GetInsetsController(
                Window,
                Window.DecorView);

            if (controller is null)
                return;

            controller.Hide(WindowInsetsCompat.Type.SystemBars());

            controller.SystemBarsBehavior =
                WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
        }
    }
}
