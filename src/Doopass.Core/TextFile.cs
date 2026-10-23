using System.Text;

namespace Doopass.Core;

/// <summary>
/// Shared text encoding settings. The original Python application reads/writes files
/// as UTF-8 <b>without</b> a BOM, so every file we write must match (a BOM breaks
/// <c>json.load</c> and pollutes <c>store.enc</c>).
/// </summary>
public static class TextFile
{
    /// <summary>UTF-8 encoding that does not emit a byte order mark.</summary>
    public static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
}
