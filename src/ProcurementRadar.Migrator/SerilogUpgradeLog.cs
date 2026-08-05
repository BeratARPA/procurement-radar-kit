using System.Globalization;
using DbUp.Engine.Output;
using Serilog;

namespace ProcurementRadar.Migrator;

/// <summary>
/// Bridges DbUp's own output into Serilog so a migration run lands in the same
/// JSON stream as everything else (D-07).
/// </summary>
internal sealed class SerilogUpgradeLog : IUpgradeLog
{
    public void LogTrace(string format, params object[] args)
        => Log.Verbose("{DbUpMessage}", Render(format, args));

    public void LogDebug(string format, params object[] args)
        => Log.Debug("{DbUpMessage}", Render(format, args));

    public void LogInformation(string format, params object[] args)
        => Log.Information("{DbUpMessage}", Render(format, args));

    public void LogWarning(string format, params object[] args)
        => Log.Warning("{DbUpMessage}", Render(format, args));

    public void LogError(string format, params object[] args)
        => Log.Error("{DbUpMessage}", Render(format, args));

    public void LogError(Exception exception, string format, params object[] args)
        => Log.Error(exception, "{DbUpMessage}", Render(format, args));

    // DbUp uses string.Format placeholders, not Serilog message templates, so the
    // text is rendered here and handed over as a single property.
    private static string Render(string format, object[] args)
        => args is { Length: > 0 }
            ? string.Format(CultureInfo.InvariantCulture, format, args)
            : format;
}
