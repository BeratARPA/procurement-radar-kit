namespace ProcurementRadar.UnitTests;

/// <summary>
/// Finds the repository root by walking up from the test assembly. Tests that
/// check files rather than behaviour need it.
/// </summary>
internal static class RepositoryRoot
{
    public static DirectoryInfo Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "ProcurementRadar.sln")))
        {
            directory = directory.Parent;
        }

        return directory ?? throw new InvalidOperationException(
            $"Walked up from '{AppContext.BaseDirectory}' without finding ProcurementRadar.sln.");
    }
}
