using Doopass.WebUi.Abstractions;
using Microsoft.JSInterop;

namespace Doopass.WebUi.Services;

/// <summary>
/// Browser clipboard. app.js tries navigator.clipboard first and falls back to
/// a hidden textarea plus execCommand('copy'); it returns false instead of
/// throwing when the browser refuses (plain HTTP on the LAN, no user gesture).
/// </summary>
public sealed class JsClipboard(IJSRuntime js) : ISecretClipboard
{
    public async Task<bool> SetTextAsync(string text, CancellationToken ct = default)
    {
        try
        {
            return await js.InvokeAsync<bool>("doopass.copyText", ct, text);
        }
        catch (JSDisconnectedException)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (JSException)
        {
            return false;
        }
    }
}
