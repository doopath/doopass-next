using Doopass.Mobile.Services;

namespace Doopass.Mobile;

/// <summary>Application shell: one window showing the Blazor WebView page.</summary>
public sealed class App(IServiceProvider services) : Microsoft.Maui.Controls.Application
{
    protected override Microsoft.Maui.Controls.Window CreateWindow(Microsoft.Maui.IActivationState? activationState) =>
        new(services.GetRequiredService<Vault>());
}
