namespace ProcurementRadar.Collector;

/// <summary>
/// Finds the config/ directory holding the Turkish data files.
/// </summary>
/// <remarks>
/// Not simply "config/ relative to the working directory": that differs between
/// the two hosts. `dotnet run` gives the CLI the shell's directory and the
/// worker its own project directory, and systemd gives whatever the unit file
/// says. Looking beside the binaries and then walking up finds it in a checkout,
/// in a published folder, and under systemd alike.
/// </remarks>
public static class ConfigDirectory
{
    public const string Name = "config";

    /// <summary>A file that must be there, used to recognise the right directory.</summary>
    private const string Marker = "keywords.json";

    /// <summary>
    /// Resolves the directory, or throws with every place that was looked at.
    /// </summary>
    public static string Find(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var given = Path.GetFullPath(explicitPath);

            return Directory.Exists(given)
                ? given
                : throw new DirectoryNotFoundException($"No config directory at '{given}'.");
        }

        var tried = new List<string>();

        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var directory = new DirectoryInfo(start);

            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, Name);
                tried.Add(candidate);

                if (File.Exists(Path.Combine(candidate, Marker)))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not find a {Name}/ directory containing {Marker}. Looked at:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, tried.Distinct()));
    }

    public static string Read(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);

        return File.Exists(path)
            ? File.ReadAllText(path)
            : throw new FileNotFoundException($"Config file not found at {path}.", path);
    }
}
