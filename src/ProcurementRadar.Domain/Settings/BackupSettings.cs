using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProcurementRadar.Domain.Settings;

/// <summary>
/// Where the backup goes and what goes in it (D-24).
/// </summary>
/// <remarks>
/// A setting rather than a constant because the destination is the one part that
/// differs per machine and per week: a partition today, a USB disk when one is
/// plugged in, somewhere else when this moves. D-16 already has the mechanism -
/// the row in `setting` is what runs, the file beside it is only the seed.
///
/// The sizes that decided the shape, measured on 13 August 2026: the database
/// compresses to 1.1 MB and the file archive is 418 MB. So the database is
/// copied every run and the archive weekly, and the archive is mirrored rather
/// than snapshotted - `raw_fetch` rows are never deleted (D-04, architecture
/// rule 2), so nothing in it ever changes and a mirror is already a full copy.
/// </remarks>
public sealed record BackupSettings
{
    /// <summary>Where to write. Empty means backups are off, and the command says so.</summary>
    [JsonPropertyName("destination")]
    public string Destination { get; init; } = string.Empty;

    [JsonPropertyName("database")]
    public bool Database { get; init; } = true;

    /// <summary>How many dated dumps to keep. Each is about a megabyte.</summary>
    [JsonPropertyName("keep_dumps")]
    public int KeepDumps { get; init; } = 14;

    [JsonPropertyName("archive")]
    public bool Archive { get; init; } = true;

    [JsonPropertyName("archive_every_days")]
    public int ArchiveEveryDays { get; init; } = 7;

    public static BackupSettings Load(string json)
    {
        var loaded = JsonSerializer.Deserialize<BackupSettings>(json)
            ?? throw new InvalidOperationException("Backup settings could not be read.");

        // A retention of zero would delete the dump it just wrote, and a negative
        // interval would mirror 418 MB on every run. Both are refused here rather
        // than clamped silently: a number that does not do what it says is worse
        // than one that is rejected.
        if (loaded.KeepDumps < 1)
        {
            throw new InvalidOperationException("keep_dumps cannot be below 1.");
        }

        if (loaded.ArchiveEveryDays < 1)
        {
            throw new InvalidOperationException("archive_every_days cannot be below 1.");
        }

        return loaded;
    }

    /// <summary>True when a destination has been set at all.</summary>
    [JsonIgnore]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Destination);
}
