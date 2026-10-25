using Android.App;
using Android.Runtime;

namespace Doopass.Mobile.Platforms.Android;

[Application(Name = "io.github.doopath.doopass.MainApplication")]
public sealed class MainApplication(IntPtr handle, JniHandleOwnership ownership) : MauiApplication(handle, ownership)
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
