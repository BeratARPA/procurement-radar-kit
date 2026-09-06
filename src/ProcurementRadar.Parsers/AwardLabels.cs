using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProcurementRadar.Parsers;

/// <summary>
/// The Turkish labels a result announcement uses, read from
/// <c>config/tr-award-labels.json</c>.
/// </summary>
/// <remarks>
/// Same reason as the keyword lists and the date patterns: the source text is
/// Turkish, so the words are data. When EKAP renames a row - and it renames rows
/// - the fix is a config edit, not a rebuild.
/// </remarks>
public sealed record AwardLabels
{
    [JsonPropertyName("result_marker")]
    public required string ResultMarker { get; init; }

    [JsonPropertyName("contract_section")]
    public required string ContractSection { get; init; }

    /// <summary>
    /// What the contract row calls the firm that won, newest wording first.
    /// </summary>
    /// <remarks>
    /// A list rather than one string, because EKAP renamed this row and a single
    /// value could not hold both spellings. The announcement used to say
    /// "Yüklenici" and now says "Yüklenicisi"; the fixture captured in July still
    /// has the old one, so every test stayed green while the live parser wrote
    /// null into every award for weeks. Keeping the previous wording here means a
    /// rename costs a config edit, which is what the remark above this class
    /// already promised.
    /// </remarks>
    [JsonPropertyName("winner_labels")]
    public IReadOnlyList<string>? WinnerLabelsFromConfig { get; init; }

    /// <summary>The wording this file used to carry, when it carried only one.</summary>
    /// <remarks>
    /// Still read, and not only for tidiness. The settings table holds whatever
    /// was seeded into it and the seeder does not overwrite a key that is already
    /// there (D-16), so on any machine that has run before, this value is the one
    /// in the database until somebody pushes a new one. A loader that rejected it
    /// would make the deploy that fixes the parser the deploy that cannot start.
    /// </remarks>
    [JsonPropertyName("winner_label")]
    public string? LegacyWinnerLabel { get; init; }

    [JsonIgnore]
    public IReadOnlyList<string> WinnerLabels =>
        WinnerLabelsFromConfig is { Count: > 0 } list
            ? list
            : LegacyWinnerLabel is { Length: > 0 } single
                ? [single]
                : throw new InvalidOperationException(
                    "tr-award-labels.json needs winner_labels (or the older winner_label).");

    [JsonPropertyName("amount_label")]
    public required string AmountLabel { get; init; }

    [JsonPropertyName("date_label")]
    public required string DateLabel { get; init; }

    [JsonPropertyName("bid_count_label")]
    public required string BidCountLabel { get; init; }

    [JsonPropertyName("valid_bid_count_label")]
    public required string ValidBidCountLabel { get; init; }

    [JsonPropertyName("item_letters")]
    public required string ItemLetters { get; init; }

    public static AwardLabels Load(string json)
        => JsonSerializer.Deserialize<AwardLabels>(json)
           ?? throw new InvalidOperationException("tr-award-labels.json is empty.");
}
