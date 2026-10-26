namespace Doopass.WebUi.Abstractions;

/// <summary>
/// Platform clipboard used by the store page. The web host copies through the
/// browser clipboard API; the Android thin client uses the native ClipboardManager,
/// which does not depend on a secure context or a still-valid user gesture.
/// Named <c>ISecretClipboard</c> to avoid clashing with MAUI's own IClipboard.
/// </summary>
public interface ISecretClipboard
{
    /// <summary>Puts <paramref name="text"/> on the clipboard.</summary>
    /// <returns><c>true</c> when the platform accepted the text.</returns>
    Task<bool> SetTextAsync(string text, CancellationToken ct = default);
}
