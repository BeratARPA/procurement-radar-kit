using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;
using UglyToad.PdfPig;
using Spreadsheet = DocumentFormat.OpenXml.Spreadsheet;

namespace ProcurementRadar.Collector;

public sealed record ExtractedText(string? Text, string Kind, IReadOnlyList<string> Problems);

/// <summary>
/// Pulls readable text out of a specification file.
/// </summary>
/// <remarks>
/// Works on bytes, never on paths, and never throws. These files come from
/// eighty different authorities: some are scanned images with no text at all,
/// some are archives inside archives, and some are simply broken. Any of those
/// has to cost one warning, not the run.
/// </remarks>
public sealed class DocumentTextExtractor
{
    /// <summary>Archives nested this deep are almost always a mistake or a trap.</summary>
    private const int MaximumArchiveDepth = 3;

    private const int MaximumEntriesPerArchive = 200;

    private readonly ILogger<DocumentTextExtractor> _logger;

    public DocumentTextExtractor(ILogger<DocumentTextExtractor> logger)
    {
        _logger = logger;
    }

    public ExtractedText Extract(byte[] content, string? fileName)
    {
        var problems = new List<string>();
        var kind = KindOf(content, fileName);
        var text = Extract(content, fileName, depth: 0, problems);

        if (string.IsNullOrWhiteSpace(text) && problems.Count == 0)
        {
            // A file was downloaded, kept, and produced nothing, and no step along
            // the way thought that worth reporting. That is the failure this
            // project fears most (spec Bölüm 11): not a crash, but a quiet empty
            // result that reads exactly like a document with nothing in it. Say it
            // out loud, whatever the reason - a format nobody wrote a reader for,
            // an archive of images, an empty sheet.
            Note(problems, $"{fileName}: nothing could be read out of it (kind: {kind})");
        }

        return new ExtractedText(
            string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
            kind,
            problems);
    }

    private string? Extract(byte[] content, string? fileName, int depth, List<string> problems)
        => KindOf(content, fileName) switch
        {
            "pdf" => FromPdf(content, fileName, problems),
            "docx" => FromWord(content, fileName, problems),
            "xlsx" => FromSpreadsheet(content, fileName, problems),
            "zip" or "rar" or "7z" => FromArchive(content, fileName, depth, problems),
            "text" => Encoding.UTF8.GetString(content),
            _ => null
        };

    /// <summary>
    /// Decided by the leading bytes first and the extension only as a fallback.
    /// An authority naming a zip ".rar" is common enough to plan for.
    /// </summary>
    private static string KindOf(byte[] content, string? fileName)
    {
        if (StartsWith(content, "%PDF"u8))
        {
            return "pdf";
        }

        // .docx, .xlsx and .zip are all zip containers; the difference is what is
        // inside, so the extension decides between them. Getting this wrong is not
        // harmless: a spreadsheet read as a plain archive yields its own xml
        // plumbing and no cell text at all.
        if (StartsWith(content, [0x50, 0x4B, 0x03, 0x04]))
        {
            return Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant() switch
            {
                ".docx" or ".dotx" => "docx",
                ".xlsx" or ".xltx" => "xlsx",
                _ => "zip"
            };
        }

        if (StartsWith(content, "Rar!"u8))
        {
            return "rar";
        }

        if (StartsWith(content, [0x37, 0x7A, 0xBC, 0xAF]))
        {
            return "7z";
        }

        return Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant() switch
        {
            ".txt" or ".csv" or ".htm" or ".html" => "text",
            ".pdf" => "pdf",
            ".docx" or ".dotx" => "docx",
            ".xlsx" or ".xltx" => "xlsx",
            ".zip" => "zip",
            ".rar" => "rar",
            ".7z" => "7z",
            _ => "unknown"
        };
    }

    private static bool StartsWith(byte[] content, ReadOnlySpan<byte> prefix)
        => content.Length >= prefix.Length && content.AsSpan(0, prefix.Length).SequenceEqual(prefix);

    private string? FromPdf(byte[] content, string? fileName, List<string> problems)
    {
        try
        {
            using var document = PdfDocument.Open(content);
            var builder = new StringBuilder();

            foreach (var page in document.GetPages())
            {
                builder.AppendLine(page.Text);
            }

            var text = builder.ToString();

            if (string.IsNullOrWhiteSpace(text))
            {
                // Very common: the authority scanned a printed page. There is
                // nothing to extract and no amount of retrying will change it.
                Note(problems, $"{fileName}: pdf carries no text layer, probably a scan");
            }

            return text;
        }
        catch (Exception exception)
        {
            Note(problems, $"{fileName}: pdf could not be read ({exception.Message})");
            return null;
        }
    }

