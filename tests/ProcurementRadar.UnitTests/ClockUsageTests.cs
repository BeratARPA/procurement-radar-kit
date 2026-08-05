namespace ProcurementRadar.UnitTests;

public sealed class ClockUsageTests
{
    private static readonly string[] ForbiddenClocks =
    [
        "DateTime.Now",
        "DateTime.UtcNow",
        "DateTimeOffset.Now",
        "DateTimeOffset.UtcNow"
    ];

    // D-08 bans the ambient clock outright: TimeProvider is injected so tests can
    // fake it, and so that a bid deadline can never depend on the machine's time
    // zone. A grep is a blunt instrument but it is the only check that keeps
    // working as the codebase grows.
    [Fact]
    public void No_production_source_file_reads_the_ambient_clock()
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
            var text = File.ReadAllText(file);

            foreach (var clock in ForbiddenClocks)
            {
                if (text.Contains(clock, StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetRelativePath(root.FullName, file)} uses {clock}");
                }
            }
        }

        // Without this the test would also pass if the scan found no files at all.
        Assert.True(scanned > 0, $"No source file was scanned under '{sourceRoot}'.");

        Assert.True(
            offenders.Count == 0,
            $"Inject TimeProvider instead (D-08):{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    private static bool IsBuildOutput(string path)
    {
        var separator = Path.DirectorySeparatorChar;

        return path.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
            || path.Contains($"{separator}bin{separator}", StringComparison.Ordinal);
    }
}
