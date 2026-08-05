using System.Xml.Linq;
using ProcurementRadar.Parsers;

namespace ProcurementRadar.UnitTests.Parsing;

/// <summary>
/// The rule that keeps the archive worth having: a parser is a pure function
/// over a body somebody else fetched. If a parser could reach the network, a
/// broken one could not be fixed and re-run against the archive, and losing that
/// is losing the data permanently.
/// </summary>
public sealed class ParserArchitectureTests
{
    private const string ParsersProject = "ProcurementRadar.Parsers";

    /// <summary>Assemblies a parser has no business touching.</summary>
    private static readonly string[] ForbiddenAssemblies =
    [
        "System.Net.Http",
        "System.Net.Primitives",
        "Npgsql",
        "Dapper",
        "System.Data.Common",
        "Microsoft.Extensions.Http"
    ];

    private static readonly string[] ForbiddenTokens =
    [
        "HttpClient",
        "WebClient",
        "DateTime.Now",
        "DateTime.UtcNow",
        "DateTimeOffset.Now",
        "DateTimeOffset.UtcNow",
        "TimeProvider",
        "IDbConnection",
        "File.ReadAll",
        "File.Open",
        "File.Exists",
        "Directory.",
        "FileStream"
    ];

    [Fact]
    public void The_parsers_assembly_references_nothing_that_can_reach_the_network_or_the_database()
    {
        var referenced = typeof(GenericTableAdapter).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name!)
            .ToArray();

        // Guards against the check passing because the list came back empty.
        Assert.Contains("AngleSharp", referenced);

        var offenders = referenced
            .Where(name => ForbiddenAssemblies.Contains(name, StringComparer.Ordinal))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"{ParsersProject} references {string.Join(", ", offenders)}.");
    }

    [Fact]
    public void The_parsers_project_references_only_the_domain()
    {
        var csproj = XDocument.Load(Path.Combine(
            RepositoryRoot.Find().FullName,
            "src", ParsersProject, $"{ParsersProject}.csproj"));

        // The elements, not the file's text: the comment in that file explains
        // why the collector is absent, and naming it there must not read as
        // referencing it.
        var referenced = csproj
            .Descendants("ProjectReference")
            .Select(element => Path.GetFileNameWithoutExtension(element.Attribute("Include")!.Value))
            .ToArray();

        Assert.Equal(["ProcurementRadar.Domain"], referenced);
    }

    [Fact]
    public void No_parser_source_file_mentions_the_network_the_clock_or_the_disk()
    {
        var root = RepositoryRoot.Find();
        var parsersRoot = Path.Combine(root.FullName, "src", ParsersProject);
        var offenders = new List<string>();
        var scanned = 0;

        foreach (var file in Directory.EnumerateFiles(parsersRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            scanned++;
            var text = File.ReadAllText(file);

            foreach (var token in ForbiddenTokens)
            {
                if (text.Contains(token, StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetRelativePath(root.FullName, file)} mentions {token}");
                }
            }
        }

        Assert.True(scanned > 0, $"No source file was scanned under '{parsersRoot}'.");
        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }
}
