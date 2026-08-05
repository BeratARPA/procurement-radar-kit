using ProcurementRadar.Parsers;

namespace ProcurementRadar.UnitTests.Parsing;

public sealed class GenericTableAdapterConfigTests
{
    private const string Complete = """
        {
          "row_selector": "table tbody tr",
          "title_selector": "td:nth-child(2)",
          "link_selector": "a",
          "date_selector": "td:nth-child(3)",
          "amount_selector": null,
          "base_url": "https://ornek.gov.tr"
        }
        """;

    [Fact]
    public void A_complete_config_is_read()
    {
        Assert.True(GenericTableAdapterConfig.TryRead(Complete, out var config, out _));

        Assert.Equal("table tbody tr", config.RowSelector);
        Assert.Equal("td:nth-child(2)", config.TitleSelector);
        Assert.Equal("a", config.LinkSelector);
        Assert.Equal("td:nth-child(3)", config.DateSelector);
        Assert.Equal("https://ornek.gov.tr", config.BaseUrl);
        // Most listings carry no amount; docs/source-collection.md says to leave
        // this null rather than invent a selector.
        Assert.Null(config.AmountSelector);
    }

    [Theory]
    [InlineData(null, "empty")]
    [InlineData("", "empty")]
    [InlineData("not json", "not valid JSON")]
    public void Something_that_is_not_a_config_is_reported(string? json, string expected)
    {
        Assert.False(GenericTableAdapterConfig.TryRead(json, out _, out var problem));
        Assert.Contains(expected, problem, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("row_selector")]
    [InlineData("title_selector")]
    [InlineData("link_selector")]
    [InlineData("base_url")]
    public void A_missing_required_selector_is_named(string field)
    {
        var json = Complete.Replace($"\"{field}\"", "\"unused\"", StringComparison.Ordinal);

        Assert.False(GenericTableAdapterConfig.TryRead(json, out _, out var problem));
        Assert.Contains(field, problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_selector_still_set_to_TODO_is_refused_rather_than_parsed()
    {
        // This is the whole point of the check. A source seeded straight from
        // config/sources.seed.json still has placeholders, and letting it through
        // would produce zero notices - which looks exactly like a parser that
        // broke, and would set off the wrong alarm in T-15.
        var json = Complete.Replace("table tbody tr", "TODO", StringComparison.Ordinal);

        Assert.False(GenericTableAdapterConfig.TryRead(json, out _, out var problem));
        Assert.Contains("TODO", problem, StringComparison.Ordinal);
        Assert.Contains("row_selector", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void An_optional_selector_left_as_TODO_is_simply_dropped()
    {
        var json = Complete.Replace("\"td:nth-child(3)\"", "\"TODO\"", StringComparison.Ordinal);

        Assert.True(GenericTableAdapterConfig.TryRead(json, out var config, out _));
        Assert.Null(config.DateSelector);
    }

    [Fact]
    public void A_base_url_that_is_not_absolute_is_refused()
    {
        var json = Complete.Replace("https://ornek.gov.tr", "/ihaleler", StringComparison.Ordinal);

        Assert.False(GenericTableAdapterConfig.TryRead(json, out _, out var problem));
        Assert.Contains("absolute", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void The_shape_in_the_seed_file_that_ships_with_the_repository_is_understood()
    {
        // Not that it is usable - every entry is still a placeholder - but that
        // the field names in config/sources.seed.json and the ones read here have
        // not drifted apart.
        var path = Path.Combine(RepositoryRoot.Find().FullName, "config", "sources.seed.json");
        var seeded = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));

        var configs = seeded.RootElement
            .GetProperty("sources")
            .EnumerateArray()
            .Where(entry => entry.TryGetProperty("adapter_config", out _))
            .Select(entry => entry.GetProperty("adapter_config").GetRawText())
            .ToArray();

        Assert.NotEmpty(configs);

        foreach (var config in configs)
        {
            Assert.False(GenericTableAdapterConfig.TryRead(config, out _, out var problem));
            // Rejected for being unfilled, not for being unrecognised.
            Assert.Contains("TODO", problem, StringComparison.Ordinal);
        }
    }
}
