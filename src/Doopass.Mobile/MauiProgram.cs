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
        builder.Services.AddSingleton<HostDiscovery>();
        builder.Services.AddSingleton<VaultPushClient>();
        builder.Services.AddScoped<VaultEventRelay>();
        builder.Services.AddScoped<RemoteVaultSession>();
        builder.Services.AddScoped<IVaultSession>(sp => sp.GetRequiredService<RemoteVaultSession>());
        builder.Services.AddScoped<IVaultEvents>(sp => sp.GetRequiredService<VaultEventRelay>());
        builder.Services.AddScoped<ToastService>();

        builder.Services.AddMauiBlazorWebView();

        // Host logging on the phone sandbox; secrets never reach the log.
        AppLogger.Initialize(
            Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "doopass.log"));

        return builder.Build();
    }
}
