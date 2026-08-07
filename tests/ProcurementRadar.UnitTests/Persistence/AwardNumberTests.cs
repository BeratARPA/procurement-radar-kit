using System.Text.RegularExpressions;
using ProcurementRadar.Persistence;

namespace ProcurementRadar.UnitTests.Persistence;

/// <summary>
/// Which notices the award sweep is allowed to ask EKAP about.
/// </summary>
/// <remarks>
/// The numbers below are real, taken out of procurement_notice.ikn after the
/// first live sweep put four direct-procurement numbers in a queue meant for
/// tenders and got nothing back for any of them. Nothing was broken - the
/// question had no answer, and the sweep had no way of knowing that.
/// </remarks>
public sealed class AwardNumberTests
{
    private static bool IsTenderNumber(string ikn)
        => Regex.IsMatch(ikn, AwardSql.TenderNumberPattern, RegexOptions.CultureInvariant);

    [Theory]
    [InlineData("2026/1166872")]
    [InlineData("2026/1071227")]
    [InlineData("2026/234567")]
    public void A_tender_registration_number_is_asked_about(string ikn)
    {
        Assert.True(IsTenderNumber(ikn));
    }

    [Theory]
    [InlineData("26DT5002097")]
    [InlineData("26DT5002098")]
    [InlineData("26DT5002099")]
    [InlineData("26DT5002107")]
    public void A_direct_procurement_number_is_not(string number)
    {
        Assert.False(IsTenderNumber(number));
    }

    [Theory]
    [InlineData("")]
    [InlineData("2026/")]
    [InlineData("2026-1166872")]
    [InlineData("İKN 2026/1166872")]
    public void Anything_else_is_not_either(string text)
    {
        // Anchored at both ends on purpose: an unanchored pattern would accept
        // a number with a label in front of it, and the search box would be
        // handed the label as well.
        Assert.False(IsTenderNumber(text));
    }
}
