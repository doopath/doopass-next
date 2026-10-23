using System.Runtime.InteropServices;

namespace Doopass.Tui.Ui;

/// <summary>
/// Best-effort keyboard layout detection for the sign-in screen. Windows exposes the
/// active layout of the calling thread; elsewhere we fall back to the script of the
/// last typed character (see <see cref="ForText"/>).
/// </summary>
internal static class KeyboardLayout
{
    /// <summary>The layout that is active right now, as "RU" / "EN" / a two-letter code.</summary>
    public static string Current()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "EN";
        }

        try
        {
            var handle = GetKeyboardLayout(0);
            var languageId = (int)(handle.ToInt64() & 0xFFFF);

            return Map(languageId & 0x3FF);
        }
        catch
        {
            return "EN";
        }
    }

    /// <summary>The layout that produced the last character of <paramref name="text"/>.</summary>
    public static string ForText(string text)
    {
        if (text.Length == 0)
        {
            return Current();
        }

        return text[^1] switch
        {
            >= 'А' and <= 'я' => "RU",
            'Ё' or 'ё' => "RU",
            >= 'A' and <= 'z' => "EN",
            _ => Current(),
        };
    }

    private static string Map(int primaryLanguage) => primaryLanguage switch
    {
        0x19 => "RU",
        0x22 => "UK",
        0x23 => "BE",
        0x09 => "EN",
        0x07 => "DE",
        0x0C => "FR",
        0x0A => "ES",
        0x10 => "IT",
        0x15 => "PL",
        0x05 => "CS",
        0x1F => "TR",
        _ => "EN",
    };

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);
}
