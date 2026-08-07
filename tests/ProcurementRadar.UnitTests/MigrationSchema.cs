using System.Text.RegularExpressions;

namespace ProcurementRadar.UnitTests;

/// <summary>
/// Replays every migration script to work out the tables and columns the
/// database ends up with. The scripts are the schema's only source of truth
/// (D-02), so tests check the repository SQL against them rather than against a
/// running database (D-09 rules out integration tests).
/// </summary>
internal sealed class MigrationSchema
{
    private static readonly Regex CreateTablePattern = new(
        @"CREATE\s+TABLE\s+(?<table>[a-z_][a-z0-9_]*)\s*\((?<body>.*?)\r?\n\);",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    // IF NOT EXISTS is optional in the pattern because it is optional in the
    // scripts. Without it here the column reads as "if" and the real one is
    // never recorded, so a repository naming it is reported as naming a column
    // that does not exist - a failure that says nothing about the SQL it fails.
    private static readonly Regex AddColumnPattern = new(
        @"ALTER\s+TABLE\s+(?<table>[a-z_][a-z0-9_]*)\s+ADD\s+COLUMN\s+"
        + @"(?:IF\s+NOT\s+EXISTS\s+)?(?<column>[a-z_][a-z0-9_]*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly string[] ConstraintKeywords =
        ["unique", "primary", "foreign", "constraint", "check", "exclude"];

    private readonly IReadOnlyDictionary<string, HashSet<string>> _columnsByTable;

    private MigrationSchema(IReadOnlyDictionary<string, HashSet<string>> columnsByTable)
    {
        _columnsByTable = columnsByTable;
    }

    public IEnumerable<string> TableNames => _columnsByTable.Keys;

    public static MigrationSchema Load()
    {
        var scriptDirectory = Path.Combine(
            RepositoryRoot.Find().FullName,
            "src", "ProcurementRadar.Migrator", "Scripts");

        var tables = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        // Name order is apply order, which is what DbUp uses too.
        foreach (var script in Directory.GetFiles(scriptDirectory, "*.sql").Order(StringComparer.Ordinal))
        {
            var sql = File.ReadAllText(script);

            foreach (Match match in CreateTablePattern.Matches(sql))
            {
                tables[match.Groups["table"].Value] = ParseColumns(match.Groups["body"].Value);
            }

            foreach (Match match in AddColumnPattern.Matches(sql))
            {
                var table = match.Groups["table"].Value;

                if (!tables.TryGetValue(table, out var columns))
                {
                    throw new InvalidOperationException(
                        $"{Path.GetFileName(script)} adds a column to '{table}', which no earlier script creates.");
                }

                columns.Add(match.Groups["column"].Value);
            }
        }

        if (tables.Count == 0)
        {
            throw new InvalidOperationException($"No CREATE TABLE statement was found under '{scriptDirectory}'.");
        }

        return new MigrationSchema(tables);
    }

    public bool HasTable(string table) => _columnsByTable.ContainsKey(table);

    public bool HasColumn(string table, string column)
        => _columnsByTable.TryGetValue(table, out var columns) && columns.Contains(column);

    private static HashSet<string> ParseColumns(string body)
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
