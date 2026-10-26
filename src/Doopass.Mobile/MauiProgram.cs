using Doopass.Core.Diagnostics;
using Doopass.Mobile.Services;
using Doopass.WebUi.Abstractions;
using Doopass.WebUi.Services;
using Microsoft.Extensions.Logging;

namespace Doopass.Mobile;

public static partial class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>();

        // One session per app (the MAUI root scope hosts "scoped" services for
        // the whole lifetime), matching the prompt's thin-client design.
        builder.Services.AddSingleton<ConnectionStore>();
        // The shared login screen resolves this optionally: only the phone has
        // another machine holding the vault, so only it shows "Change host".
        builder.Services.AddSingleton<HostConnectionState>();
        builder.Services.AddSingleton<IHostConnection>(sp => sp.GetRequiredService<HostConnectionState>());
        builder.Services.AddSingleton<HostDiscovery>();
        builder.Services.AddSingleton<VaultPushClient>();
        builder.Services.AddScoped<VaultEventRelay>();
        builder.Services.AddScoped<RemoteVaultSession>();
        builder.Services.AddScoped<IVaultSession>(sp => sp.GetRequiredService<RemoteVaultSession>());
        builder.Services.AddScoped<IVaultEvents>(sp => sp.GetRequiredService<VaultEventRelay>());
        builder.Services.AddScoped<ToastService>();

        // The WebView clipboard API is blocked outside a user gesture; copy
        // secrets through the native clipboard instead.
        builder.Services.AddScoped<ISecretClipboard, AndroidClipboard>();

        // The window content page is resolved from DI in App.CreateWindow.
        builder.Services.AddTransient<Vault>();

        builder.Services.AddMauiBlazorWebView();

        // Host logging on the phone sandbox; secrets never reach the log.
        AppLogger.Initialize(
            Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "doopass.log"));

        // Startup failures must land somewhere: write them to the same log and
        // echo to logcat (adb logcat shows it even before the UI exists).
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                AppLogger.Error("Unhandled exception", exception);
            }

            Console.WriteLine($"[Doopass] Unhandled: {args.ExceptionObject}");
        };

#if ANDROID
        Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, args) =>
        {
            AppLogger.Error("Android unhandled exception", args.Exception);
        };
#endif

        return builder.Build();
    }
}
