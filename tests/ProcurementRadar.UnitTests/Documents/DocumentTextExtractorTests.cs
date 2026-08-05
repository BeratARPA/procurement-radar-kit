using Microsoft.Extensions.Logging.Abstractions;
using ProcurementRadar.Collector;

namespace ProcurementRadar.UnitTests.Documents;

public sealed class DocumentTextExtractorTests
{
    private static readonly DocumentTextExtractor Extractor =
        new(NullLogger<DocumentTextExtractor>.Instance);

    [Fact]
    public void Text_comes_out_of_a_pdf()
    {
        var result = Extractor.Extract(SampleFiles.Pdf("Yazilim Bakim Hizmeti Alimi"), "sartname.pdf");

        Assert.Equal("pdf", result.Kind);
        Assert.Contains("Yazilim Bakim Hizmeti Alimi", result.Text!, StringComparison.Ordinal);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void Text_comes_out_of_a_word_document()
    {
        var result = Extractor.Extract(SampleFiles.Docx("Mevcut sisteme modül eklenmesi"), "teknik.docx");

        Assert.Equal("docx", result.Kind);
        Assert.Contains("modül eklenmesi", result.Text!, StringComparison.Ordinal);
    }

    [Fact]
    public void Cells_come_out_of_a_spreadsheet()
    {
        var result = Extractor.Extract(
            SampleFiles.Xlsx(
                ["Sıra No", "Malın / Hizmetin Adı", "Miktar", "Birim"],
                ["1", "Personel Takip Yazılımı Bakım ve Destek Hizmeti", "12", "Ay"]),
            "teklif-cetveli.xlsx");

        Assert.Equal("xlsx", result.Kind);
        Assert.Contains("Bakım ve Destek Hizmeti", result.Text!, StringComparison.Ordinal);
        Assert.Contains("Malın / Hizmetin Adı", result.Text!, StringComparison.Ordinal);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void A_spreadsheet_is_not_read_as_a_plain_archive()
    {
        // The regression this guards is silent, which is why it is worth its own
        // test. An .xlsx is a zip, so it opens perfectly well as one - and every
        // entry inside is xml the extractor has no reader for, so the result is
        // an empty string and no complaint. Forty per cent of the documents the
        // first two live sources link to are spreadsheets.
        var result = Extractor.Extract(
            SampleFiles.Xlsx(["Web Sitesi Yenileme İşi"]),
            "cetvel.xlsx");

        Assert.NotEqual("zip", result.Kind);
        Assert.Contains("Web Sitesi Yenileme İşi", result.Text!, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_comes_out_of_the_files_inside_a_zip()
    {
        var zip = SampleFiles.Zip(
            ("idari.txt", SampleFiles.Text("İdari şartname metni")),
            ("teknik.pdf", SampleFiles.Pdf("Teknik sartname metni")));

        var result = Extractor.Extract(zip, "ihale.zip");

        Assert.Equal("zip", result.Kind);
        Assert.Contains("İdari şartname metni", result.Text!, StringComparison.Ordinal);
        Assert.Contains("Teknik sartname metni", result.Text!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_zip_named_rar_is_still_read()
    {
        // Authorities do this. The leading bytes decide, not the extension.
        var result = Extractor.Extract(
            SampleFiles.Zip(("belge.txt", SampleFiles.Text("içerik"))),
            "ekler.rar");

        Assert.Contains("içerik", result.Text!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("bozuk.pdf")]
    [InlineData("bozuk.zip")]
    [InlineData("bilinmeyen.xyz")]
    public void A_file_that_cannot_be_read_costs_a_warning_and_nothing_else(string fileName)
    {
        var content = fileName.EndsWith(".pdf", StringComparison.Ordinal)
            ? SampleFiles.BrokenPdf()
            : fileName.EndsWith(".zip", StringComparison.Ordinal)
                ? SampleFiles.BrokenZip()
                : [0x01, 0x02, 0x03];

        // The point of the ticket: no exception escapes, whatever arrives.
        var result = Extractor.Extract(content, fileName);

        Assert.Null(result.Text);

        // And the other half of the test's own name: it costs a warning. The
        // unknown extension used to cost nothing at all.
        Assert.NotEmpty(result.Problems);
    }

    [Fact]
    public void A_file_that_yields_no_text_at_all_says_so()
    {
        // An archive of images opens cleanly, contains nothing readable, and
        // would otherwise be stored as a document with an empty text column -
        // indistinguishable from one the extractor never looked at.
        var result = Extractor.Extract(
            SampleFiles.Zip(("plan.png", [0x89, 0x50, 0x4E, 0x47])),
            "ekler.zip");

        Assert.Null(result.Text);
        Assert.Contains("nothing could be read", string.Join(" ", result.Problems), StringComparison.Ordinal);
    }

    [Fact]
    public void A_broken_file_is_reported_rather_than_passing_silently()
    {
        var result = Extractor.Extract(SampleFiles.BrokenPdf(), "bozuk.pdf");

        Assert.NotEmpty(result.Problems);
        Assert.Contains("bozuk.pdf", result.Problems[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_scanned_pdf_says_so_instead_of_looking_empty()
    {
        // Extremely common, and worth telling apart from a parser that broke.
        var result = Extractor.Extract(SampleFiles.PdfWithoutText(), "taranmis.pdf");

        Assert.Null(result.Text);
        Assert.Contains("no text layer", string.Join(" ", result.Problems), StringComparison.Ordinal);
    }

    [Fact]
    public void One_unreadable_entry_does_not_cost_the_rest_of_an_archive()
    {
        var zip = SampleFiles.Zip(
            ("bozuk.pdf", SampleFiles.BrokenPdf()),
            ("iyi.txt", SampleFiles.Text("okunabilir metin")));

        var result = Extractor.Extract(zip, "karisik.zip");

        Assert.Contains("okunabilir metin", result.Text!, StringComparison.Ordinal);
        Assert.NotEmpty(result.Problems);
    }

    [Fact]
    public void A_zip_inside_a_zip_is_followed()
    {
        var inner = SampleFiles.Zip(("ic.txt", SampleFiles.Text("iç metin")));
        var outer = SampleFiles.Zip(("ekler.zip", inner));

        Assert.Contains("iç metin", Extractor.Extract(outer, "dis.zip").Text!, StringComparison.Ordinal);
    }

    [Fact]
    public void Archives_nested_beyond_the_limit_are_left_alone()
    {
        var content = SampleFiles.Zip(("derin.txt", SampleFiles.Text("cok derin")));

        for (var depth = 0; depth < 5; depth++)
        {
            content = SampleFiles.Zip(($"katman{depth}.zip", content));
        }

        var result = Extractor.Extract(content, "matruska.zip");

        // A zip bomb is a zip bomb whether it is malice or an accident.
        Assert.Contains("nested more than", string.Join(" ", result.Problems), StringComparison.Ordinal);
        Assert.DoesNotContain("cok derin", result.Text ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_file_is_not_an_error()
    {
        var result = Extractor.Extract([], "bos.pdf");

        Assert.Null(result.Text);
    }
}
