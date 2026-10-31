using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Doopass.WebClient;
using Doopass.WebUi.Abstractions;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// DI: same abstractions resolved to web-specific implementations.
builder.Services.AddScoped<VaultEventRelay>();
builder.Services.AddScoped<WebApiVaultSession>();
builder.Services.AddScoped<IVaultSession>(sp => sp.GetRequiredService<WebApiVaultSession>());
builder.Services.AddScoped<IVaultEvents>(sp => sp.GetRequiredService<VaultEventRelay>());
builder.Services.AddScoped<Doopass.WebUi.Services.ToastService>();
builder.Services.AddScoped<Doopass.WebUi.Services.JsClipboard>();

// SignalR push channel: started manually after login.
builder.Services.AddScoped<WebVaultPushClient>();

await builder.Build().RunAsync();
