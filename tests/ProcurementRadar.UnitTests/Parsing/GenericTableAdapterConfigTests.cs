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
    [InlineData("base_url")]
    public void A_missing_required_selector_is_named(string field)
    {
        var json = Complete.Replace($"\"{field}\"", "\"unused\"", StringComparison.Ordinal);

        Assert.False(GenericTableAdapterConfig.TryRead(json, out _, out var problem));
        Assert.Contains(field, problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_listing_with_no_detail_pages_is_a_valid_config()
    {
        // Ankara İl Sağlık Müdürlüğü publishes its Doğrudan Temin table with no
        // detail page at all: the links in a row are the specification files.
        // Requiring link_selector would have made that source unusable.
        var json = Complete
            .Replace("\"link_selector\": \"a\",", "\"link_selector\": null,", StringComparison.Ordinal);

        Assert.True(GenericTableAdapterConfig.TryRead(json, out var config, out var problem), problem);
        Assert.Null(config.LinkSelector);
    }

    [Fact]
    public void A_time_selector_is_read_when_present()
    {
        var json = Complete
            .Replace("\"amount_selector\": null",
                "\"amount_selector\": null, \"time_selector\": \"td:nth-child(4)\"",
                StringComparison.Ordinal);

        Assert.True(GenericTableAdapterConfig.TryRead(json, out var config, out _));
        Assert.Equal("td:nth-child(4)", config.TimeSelector);
    }

    [Fact]
    public void A_config_without_a_time_selector_is_still_valid()
    {
        // Most listings keep the time in the same cell as the date, or have no
        // time at all. This stays optional.
        Assert.True(GenericTableAdapterConfig.TryRead(Complete, out var config, out _));
        Assert.Null(config.TimeSelector);
    }

    [Fact]
    public void A_document_selector_is_read_when_present()
    {
        var json = Complete
            .Replace("\"amount_selector\": null", "\"amount_selector\": null, \"document_selector\": \"a\"",
                StringComparison.Ordinal);

        Assert.True(GenericTableAdapterConfig.TryRead(json, out var config, out _));
        Assert.Equal("a", config.DocumentSelector);
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
    public void Every_config_in_the_seed_file_is_either_usable_or_openly_unfilled()
    {
        // Guards the seam between the file the team edits by hand and the code
        // that reads it. A config may be complete, or it may still hold
        // placeholders - but it must never be rejected for a reason that means
        // the two have drifted apart, because that would look like an empty
        // source rather than a mistake.
        var path = Path.Combine(RepositoryRoot.Find().FullName, "config", "sources.seed.json");
        var seeded = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));

        // Only this adapter's rows. Since T-16 the registry also holds EKAP
        // sources, whose adapter_config is a search term rather than a set of
        // selectors - reading one with this parser would fail for a reason that
        // means nothing.
        var configs = seeded.RootElement
            .GetProperty("sources")
            .EnumerateArray()
            .Where(entry => entry.TryGetProperty("adapter_config", out _)
                && entry.TryGetProperty("adapter_key", out var key)
                && key.GetString() == GenericTableAdapter.AdapterKey)
            .Select(entry => entry.GetProperty("adapter_config").GetRawText())
            .ToArray();

        Assert.NotEmpty(configs);

        var usable = 0;

        foreach (var config in configs)
        {
            if (GenericTableAdapterConfig.TryRead(config, out _, out var problem))
            {
                usable++;
                continue;
            }

            Assert.Contains("TODO", problem, StringComparison.Ordinal);
        }

        Assert.True(usable > 0, "No source in the seed file is ready to poll.");
    }
}
