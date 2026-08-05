using System.Text;
using System.Text.RegularExpressions;

namespace ProcurementRadar.Collector;

/// <summary>
/// Turns a downloaded body into text.
/// </summary>
/// <remarks>
/// This matters more here than it would on most sites. Turkish public authority
/// pages are still routinely served as windows-1254 or iso-8859-9, and plenty of
/// them declare the encoding only in a meta tag. Guessing UTF-8 turns every
/// ş, ğ and İ in a notice title into a replacement character, and the title is
/// what the keyword filter reads.
/// </remarks>
public static class BodyDecoder
{
    private const int SniffWindowBytes = 4096;

    private static readonly Regex MetaCharsetPattern = new(
        """<meta[^>]+charset\s*=\s*["']?\s*(?<charset>[a-z0-9_\-]+)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static BodyDecoder()
    {
        // windows-1254 and iso-8859-9 are not in the default .NET Core set.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Decodes <paramref name="bytes"/> and reports which encoding was used, so
    /// the choice is visible rather than guessed at again later.
    /// </summary>
    public static (string Text, string CharSet) Decode(byte[] bytes, string? contentTypeCharSet)
    {
        // A byte order mark is the only self-describing signal, so it wins. The
        // mark itself is stripped: left in place it becomes a zero-width
        // character at the head of the document, which then turns up in the
        // first parsed title.
        if (TryReadByteOrderMark(bytes, out var bomEncoding, out var markLength))
        {
            return (
                bomEncoding.GetString(bytes, markLength, bytes.Length - markLength),
                bomEncoding.WebName);
        }

        if (TryResolve(contentTypeCharSet, out var headerEncoding))
        {
            return (headerEncoding.GetString(bytes), headerEncoding.WebName);
        }

        if (TryResolve(SniffMetaCharSet(bytes), out var metaEncoding))
        {
            return (metaEncoding.GetString(bytes), metaEncoding.WebName);
        }

        return (Encoding.UTF8.GetString(bytes), Encoding.UTF8.WebName);
    }

    private static bool TryReadByteOrderMark(byte[] bytes, out Encoding encoding, out int markLength)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            encoding = Encoding.UTF8;
            markLength = 3;
            return true;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            encoding = Encoding.Unicode;
            markLength = 2;
            return true;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            encoding = Encoding.BigEndianUnicode;
            markLength = 2;
            return true;
        }

        encoding = Encoding.UTF8;
        markLength = 0;
        return false;
    }

    private static string? SniffMetaCharSet(byte[] bytes)
    {
        // The declaration is required to be near the top of the document, and
        // ASCII survives every encoding we care about, so a latin1 read of the
        // first few kilobytes is enough to find it.
        var window = Encoding.Latin1.GetString(bytes, 0, Math.Min(bytes.Length, SniffWindowBytes));
        var match = MetaCharsetPattern.Match(window);

        return match.Success ? match.Groups["charset"].Value : null;
    }

    private static bool TryResolve(string? name, out Encoding encoding)
    {
        encoding = Encoding.UTF8;

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        try
        {
            encoding = Encoding.GetEncoding(name.Trim().Trim('"', '\''));
            return true;
        }
        catch (ArgumentException)
        {
            // An encoding nobody has heard of. Fall through to the next signal
            // rather than throwing away the page.
            return false;
        }
    }
}
