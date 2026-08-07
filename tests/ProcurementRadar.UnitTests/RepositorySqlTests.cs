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
    // Which tables each SQL holder is allowed to name. Most work against one;
    // the notification queries join across four to assemble a message.
    private static readonly (Type Holder, string[] Tables)[] SqlHolders =
    [
        (typeof(SourceSql), ["source"]),
        (typeof(RawFetchSql), ["raw_fetch"]),
        (typeof(NoticeSql), ["procurement_notice"]),
        (typeof(NoticeScoreSql), ["notice_score"]),
        // Three tables since T-16: choosing which notices deserve an announcement
        // means asking what scored and what already has text.
        (typeof(NoticeDocumentSql),
            ["notice_document", "procurement_notice", "notice_score"]),
        (typeof(NotificationSql),
            ["notification", "procurement_notice", "notice_score", "notice_document"]),
        (typeof(HealthSql),
            ["health_alert", "source", "raw_fetch", "procurement_notice", "notification"]),
        // The sweep asks which concluded notices have no result yet, so it reads
        // the notice it is about as well as the award it is looking for.
        (typeof(AwardSql), ["award", "procurement_notice"])
    ];

    // Words that follow FROM/INTO/UPDATE/JOIN without being a table name.
    // "ON CONFLICT ... DO UPDATE SET" is the one that actually shows up.
    private static readonly string[] NotTableNames = ["set", "select", "values", "where"];

    private static readonly Regex TableReferencePattern = new(
        @"\b(?:FROM|INTO|UPDATE|JOIN)\s+(?<table>[a-z_][a-z0-9_]*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // The word boundary keeps the pattern from starting mid-identifier, and the
    // lookbehind keeps a PostgreSQL cast out of it: count(*)::int AS "EmptyParses"
    // otherwise reports "int" as a column that does not exist.
    private static readonly Regex AliasedColumnPattern = new(
        @"\b(?<!:)(?<column>[a-z_][a-z0-9_]*)\s+AS\s+""(?<property>[A-Za-z][A-Za-z0-9]*)""",
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
                "award", "health_alert", "notice_document", "notice_score", "notification",
                "procurement_notice", "raw_fetch", "source"
            },
            _schema.TableNames.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_statement_is_a_compile_time_constant()
    {
        var problems = new List<string>();

        foreach (var (holder, _) in SqlHolders)
        {
            foreach (var field in holder.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field is not { IsLiteral: true, IsInitOnly: false } || field.FieldType != typeof(string))
                {
                    problems.Add($"{holder.Name}.{field.Name} is not a const string");
                }
                else if (string.IsNullOrWhiteSpace((string)field.GetRawConstantValue()!))
                {
                    problems.Add($"{holder.Name}.{field.Name} is empty");
                }
            }

            // Nothing here may assemble SQL at run time. Every statement being a
            // constant is what makes it impossible for a value read from a page
            // to end up as SQL, and it is a stronger guarantee than reviewing
            // each query for concatenation.
            foreach (var method in holder.GetMethods(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                problems.Add($"{holder.Name}.{method.Name} builds SQL at run time");
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

        foreach (var (holder, tables) in SqlHolders)
        {
            foreach (var (name, sql) in StatementsOf(holder))
            {
                foreach (Match match in AliasedColumnPattern.Matches(sql))
                {
                    var column = match.Groups["column"].Value;
                    checkedColumns++;

                    if (!tables.Any(table => _schema.HasColumn(table, column)))
                    {
                        problems.Add(
                            $"{name} selects '{column}', which is not a column of "
                            + string.Join(" or ", tables));
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
    public void The_upsert_touches_only_last_seen_at_when_a_notice_is_already_known()
    {
        var conflictClause = NoticeSql.Upsert[NoticeSql.Upsert.IndexOf(
            "ON CONFLICT", StringComparison.Ordinal)..];

        // first_seen_at is the product's whole competitive edge: how many hours
        // before everyone else we saw this. Recomputing it once destroys it for
        // that notice permanently.
        Assert.DoesNotContain("first_seen_at", conflictClause, StringComparison.Ordinal);

        // Spec Bölüm 8: on a collision the first record is kept and only
        // last_seen_at moves. Exactly one assignment, not "at least one".
        var assignments = Regex.Matches(conflictClause, @"(?<column>[a-z_]+)\s*=\s*EXCLUDED\.")
            .Select(match => match.Groups["column"].Value)
            .ToArray();

        Assert.Equal(["last_seen_at"], assignments);
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
