using Android.Content;
using Android.OS;
using Doopass.Core.Diagnostics;
using Doopass.WebUi.Abstractions;
using Microsoft.Maui.ApplicationModel;

namespace Doopass.Mobile.Services;

/// <summary>
/// Android clipboard. The WebView path (navigator.clipboard / execCommand) is
/// blocked because the copy happens after an awaited vault read, so the user
/// gesture is gone; ClipboardManager has neither requirement. The clip is also
/// flagged as sensitive so Android 13+ does not preview the secret.
/// </summary>
public sealed class AndroidClipboard : ISecretClipboard
{
    private const string SensitiveExtra = "android.content.extra.IS_SENSITIVE";

    /// <inheritdoc />
    public async Task<bool> SetTextAsync(string text, CancellationToken ct = default)
    {
        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var manager = (ClipboardManager?)Android.App.Application.Context
                    .GetSystemService(Context.ClipboardService);

                if (manager is null)
                {
                    return;
                }

                var clip = ClipData.NewPlainText("Doopass secret", text);

                if (clip is null)
                {
                    return;
                }

                if (OperatingSystem.IsAndroidVersionAtLeast(33) && clip.Description is not null)
                {
                    var extras = new PersistableBundle();
                    extras.PutBoolean(SensitiveExtra, true);
                    clip.Description.Extras = extras;
                }

                manager.PrimaryClip = clip;
            });

            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Writing to the Android clipboard failed", ex);
            return false;
        }
    }
}
