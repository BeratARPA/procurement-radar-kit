using System.Reflection;
using System.Text.RegularExpressions;
using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Persistence;

namespace ProcurementRadar.UnitTests.Persistence;

/// <summary>
/// Every <c>@Parameter</c> in the notice upsert has somewhere to come from.
/// </summary>
/// <remarks>
/// A column was added to procurement_notice and to the SQL, and the anonymous
/// object that supplies the values was not touched. Nothing complained: it
/// compiled, every test passed, and PostgreSQL does not report a missing
/// parameter - @ is its absolute-value operator, so @BidDeadlineTimeAssumed
/// parsed as an operator applied to a column, and the error read "column
/// biddeadlinetimeassumed does not exist" for a column that did exist.
///
/// Parsing was dead for two hours before anybody read the log. Nothing was lost -
/// the archive had the pages and the rows were still unprocessed - but the only
/// thing that noticed was a line in a file.
///
/// This is the cheap half of what an integration test would do, and D-09 rules
/// those out: the parameter names in the statement are checked against the
/// entity that fills it.
/// </remarks>
public sealed class SqlParameterTests
{
    private static readonly Regex ParameterPattern = new(
        @"@(?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.CultureInvariant);

    // Supplied by the repository rather than read off the entity: the timestamps
    // the caller is not allowed to choose, and the enum written as text.
    private static readonly string[] SuppliedByHand = ["SeenAt", "Status"];

    [Fact]
    public void The_notice_upsert_asks_for_nothing_it_cannot_be_given()
    {
        var sql = (string)typeof(NoticeSql)
            .GetField("Upsert", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!
            .GetRawConstantValue()!;

        var available = typeof(ProcurementNotice)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .Concat(SuppliedByHand)
            .ToHashSet(StringComparer.Ordinal);

        var missing = ParameterPattern
            .Matches(sql)
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .Where(name => !available.Contains(name))
            .ToList();

        Assert.True(
            missing.Count == 0,
            "NoticeSql.Upsert names parameters nothing supplies: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_column_the_notice_upsert_writes_is_given_a_value()
    {
        // The other half of the same mistake: a column in the list with no value
        // beside it. PostgreSQL catches this one, but only once a page has been
        // fetched, parsed and is being stored - which is minutes after the deploy
        // and nowhere near it.
        var sql = (string)typeof(NoticeSql)
            .GetField("Upsert", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!
            .GetRawConstantValue()!;

        var columns = Between(sql, "INSERT INTO procurement_notice (", ")");
        var values = Between(sql, "VALUES (", ")");

        Assert.Equal(Count(columns), Count(values));
    }

    private static string Between(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal) + start.Length;

        return text[from..text.IndexOf(end, from, StringComparison.Ordinal)];
    }

    private static int Count(string list)
        => list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
}
