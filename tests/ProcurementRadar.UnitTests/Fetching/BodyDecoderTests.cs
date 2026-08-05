using System.Text;
using ProcurementRadar.Collector;

namespace ProcurementRadar.UnitTests.Fetching;

/// <summary>
/// Turkish authority pages are still routinely served as windows-1254, and a
/// wrong guess here corrupts exactly the characters the keyword filter reads.
/// </summary>
public sealed class BodyDecoderTests
{
    private const string Turkish = "Yazılım Bakım ve Destek Hizmeti Alımı — İhale Şartnamesi";

    // Same text without the em dash. iso-8859-9 has no code point for it, so a
    // round trip through that encoding would lose the character and the test
    // would be measuring the wrong thing.
    private const string TurkishInLatin5 = "Yazılım Bakım ve Destek Hizmeti Alımı - İhale Şartnamesi";

    static BodyDecoderTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void The_charset_from_the_content_type_header_is_used()
    {
        var bytes = Encoding.GetEncoding("windows-1254").GetBytes(Turkish);

        var (text, charSet) = BodyDecoder.Decode(bytes, "windows-1254");

        Assert.Equal(Turkish, text);
        Assert.Equal("windows-1254", charSet);
    }

    [Fact]
    public void A_meta_tag_is_used_when_the_header_says_nothing()
    {
        var html = $"<html><head><meta charset=\"iso-8859-9\"></head><body>{TurkishInLatin5}</body></html>";
        var bytes = Encoding.GetEncoding("iso-8859-9").GetBytes(html);

        var (text, charSet) = BodyDecoder.Decode(bytes, contentTypeCharSet: null);

        Assert.Contains(TurkishInLatin5, text, StringComparison.Ordinal);
        Assert.Equal("iso-8859-9", charSet);
    }

    [Fact]
    public void The_older_http_equiv_form_of_the_meta_tag_also_works()
    {
        var html = "<html><head><meta http-equiv=\"Content-Type\" "
            + $"content=\"text/html; charset=windows-1254\"></head><body>{Turkish}</body></html>";
        var bytes = Encoding.GetEncoding("windows-1254").GetBytes(html);

        var (text, _) = BodyDecoder.Decode(bytes, contentTypeCharSet: null);

        Assert.Contains(Turkish, text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_byte_order_mark_beats_a_header_that_disagrees_with_it()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(Turkish)).ToArray();

        var (text, _) = BodyDecoder.Decode(bytes, "windows-1254");

        Assert.Equal(Turkish, text);
    }

    [Fact]
    public void Utf8_is_the_fallback_when_nothing_declares_anything()
    {
        var bytes = Encoding.UTF8.GetBytes(Turkish);

        var (text, charSet) = BodyDecoder.Decode(bytes, contentTypeCharSet: null);

        Assert.Equal(Turkish, text);
        Assert.Equal("utf-8", charSet);
    }

    [Fact]
    public void An_encoding_nobody_has_heard_of_does_not_throw_the_page_away()
    {
        var bytes = Encoding.UTF8.GetBytes(Turkish);

        var (text, charSet) = BodyDecoder.Decode(bytes, "utf-42");

        Assert.Equal(Turkish, text);
        Assert.Equal("utf-8", charSet);
    }

    [Fact]
    public void Guessing_utf8_on_a_windows_1254_page_would_corrupt_it()
    {
        // The point of the whole class, stated as a test: this is what the naive
        // path produces, and why the detection above has to happen.
        var bytes = Encoding.GetEncoding("windows-1254").GetBytes(Turkish);

        var naive = Encoding.UTF8.GetString(bytes);

        Assert.NotEqual(Turkish, naive);
        Assert.Contains('�', naive);
    }
}
