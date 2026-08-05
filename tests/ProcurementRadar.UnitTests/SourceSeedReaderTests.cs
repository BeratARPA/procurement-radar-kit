using System.Text.Json;
using ProcurementRadar.Cli;
using ProcurementRadar.Domain.Enums;

namespace ProcurementRadar.UnitTests;

public sealed class SourceSeedReaderTests
{
    [Fact]
    public void An_entry_whose_url_is_still_a_placeholder_is_skipped_and_counted()
    {
        var plan = SourceSeedReader.Read(
            """
            {
              "sources": [
                { "authority_name": "A", "authority_type": "municipality", "province": "Ankara",
                  "url": "TODO", "adapter_key": "generic_table" },
                { "authority_name": "B", "authority_type": "municipality", "province": "Ankara",
                  "url": "https://example.gov.tr/ihaleler", "adapter_key": "generic_table" }
              ]
            }
            """);

        Assert.Equal(1, plan.SkippedPlaceholders);
        Assert.Empty(plan.Rejections);
        Assert.Equal("B", Assert.Single(plan.Sources).AuthorityName);
    }

    [Theory]
    [InlineData("\"TODO\"")]
    [InlineData("\"todo\"")]
    [InlineData("\"  TODO  \"")]
    [InlineData("\"\"")]
    [InlineData("null")]
    public void A_url_that_nobody_has_filled_in_never_reaches_the_database(string urlJson)
    {
        var plan = SourceSeedReader.Read(
            $$"""
            {
              "sources": [
                { "authority_name": "A", "authority_type": "municipality", "province": "Ankara",
                  "url": {{urlJson}}, "adapter_key": "generic_table" }
              ]
            }
            """);

        Assert.Empty(plan.Sources);
        Assert.Equal(1, plan.SkippedPlaceholders);
    }

    [Fact]
    public void A_complete_entry_maps_onto_the_schema()
    {
        var plan = SourceSeedReader.Read(
            """
            {
              "sources": [
                {
                  "authority_name": "  Çankaya Belediyesi  ",
                  "authority_type": "municipality",
                  "province": "Ankara",
                  "district": "Çankaya",
                  "url": "https://example.gov.tr/ihaleler",
                  "adapter_key": "generic_table",
                  "poll_interval_minutes": 30,
                  "is_active": true,
                  "adapter_config": { "row_selector": "table tbody tr", "amount_selector": null },
                  "notes": ""
                }
              ]
            }
            """);

        var source = Assert.Single(plan.Sources);

        Assert.Equal("Çankaya Belediyesi", source.AuthorityName);
        Assert.Equal(AuthorityType.Municipality, source.AuthorityType);
        Assert.Equal("Çankaya", source.District);
        Assert.Equal("https://example.gov.tr/ihaleler", source.Url);
        Assert.Equal(30, source.PollIntervalMinutes);
        Assert.True(source.IsActive);
        // An empty note is stored as null rather than as an empty string.
        Assert.Null(source.Notes);

        var config = JsonDocument.Parse(source.AdapterConfig!).RootElement;
        Assert.Equal("table tbody tr", config.GetProperty("row_selector").GetString());
    }

    [Fact]
    public void A_source_nobody_has_observed_yet_stays_out_of_the_rotation()
    {
        var plan = SourceSeedReader.Read(
            """
            {
              "sources": [
                { "authority_name": "A", "authority_type": "university", "province": "Ankara",
                  "url": "https://example.edu.tr/ihale", "adapter_key": "generic_table" }
              ]
            }
            """);

        Assert.False(Assert.Single(plan.Sources).IsActive);
    }

    [Theory]
    [InlineData("\"authority_type\": \"belediye\"", "not a known authority_type")]
    [InlineData("\"authority_type\": \"municipality\", \"province\": \"\"", "province is missing")]
    [InlineData("\"authority_type\": \"municipality\", \"poll_interval_minutes\": 0", "must be positive")]
    public void A_broken_entry_is_reported_instead_of_written(string fragment, string expectedReason)
    {
        var plan = SourceSeedReader.Read(
            $$"""
            {
              "sources": [
                { "authority_name": "A", "province": "Ankara",
                  "url": "https://example.gov.tr/ihaleler", "adapter_key": "generic_table",
                  {{fragment}} }
              ]
            }
            """);

        Assert.Empty(plan.Sources);
        Assert.Contains(expectedReason, Assert.Single(plan.Rejections), StringComparison.Ordinal);
    }

    [Fact]
    public void One_broken_entry_does_not_discard_the_rest_of_the_file()
    {
        var plan = SourceSeedReader.Read(
            """
            {
              "sources": [
                { "authority_name": "Broken", "authority_type": "nonsense", "province": "Ankara",
                  "url": "https://example.gov.tr/a", "adapter_key": "generic_table" },
                { "authority_name": "Fine", "authority_type": "hospital", "province": "Ankara",
                  "url": "https://example.gov.tr/b", "adapter_key": "generic_table" }
              ]
            }
            """);

        Assert.Equal("Fine", Assert.Single(plan.Sources).AuthorityName);
        Assert.Single(plan.Rejections);
    }

    [Fact]
    public void The_seed_file_that_ships_with_the_repository_is_readable()
    {
        var path = Path.Combine(RepositoryRoot.Find().FullName, "config", "sources.seed.json");

        var plan = SourceSeedReader.Read(File.ReadAllText(path));

        // Every entry is still a placeholder today. What matters here is that the
        // file parses and that the unknown top-level keys in it are ignored, so
        // this test keeps working once the team starts filling urls in.
        Assert.Empty(plan.Rejections);
        Assert.Equal(4, plan.SkippedPlaceholders + plan.Sources.Count);
    }
}
