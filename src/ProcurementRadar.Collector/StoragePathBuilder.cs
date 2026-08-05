using System.Globalization;

namespace ProcurementRadar.Collector;

/// <summary>
/// Builds the archive layout fixed by D-04:
/// <c>{PR_DATA_DIR}/raw/{source_id}/{yyyy}/{MM}/{dd}/{raw_fetch_id}.html.gz</c>
/// </summary>
public sealed class StoragePathBuilder
{
    public const string DataDirectoryVariable = "PR_DATA_DIR";
    public const string DefaultDataDirectory = "./data";

    public StoragePathBuilder(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        DataDirectory = Path.GetFullPath(dataDirectory);
    }

    public string DataDirectory { get; }

    public static StoragePathBuilder FromEnvironment()
        => new(Environment.GetEnvironmentVariable(DataDirectoryVariable) is { Length: > 0 } configured
            ? configured
            : DefaultDataDirectory);

    /// <summary>
    /// The path stored in <c>raw_fetch.body_path</c>. Relative to the data
    /// directory and always with forward slashes, so the same rows work whether
    /// the archive is read on the Windows machine it was developed on or the
    /// Linux one it runs on, and so moving the directory does not invalidate
    /// every row.
    /// </summary>
    public string RawFetchPath(long sourceId, DateTimeOffset fetchedAt, long rawFetchId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rawFetchId);

        // Foldered by UTC date, like every other timestamp in the system (D-08).
        // Local dates would put two files from the same evening in different
        // folders depending on where the machine happens to be.
        var utc = fetchedAt.ToUniversalTime();

        return string.Create(
            CultureInfo.InvariantCulture,
            $"raw/{sourceId}/{utc:yyyy}/{utc:MM}/{utc:dd}/{rawFetchId}.html.gz");
    }

    /// <summary>
    /// The path stored in <c>notice_document.storage_path</c>, per D-04:
    /// <c>doc/{notice_id}/{sha256_first8}_{sanitized_file_name}</c>.
    /// </summary>
    /// <remarks>
    /// The hash prefix is what keeps two files called "sartname.pdf" on the same
    /// notice apart, and the sanitising is what keeps a file name an authority
    /// chose from deciding where on the disk it lands.
    /// </remarks>
    public string DocumentPath(long noticeId, string contentHash, string? fileName)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(noticeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        var prefix = contentHash.Length >= 8 ? contentHash[..8] : contentHash;

        return $"doc/{noticeId}/{prefix}_{Sanitize(fileName)}";
    }

    /// <summary>
    /// Reduces a file name from a web page to something safe to write.
    /// Separators, traversal and anything unusual are dropped rather than
    /// escaped.
    /// </summary>
    internal static string Sanitize(string? fileName)
    {
        const int MaximumLength = 80;
        const string Fallback = "document";

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return Fallback;
        }

        var span = fileName.AsSpan();
        var lastSeparator = span.LastIndexOfAny('/', '\\');
        if (lastSeparator >= 0)
        {
            span = span[(lastSeparator + 1)..];
        }

        var builder = new System.Text.StringBuilder(span.Length);

        foreach (var character in span)
        {
            if (char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')
            {
                builder.Append(character);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var cleaned = builder.ToString().Trim('-', '.');

        if (cleaned.Length > MaximumLength)
        {
            cleaned = cleaned[..MaximumLength];
        }

        return cleaned.Length == 0 ? Fallback : cleaned;
    }

    /// <summary>Resolves a stored path against the data directory.</summary>
    public string Absolute(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var native = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var combined = Path.GetFullPath(Path.Combine(DataDirectory, native));

        // A stored path is data, and data can be wrong. Refuse anything that
        // climbs out of the archive rather than reading an arbitrary file.
        if (!combined.StartsWith(DataDirectory, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"'{relativePath}' resolves outside the data directory.",
                nameof(relativePath));
        }

        return combined;
    }
}
