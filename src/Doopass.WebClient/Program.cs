using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
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
// JsClipboard is resolved per-request like the other thin-client services above;
// map it to its abstraction so pages (Store, Login) can inject ISecretClipboard.
builder.Services.AddScoped<ISecretClipboard>(sp => sp.GetRequiredService<Doopass.WebUi.Services.JsClipboard>());

// SignalR push channel for the thin client. Built below and driven by the
// session lifecycle, not resolved per-render from a child scope. The hub URL is
// built from the host base address (same origin as the API), not HttpClient's
// BaseAddress, which cannot be read in Blazor WebAssembly.
var baseAddress = builder.HostEnvironment.BaseAddress;
builder.Services.AddScoped(sp => new WebVaultPushClient(
    sp.GetRequiredService<IJSRuntime>(), sp.GetRequiredService<VaultEventRelay>(), baseAddress));

var host = builder.Build();

// Restore any token saved in sessionStorage before the app renders, so a
// reload (or a freshly opened tab) unlocks straight away. Then start the push
// channel for that session.
var session = host.Services.GetRequiredService<WebApiVaultSession>();
await session.InitializeAsync();

var push = host.Services.GetRequiredService<WebVaultPushClient>();
await push.StartAsync();

session.SessionActivated += async () => await push.StartAsync();
session.SessionCleared += async () => await push.StopAsync();

await host.RunAsync();
