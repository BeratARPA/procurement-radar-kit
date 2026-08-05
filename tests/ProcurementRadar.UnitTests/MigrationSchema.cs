using System.Text.RegularExpressions;

namespace ProcurementRadar.UnitTests;

/// <summary>
/// Reads the tables and columns out of 001_initial.sql. The migration script is
/// the schema's only source of truth (D-02), so tests check the repository SQL
/// against it rather than against a running database (D-09 rules out integration
/// tests).
/// </summary>
internal sealed class MigrationSchema
{
    private static readonly Regex CreateTablePattern = new(
        @"CREATE\s+TABLE\s+(?<table>[a-z_][a-z0-9_]*)\s*\((?<body>.*?)\r?\n\);",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly string[] ConstraintKeywords =
        ["unique", "primary", "foreign", "constraint", "check", "exclude"];

    private readonly IReadOnlyDictionary<string, IReadOnlySet<string>> _columnsByTable;

    private MigrationSchema(IReadOnlyDictionary<string, IReadOnlySet<string>> columnsByTable)
    {
        _columnsByTable = columnsByTable;
    }

    public IEnumerable<string> TableNames => _columnsByTable.Keys;

    public static MigrationSchema Load()
    {
        var path = Path.Combine(
            RepositoryRoot.Find().FullName,
            "src", "ProcurementRadar.Migrator", "Scripts", "001_initial.sql");

        var tables = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);

        foreach (Match match in CreateTablePattern.Matches(File.ReadAllText(path)))
        {
            tables[match.Groups["table"].Value] = ParseColumns(match.Groups["body"].Value);
        }

        if (tables.Count == 0)
        {
            throw new InvalidOperationException($"No CREATE TABLE statement was found in '{path}'.");
        }

        return new MigrationSchema(tables);
    }

    public bool HasTable(string table) => _columnsByTable.ContainsKey(table);

    public bool HasColumn(string table, string column)
        => _columnsByTable.TryGetValue(table, out var columns) && columns.Contains(column);

    private static IReadOnlySet<string> ParseColumns(string body)
    {
        var columns = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rawLine in body.Split('\n'))
        {
            var commentStart = rawLine.IndexOf("--", StringComparison.Ordinal);
            var line = (commentStart >= 0 ? rawLine[..commentStart] : rawLine).Trim();

            if (line.Length == 0)
            {
                continue;
            }

            var name = line.Split([' ', '\t', '('], StringSplitOptions.RemoveEmptyEntries)[0].TrimEnd(',');

            if (ConstraintKeywords.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            columns.Add(name);
        }

        return columns;
    }
}
