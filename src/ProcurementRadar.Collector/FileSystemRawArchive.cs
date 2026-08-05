using System.IO.Compression;
using Microsoft.Extensions.Logging;

namespace ProcurementRadar.Collector;

public interface IRawArchive
{
    /// <summary>
    /// Writes one body and returns the path to store in
    /// <c>raw_fetch.body_path</c>.
    /// </summary>
    Task<string> WriteAsync(
        long sourceId,
        long rawFetchId,
        DateTimeOffset fetchedAt,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken);

    /// <summary>Reads a body back, exactly as it arrived.</summary>
    Task<byte[]> ReadAsync(string bodyPath, CancellationToken cancellationToken);
}

/// <summary>
/// Gzipped bodies on local disk (D-04). No object storage: the whole archive is
/// a couple of hundred megabytes a year.
/// </summary>
public sealed class FileSystemRawArchive : IRawArchive
{
    private readonly StoragePathBuilder _paths;
    private readonly ILogger<FileSystemRawArchive> _logger;

    public FileSystemRawArchive(StoragePathBuilder paths, ILogger<FileSystemRawArchive> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public async Task<string> WriteAsync(
        long sourceId,
        long rawFetchId,
        DateTimeOffset fetchedAt,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken)
    {
        var relativePath = _paths.RawFetchPath(sourceId, fetchedAt, rawFetchId);
        var absolutePath = _paths.Absolute(relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        // Written aside and then moved into place. A process that dies mid-write
        // leaves a stray .tmp rather than a truncated archive entry that would
        // read as a valid but half-empty page for the rest of the project's life.
        var temporaryPath = absolutePath + ".tmp";

        try
        {
            await using (var file = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                useAsync: true))
            await using (var gzip = new GZipStream(file, CompressionLevel.SmallestSize))
            {
                await gzip.WriteAsync(body, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, absolutePath, overwrite: true);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }

        _logger.LogInformation(
            "Archived {ByteCount} bytes for source {SourceId} at {BodyPath}",
            body.Length,
            sourceId,
            relativePath);

        return relativePath;
    }

    public async Task<byte[]> ReadAsync(string bodyPath, CancellationToken cancellationToken)
    {
        var absolutePath = _paths.Absolute(bodyPath);

        await using var file = new FileStream(
            absolutePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var buffer = new MemoryStream();

        await gzip.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        return buffer.ToArray();
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // The original failure is the one worth reporting.
        }
    }
}
