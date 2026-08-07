using System.Text.RegularExpressions;

namespace ProcurementRadar.UnitTests.Persistence;

/// <summary>
/// Nothing may ask Dapper for a <c>DateTimeOffset</c> scalar.
/// </summary>
/// <remarks>
/// Npgsql hands a timestamptz back as a DateTime with Kind Utc. Dapper's row
/// path has a type handler for that; its scalar path converts with
/// System.Convert, which has no cast between the two and throws at run time.
///
/// A grep, because nothing else can catch it. It compiles. It passes every unit
/// test, because the tests put a fake repository behind the interface. It fails
/// only against a real database, and only in the one code path that reads a
/// timestamp as a scalar - which is how the settings refresher spent its first
/// twenty-nine minutes logging an error nobody was watching for yet.
/// </remarks>
public sealed class ScalarTimestampTests
{
    [Fact]
    public void No_repository_reads_a_timestamp_as_a_DateTimeOffset_scalar()
    {
        var persistence = Path.Combine(
            RepositoryRoot.Find().FullName, "src", "ProcurementRadar.Persistence");

        var pattern = new Regex(
            @"ExecuteScalarAsync<\s*DateTimeOffset",
            RegexOptions.CultureInvariant);

        var offenders = Directory
            .EnumerateFiles(persistence, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => pattern.IsMatch(File.ReadAllText(file)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Read the timestamp as DateTime and convert it: " + string.Join(", ", offenders));
    }
}
