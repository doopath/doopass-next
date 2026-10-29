using Microsoft.AspNetCore.Components.WebView.Maui;

namespace Doopass.Mobile;

/// <summary>
/// The single content page: a BlazorWebView rooted at the mobile app router
/// component (ConnectGuard + RCL Routes with the Connect page attached).
/// </summary>
public sealed class Vault : Microsoft.Maui.Controls.ContentPage
{
    /// <summary>Creates the page that hosts the Blazor WebView.</summary>
    /// <param name="services">App service provider (the WebView resolves components from it).</param>
    public Vault(IServiceProvider services)
    {
        Title = "Doopass";

        // The WebView picks its service provider from the app root (AddMauiBlazorWebView()).
        var webView = new BlazorWebView
        {
            HostPage = "wwwroot/index.html",
        };

        webView.RootComponents.Add(new RootComponent
        {
            Selector = "#app",
            ComponentType = typeof(Components.AppRoot),
        });

        Content = webView;
    }
}
