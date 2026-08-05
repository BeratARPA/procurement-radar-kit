using System.Text.Json;
using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Enums;

namespace ProcurementRadar.Cli;

/// <summary>
/// What a seed file turns into: the rows worth writing, how many entries were
/// still placeholders, and why anything else was left out.
/// </summary>
internal sealed record SeedPlan(
    IReadOnlyList<Source> Sources,
    int SkippedPlaceholders,
    IReadOnlyList<string> Rejections);

/// <summary>
/// Turns seed JSON into rows. A pure function over the file's text: no file
/// system, no database, no clock, so it can be tested on its own.
/// </summary>
internal static class SourceSeedReader
{
    /// <summary>
    /// A url nobody has filled in yet. docs/source-collection.md tells the team
    /// to leave this in place rather than invent an address.
    /// </summary>
    public const string PlaceholderUrl = "TODO";

    private const int DefaultPollIntervalMinutes = 15;

    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static SeedPlan Read(string json)
    {
        var file = JsonSerializer.Deserialize<SourceSeedFile>(json, Options)
            ?? throw new JsonException("The seed file deserialized to null.");

        var sources = new List<Source>();
        var rejections = new List<string>();
        var placeholders = 0;
        var index = 0;

        foreach (var entry in file.Sources)
        {
            index++;
            var label = string.IsNullOrWhiteSpace(entry.AuthorityName)
                ? $"entry {index}"
                : $"entry {index} ({entry.AuthorityName})";

            if (IsPlaceholder(entry.Url))
            {
                placeholders++;
                continue;
            }

            if (TryMap(entry, label, out var source, out var reason))
            {
                sources.Add(source);
            }
            else
            {
                rejections.Add(reason);
            }
        }

        return new SeedPlan(sources, placeholders, rejections);
    }

    private static bool IsPlaceholder(string? url)
        => string.IsNullOrWhiteSpace(url)
            || string.Equals(url.Trim(), PlaceholderUrl, StringComparison.OrdinalIgnoreCase);

    private static bool TryMap(SourceSeedEntry entry, string label, out Source source, out string reason)
    {
        source = null!;

        if (string.IsNullOrWhiteSpace(entry.AuthorityName))
        {
            reason = $"{label}: authority_name is missing";
            return false;
        }

        if (!EnumText.TryParse<AuthorityType>(entry.AuthorityType, out var authorityType))
        {
            reason = $"{label}: '{entry.AuthorityType}' is not a known authority_type";
            return false;
        }

        if (string.IsNullOrWhiteSpace(entry.Province))
        {
            reason = $"{label}: province is missing";
            return false;
        }

        if (string.IsNullOrWhiteSpace(entry.AdapterKey))
        {
            reason = $"{label}: adapter_key is missing";
            return false;
        }

        var pollInterval = entry.PollIntervalMinutes ?? DefaultPollIntervalMinutes;
        if (pollInterval <= 0)
        {
            reason = $"{label}: poll_interval_minutes must be positive, got {pollInterval}";
            return false;
        }

        source = new Source
        {
            AuthorityName = entry.AuthorityName.Trim(),
            AuthorityType = authorityType,
            Province = entry.Province.Trim(),
            District = Blank(entry.District),
            Url = entry.Url!.Trim(),
            AdapterKey = entry.AdapterKey.Trim(),
            AdapterConfig = entry.AdapterConfig is { } config
                ? JsonSerializer.Serialize(config)
                : null,
            PollIntervalMinutes = pollInterval,
            // A source nobody has observed yet stays out of the rotation until
            // somebody flips it on; see the three-day observation step in
            // docs/spec.md section 5.
            IsActive = entry.IsActive ?? false,
            Notes = Blank(entry.Notes)
        };

        reason = string.Empty;
        return true;
    }

    private static string? Blank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
