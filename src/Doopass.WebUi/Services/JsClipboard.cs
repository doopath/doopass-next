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
    /// <summary>
    /// Copies <paramref name="text"/> through the browser helper; never throws,
    /// because a refused copy must not tear down the circuit.
    /// </summary>
    /// <param name="text">Secret to place on the clipboard.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> when the browser accepted the text.</returns>
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
