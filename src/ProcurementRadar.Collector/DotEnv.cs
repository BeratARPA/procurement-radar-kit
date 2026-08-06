namespace ProcurementRadar.Collector;

/// <summary>
/// Loads the <c>.env</c> file D-06 describes into the process environment.
/// </summary>
/// <remarks>
/// D-06 named <c>.env</c> as how secrets reach a running worker, and on Linux
/// systemd reads it for us through EnvironmentFile. Nothing reads it on Windows:
/// a scheduled task inherits whatever the session has and nothing more, so a
/// worker started that way came up with no connection string and stopped on its
/// first line. Doing it here means one mechanism on both machines.
///
/// A variable already set in the environment always wins. The file is a default,
/// not an override - otherwise a value exported deliberately for one run would be
/// silently replaced by whatever the file happens to say.
/// </remarks>
public static class DotEnv
{
    /// <summary>
    /// Finds <c>.env</c> beside the application or in a directory above it, and
    /// applies it. Missing file is not an error: the environment may already
    /// carry everything, which is how the Linux service runs.
    /// </summary>
    public static string? Load(string? startDirectory = null)
    {
        var path = Find(startDirectory ?? AppContext.BaseDirectory)
            ?? Find(Directory.GetCurrentDirectory());

        if (path is null)
        {
            return null;
        }

        foreach (var line in File.ReadAllLines(path))
        {
            var text = line.Trim();

            if (text.Length == 0 || text[0] == '#')
            {
                continue;
            }

            var separator = text.IndexOf('=');

            if (separator < 1)
            {
                continue;
            }

            var name = text[..separator].Trim();
            var value = text[(separator + 1)..].Trim();

            // A value may be quoted to keep spaces; the quotes are not part of it.
            if (value.Length > 1
                && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            if (name.Length == 0
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))
            {
                continue;
            }

            Environment.SetEnvironmentVariable(name, value);
        }

        return path;
    }

    private static string? Find(string directory)
    {
        var current = new DirectoryInfo(directory);

        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, ".env");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }
}
