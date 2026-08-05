using ProcurementRadar.Parsers;

namespace ProcurementRadar.UnitTests.Parsing;

public sealed class TurkishMoneyParserTests
{
    [Theory]
    [InlineData("180.000,00 TL", 180000)]
    [InlineData("180.000 TL", 180000)]
    [InlineData("1.234.567,89 ₺", 1234567.89)]
    [InlineData("Yaklaşık maliyet: 45.000,50 TL + KDV", 45000.50)]
    [InlineData("  12,5  ", 12.5)]
    [InlineData("999", 999)]
    public void Amounts_are_read_the_way_the_pages_print_them(string text, decimal expected)
    {
        Assert.Equal(expected, TurkishMoneyParser.TryParse(text));
    }

    [Fact]
    public void The_dot_groups_thousands_rather_than_marking_a_decimal()
    {
        // The trap this class exists for: read with the invariant culture,
        // 180.000 TL silently becomes 180 lira.
        Assert.Equal(180000m, TurkishMoneyParser.TryParse("180.000 TL"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Belirtilmemiş")]
    [InlineData("-")]
    public void Anything_without_a_number_comes_back_null(string? text)
    {
        Assert.Null(TurkishMoneyParser.TryParse(text));
    }

    [Fact]
    public void A_negative_amount_is_not_a_cost()
    {
        Assert.Null(TurkishMoneyParser.TryParse("-500 TL"));
    }
}
