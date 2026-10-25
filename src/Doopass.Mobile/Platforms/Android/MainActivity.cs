using Android.App;
using Android.Content.PM;

namespace Doopass.Mobile.Platforms.Android;

/// <summary>
/// Single MAUI activity hosting the BlazorWebView. Android back is left to the
/// WebView's history handling; only system-level myths like closing the app
/// from "/connect" get tuned later if real devices show a problem.
/// </summary>
[Activity(
    Name = "io.github.doopath.doopass.MainActivity",
    Label = "Doopass",
    MainLauncher = true,
    Theme = "@style/Maui.SplashTheme",
    ConfigurationChanges = ConfigChanges.ScreenSize
        | ConfigChanges.Orientation
        | ConfigChanges.ScreenLayout
        | ConfigChanges.Keyboard
        | ConfigChanges.KeyboardHidden
        | ConfigChanges.UiMode)]
public sealed class MainActivity : MauiAppCompatActivity
{
}
