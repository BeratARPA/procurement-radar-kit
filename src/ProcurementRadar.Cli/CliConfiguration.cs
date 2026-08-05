using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace ProcurementRadar.Cli;

internal static class CliConfiguration
{
    /// <summary>
    /// D-06: user secrets while developing, environment variables when running
    /// for real. Environment variables win so the host can override a developer.
    /// Returns null when nothing is configured; the caller reports it.
    /// </summary>
    public static string? ConnectionString()
        => new ConfigurationBuilder()
            .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true)
            .AddEnvironmentVariables()
            .Build()["PR_DB_CONNECTION_STRING"];
}
