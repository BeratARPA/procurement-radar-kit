using System.Text;

namespace ProcurementRadar.UnitTests.Scoring;

/// <summary>
/// Turkish belongs in config/, never in a .cs file.
/// </summary>
/// <remarks>
/// The rule is not about tidiness. Keyword lists, date formats, contact labels
/// and suffix lists are the parts of this system that get tuned weekly by
/// somebody reading notices, and a term buried in a string literal needs a
/// developer, a rebuild and a deploy to change. Comments are exempt: they
/// explain the data, and quoting a Turkish word to say what it does is not the
/// same as depending on it.
/// </remarks>
public sealed class TurkishInCodeTests
{
    private static readonly char[] TurkishLetters =
        ['ç', 'ğ', 'ı', 'ö', 'ş', 'ü', 'Ç', 'Ğ', 'İ', 'Ö', 'Ş', 'Ü', 'â', 'î', 'û', 'Â', 'Î', 'Û'];

    [Fact]
    public void No_production_source_file_carries_Turkish_outside_a_comment()
    {
        var root = RepositoryRoot.Find();
        var sourceRoot = Path.Combine(root.FullName, "src");
        var offenders = new List<string>();
        var scanned = 0;

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(file))
            {
                continue;
            }

            scanned++;

            var code = WithoutComments(File.ReadAllText(file));
            var line = 1;

            foreach (var character in code)
            {
                if (character == '\n')
                {
                    line++;
                }
                else if (Array.IndexOf(TurkishLetters, character) >= 0)
                {
                    offenders.Add(
                        $"{Path.GetRelativePath(root.FullName, file)}:{line} contains '{character}'");
                }
            }
        }

        Assert.True(scanned > 0, $"No source file was scanned under '{sourceRoot}'.");

        Assert.True(
            offenders.Count == 0,
            "Turkish text belongs in config/*.json, not in code:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders.Distinct()));
    }

    [Fact]
    public void The_comment_stripper_keeps_code_and_drops_comments()
    {
        // The check above is only worth as much as this is. A stripper that ate
        // everything would pass it silently.
        const string Source = """
            // yorum: yazılım
            var a = "kod";
            /* blok: bakım */
            var b = "https://example.gov.tr"; // sondaki yorum: donanım
            var c = @"verbatim // not a comment";
            """;

        var stripped = WithoutComments(Source);

        Assert.Contains("\"kod\"", stripped, StringComparison.Ordinal);
        Assert.Contains("https://example.gov.tr", stripped, StringComparison.Ordinal);
        Assert.Contains("verbatim // not a comment", stripped, StringComparison.Ordinal);

        Assert.DoesNotContain("yazılım", stripped, StringComparison.Ordinal);
        Assert.DoesNotContain("bakım", stripped, StringComparison.Ordinal);
        Assert.DoesNotContain("donanım", stripped, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Turkish_term_in_a_string_literal_is_caught()
    {
        var stripped = WithoutComments("""var t = "yazılım";""");

        Assert.Contains(stripped, character => Array.IndexOf(TurkishLetters, character) >= 0);
    }

    private static bool IsBuildOutput(string path)
    {
        var separator = Path.DirectorySeparatorChar;

        return path.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
            || path.Contains($"{separator}bin{separator}", StringComparison.Ordinal);
    }

    /// <summary>
    /// Removes comments and leaves everything else, including string literals.
    /// Tracks strings so that a <c>//</c> inside a url is not read as the start
    /// of a comment.
    /// </summary>
    private static string WithoutComments(string source)
    {
        var kept = new StringBuilder(source.Length);
        var i = 0;

        while (i < source.Length)
        {
            var current = source[i];
            var next = i + 1 < source.Length ? source[i + 1] : '\0';

            if (current == '/' && next == '/')
            {
                while (i < source.Length && source[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            if (current == '/' && next == '*')
            {
                i += 2;
                while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/'))
                {
                    // Newlines are kept so reported line numbers stay honest.
                    if (source[i] == '\n')
                    {
                        kept.Append('\n');
                    }

                    i++;
                }

                i += 2;
                continue;
            }

            if (current == '"' && next == '"' && i + 2 < source.Length && source[i + 2] == '"')
            {
                i = CopyRawString(source, i, kept);
                continue;
            }

            if (current == '@' && next == '"')
            {
                i = CopyVerbatimString(source, i, kept);
                continue;
            }

            if (current is '"' or '\'')
            {
                i = CopyQuoted(source, i, kept, current);
                continue;
            }

            kept.Append(current);
            i++;
        }

        return kept.ToString();
    }

    private static int CopyQuoted(string source, int start, StringBuilder kept, char quote)
    {
        kept.Append(source[start]);
        var i = start + 1;

        while (i < source.Length && source[i] != quote)
        {
            if (source[i] == '\\' && i + 1 < source.Length)
            {
                kept.Append(source[i]);
                i++;
            }

            kept.Append(source[i]);
            i++;
        }

        if (i < source.Length)
        {
            kept.Append(source[i]);
            i++;
        }

        return i;
    }

    private static int CopyVerbatimString(string source, int start, StringBuilder kept)
    {
        kept.Append(source[start]).Append(source[start + 1]);
        var i = start + 2;

        while (i < source.Length)
        {
            if (source[i] == '"')
            {
                if (i + 1 < source.Length && source[i + 1] == '"')
                {
                    kept.Append('"').Append('"');
                    i += 2;
                    continue;
                }

                kept.Append('"');
                return i + 1;
            }

            kept.Append(source[i]);
            i++;
        }

        return i;
    }

    private static int CopyRawString(string source, int start, StringBuilder kept)
    {
        kept.Append("\"\"\"");
        var i = start + 3;

        while (i + 2 < source.Length
               && !(source[i] == '"' && source[i + 1] == '"' && source[i + 2] == '"'))
        {
            kept.Append(source[i]);
            i++;
        }

        kept.Append("\"\"\"");

        return Math.Min(i + 3, source.Length);
    }
}
