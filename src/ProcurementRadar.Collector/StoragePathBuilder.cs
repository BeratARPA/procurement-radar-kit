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