    private string? FromWord(byte[] content, string? fileName, List<string> problems)
    {
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var document = WordprocessingDocument.Open(stream, isEditable: false);

            return document.MainDocumentPart?.Document?.Body?.InnerText;
        }
        catch (Exception exception)
        {
            Note(problems, $"{fileName}: word document could not be read ({exception.Message})");
            return null;
        }
    }

    /// <summary>
    /// Reads the cells of a spreadsheet, sheet by sheet, in the order a person
    /// would see the tabs.
    /// </summary>
    /// <remarks>
    /// Worth the trouble because of what these files usually are. Turkish
    /// authorities publish the "teklif cetveli" - the schedule listing what is
    /// being bought, line by line - as a spreadsheet, and that schedule is often
    /// the only place the actual items are named. The notice title above it can
    /// say nothing more than "ihtiyaçların karşılanması işi".
    /// </remarks>
    private string? FromSpreadsheet(byte[] content, string? fileName, List<string> problems)
    {
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var document = SpreadsheetDocument.Open(stream, isEditable: false);

            var workbook = document.WorkbookPart;

            if (workbook is null)
            {
                Note(problems, $"{fileName}: spreadsheet has no workbook part");
                return null;
            }

            var shared = workbook.SharedStringTablePart?.SharedStringTable;
            var builder = new StringBuilder();

            foreach (var sheet in workbook.Workbook?.Sheets?.Elements<Spreadsheet.Sheet>()
                     ?? Enumerable.Empty<Spreadsheet.Sheet>())
            {
                if (sheet.Id?.Value is not { } partId ||
                    workbook.GetPartById(partId) is not WorksheetPart { Worksheet: { } worksheet })
                {
                    continue;
                }

                foreach (var row in worksheet.Descendants<Spreadsheet.Row>())
                {
                    var line = string.Join(
                        '\t',
                        row.Elements<Spreadsheet.Cell>()
                            .Select(cell => ValueOf(cell, shared))
                            .Where(value => !string.IsNullOrWhiteSpace(value)));

                    if (line.Length > 0)
                    {
                        builder.AppendLine(line);
                    }
                }
            }

            return builder.ToString();
        }
        catch (Exception exception)
        {
            Note(problems, $"{fileName}: spreadsheet could not be read ({exception.Message})");
            return null;
        }
    }

    /// <summary>
    /// A cell's text. Most cells hold an index into the shared string table
    /// rather than the string itself, which is why reading the sheet xml alone
    /// gives numbers where the words should be.
    /// </summary>
    private static string? ValueOf(Spreadsheet.Cell cell, Spreadsheet.SharedStringTable? shared)
    {
        if (cell.DataType?.Value == Spreadsheet.CellValues.SharedString)
        {
            return shared is not null
                && int.TryParse(
                    cell.CellValue?.InnerText,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var index)
                && index >= 0
                && index < shared.ChildElements.Count
                    ? shared.ChildElements[index].InnerText
                    : null;
        }

        if (cell.DataType?.Value == Spreadsheet.CellValues.InlineString)
        {
            return cell.InlineString?.InnerText;
        }

        // Numbers, dates and booleans all arrive as their raw stored value. No
        // attempt is made to apply the cell format: what this text feeds is a
        // keyword filter, and a date serial number does it no harm.
        return cell.CellValue?.InnerText;
    }

    private string? FromArchive(byte[] content, string? fileName, int depth, List<string> problems)
    {
        if (depth >= MaximumArchiveDepth)
        {
            Note(problems, $"{fileName}: archives nested more than {MaximumArchiveDepth} deep are not followed");
            return null;
        }

        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var archive = ArchiveFactory.OpenArchive(stream);
            var builder = new StringBuilder();
            var entries = 0;

            foreach (var entry in archive.Entries)
            {
                if (entry.IsDirectory)
                {
                    continue;
                }

                if (++entries > MaximumEntriesPerArchive)
                {
                    Note(problems, $"{fileName}: stopped after {MaximumEntriesPerArchive} entries");
                    break;
                }

                try
                {
                    using var entryStream = entry.OpenEntryStream();
                    using var buffer = new MemoryStream();
                    entryStream.CopyTo(buffer);

                    var inner = Extract(buffer.ToArray(), entry.Key, depth + 1, problems);

                    if (!string.IsNullOrWhiteSpace(inner))
                    {
                        builder.AppendLine(inner);
                    }
                }
                catch (Exception exception)
                {
                    // One unreadable entry does not spoil the rest of the archive.
                    Note(problems, $"{fileName}/{entry.Key}: entry could not be read ({exception.Message})");
                }
            }

            return builder.ToString();
        }
        catch (Exception exception)
        {
            Note(problems, $"{fileName}: archive could not be opened ({exception.Message})");
            return null;
        }
    }

    private void Note(List<string> problems, string problem)
    {
        problems.Add(problem);
        _logger.LogWarning("{Problem}", problem);
    }
}
