using System.Globalization;
using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Spreadsheet = DocumentFormat.OpenXml.Spreadsheet;

namespace ProcurementRadar.UnitTests.Documents;

/// <summary>
/// Builds the file formats the extractor has to cope with.
/// </summary>
/// <remarks>
/// These are format samples, not site fixtures: what is under test is whether a
/// pdf, a docx and a zip can be read, which does not depend on any particular
/// authority's document. Adapter fixtures still have to be real captured pages.
/// </remarks>
internal static class SampleFiles
{
    /// <summary>
    /// A complete, if very small, PDF carrying one line of text. Written out by
    /// hand because every library that produces PDFs is either a dependency this
    /// project does not need or one with licence conditions.
    /// </summary>
    public static byte[] Pdf(string text)
    {
        var content = $"BT /F1 24 Tf 72 700 Td ({text}) Tj ET";

        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R "
                + "/Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };

        var body = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();

        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(body.Length);
            body.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xrefOffset = body.Length;

        body.Append($"xref\n0 {objects.Length + 1}\n");
        body.Append("0000000000 65535 f \n");

        foreach (var offset in offsets)
        {
            // Every entry is exactly twenty bytes, which the format requires.
            body.Append($"{offset:D10} 00000 n \n");
        }

        body.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\n");
        body.Append($"startxref\n{xrefOffset}\n%%EOF");

        return Encoding.ASCII.GetBytes(body.ToString());
    }

    /// <summary>A PDF with a page and no text on it, like a scanned sheet.</summary>
    public static byte[] PdfWithoutText() => Pdf(string.Empty);

    public static byte[] Docx(string text)
    {
        using var stream = new MemoryStream();

        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var body = new Body(new Paragraph(new Run(new Text(text))));
            document.AddMainDocumentPart().Document = new Document(body);
        }

        return stream.ToArray();
    }

    /// <summary>
    /// A workbook whose cells hold the given rows, written the way Excel writes
    /// them: the strings live in the shared string table and each cell carries an
    /// index into it. A reader that skips that indirection gets numbers back.
    /// </summary>
    public static byte[] Xlsx(params string[][] rows)
    {
        using var stream = new MemoryStream();

        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbook = document.AddWorkbookPart();
            workbook.Workbook = new Spreadsheet.Workbook();

            var strings = rows.SelectMany(row => row).Distinct(StringComparer.Ordinal).ToList();

            workbook.AddNewPart<SharedStringTablePart>().SharedStringTable =
                new Spreadsheet.SharedStringTable(
                    strings.Select(value =>
                        new Spreadsheet.SharedStringItem(new Spreadsheet.Text(value))));

            var sheetData = new Spreadsheet.SheetData();

            foreach (var row in rows)
            {
                sheetData.Append(new Spreadsheet.Row(row.Select(value => new Spreadsheet.Cell
                {
                    DataType = Spreadsheet.CellValues.SharedString,
                    CellValue = new Spreadsheet.CellValue(
                        strings.IndexOf(value).ToString(CultureInfo.InvariantCulture))
                })));
            }

            var worksheet = workbook.AddNewPart<WorksheetPart>();
            worksheet.Worksheet = new Spreadsheet.Worksheet(sheetData);

            workbook.Workbook
                .AppendChild(new Spreadsheet.Sheets())
                .AppendChild(new Spreadsheet.Sheet
                {
                    Id = workbook.GetIdOfPart(worksheet),
                    SheetId = 1,
                    Name = "Teklif Cetveli"
                });
        }

        return stream.ToArray();
    }

    public static byte[] Zip(params (string Name, byte[] Content)[] entries)
    {
        using var stream = new MemoryStream();

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var entryStream = archive.CreateEntry(name).Open();
                entryStream.Write(content);
            }
        }

        return stream.ToArray();
    }

    public static byte[] Text(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>Bytes that claim to be a PDF and are not.</summary>
    public static byte[] BrokenPdf() => Encoding.ASCII.GetBytes("%PDF-1.4\nthis is not a pdf at all");

    /// <summary>Bytes that claim to be a zip and are not.</summary>
    public static byte[] BrokenZip() => [0x50, 0x4B, 0x03, 0x04, 0xFF, 0xFE, 0x00, 0x11, 0x22];
}
