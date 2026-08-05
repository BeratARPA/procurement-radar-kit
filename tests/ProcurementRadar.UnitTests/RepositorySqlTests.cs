using System.Reflection;
using System.Text.RegularExpressions;
using ProcurementRadar.Persistence;

namespace ProcurementRadar.UnitTests;

/// <summary>
/// The repositories carry hand-written SQL (D-01) and nothing at build time
/// checks it against the schema. These tests do: every table the SQL touches and
/// every column it names has to exist in 001_initial.sql. A renamed column now
/// fails here rather than at three in the morning when a source goes silent.
/// </summary>
public sealed class RepositorySqlTests
{
    // Each SQL holder works against exactly one table.
    private static readonly (Type Holder, string Table)[] SqlHolders =
    [
        (typeof(SourceSql), "source"),
        (typeof(RawFetchSql), "raw_fetch"),
        (typeof(NoticeSql), "procurement_notice")
    ];

    // Words that follow FROM/INTO/UPDATE/JOIN without being a table name.
    // "ON CONFLICT ... DO UPDATE SET" is the one that actually shows up.
    private static readonly string[] NotTableNames = ["set", "select", "values", "where"];

    private static readonly Regex TableReferencePattern = new(
        @"\b(?:FROM|INTO|UPDATE|JOIN)\s+(?<table>[a-z_][a-z0-9_]*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex AliasedColumnPattern = new(
        @"(?<column>[a-z_][a-z0-9_]*)\s+AS\s+""(?<property>[A-Za-z][A-Za-z0-9]*)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex InsertColumnListPattern = new(
        @"INSERT\s+INTO\s+(?<table>[a-z_][a-z0-9_]*)\s*\((?<columns>[^)]*)\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly MigrationSchema _schema = MigrationSchema.Load();

    [Fact]
    public void The_migration_script_parses_into_the_tables_the_spec_lists()
    {
        Assert.Equal(
            new[]
            {
                "award", "notice_document", "notice_score", "notification",
                "procurement_notice", "raw_fetch", "source"
            },
            _schema.TableNames.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_statement_is_non_empty_and_parameterised()
    {
        var problems = new List<string>();

        foreach (var (name, sql) in AllStatements())
        {
            if (string.IsNullOrWhiteSpace(sql))
            {
                problems.Add($"{name} is empty");
            }

            if (sql.Contains('\'', StringComparison.Ordinal) && !sql.Contains("'new'", StringComparison.Ordinal))
            {
                problems.Add($"{name} contains a literal string; use a parameter");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Every_table_the_repositories_touch_exists_in_the_schema()
    {
        var problems = new List<string>();

        foreach (var (name, sql) in AllStatements())
        {
            foreach (Match match in TableReferencePattern.Matches(sql))
            {
                var table = match.Groups["table"].Value;

                if (NotTableNames.Contains(table, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!_schema.HasTable(table))
                {
                    problems.Add($"{name} reads table '{table}', which 001_initial.sql does not create");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Every_aliased_column_exists_on_the_table_it_is_read_from()
    {
        var problems = new List<string>();
        var checkedColumns = 0;

        foreach (var (holder, table) in SqlHolders)
        {
            foreach (var (name, sql) in StatementsOf(holder))
            {
                foreach (Match match in AliasedColumnPattern.Matches(sql))
                {
                    var column = match.Groups["column"].Value;
                    checkedColumns++;

                    if (!_schema.HasColumn(table, column))
                    {
                        problems.Add($"{name} selects '{column}', which is not a column of '{table}'");
                    }
                }
            }
        }

        // Without this the test would also pass if the pattern stopped matching.
        Assert.True(checkedColumns > 0, "No aliased column was found in the repository SQL.");

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Every_inserted_column_exists_on_its_table()
    {
        var problems = new List<string>();

        foreach (var (name, sql) in AllStatements())
        {
            foreach (Match match in InsertColumnListPattern.Matches(sql))
            {
                var table = match.Groups["table"].Value;

                foreach (var column in SplitColumnList(match.Groups["columns"].Value))
                {
                    if (!_schema.HasColumn(table, column))
                    {
                        problems.Add($"{name} inserts into '{table}.{column}', which does not exist");
                    }
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void The_upsert_never_overwrites_first_seen_at()
    {
        var conflictClause = NoticeSql.Upsert[NoticeSql.Upsert.IndexOf(
            "ON CONFLICT", StringComparison.Ordinal)..];

        Assert.DoesNotContain("first_seen_at", conflictClause, StringComparison.Ordinal);
        Assert.Contains("last_seen_at = EXCLUDED.last_seen_at", conflictClause, StringComparison.Ordinal);
    }

    private static IEnumerable<(string Name, string Sql)> AllStatements()
        => SqlHolders.SelectMany(holder => StatementsOf(holder.Holder));

    private static IEnumerable<(string Name, string Sql)> StatementsOf(Type holder)
    {
        foreach (var field in holder.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            {
                yield return ($"{holder.Name}.{field.Name}", (string)field.GetRawConstantValue()!);
            }
        }
    }

    private static IEnumerable<string> SplitColumnList(string columns)
        => columns
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(column => column.Trim());
}
